using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 重击：起手 → 生效 → 收招三段 + 独立冷却。
    /// </summary>
    /// <remarks>
    /// <para><b>不进连段</b>：一次性单发，打完就收。所以没有 <see cref="AttackSolver"/> 那套
    /// 「缓冲下一刀」—— 不合法就丢掉。和 <see cref="DashSolver"/> 同一个立场：它是主动选择，
    /// 不必迁就时机。</para>
    /// <para><b>不做命中判定</b>：和 <see cref="AttackSolver"/> 一样只回答「现在该不该打这一下」，
    /// 判定本身留在 <see cref="PlayerController"/>（要用 CombatSystem、位置、朝向）。</para>
    /// <para><b>计时用 <c>fixedDeltaTime</c></b>，暂停时 <c>FixedUpdate</c> 提前返回，
    /// 所以连冷却一起天然冻结（与冲刺、连段同理）。</para>
    /// </remarks>
    public sealed class HeavyAttackSolver
    {
        /// <summary>三段 + 空闲。见 <c>Doc/美术与动画接入方案.md</c> §十。</summary>
        private enum Phase
        {
            None,
            Windup,
            Active,
            Recover,
        }

        private Phase _phase = Phase.None;
        private float _timer;
        private float _cooldownLeft;

        /// <summary>正在挥（起手 / 生效 / 收招都算）。</summary>
        public bool IsBusy => _phase != Phase.None;

        /// <summary>冷却剩余秒数，≤ 0 表示可以起手。</summary>
        public float CooldownLeft => _cooldownLeft > 0f ? _cooldownLeft : 0f;

        /// <summary>打断仍消耗本次重击冷却，不能靠受击反复重开。</summary>
        public void Cancel(MotionTuning tuning)
        {
            if (IsBusy) _cooldownLeft = Mathf.Max(_cooldownLeft, tuning.HeavyCooldown);
            _phase = Phase.None;
            _timer = 0f;
        }

        /// <summary>
        /// 推进一帧并给出结论。
        /// </summary>
        /// <param name="pressed">
        /// 本帧是不是一次**重击意图**（「下 + 攻击」，由 <see cref="IsHeavyIntent"/> 判定）。
        /// ⚠️ 不合法时这里应该传 <c>true</c> 然后**什么都不发生** —— 由调用方保证
        /// 这次按键不会被回退给轻击。
        /// </param>
        /// <param name="canStart">外部条件（见 <see cref="CanStart"/>）。</param>
        public Decision Advance(MotionTuning tuning, bool pressed, bool canStart, float dt)
        {
            var decision = new Decision();

            // 按下先于推进：和 AttackSolver 同一个次序理由 ——
            // 按键发生在视觉帧、推进发生在物理帧，颠倒会让「刚能出招那一帧按下」被吃掉。
            if (pressed) TryBegin(tuning, canStart, ref decision);

            if (_cooldownLeft > 0f) _cooldownLeft -= dt;

            if (_phase != Phase.None)
            {
                _timer -= dt;
                if (_timer <= 0f) AdvancePhase(tuning, ref decision);
            }

            return decision;
        }

        private void TryBegin(MotionTuning tuning, bool canStart, ref Decision decision)
        {
            if (_phase != Phase.None) return;    // 还在挥，接不了
            if (_cooldownLeft > 0f) return;      // 冷却里，丢掉
            if (!canStart) return;               // 空中 / 轻击在跑 / 冲刺中，丢掉

            _phase         = Phase.Windup;
            _timer         = tuning.HeavyWindup;
            decision.Swing = true;
        }

        private void AdvancePhase(MotionTuning tuning, ref Decision decision)
        {
            switch (_phase)
            {
                case Phase.Windup:
                    // 生效段的唯一命中判定：只在开窗那一瞬做一次（和轻击一致）。
                    decision.Strike = true;
                    _phase = Phase.Active;
                    _timer = tuning.HeavyActive;
                    break;

                case Phase.Active:
                    _phase = Phase.Recover;
                    _timer = tuning.HeavyRecover;
                    break;

                case Phase.Recover:
                    _phase        = Phase.None;
                    _cooldownLeft = tuning.HeavyCooldown;   // 冷却从收招结束起算
                    break;
            }
        }

        /// <summary>
        /// 本帧是不是一次重击意图：「下」和攻击键**同一帧**按下。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>「下」按重力方向判，不是屏幕方向</b>：本项目有重力反转规则
        /// （<c>Physics2D.gravity</c> 会被翻），反转之后地面在上面，
        /// 用 <c>move.y &lt; 0</c> 会把「朝向地面的下」判反。所以拿「上」方向点乘。</para>
        /// <para>死区取 <c>0.5</c> 而不是 0 —— 手柄摇杆会有漂移。</para>
        /// </remarks>
        /// <param name="up">当前「上」方向 = 重力反方向，来自 <c>ContactSensor.State.Up</c>。</param>
        public static bool IsHeavyIntent(Vector2 move, bool attackPressed, Vector2 up)
            => attackPressed && Vector2.Dot(move, -up) > 0.5f;

        /// <summary>
        /// 三个外部条件缺一不可：不在地面 / 轻击在跑 / 正在冲刺，都不给放。
        /// </summary>
        /// <remarks>
        /// <para>抽成纯函数是为了可测，与 <see cref="DashSolver.CanDash"/> 同一个理由。</para>
        /// <para>冷却**不在这里**判 —— 它是本类自己的状态，由 <see cref="Advance"/> 内部把关，
        /// 避免同一个条件在两处各判一次。</para>
        /// <para><b>为什么要挡冲刺</b>：两者都是地面合法，冲刺在 ③ 步、重击在 ③′ 步，
        /// 同帧按下时冲刺已经置起 <c>IsDashing</c>。不挡的话动画会被冲刺顶掉，
        /// 重击的伤害却照样落地 —— 变成「打了但看不见」。</para>
        /// </remarks>
        public static bool CanStart(bool lightComboBusy, bool grounded, bool dashing)
            => !lightComboBusy && grounded && !dashing;

        /// <summary>
        /// 本帧的重击结论。
        /// </summary>
        public struct Decision
        {
            /// <summary>本帧起手 → 发 <c>OnHeavyAttacked</c>。</summary>
            public bool Swing;

            /// <summary>本帧是「生效段开窗」那一瞬 → **该做一次命中判定**。</summary>
            public bool Strike;
        }
    }
}
