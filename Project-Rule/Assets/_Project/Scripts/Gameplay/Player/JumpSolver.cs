using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 跳跃：跳数与土狼时间维护（②）、输入缓冲、三档起跳优先级（⑦）、可变跳跃高度（⑧）。
    /// </summary>
    /// <remarks>
    /// <para><b>不改速度</b>。它只回答「本帧跳了哪一档、用多大速度」，由
    /// <see cref="VelocitySolver"/> 落到速度上。削速同理：削多少要看当时的上升速度，
    /// 所以这里只置一个标记。</para>
    /// <para><b>不改朝向、不发事件</b>：<see cref="Decision.Kind"/> 交回调用方，
    /// 由 <see cref="PlayerController"/> 选发哪个 <c>On*Jumped</c>。</para>
    /// <para>⚠️ 冲刺期间**只推进计数、不消费输入**：跳数与土狼时间必须一直走
    /// （冲刺结束的下一帧就要拿到正确的跳数），而按下的跳跃要**留到冲刺结束那一刻**兑现。
    /// 所以 <see cref="_jumpPressed"/> / <see cref="_jumpCutQueued"/> 是**跨物理帧粘滞**的。</para>
    /// </remarks>
    public sealed class JumpSolver
    {
        /// <summary>本帧跳了哪一档。<see cref="None"/> = 没跳。</summary>
        public enum JumpKind
        {
            None,
            Ground,
            Wall,
            Air,
        }

        private bool  _jumpPressed;      // 粘滞：冲刺期间不消费，留到冲刺结束兑现
        private bool  _jumpCutQueued;    // 粘滞：同上
        private float _bufferLeft;
        private float _coyoteLeft;
        private int   _jumpsUsed;        // 本次落地以来已用掉几段跳

        /// <summary>本次落地以来已用掉几段跳（供 HUD / 调试浮层算剩余）。</summary>
        public int JumpsUsed => _jumpsUsed;

        /// <summary>打断只清输入缓冲，不凭空补回空中跳数。</summary>
        public void ClearBufferedInput()
        {
            _jumpPressed = false;
            _jumpCutQueued = false;
            _bufferLeft = 0f;
        }

        /// <summary>
        /// 推进一帧并给出结论。
        /// </summary>
        /// <param name="dashing">本帧是否处于冲刺中 —— 是则整块跳过（但计数已推进）。</param>
        public Decision Solve(MotionTuning tuning, PlayerInput.State input,
                              ContactSensor.State contact, bool dashing, float dt)
        {
            // 先把输入记成粘滞标记：即使本帧冲刺跳过，也不能丢。
            if (input.JumpPressed)  _jumpPressed   = true;
            if (input.JumpReleased) _jumpCutQueued = true;

            // ② 跳数与土狼时间：与冲刺无关，一直推进。
            if (contact.Grounded)
            {
                _coyoteLeft = tuning.CoyoteTime;
                _jumpsUsed  = 0;                      // 落地回满跳数
            }
            else
            {
                _coyoteLeft -= dt;

                // 土狼窗口一关，第一段跳就当作已用掉 —— 从平台走下来之后只剩空中段。
                // ⚠️ 窗口**内**不受这条影响：那时第一段 + 空中段都还在，
                //    所以「走下来再跳」和「站着跳」拿到的是同样的跳数。
                if (_coyoteLeft <= 0f) _jumpsUsed = Mathf.Max(_jumpsUsed, 1);
            }

            var decision = new Decision { Kind = JumpKind.None };

            // ⑦ 冲刺期间整块不跑：冲刺中按下的跳跃会被**留到冲刺结束那一刻**兑现
            //    （跳跃缓冲通常短于冲刺剩余时间，衔接不上就是正常放弃）。
            if (dashing) return decision;

            // 输入缓冲：落地前提前按也算数
            if (_jumpPressed)
            {
                _bufferLeft  = tuning.JumpBuffer;
                _jumpPressed = false;
            }
            else if (_bufferLeft > 0f)
            {
                _bufferLeft -= dt;
            }

            if (_bufferLeft > 0f) ResolveJump(tuning, input, contact, ref decision);
            ApplyJumpCut(ref decision);

            return decision;
        }

        /// <summary>
        /// 三档优先级判定。**顺序即语义**。
        /// </summary>
        /// <remarks>
        /// 优先级：地面 / 土狼跳 &gt; 蹬墙跳 &gt; 空中跳。
        /// <para>⚠️ 蹬墙必须排在**空中跳之前**：否则贴着墙、跳数还没用完时，
        /// 空中跳会抢先把这一下吃掉，玩家得先把二段跳耗完才蹬得上墙 —— 反直觉。</para>
        /// </remarks>
        private void ResolveJump(MotionTuning tuning, PlayerInput.State input,
                                 ContactSensor.State contact, ref Decision decision)
        {
            int maxJumps = MaxJumps(tuning.MaxAirJumps);

            if (contact.Grounded || _coyoteLeft > 0f)
            {
                decision.Kind         = JumpKind.Ground;
                decision.AlongUpSpeed = tuning.JumpSpeed;
                _bufferLeft = 0f;                 // 一次输入只跳一段
                _coyoteLeft = 0f;                 // 起跳后土狼窗口作废，否则按住方向就能连跳
                _jumpsUsed  = Mathf.Min(_jumpsUsed + 1, maxJumps);
                return;
            }

            // 贴墙能力关掉时这一档直接作废 —— wallPush 恒为 0，下面的分支自然跳过，落回空中段。
            // ⚠️ 用**中段**探针（Climb），不是全高的 Wall —— 矮台 / 台阶不该能蹬。
            float wallPush = tuning.WallClimbEnabled
                ? ResolveWallJumpPush(contact.ClimbLeft, contact.ClimbRight, input.Move.x,
                                      contact.Grounded,
                                      tuning.WallJumpPushSpeed, tuning.WallClimbPushSpeed)
                : 0f;

            if (!Mathf.Approximately(wallPush, 0f))
            {
                decision.Kind         = JumpKind.Wall;
                decision.PushSpeed    = wallPush;
                decision.AlongUpSpeed = tuning.WallJumpUpSpeed;
                _jumpsUsed  = 0;                  // 蹬墙刷新跳数 —— 一面墙就能一路爬上去
                _coyoteLeft = 0f;
                _bufferLeft = 0f;
                return;
            }

            // 走到这里必然离地且土狼窗口已过，所以这个判据只可能给出空中段的速度。
            float airSpeed = ResolveJumpSpeed(contact.Grounded, _coyoteLeft > 0f,
                                              _jumpsUsed, maxJumps,
                                              tuning.JumpSpeed, tuning.AirJumpSpeed);

            // 跳数为 0 时**不清掉缓冲** —— 留到落地那一刻兑现，这就是跳跃缓冲。
            if (airSpeed <= 0f) return;

            decision.Kind         = JumpKind.Air;
            decision.AlongUpSpeed = airSpeed;
            _bufferLeft = 0f;                     // 一次输入只跳一段
            _coyoteLeft = 0f;
            _jumpsUsed  = Mathf.Min(_jumpsUsed + 1, maxJumps);
        }

        /// <summary>⑧ 可变跳跃高度：提前松键就把上升速度削掉，跳得矮一些。</summary>
        /// <remarks>只置标记，由 <see cref="VelocitySolver"/> 按 <c>JumpCutMultiplier</c> 应用 ——
        /// 真正削多少要看当时的上升速度。</remarks>
        private void ApplyJumpCut(ref Decision decision)
        {
            if (!_jumpCutQueued) return;

            decision.JustCut  = true;
            _jumpCutQueued    = false;
        }

        /// <summary>总跳数上限 = 第一段 + 空中段。</summary>
        public static int MaxJumps(int maxAirJumps) => 1 + maxAirJumps;

        /// <summary>
        /// 本帧起跳用哪一档速度。<b>返回 0 表示这一段跳用不了。</b>
        /// </summary>
        /// <param name="grounded">此刻是否踩在支撑面上。</param>
        /// <param name="inCoyoteWindow">是否还在土狼时间内。</param>
        /// <param name="jumpsUsed">本次落地以来已用掉几段跳。</param>
        /// <param name="maxJumps">总跳数上限。</param>
        /// <param name="groundSpeed">第一段的速度。</param>
        /// <param name="airSpeed">空中段的速度。</param>
        /// <remarks>
        /// <para><b>只用跳数决定不了能不能跳</b>：踩在地上时跳数当然是够的，
        /// 而离开地面却还在土狼窗口内的那一下，本来就该和站着跳等价。
        /// 所以判据是「<b>在地面 / 窗口内 → 算第一段</b>，否则 → 扣空中段」。</para>
        /// <para>调用方负责在窗口关闭时把 <c>jumpsUsed</c> 抬到至少 1
        /// （「走下来就等于用掉了第一段」），本函数不管那件事，只做判断。</para>
        /// <para>抽成纯函数是为了可测：真机验证要靠真按键，而失焦的编辑器会重置输入设备；
        /// 手动调 <c>FixedUpdate</c> 又会因为 <c>Rigidbody2D.Slide</c> 在物理步之外调用而卡死。</para>
        /// </remarks>
        public static float ResolveJumpSpeed(bool grounded, bool inCoyoteWindow, int jumpsUsed, int maxJumps,
                                             float groundSpeed, float airSpeed)
        {
            if (grounded || inCoyoteWindow) return groundSpeed;   // 第一段：地面 / 土狼
            return jumpsUsed < maxJumps ? airSpeed : 0f;          // 空中段
        }

        /// <summary>
        /// 能不能蹬墙跳，以及弹开的**水平速度**（带符号）。
        /// </summary>
        /// <param name="wallLeft">左侧贴着竖墙。</param>
        /// <param name="wallRight">右侧贴着竖墙。</param>
        /// <param name="moveInputX">横向输入。</param>
        /// <param name="grounded">是否踩在支撑面上。</param>
        /// <param name="pushSpeed">朝墙外按时的推力 —— 「跳出去」用，要够远。</param>
        /// <param name="climbSpeed">朝墙按（或不按）时的推力 —— 「爬升」用，**要小到方向键能立刻把人拉回墙**。</param>
        /// <returns>水平速度：正 = 往右弹，负 = 往左弹，<b>0 = 不能蹬</b>。</returns>
        /// <remarks>
        /// <para>两档推力是这套动作的核心：</para>
        /// <list type="bullet">
        /// <item><b>朝墙外按</b> → <c>pushSpeed</c>：明确地想离开这面墙（去对面、跳出去）。</item>
        /// <item><b>朝墙按 / 不按</b> → <c>climbSpeed</c>：**仍然是往墙外弹**，但只弹一点点。
        /// 因为你还按着朝墙的方向，空中加速度会立刻把你拉回去 —— 于是「贴墙 → 跳 → 贴回去 → 跳」
        /// 能一路爬上去，<b>一面墙就够</b>。若这里用大推力，人会被推飞、接不上。</item>
        /// </list>
        /// <para>踩在地上时返回 0：那是普通起跳的场合，不该被蹬墙截胡。</para>
        /// <para>抽成纯函数是为了可测，与 <see cref="ResolveJumpSpeed"/> 同一个理由。</para>
        /// </remarks>
        public static float ResolveWallJumpPush(bool wallLeft, bool wallRight, float moveInputX,
                                                bool grounded, float pushSpeed, float climbSpeed)
        {
            if (grounded) return 0f;

            // 先看「明确想离开哪面墙」
            if (wallRight && moveInputX < -0.01f) return -pushSpeed;   // 墙在右、按左 → 往左蹬出去
            if (wallLeft  && moveInputX >  0.01f) return  pushSpeed;   // 墙在左、按右 → 往右蹬出去

            // 否则只是在贴墙爬升：仍然往外弹，但只弹一点点
            if (wallRight) return -climbSpeed;
            if (wallLeft)  return  climbSpeed;

            return 0f;
        }

        /// <summary>
        /// 本帧的跳跃结论。
        /// </summary>
        /// <remarks>
        /// <para><see cref="AlongUpSpeed"/> 与 <see cref="JustCut"/> 可以**同帧都置位**
        /// （极快的点按：同一物理帧内按+松）—— 这是既有行为，别做成互斥。</para>
        /// <para><see cref="Kind"/> 是 <see cref="None"/> 时 <see cref="AlongUpSpeed"/> 为 0，
        /// <see cref="VelocitySolver"/> 据此不动竖直速度。</para>
        /// </remarks>
        public struct Decision
        {
            public JumpKind Kind;

            /// <summary>沿「上」方向的起跳速度。</summary>
            public float AlongUpSpeed;

            /// <summary>带符号的水平推力。仅 <see cref="JumpKind.Wall"/> 非 0。</summary>
            public float PushSpeed;

            /// <summary>松键削速（可变跳跃高度）→ <see cref="VelocitySolver"/> 按 <c>JumpCutMultiplier</c> 应用。</summary>
            /// <remarks>⚠️ 刻意用布尔而不是「削速系数」：系数默认值会是 0，
            /// 任何默认构造的结论都会把竖直速度乘成 0。系数从参数里取就没有这个陷阱。</remarks>
            public bool JustCut;
        }
    }
}
