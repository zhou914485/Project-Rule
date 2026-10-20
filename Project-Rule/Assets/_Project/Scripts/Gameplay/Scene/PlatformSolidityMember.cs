using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 虚实平台成员：挂在平台 A 或 B 上，监听全局 <see cref="PlatformSolidityState"/>，
    /// 在「有实体 + 不透明」和「无碰撞 + 半透明」之间切换。
    /// </summary>
    /// <remarks>
    /// <para><b>角色</b>：A 在全局 = 1 时实体、= 0 时虚化；B 相反。</para>
    ///
    /// <para><b>有实体</b>：把 <see cref="_solidColliders"/> 里的**非 Trigger** 碰撞体
    /// <c>enabled = true</c>，精灵 alpha = <see cref="_solidAlpha"/>（默认 1，不透明）。</para>
    ///
    /// <para><b>虚化</b>：碰撞体 <c>enabled = false</c>，精灵 alpha = <see cref="_ghostAlpha"/>
    /// （默认 0.35，半透明）。人可以直接穿过去。</para>
    ///
    /// <para><b>不卡玩家</b>：从虚化变实体前，先用 <see cref="Physics2D.OverlapBox"/> 查平台范围里
    /// 有没有玩家；有就**先保持虚化**，等玩家离开再变实体（<see cref="IsWaitingForClear"/> 为 true）。
    /// 否则玩家正好站在将要变实体的平台里，Kinematic + <c>Slide</c> 会被卡住 —— 这是本工程反复踩过的坑。
    /// 只处理玩家；箱子 / 敌人由它们自己的物理负责。可以在 Inspector 关掉这个等待。</para>
    ///
    /// <para><b>暂停</b>：不额外判暂停 —— 暂停时玩家不动，等待检查不会改变结果；平台状态也不会自己跳。</para>
    ///
    /// <para><b>引用留空会自动收集</b>：非 Trigger 碰撞体（含子物体）与子物体上的所有 SpriteRenderer。
    /// 生成工具会显式接好一个碰撞体 / 一个白盒精灵 / 一个标签。</para>
    ///
    /// <para><b>编辑模式</b>：不会自动改状态（避免在场景里随手把碰撞体关掉）；Inspector 的
    /// 「按全局状态刷新」/<see cref="RefreshState"/> 可以预览。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlatformSolidityMember : MonoBehaviour
    {
        /// <summary>平台属于 A 还是 B。</summary>
        public enum Role
        {
            /// <summary>全局 = 1 时实体、= 0 时虚化。</summary>
            A = 0,

            /// <summary>全局 = 1 时虚化、= 0 时实体。</summary>
            B = 1,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("角色")]
        [Tooltip("A：全局 = 1 时实体、= 0 时虚化；B 相反。")]
        [SerializeField] private Role _role = Role.A;

        [Header("形态")]
        [Tooltip("有实体时的不透明度。1 = 不透明。")]
        [SerializeField, Range(0f, 1f)] private float _solidAlpha = 1f;

        [Tooltip("无碰撞时的半透明度。默认 0.35。")]
        [SerializeField, Range(0f, 1f)] private float _ghostAlpha = 0.35f;

        [Tooltip("变成实体前检查玩家是否卡在平台范围里：卡住就先保持虚化，等玩家出来再变实体。\n" +
                 "关掉会立刻变实体 —— 玩家如果在里面会被卡住。")]
        [SerializeField] private bool _waitForPlayerClear = true;

        [Header("引用（留空自动收集）")]
        [Tooltip("会跟着「有实体 / 无碰撞」开关的实心碰撞体。\n" +
                 "留空 = 自动收集子物体上所有**非 Trigger** 碰撞体。")]
        [SerializeField] private Collider2D[] _solidColliders;

        [Tooltip("会跟着变透明 / 不透明的精灵。\n" +
                 "留空 = 自动收集子物体上所有 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer[] _visuals;

        [Tooltip("白盒标签：显示 A/B 与当前状态。接正式美术时清空。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>查玩家重叠的缓存，避免每次分配。平台上可能站着箱子 / 敌人，给宽一点。</summary>
        private readonly Collider2D[] _overlapHits = new Collider2D[24];

        // ==================================================================
        //  只读状态（调试用）
        // ==================================================================

        /// <summary>当前角色。</summary>
        public Role PlatformRole => _role;

        /// <summary>按全局参数，这个平台**应该**是实体还是虚化（不含「等玩家让开」）。</summary>
        public bool WantsSolid { get; private set; }

        /// <summary>实际碰撞状态（等玩家让开时会是 false，即使 <see cref="WantsSolid"/> 为 true）。</summary>
        public bool IsSolid { get; private set; }

        /// <summary>该变实体、但玩家还卡在范围里，暂时保持虚化。</summary>
        public bool IsWaitingForClear { get; private set; }

        /// <summary>被开关的实心碰撞体数量（调试 / Inspector 用）。</summary>
        public int SolidColliderCount => _solidColliders != null ? _solidColliders.Length : 0;

        /// <summary>会变透明的精灵数量（调试 / Inspector 用）。</summary>
        public int VisualCount => _visuals != null ? _visuals.Length : 0;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            CollectIfNeeded();

            if (SolidColliderCount == 0)
            {
                Debug.LogWarning("[PlatformSolidityMember] 没有实心碰撞体 —— 这个平台永远不会挡住人。", this);
            }

            if (VisualCount == 0)
            {
                Debug.LogWarning("[PlatformSolidityMember] 没有 SpriteRenderer —— 不会变半透明。", this);
            }

            WarnIfWrongLayer();
        }

        /// <summary>实心碰撞体不在 Ground 层时玩家根本踩不到，提前报一次。</summary>
        private void WarnIfWrongLayer()
        {
            int ground = LayerMask.NameToLayer("Ground");
            if (ground < 0 || SolidColliderCount == 0) return;

            for (int i = 0; i < _solidColliders.Length; i++)
            {
                Collider2D collider = _solidColliders[i];
                if (collider == null || collider.isTrigger) continue;
                if (collider.gameObject.layer == ground) return;
            }

            Debug.LogWarning("[PlatformSolidityMember] 实心碰撞体不在 Ground 层 —— " +
                             "玩家的 Slide / 接地探测可能看不到它。", this);
        }

        private void OnEnable()
        {
            PlatformSolidityState.Changed += HandleChanged;
            RefreshState();
        }

        private void OnDisable()
        {
            PlatformSolidityState.Changed -= HandleChanged;
        }

        private void FixedUpdate()
        {
            // 只有「该变实体但玩家还卡着」时才需要每帧重查；其余状态由全局事件驱动。
            if (!Application.isPlaying || !IsWaitingForClear) return;

            ApplyState();
        }

        private void HandleChanged(bool aIsSolid) => ApplyState();

        /// <summary>按当前全局参数重新算一次（Inspector 的刷新按钮 / 编辑器预览用）。</summary>
        /// <remarks>顺手再收集一次引用 —— 手动加组件时可以先点这个把碰撞体 / 精灵接上。</remarks>
        public void RefreshState()
        {
            CollectIfNeeded();
            ApplyState();
        }

        // ==================================================================
        //  状态
        // ==================================================================

        private void ApplyState()
        {
            bool targetSolid = _role == Role.A
                ? PlatformSolidityState.AIsSolid
                : !PlatformSolidityState.AIsSolid;

            WantsSolid = targetSolid;

            bool solid = targetSolid;
            bool waiting = false;

            if (Application.isPlaying && targetSolid && _waitForPlayerClear && IsPlayerOverlapping())
            {
                solid = false;
                waiting = true;
            }

            IsWaitingForClear = waiting;
            SetSolid(solid);
        }

        private void SetSolid(bool solid)
        {
            IsSolid = solid;

            if (_solidColliders != null)
            {
                for (int i = 0; i < _solidColliders.Length; i++)
                {
                    Collider2D collider = _solidColliders[i];
                    if (collider == null || collider.isTrigger) continue;
                    if (collider.enabled != solid) collider.enabled = solid;
                }
            }

            float alpha = solid ? _solidAlpha : _ghostAlpha;
            if (_visuals != null)
            {
                for (int i = 0; i < _visuals.Length; i++)
                {
                    SpriteRenderer visual = _visuals[i];
                    if (visual == null) continue;

                    Color color = visual.color;
                    if (Mathf.Approximately(color.a, alpha)) continue;

                    color.a = alpha;
                    visual.color = color;
                }
            }

            RefreshLabel();
        }

        private void RefreshLabel()
        {
            if (_label == null) return;

            string role = _role == Role.A ? "A" : "B";
            string state = IsWaitingForClear ? "等待" : IsSolid ? "实体" : "虚化";
            string wanted = role + " · " + state;

            if (_label.text != wanted) _label.text = wanted;
        }

        // ==================================================================
        //  玩家重叠检查
        // ==================================================================

        /// <summary>
        /// 平台范围里有没有**玩家身体**。站在顶面 / 在下方的不算「卡在里面」。
        /// </summary>
        /// <remarks>
        /// <para>用玩家碰撞体的 bounds 判：脚底高过平台顶面（留 5 cm 容差）＝ 站在上面；
        /// 头顶低于平台底面 ＝ 在下面。其余与平台包围盒相交的情况都算卡住。</para>
        /// <para>⚠️ 只处理玩家：箱子 / 敌人有自己的物理，Kinematic 敌人目前没有承载入口，
        /// 不在本组件的保护范围内。</para>
        /// </remarks>
        private bool IsPlayerOverlapping()
        {
            if (!TryGetSolidBounds(out Bounds bounds)) return false;

            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(
                (Vector2)bounds.center, (Vector2)bounds.size, 0f, filter, _overlapHits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _overlapHits[i];
                if (hit == null || !PlayerLookup.IsPlayer(hit)) continue;

                Bounds player = hit.bounds;
                if (player.min.y >= bounds.max.y - 0.05f) continue;   // 站在顶面
                if (player.max.y <= bounds.min.y + 0.05f) continue;   // 在平台下方

                return true;
            }

            return false;
        }

        /// <summary>所有实心碰撞体的并集包围盒。</summary>
        private bool TryGetSolidBounds(out Bounds bounds)
        {
            bounds = new Bounds();

            if (_solidColliders == null) return false;

            bool has = false;
            for (int i = 0; i < _solidColliders.Length; i++)
            {
                Collider2D collider = _solidColliders[i];
                if (collider == null || collider.isTrigger) continue;

                Bounds b = BoundsOf(collider);
                if (!has)
                {
                    bounds = b;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(b);
                }
            }

            return has;
        }

        /// <summary>
        /// 碰撞体的世界包围盒。**BoxCollider2D 直接从 size / offset 算** ——
        /// 碰撞体被关掉（虚化）时也能拿到范围，不依赖 <c>Collider2D.bounds</c> 在禁用状态下的行为。
        /// </summary>
        /// <remarks>平台按轴对齐使用；带旋转的根物体只做近似包围，够用来判玩家重叠。</remarks>
        private static Bounds BoundsOf(Collider2D collider)
        {
            if (collider is BoxCollider2D box)
            {
                Vector3 scale = box.transform.lossyScale;
                var size = new Vector2(
                    Mathf.Abs(box.size.x * scale.x),
                    Mathf.Abs(box.size.y * scale.y));
                var center = (Vector2)box.transform.TransformPoint(box.offset);
                return new Bounds(center, size);
            }

            return collider.bounds;
        }

        // ==================================================================
        //  引用收集
        // ==================================================================

        private void CollectIfNeeded()
        {
            if (_solidColliders == null || _solidColliders.Length == 0)
            {
                Collider2D[] all = GetComponentsInChildren<Collider2D>(true);
                _solidColliders = System.Array.FindAll(all, c => !c.isTrigger);
            }

            if (_visuals == null || _visuals.Length == 0)
            {
                _visuals = GetComponentsInChildren<SpriteRenderer>(true);
            }
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接好角色、碰撞体、白盒精灵与标签。</summary>
        public void Configure(Role role, Collider2D solid, SpriteRenderer visual, TextMesh label)
        {
            _role = role;
            _solidColliders = solid != null ? new[] { solid } : new Collider2D[0];
            _visuals = visual != null ? new[] { visual } : new SpriteRenderer[0];
            _label = label;

            RefreshState();
        }

        private void OnValidate()
        {
            _solidAlpha = Mathf.Clamp01(_solidAlpha);
            _ghostAlpha = Mathf.Clamp01(_ghostAlpha);

            // 编辑模式只收集引用，不自动改状态（否则在场景里选一下就把碰撞体关了）。
            if (!Application.isPlaying) CollectIfNeeded();
        }

        private void OnDrawGizmosSelected()
        {
            if (!TryGetSolidBounds(out Bounds bounds)) return;

            Gizmos.color = _role == Role.A
                ? new Color(0.3f, 0.75f, 1f, 0.9f)
                : new Color(1f, 0.55f, 0.2f, 0.9f);

            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
