using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 攻击连段：起手 → 生效 → 收招三阶段，含连段缓冲（③′）。
    /// </summary>
    /// <remarks>
    /// <para><b>不做命中判定</b>：它只回答「现在该不该打这一下」。判定本身要用
    /// <c>CombatSystem</c>、位置、朝向、攻击盒、能量，那些都留在
    /// <see cref="PlayerController"/> —— 这就是本类的解耦点。</para>
    /// <para><b>计时用 <c>fixedDeltaTime</c></b>，而暂停时 <c>FixedUpdate</c> 提前返回，
    /// 所以连段天然冻结，不需要额外的暂停分支（与冲刺同理）。</para>
    /// <para>⚠️ <b>按下先于推进</b>：原实现里按键发生在视觉帧、计时推进发生在物理帧，
    /// 于是按下总是先于它之后的那次推进。次序反过来会让「收招段最后一帧按下」变成
    /// 收招先过期、再从头起手 —— 连段会断。</para>
    /// </remarks>
    public sealed class AttackSolver
    {
        /// <summary>连段的三个阶段。见 <c>Doc/美术与动画接入方案.md</c> §十。</summary>
        private enum Phase
        {
            None,
            Windup,
            Active,
            Recover,
        }

        private Phase _phase = Phase.None;
        private float _timer;
        private int   _comboIndex = -1;   // 未出招时为 −1，出招后 0、1、2…
        private bool  _buffered;          // 起手 / 生效段按下 → 留到收招段兑现

        /// <summary>当前连段打到第几刀（0 起数）；没在出招时是 −1。</summary>
        public int ComboIndex => _comboIndex;

        /// <summary>受击打断：连段与待兑现的下一刀一起丢弃。</summary>
        public void Cancel()
        {
            _phase = Phase.None;
            _timer = 0f;
            _comboIndex = -1;
            _buffered = false;
        }

        /// <summary>
        /// 推进一帧并给出结论。
        /// </summary>
        /// <param name="pressed">本物理帧是否有攻击键按下（已被 <see cref="PlayerInput.Sample"/> 取走）。</param>
        public Decision Advance(MotionTuning tuning, bool pressed, float dt)
        {
            var decision = new Decision { ComboIndex = -1 };

            if (pressed) Queue(tuning, ref decision);

            if (_phase != Phase.None)
            {
                _timer -= dt;
                if (_timer <= 0f) AdvancePhase(tuning, ref decision);
            }

            decision.ComboIndex = _comboIndex;
            return decision;
        }

        /// <summary>按下攻击键。三种去向：起手 / 接下一刀 / 先存着。</summary>
        private void Queue(MotionTuning tuning, ref Decision decision)
        {
            switch (_phase)
            {
                case Phase.None:
                    // 空着 → 起手
                    BeginSwing(tuning, 0, ref decision);
                    return;

                case Phase.Recover:
                    // 收招段 → 接下一刀（打满了就丢掉这次输入）
                    TryAdvanceCombo(tuning, ref decision);
                    return;

                default:
                    // 起手 / 生效段 → 先存着，等进收招段再兑现。
                    // 不存的话连段就只能「卡在收招那一下」按，手感很差。
                    _buffered = true;
                    return;
            }
        }

        private void AdvancePhase(MotionTuning tuning, ref Decision decision)
        {
            switch (_phase)
            {
                case Phase.Windup:
                    // 生效段的唯一命中判定：**只在开窗那一瞬做一次**，之后是收招 —— 不做持续判定。
                    // 一刀砍两次会把无敌帧和伤害规则都算乱。
                    decision.Strike = true;
                    _phase = Phase.Active;
                    _timer = tuning.AttackActive;
                    break;

                case Phase.Active:
                    _phase = Phase.Recover;
                    _timer = tuning.AttackRecover;

                    // 起手 / 生效段存下的那一下，现在兑现
                    if (_buffered)
                    {
                        _buffered = false;
                        TryAdvanceCombo(tuning, ref decision);
                    }
                    break;

                case Phase.Recover:
                    _comboIndex = -1;
                    _phase      = Phase.None;
                    break;
            }
        }

        /// <summary>能不能接下一刀。打满了返回 false。</summary>
        private bool TryAdvanceCombo(MotionTuning tuning, ref Decision decision)
        {
            if (_comboIndex + 1 >= tuning.MaxCombo) return false;

            BeginSwing(tuning, _comboIndex + 1, ref decision);
            return true;
        }

        private void BeginSwing(MotionTuning tuning, int index, ref Decision decision)
        {
            _comboIndex = index;
            _phase      = Phase.Windup;
            _timer      = tuning.AttackWindup;
            _buffered   = false;
            decision.Swing = true;
        }

        /// <summary>
        /// 本帧的攻击结论。
        /// </summary>
        public struct Decision
        {
            /// <summary>挥出一刀 —— 连段的**第一下和之后每一下**都会置位 → 发 <c>OnAttacked</c>。</summary>
            public bool Swing;

            /// <summary>当前连段打到第几刀；没在出招时 −1。</summary>
            public int ComboIndex;

            /// <summary>本帧是「生效段开窗」那一瞬 → **该做一次命中判定**。</summary>
            public bool Strike;
        }
    }
}
