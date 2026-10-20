using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 身体：**唯一持有速度的地方**。负责位移与碰撞响应（⑧ 击退衰减 · ⑨ Slide · ⑩ 撞墙撞顶），
    /// 以及暂停清零。
    /// </summary>
    /// <remarks>
    /// <para><b>它不决定速度是多少</b> —— 那是 <see cref="VelocitySolver"/> 的返回值，
    /// 经 <see cref="Apply"/> 落到身上。于是「谁写速度」只剩这一个类，
    /// 冲刺结束那条例外也消失了（系数由 <see cref="DashSolver"/> 给、由解算器应用）。</para>
    /// <para><b>Kinematic 刚体的配置也住在这里</b>：位移完全由代码决定，手感可控。
    /// 代价是引擎不再替我们挡住静态地形 —— <c>MovePosition</c> 自己不处理碰撞 ——
    /// 所以位移必须走 <see cref="Rigidbody2D.Slide"/>：它按 <c>surfaceUp</c> 解析接触面角度、
    /// 在设定斜率内沿面滑行、并多次叠代处理拐角。</para>
    /// <para>换个实体复用本类时，这套刚体配置会自动跟着走 —— 这正是把它放在这里的原因。</para>
    /// </remarks>
    public sealed class MotionMover
    {
        private readonly Rigidbody2D _body;
        private readonly Collider2D _bodyCollider;
        private Vector2 _platformDisplacement;
        private Collider2D _platform;

        private Vector2 _velocity;          // 自己维护的速度：重力 / 输入 / 跳跃
        private Vector2 _externalVelocity;  // 击退等外部冲量，独立衰减，不掺进重力与摩擦
        private float   _facing = 1f;
        private bool    _wallSliding;

        public MotionMover(Rigidbody2D body)
        {
            _body = body;
            _bodyCollider = body.GetComponent<Collider2D>();
            ConfigureBody();
        }

        /// <summary>当前速度（自己维护的那个，**不含**击退等外部冲量）。</summary>
        /// <remarks>给表现层用（动画判上升/下降、跑步快慢）。玩法逻辑不要读它做判断 ——
        /// 那样会把「速度怎么算」泄漏到外面。</remarks>
        public Vector2 Velocity => _velocity;

        /// <summary>当前朝向：+1 右，−1 左。</summary>
        public float Facing => _facing;

        /// <summary>是否正在贴墙缓慢下滑。</summary>
        public bool WallSliding => _wallSliding;

        /// <summary>把解算结果落到身上。**每物理帧唯一一次写速度。**</summary>
        public void Apply(VelocitySolver.Result solved)
        {
            _velocity    = solved.Velocity;
            _facing      = solved.Facing;
            _wallSliding = solved.WallSliding;
        }

        /// <summary>⑧⑨⑩ 击退衰减 → 承载 → 位移 → 撞墙撞顶清分量。</summary>
        public void Move(MotionTuning tuning, ContactSensor.State contact, float dt)
        {
            // ⑧ 击退：外部速度独立衰减
            _externalVelocity = Vector2.MoveTowards(_externalVelocity, Vector2.zero,
                                                    tuning.KnockbackDecay * dt);

            Vector2 slideVelocity = _velocity + _externalVelocity;
            bool carrying = contact.Grounded && Vector2.Dot(_velocity + _externalVelocity, contact.Up) <= .01f
                && _platform != null && _platformDisplacement.sqrMagnitude > .000001f;
            if (carrying) slideVelocity += _platformDisplacement / Mathf.Max(.0001f, dt);

            // ⑧′ 承载：脚下是会动的支撑面（滑板车 / 升降台）时，把它这一步要走的位移并进来。
            //
            //    ⚠️ **只加进喂给 Slide 的这一份，绝不写回 `_velocity`。** 两个原因：
            //       ① 平台速度不是玩家自己的速度。写回去的话，从平台上跳下来会带着
            //          平台的水平动量继续滑 —— 而设计要求是「跳离平台就恢复自己的速度」；
            //       ② ⑩ 会按 `remainingVelocity` 清零 `_velocity` 的分量。承载速度留在
            //          `slideVelocity` 里时，被平台顶着撞墙会同时把平台那份清掉，正好是对的；
            //          写进 `_velocity` 就把玩家自己的速度也一起赔进去了。
            //
            //    放在 ⑨′ 之前，是为了让「平台把你顶进墙 / 顶到天花板」也走同一套预夹。
            slideVelocity += contact.CarrierVelocity;

            // ⑨′ 顶墙 / 顶天花板预夹：已知哪个方向贴着墙或天花板，就把「顶进去」的那一份先去掉。
            //
            //    ⚠️ 这一条不是优化，是**必须的兜底**。`Rigidbody2D.Slide` 碰到「过不去」的面时有两种
            //       失败模式，都**不报错、也不在 remainingVelocity 里体现**：
            //         ① 碰到不能沿其滑动的面（surfaceSlideAngle 挡不住的竖墙、或墙下沿拐角这种
            //            斜法线接触）→ 把**整笔位移取消**；
            //         ② 碰到天花板 → 把位移**截到「缝」那么长**。
            //       两种都**照样报 remainingVelocity = (0,0)**，于是 ⑩ 以为「移动成功了」。
            //       ① 的表现是「速度满速、位置不动」（实测：真实左墙下沿 y=1.72、玩家 y=1.4473 时
            //          必然冻结；加叠代次数 8/16/32 无效）；② 的表现是「顶着天花板挂住」，
            //          直到重力把上升速度吃光才掉下来（起跳 16.5 要 0.22 秒，读起来像「粘住」）。
            //
            //    ⚠️ **必须同时改 `_velocity` 本身**，不能只改喂给 Slide 的副本 —— 只改副本的话
            //       剩余速度变成 0，⑩ 反而不清零了，速度会一直留着（第一版就是这么错的）。
            //
            //    被夹掉的都是「本来就过不去」的分量，所以没有副作用；夹完立刻清掉，
            //    手感就是「撞上去、停住、然后该掉就掉」。
            if (contact.WallLeft && slideVelocity.x < 0f)
            {
                slideVelocity.x = 0f;
                _velocity.x    = 0f;
            }
            if (contact.WallRight && slideVelocity.x > 0f)
            {
                slideVelocity.x = 0f;
                _velocity.x    = 0f;
            }
            if (contact.Ceiling)
            {
                float alongUp = Vector2.Dot(slideVelocity, contact.Up);
                if (alongUp > 0f) slideVelocity -= contact.Up * alongUp;

                float riseSpeed = Vector2.Dot(_velocity, contact.Up);
                if (riseSpeed > 0f) _velocity -= contact.Up * riseSpeed;
            }

            bool ignored = carrying && _bodyCollider != null && !Physics2D.GetIgnoreCollision(_bodyCollider, _platform);
            if (ignored) Physics2D.IgnoreCollision(_bodyCollider, _platform, true);
            try
            {
                // 承载位移和自主移动只提交一次 Slide。下降时临时忽略支撑面，其他墙和天花板仍阻挡。
                var result = _body.Slide(slideVelocity, dt, BuildSlideMovement(tuning, contact));
                if (Mathf.Abs(result.remainingVelocity.x) > 0.001f) _velocity.x = 0f;
                if (Mathf.Abs(result.remainingVelocity.y) > 0.001f) _velocity.y = 0f;
            }
            finally
            {
                if (ignored) Physics2D.IgnoreCollision(_bodyCollider, _platform, false);
                _platformDisplacement = Vector2.zero; _platform = null;
            }
        }

        /// <summary>
        /// 外部冲量入口（击退）。见 <c>IKnockbackReceiver</c>。
        /// </summary>
        /// <remarks><b>不能</b>直接写 <c>Rigidbody2D.linearVelocity</c>：本类每个物理帧都会整体
        /// 覆盖速度，那样写进去的冲量活不过一帧。</remarks>
        public void AddImpulse(Vector2 impulse) => _externalVelocity += impulse;

        /// <summary>受击收掉自主动量，保留伤害入口刚施加的真实击退与抗性结果。</summary>
        public void CancelSelfMotion()
        {
            _velocity = Vector2.zero;
            _wallSliding = false;
        }

        /// <summary>短定格不提交移动，也不消耗已保存的击退。</summary>
        public void FreezeStep() => _body.linearVelocity = Vector2.zero;
        /// <summary>由支撑平台在本帧玩家移动前提交。起跳时自动丢弃，不写入玩家自主速度。</summary>
        public void CarryOnPlatform(Vector2 displacement, Collider2D platform)
        {
            _platformDisplacement = displacement; _platform = platform;
        }

        /// <summary>
        /// 暂停：速度清零。
        /// </summary>
        /// <remarks>不清的话重力会在暂停的每一帧继续堆积，恢复的瞬间一次性位移出去。</remarks>
        public void Halt()
        {
            _velocity         = Vector2.zero;
            _externalVelocity = Vector2.zero;
            _platformDisplacement = Vector2.zero; _platform = null;
        }

        /// <summary>Kinematic + 自己解算。gravityScale 置 0：重力在 <see cref="VelocitySolver"/> 里自己积分。</summary>
        private void ConfigureBody()
        {
            _body.bodyType   = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;   // 平台角色不翻滚

            // 插值：物理是 50Hz 定步长，渲染帧率更高。不开插值的话 Transform 只在物理帧更新，
            // 摄像机会很顺、角色却在"跳格子"（横向移动时最明显）。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Kinematic 与静态体 / 推动物之间也要产生接触
            _body.useFullKinematicContacts = true;
        }

        /// <summary>每次物理帧的 Slide 配置。</summary>
        private Rigidbody2D.SlideMovement BuildSlideMovement(MotionTuning tuning, ContactSensor.State contact)
        {
            // 上升途中不加吸附，否则起跳会被按回地面。
            // 向上的外部击退也必须脱离地面，不能被贴地吸附按回去。
            bool anchored = contact.Grounded && Vector2.Dot(_velocity + _externalVelocity, contact.Up) <= 0.01f;

            return new Rigidbody2D.SlideMovement
            {
                surfaceUp         = contact.Up,
                surfaceSlideAngle = tuning.SurfaceSlideAngle,
                gravitySlipAngle  = 0f,                  // 不沿斜面自动下滑
                gravity           = Vector2.zero,        // 重力已在 VelocitySolver 里自己积分
                maxIterations     = 4,                   // 拐角要几次叠代才滑得干净
                surfaceAnchor     = anchored ? -contact.Up * tuning.SurfaceAnchorDistance : Vector2.zero,
                useLayerMask      = true,
                layerMask         = tuning.SolidLayer | tuning.BodyBlockLayer,
                useAttachedTriggers = false,
                useSimulationMove = true,                // 交给模拟步生效：暂停（不 Simulate）时自然静止
            };
        }
    }
}
