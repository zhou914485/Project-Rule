using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>AI 只提交移动意图。这里独立处理重力、摩擦、碰撞和插值，不读取玩家输入。</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class EnemyMotor : MonoBehaviour, IKnockbackReceiver, IMomentumSource
    {
        public LayerMask SolidMask = 1 << 8;
        [Tooltip("仅阻挡身体移动，不作为地面或悬崖支撑。默认 Enemy 层，包含 Boss 与召唤小兵。")]
        public LayerMask BodyBlockMask = 1 << 9;
        public Vector2 Intent { get; set; }
        public float SpeedMultiplier { get; set; } = 1f;
        // 仅对已验证落脚点的导航步开放；Actor 每个物理步先清除，攻击/巡逻不会继承。
        public bool AllowLedgeTraversal { get; set; }
        public bool Grounded { get; private set; }
        public Vector2 Up { get; private set; } = Vector2.up;
        public Vector2 Velocity { get; private set; }
        public Vector2 GravityDrift { get; private set; }
        public Vector2 ContactVelocity => Velocity + _impulse;

        /// <summary>
        /// 本物理帧的动量（kg·单位/秒），给「按动量判定」的机关读（见 <see cref="IMomentumSource"/>）。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>必须自报，不能靠刚体</b>：本类把刚体设成 Kinematic、位移走
        /// <c>Rigidbody2D.Slide</c> —— 实测这条路径<b>不会</b>更新 <c>linearVelocity</c>
        /// （模拟一步之后仍是 (0,0)）。机关从刚体上读到的动量永远是 0，表现就是
        /// 「敌人撞上去按钮不响」。真实速度只有本类自己知道。</para>
        /// <para>质量读 <c>Rigidbody2D.mass</c> —— 质量规则写的就是它，
        /// 机关于是不必认识质量规则存在。</para>
        /// </remarks>
        public Vector2 Momentum => _body != null ? Velocity * _body.mass : Vector2.zero;
        public bool MassReduced => _rulesBody != null && _body.mass < _rulesBody.BaseMass * .99f;
        private Vector2 _impulse;
        private Rigidbody2D _body;
        private EnemyActor _actor;
        private RuleAffectedBody _rulesBody;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];

        // Kinematic 自主运动不吃引擎材质阻力，需在真实速度上消费规则。
        public float GroundSpeedScale => 1f / Mathf.Max(1f, RuleSystem.Instance?.Attributes.Values.FrictionScale ?? 1f);
        public float MassResponse => Mathf.Clamp((_rulesBody != null ? _rulesBody.BaseMass : _body.mass)
            / Mathf.Max(.01f, _body.mass), .1f, 4f);
        public float GroundAcceleration => 35f * MassResponse * Mathf.Max(0.02f, RuleSystem.Instance?.Attributes.Values.FrictionScale ?? 1f);
        public float JumpSpeed(float height) => Mathf.Sqrt(2f * Mathf.Max(.01f, Physics2D.gravity.magnitude * _actor.Definition.GravityScale) * Mathf.Max(0f, height));

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _actor = GetComponent<EnemyActor>();
            _rulesBody = GetComponent<RuleAffectedBody>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _body.useFullKinematicContacts = true;
        }

        public void Tick(float dt)
        {
            if (_actor.Definition == null || dt <= 0f) return;
            EnemyDefinition config = _actor.Definition;
            bool flying = _actor.IsFlying;
            if (Physics2D.gravity.sqrMagnitude > 0.001f) Up = -Physics2D.gravity.normalized;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(SolidMask);
            int count = _body.Cast(-Up, filter, _hits, 0.06f);
            Grounded = false;
            for (int i = 0; i < count; i++)
                if (Vector2.Dot(_hits[i].normal, Up) > 0.55f) { Grounded = true; break; }

            // 飞行的操纵速度与规则造成的漂移分开，AI 转向不会每帧抹掉反重力效果。
            Vector2 velocity = Velocity - GravityDrift;
            float massResponse = MassResponse;
            var attributes = RuleSystem.Instance?.Attributes;
            float direction = WorldDirection(Intent.x);
            float acceleration = (flying ? Mathf.Max(35f, config.FlightAcceleration) : 35f) * massResponse;
            if (Grounded && attributes != null)
                acceleration *= Mathf.Max(0f, attributes.Values.FrictionScale);
            velocity.x = Mathf.MoveTowards(velocity.x, direction * config.MoveSpeed * SpeedMultiplier * (Grounded ? GroundSpeedScale : 1f), acceleration * dt);
            if (flying && !_actor.Defeated)
            {
                velocity.y = Mathf.MoveTowards(velocity.y, Intent.y * config.MoveSpeed * SpeedMultiplier,
                    Mathf.Max(1f, config.FlightAcceleration) * massResponse * dt);
                // 正常悬浮的升力抵消 GravityRule 约定的 (0,-9.81)。规则改变重力后保留差值：
                // 增强下坠、削弱/失重上浮、反转向上加速；规则结束后由飞行控制逐渐稳住。
                Vector2 gravityChange = Physics2D.gravity - new Vector2(0f, -9.81f);
                GravityDrift = gravityChange.sqrMagnitude > .001f
                    ? Vector2.ClampMagnitude(GravityDrift + gravityChange * config.GravityScale * dt, 28f)
                    : Vector2.MoveTowards(GravityDrift, Vector2.zero, Mathf.Max(1f, config.FlightAcceleration) * dt);
                velocity += GravityDrift;
            }
            else
            {
                // 飞行结束（例如死亡）时保留原有动量，改由普通重力接管。
                velocity += GravityDrift;
                GravityDrift = Vector2.zero;
                velocity += Physics2D.gravity * config.GravityScale * dt;
                if (Grounded && Vector2.Dot(velocity, Up) < 0f) velocity -= Up * Vector2.Dot(velocity, Up);
                velocity.y = Mathf.Clamp(velocity.y, -28f, 28f);
                // 白盒可直接追击；正式地图缺少支撑面时停止，不盲走下悬崖。
                if (Grounded && !AllowLedgeTraversal && Vector2.Dot(velocity, Up) <= .01f && Mathf.Abs(direction) > 0.01f)
                {
                    Vector2 probe = _body.position - Up * (config.BodySize.y * 0.5f - 0.1f)
                        + Vector2.right * Mathf.Sign(direction) * (config.BodySize.x * 0.5f + 0.15f);
                    if (!Physics2D.Raycast(probe, -Up, 0.45f, SolidMask)) velocity.x = 0f;
                }
            }
            _impulse = Vector2.MoveTowards(_impulse, Vector2.zero, 24f * (Grounded ? Mathf.Max(0f, attributes?.Values.FrictionScale ?? 1f) : 1f) * dt);
            var movement = new Rigidbody2D.SlideMovement
            {
                surfaceUp = Up, surfaceSlideAngle = 60f, gravitySlipAngle = 0f,
                // 只在贴着当前重力方向的支撑面时吸附。默认世界向下的 Anchor 会把反重力实体拉回旧地面。
                surfaceAnchor = !flying && Grounded && Vector2.Dot(velocity, Up) <= .01f
                    ? -Up * .05f : Vector2.zero,
                gravity = Vector2.zero, maxIterations = 6, useLayerMask = true,
                // 玩家身体只参与位移阻挡，不作为接地或悬崖探测的支撑面。
                layerMask = SolidMask | _actor.PlayerMask | BodyBlockMask, useAttachedTriggers = false, useSimulationMove = true
            };
            var slide = _body.Slide(velocity + _impulse, dt, movement);
            if (slide.slideHit.collider != null)
            {
                Vector2 normal = slide.slideHit.normal;
                bool bounced = _actor.Brain?.OnMotorCollision(slide.slideHit.collider, normal, velocity + _impulse) == true;
                if (Vector2.Dot(GravityDrift, normal) < 0f)
                {
                    GravityDrift -= normal * Vector2.Dot(GravityDrift, normal);
                    if (Vector2.Dot(velocity, normal) < 0f) velocity -= normal * Vector2.Dot(velocity, normal);
                }
                if (!bounced && Mathf.Abs(normal.x) > 0.9f && velocity.x * normal.x < 0f) velocity.x = 0f;
                if (!bounced && Vector2.Dot(normal, Up) < -0.5f && Vector2.Dot(velocity, Up) > 0f)
                    velocity -= Up * Vector2.Dot(velocity, Up);
            }
            Velocity = velocity;
        }

        /// <summary>
        /// 本帧提交的意图会被翻成哪个**世界**方向（摩擦反转时是反的）。
        /// </summary>
        /// <remarks>
        /// <para>「摩擦 × 反转」的语义是「按右往左走」：<see cref="Intent"/> 是意图方向，
        /// 实际位移是它的反向。所有**世界坐标**的探测（往前有没有墙、有没有地）都必须用这里
        /// 返回的方向，不能直接用意图方向 —— 否则探的是实体根本不去的哪一侧，
        /// 表现就是「意图一直在推、人却顶在墙上不动」。</para>
        /// <para>与 <c>Tick</c> 里驱动位移用的是同一个判据，抽出来是为了不再有第二份真相。</para>
        /// </remarks>
        public float WorldDirection(float intent)
        {
            if (!Grounded) return intent;

            var attributes = RuleSystem.Instance?.Attributes;
            return attributes != null && attributes.Values.FrictionInverted ? -intent : intent;
        }

        /// <summary>检查真实身体阻挡；不能把摩擦导致的低速误判成撞到了玩家。</summary>
        /// <remarks>
        /// ⚠️ <paramref name="direction"/> 是**意图**方向（AI 想走哪边），不是世界方向 ——
        /// 内部用 <see cref="WorldDirection"/> 换算。摩擦反转时实体实际走反方向，
        /// 直接拿意图方向去探，会把墙探在背后那一侧。
        /// </remarks>
        public bool HasHorizontalObstacle(float direction)
        {
            // Rigidbody.Cast 排除自身；只接受迎面接触，不把脚下地面当成横向墙壁。
            if (HasWallAhead(direction)) return true;
            if (!Grounded) return false;

            float side = Mathf.Sign(WorldDirection(direction));
            EnemyDefinition config = _actor.Definition;
            Vector2 probe = _body.position - Up * (config.BodySize.y * .5f - .1f)
                + Vector2.right * side * (config.BodySize.x * .5f + .15f);
            return !Physics2D.Raycast(probe, -Up, .45f, SolidMask);
        }

        /// <summary>
        /// 只查「正前方有没有**墙**」，**不看脚下有没有地**。
        /// </summary>
        /// <remarks>
        /// 与 <see cref="HasHorizontalObstacle"/> 的区别只有悬崖那一条。被规则**推着走**的时候要用这个：
        /// 「摩擦 × 反转」把敌人逼着后退时，它必须能**被逼下坑** —— 用带悬崖检测的那个版本，
        /// 它会在坑边刹住，玩家永远推不进去。
        /// </remarks>
        public bool HasTerrainWallAhead(float direction) => CastWallAhead(direction, SolidMask | BodyBlockMask);
        public bool HasWallAhead(float direction) => CastWallAhead(direction, SolidMask | BodyBlockMask | _actor.PlayerMask);
        private bool CastWallAhead(float direction, LayerMask mask)
        {
            float side = Mathf.Sign(WorldDirection(direction));
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(mask);
            int count = _body.Cast(Vector2.right * side, filter, _hits, .035f);
            for (int i = 0; i < count; i++)
                if (_hits[i].normal.x * side < -.5f) return true;
            return false;
        }
        public void AddImpulse(Vector2 impulse) => _impulse += impulse / Mathf.Max(0.1f, _body.mass);
        public void Jump(float height)
        {
            if (!Grounded) return;
            float speed = JumpSpeed(height);
            Velocity += Up * (speed - Vector2.Dot(Velocity, Up));
        }
        public void Stop()
        {
            // 普通收步不能凭空吃掉冰面动量；拥抱、引信、死亡等明确锁位仍立即停住。
            bool sliding = Grounded && !_actor.Defeated && _actor.Brain?.MovementEnabled == true
                && (RuleSystem.Instance?.Attributes.Values.FrictionScale ?? 1f) < 1f;
            Intent = Vector2.zero;
            if (!sliding) Velocity = _impulse = GravityDrift = Vector2.zero;
            SpeedMultiplier = 1f; AllowLedgeTraversal = false;
        }
        public Vector2 CaptureImpulse() => _impulse;
        public void Restore(Vector2 velocity, Vector2 impulse, Vector2 gravityDrift = default)
        {
            Velocity = velocity; _impulse = impulse; GravityDrift = gravityDrift;
            // 全局重力随快照恢复后，立即更新派生朝上方向，避免原画和弱点仍用未来时间线的方向。
            if (Physics2D.gravity.sqrMagnitude > .001f) Up = -Physics2D.gravity.normalized;
        }
    }
}
