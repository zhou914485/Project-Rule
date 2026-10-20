using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 冲刺：次数（落地回满）、计时、冷却、方向（③）。
    /// </summary>
    /// <remarks>
    /// <para><b>不改速度</b>。速度的接管在 <see cref="VelocitySolver"/>（它只看
    /// <see cref="Decision.Dashing"/> 与 <see cref="Decision.Direction"/>）；
    /// 冲刺**结束**那一帧的收速也由它代劳 —— 这里只置一个标记
    /// <see cref="Decision.JustEnded"/>。于是「谁写速度」这件事只剩一处。</para>
    /// <para><b>不做输入缓冲</b>：冲不了就是冲不了（次数用尽 / 正在冲刺 / 冷却中），
    /// 请求直接丢掉。它是主动选择，不像跳跃那样需要迁就落地时机。</para>
    /// </remarks>
    public sealed class DashSolver
    {
        private float   _dashLeft;          // 冲刺剩余秒数，> 0 表示正在冲刺
        private Vector2 _dashDirection;
        private int     _dashesLeft;
        private float   _cooldownLeft;

        /// <summary>正在冲刺。</summary>
        public bool IsDashing => _dashLeft > 0f;

        /// <summary>还能冲几次。</summary>
        public int DashesLeft => _dashesLeft;

        /// <summary>冲刺方向，只有水平分量。不在冲刺时是上一次的方向，不必读。</summary>
        public Vector2 Direction => _dashDirection;

        /// <summary>
        /// 起手就有冲刺，不必先落地一次。由 <see cref="PlayerController"/> 在 <c>Awake</c> 调用。
        /// </summary>
        public void Reset(MotionTuning tuning) => _dashesLeft = tuning.MaxDashes;

        /// <summary>受击结束冲刺，保留已消耗的次数与冷却。</summary>
        public void Cancel(MotionTuning tuning)
        {
            if (IsDashing) EndDash(tuning);
        }

        /// <summary>
        /// 推进一帧并给出结论。
        /// </summary>
        /// <param name="facing">当前朝向，供「无横向输入时朝当前朝向冲」使用。</param>
        public Decision Advance(MotionTuning tuning, PlayerInput.State input,
                                ContactSensor.State contact, float facing, float dt)
        {
            var decision = new Decision();

            if (_cooldownLeft > 0f) _cooldownLeft -= dt;

            // 落地回满。冲刺**贴地滑行**时也满足这条 —— 所以同一次冲刺期间会被反复回满，
            // 靠「正在冲刺」这个条件挡住重复触发（见 <see cref="CanDash"/>）。
            if (contact.Grounded) _dashesLeft = tuning.MaxDashes;

            if (_dashLeft > 0f)
            {
                _dashLeft -= dt;
                if (_dashLeft <= 0f)
                {
                    EndDash(tuning);
                    decision.JustEnded = true;
                }
            }

            if (input.DashPressed && CanDash(_dashesLeft, _dashLeft, _cooldownLeft))
            {
                _dashDirection = ResolveDashDirection(input.Move, facing);
                _dashLeft      = tuning.DashDuration;
                _dashesLeft    = Mathf.Max(_dashesLeft - 1, 0);
                decision.Started = true;
            }

            decision.Dashing   = IsDashing;
            decision.Direction = _dashDirection;
            return decision;
        }

        /// <summary>冲刺结束：开始冷却。收速由 <see cref="VelocitySolver"/> 按参数里的系数代劳。</summary>
        private void EndDash(MotionTuning tuning)
        {
            _dashLeft     = 0f;
            _cooldownLeft = tuning.DashCooldown;
        }

        /// <summary>
        /// 能不能起冲。<b>三个条件缺一不可</b>：还有次数、没在冲、冷却已过。
        /// </summary>
        /// <remarks>抽成纯函数是为了可测，与 <see cref="JumpSolver.ResolveJumpSpeed"/> 同一个理由。</remarks>
        public static bool CanDash(int dashesLeft, float dashTimeLeft, float cooldownLeft)
            => dashesLeft > 0 && dashTimeLeft <= 0f && cooldownLeft <= 0f;

        /// <summary>
        /// 冲刺方向：**只有左右两向**。有横向输入就按它，没有就朝当前朝向。
        /// </summary>
        /// <remarks>
        /// <para>竖直方向恒为 0 —— 冲刺**不动**垂直速度，那是重力与跳跃的领地。
        /// 所以冲刺期间角色照样会被重力拉下去 / 被跳跃顶上去，冲刺只负责「横着推一把」。</para>
        /// <para>⚠️ 这里读的是**原始输入**，不经过 <see cref="VelocitySolver.ResolveMoveDirection"/> ——
        /// 「摩擦 × 反转」翻的是**地面给的力**，冲刺是自己推自己，不该被翻。
        /// 详见 <c>Doc/玩家动作设计.md</c> §3.4。</para>
        /// </remarks>
        public static Vector2 ResolveDashDirection(Vector2 moveInput, float facing)
        {
            bool pressing = Mathf.Abs(moveInput.x) > 0.01f;
            float sign = pressing ? Mathf.Sign(moveInput.x) : (facing >= 0f ? 1f : -1f);
            return new Vector2(sign, 0f);
        }

        /// <summary>
        /// 本帧的冲刺结论。
        /// </summary>
        public struct Decision
        {
            /// <summary>本帧起冲（次数已扣、方向已定）→ 发 <c>OnDashed</c>。</summary>
            public bool Started;

            /// <summary>本帧处于冲刺中。后续步骤（重力 / 水平 / 跳跃）整块看它。</summary>
            public bool Dashing;

            /// <summary>冲刺方向，只有水平分量。</summary>
            public Vector2 Direction;

            /// <summary>本帧**刚结束**冲刺 → <see cref="VelocitySolver"/> 按 <c>DashExitSpeedScale</c> 收水平速度。</summary>
            /// <remarks>⚠️ 刻意用布尔而不是「收速系数」：系数默认值会是 0，
            /// 任何默认构造的结论都会把水平速度乘成 0。系数从参数里取就没有这个陷阱。</remarks>
            public bool JustEnded;
        }
    }
}
