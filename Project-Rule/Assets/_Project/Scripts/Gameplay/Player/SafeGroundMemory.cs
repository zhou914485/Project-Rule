using UnityEngine;
using UnityEngine.Tilemaps;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 安全落点记忆：记住玩家最后站立的「无刺瓦片地面」，被刺打中时由
    /// <see cref="PlayerController"/> 搬回去。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不加快照</b>：这里只需要一个点。世界快照会连同敌人、规则、计时一起回滚，
    /// 既重又会把「掉血 / 掉落」的上下文一起抹掉；本类只存玩家自己的几个值类型，和
    /// <c>PlayerController.RespawnPosition</c> 是同一类内存数据（约束 C5：不序列化物理状态）。</para>
    ///
    /// <para><b>记录时机 = 每个接地帧</b>：在 <see cref="ContactSensor.Sense"/> 之后调用
    /// <see cref="Observe"/>，站在合格支撑面上就覆盖；离地后不再刷新，所以「起跳前最后一帧」
    /// 天然就是回退点。走落平台、被击退、被弹簧弹飞、被移动平台带走都不需要额外的状态判断。</para>
    ///
    /// <para><b>白名单，不是黑名单</b>：只认 <see cref="TilemapCollider2D"/> + 无刚体 + 不是
    /// <see cref="IMovingSurface"/> / <see cref="ISpringSurface"/>。以后新增任何移动平台，
    /// 默认就被排除，不用回来补规则。单向跳穿平台（<c>OneWayPlatform</c> 的
    /// <c>BoxCollider2D</c>）不在白名单里 —— 它有一小段放行窗口，传送回去可能直接漏下去。</para>
    ///
    /// <para><b>只存值类型</b>：不存 <c>Collider2D</c> / <c>Transform</c> 引用。分区卸载会把
    /// 对象销毁，存引用迟早是 <c>MissingReferenceException</c>；返回前重新做一次地面与刺的
    /// 复验，比拿着旧引用更可靠。</para>
    /// </remarks>
    public sealed class SafeGroundMemory
    {
        /// <summary>复验支撑面时最多拿到的命中数。</summary>
        private const int MaxSupportHits = 4;

        /// <summary>复验支撑面时向下扫的距离（米）。</summary>
        private const float SupportProbeDistance = 0.12f;

        /// <summary>一个安全落点。全是值类型，可以安全地跨分区存活。</summary>
        private struct Spot
        {
            public bool    Has;
            public Vector2 Position;
            public Vector2 Up;
            public Bounds  BodyBounds;
            public Bounds  HazardBounds;
        }

        private readonly Collider2D _body;
        private readonly RaycastHit2D[] _supportHits = new RaycastHit2D[MaxSupportHits];
        private Spot _spot;

        /// <summary>用玩家的身体碰撞体构造。碰撞体只用来读包围盒，不持有它的生命周期。</summary>
        public SafeGroundMemory(Collider2D body)
        {
            _body = body;
        }

        /// <summary>当前有没有可用的安全落点。</summary>
        public bool HasSpot => _spot.Has;

        /// <summary>安全落点的世界坐标（只在 <see cref="HasSpot"/> 为真时有意义）。</summary>
        public Vector2 Position => _spot.Position;

        /// <summary>记录这个点时的「上」方向（重力反转后旧点作废）。</summary>
        public Vector2 Up => _spot.Up;

        /// <summary>清掉记忆。死亡 / 复活时用 —— 下一个接地帧会重新记。</summary>
        public void Forget() => _spot = default;

        /// <summary>
        /// 每物理帧在 <see cref="ContactSensor.Sense"/> 之后调用：站在合格的瓦片地面上就刷新安全点。
        /// </summary>
        /// <param name="contact">本帧的接触结果。</param>
        /// <param name="hurtbox">受击箱（没有就退回身体包围盒）—— 刺判定用的就是这块几何。</param>
        /// <param name="hazardMargin">判「无刺」时向外扩的余量（米）。</param>
        public void Observe(ContactSensor.State contact, Collider2D hurtbox, float hazardMargin)
        {
            if (_body == null) return;
            if (!contact.Grounded) return;
            if (!IsStaticTileGround(contact.GroundCollider)) return;

            Bounds bodyBounds = _body.bounds;
            Bounds hazardBounds = Expand(hurtbox != null ? hurtbox.bounds : bodyBounds, hazardMargin);

            // ⚠️ 查受击箱，不是只查脚底那一格：刺格没有碰撞体，人是站在旁边瓦片上、
            //    受击箱压进刺格才受伤的。只查脚底会漏掉「站在刺边」的位置。
            if (HazardField.AnyOverlaps(hazardBounds)) return;

            _spot.Has          = true;
            _spot.Position     = ReadPosition();
            _spot.Up           = contact.Up;
            _spot.BodyBounds   = bodyBounds;
            _spot.HazardBounds = hazardBounds;
        }

        /// <summary>
        /// 被刺命中时取回安全点；会重新校验，失败返回 <c>false</c>（调用方保持正常击退）。
        /// </summary>
        /// <param name="currentUp">当前「上」方向 —— 和记录时不一致就不回去（地面已经变天花板）。</param>
        /// <param name="solidLayer">地面层，复验支撑面用。</param>
        /// <param name="position">通过校验时的目标世界坐标。</param>
        public bool TryRecall(Vector2 currentUp, LayerMask solidLayer, out Vector2 position)
        {
            position = _spot.Position;
            if (!_spot.Has) return false;
            if (_spot.Up.sqrMagnitude < 0.5f) return false;
            if (Vector2.Dot(currentUp, _spot.Up) < 0.99f) return false;

            // ① 瓦片还在（塌陷地板 / 分区卸载 / 被别的东西清掉）。
            if (!HasStaticSupport(_spot, solidLayer)) return false;

            // ② 这个位置现在仍然无刺（刺格被新加 / 分区重新加载后可能变了）。
            if (HazardField.AnyOverlaps(_spot.HazardBounds)) return false;

            return true;
        }

        /// <summary>
        /// 支撑面白名单：只认**静态的**整张瓦片地面。
        /// </summary>
        /// <remarks>
        /// <para>单向跳穿平台是 <c>OneWayPlatform</c> 生成的 <c>BoxCollider2D</c>，不算；
        /// 移动平台 / 电梯 / 铁箱 / 巨石都带刚体，也不算；<c>IMovingSurface</c> 与
        /// <c>ISpringSurface</c> 是兜底（约定上它们才代表「会动 / 会弹的支撑面」）。</para>
        /// <para>抽成公开静态函数是为了能在编辑期直接验证白名单，不需要跑 Play。</para>
        /// </remarks>
        public static bool IsStaticTileGround(Collider2D ground)
        {
            if (ground == null) return false;

            // 只认整张瓦片表生成的那个碰撞体；手摆 BoxCollider2D / 单向平台都不算。
            if (!(ground is TilemapCollider2D)) return false;

            // 会动的支撑面带刚体（移动平台 / 电梯 / 回弹块），一律不要。
            if (ground.attachedRigidbody != null) return false;

            // 兜底：有些面可能没有刚体，但按约定实现 IMovingSurface / ISpringSurface。
            if (ground.GetComponentInParent<IMovingSurface>() != null) return false;
            if (ground.GetComponentInParent<ISpringSurface>() != null) return false;

            return true;
        }

        /// <summary>从记录时的身体包围盒往下扫一点，看还是不是静态瓦片地面。</summary>
        private bool HasStaticSupport(Spot spot, LayerMask solidLayer)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(solidLayer);

            Vector2 size = spot.BodyBounds.size;
            size.x = Mathf.Max(0.05f, size.x * 0.9f);
            size.y = Mathf.Max(0.05f, size.y * 0.9f);

            int count = Physics2D.BoxCast(spot.BodyBounds.center, size, 0f, -spot.Up,
                                          filter, _supportHits, SupportProbeDistance);
            for (int i = 0; i < count; i++)
            {
                // 只认法线朝着「上」的接触（墙 / 天花板不算支撑）。
                if (Vector2.Dot(_supportHits[i].normal, spot.Up) <= 0.5f) continue;
                if (!IsStaticTileGround(_supportHits[i].collider)) continue;
                return true;
            }
            return false;
        }

        /// <summary>玩家根节点当前的位置：优先读刚体的物理位置（插值关着，和包围盒一致）。</summary>
        private Vector2 ReadPosition()
        {
            Rigidbody2D body = _body.attachedRigidbody;
            return body != null ? body.position : (Vector2)_body.transform.position;
        }

        /// <summary>按四边各 <paramref name="margin"/> 米外扩包围盒（<c>Bounds.Expand</c> 的语义容易记错，这里写死）。</summary>
        private static Bounds Expand(Bounds bounds, float margin)
        {
            if (margin <= 0f) return bounds;
            Vector3 size = bounds.size;
            bounds.size = new Vector3(size.x + margin * 2f, size.y + margin * 2f, size.z);
            return bounds;
        }
    }
}
