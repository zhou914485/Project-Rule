using System;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Infrastructure.Time
{
    /// <summary>
    /// 时间管理器：全局唯一的时间权威，<b>时间机制开关的唯一持有者</b>（接口文档 §3.1）。
    /// </summary>
    /// <remarks>
    /// <para><b>它只负责速率与开关，不负责恒定 / 反转的行为逻辑</b> —— 那是规则系统的事。</para>
    /// <para>暂停<b>不能用 <c>Time.timeScale = 0</c></b>（策划案 §3.1：取消暂停的瞬间会瞬移穿碰撞体），
    /// 正确做法是切换 <c>Physics2D.simulationMode</c>。因此 <c>Time.timeScale</c> 始终保持 1，
    /// <c>Time.deltaTime</c> 在暂停期间照常流逝 —— 需要被暂停冻结的系统（规则时限、冷却等）
    /// 应改读 <see cref="DeltaTime"/>（暂停时返回 0）。</para>
    /// </remarks>
    public interface ITimeManager
    {
        // ------------------------------------------------------------------
        //  查询
        // ------------------------------------------------------------------

        /// <summary>所有时间来源乘法合成后的最终倍率。</summary>
        float TimeScale { get; }

        /// <summary>是否处于暂停（物理模拟已切到手动步进模式）。</summary>
        bool IsPaused { get; }

        /// <summary>
        /// 时间机制是否可用。
        /// </summary>
        /// <remarks>
        /// 供冷却 / Buff / Debuff 读取的<b>唯一来源</b>。
        /// 「时间失效」规则生效时它为 false，<see cref="TimeScale"/> 不变。
        /// </remarks>
        bool TimeMechanicsEnabled { get; }

        /// <summary>
        /// 供规则系统等按帧推进的系统使用的增量时间。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>v3.0 §3.1 未列出该成员</b>：暂停不走 <c>Time.timeScale = 0</c>，
        /// 所以 <c>Time.deltaTime</c> 在暂停期间照常推进，必须有一个「暂停时为 0」的
        /// 增量出口（<c>RuleSystemDriver</c> 的 TODO(P1) 指名要它）。
        /// <b>待团队确认后并入冻结文档。</b></para>
        /// </remarks>
        float DeltaTime { get; }

        // ------------------------------------------------------------------
        //  时间来源（乘法合成，按来源去重）
        // ------------------------------------------------------------------

        /// <summary>设置某个来源的倍率。同一来源重复推送会覆盖（去重），而不是累乘。</summary>
        void PushScale(TimeSource source, float scale);

        /// <summary>移除某个来源的倍率。来源不存在时静默忽略。</summary>
        void RemoveScale(TimeSource source);

        // ------------------------------------------------------------------
        //  暂停
        // ------------------------------------------------------------------

        /// <summary>
        /// 设置暂停状态。
        /// </summary>
        /// <remarks>
        /// 暂停 = <c>Physics2D.simulationMode = Script</c>（停物理步进）；
        /// 恢复 = <c>Physics2D.simulationMode = FixedUpdate</c>。
        /// <see cref="TimeScale"/> 不受影响。
        /// </remarks>
        void SetPaused(bool paused);

        /// <summary>设置时间机制开关（「时间失效」规则使用）。不改 <see cref="TimeScale"/>。</summary>
        void SetTimeMechanicsEnabled(bool enabled);

        // ------------------------------------------------------------------
        //  事件
        // ------------------------------------------------------------------

        /// <summary>合成倍率发生变化后触发（携带新倍率）。订阅者：VFX · 音频。</summary>
        event Action<float> OnTimeScaleChanged;

        /// <summary>暂停状态变化后触发。订阅者：敌人 AI · 玩家控制 · UI。</summary>
        event Action<bool> OnPausedChanged;

        /// <summary>时间机制开关变化后触发。订阅者：冷却 / Buff 系统。</summary>
        event Action<bool> OnTimeMechanicsChanged;
    }
}
