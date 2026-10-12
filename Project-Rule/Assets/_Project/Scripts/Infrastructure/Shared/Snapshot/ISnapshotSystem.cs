using System;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 快照系统：为「时间恒定」「时间反转」「重开关卡」提供统一的世界状态保存 / 恢复能力（接口文档 §3.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>恢复语义 = 世界回到那一刻</b>：实体位置 / 速度 / 动画 / 状态、规则表（含剩余时间）、
    /// 属性管理器值、对象激活状态全部写回。</para>
    /// <para>⚠️ <b>恢复不触发任何规则系统事件</b>（<c>OnRuleExpired</c> 等与
    /// <c>IRuleTable.RestoreSlots</c> 的契约一致）—— 需要在恢复后收尾的模块订阅 <see cref="OnRestored"/>。</para>
    /// <para>⚠️ <b>对象一律「禁用」而非销毁</b> —— 这是对象激活状态能恢复的前提（架构 D15）。</para>
    /// </remarks>
    public interface ISnapshotSystem
    {
        // ------------------------------------------------------------------
        //  单点快照（时间恒定 / 重开关卡）
        // ------------------------------------------------------------------

        /// <summary>采集当前世界状态。</summary>
        Snapshot Capture();

        /// <summary>把快照内容写回，使世界回到该时刻。写完全部状态后才触发 <see cref="OnRestored"/>。</summary>
        void Restore(Snapshot snap);

        // ------------------------------------------------------------------
        //  环形缓冲（时间反转）
        // ------------------------------------------------------------------

        /// <summary>采集并压入环形缓冲，返回该快照的序号（单调递增，不受裁剪影响）。</summary>
        int CaptureToBuffer();

        /// <summary>只读倒数第二张快照。缓冲不足两张时返回 false。</summary>
        /// <remarks>「只读倒数第二张」是时间反转的回溯目标：保证至少能回到 5 秒前。</remarks>
        bool TryPeekSecondLast(out Snapshot snap);

        /// <summary>丢弃所有比 <paramref name="index"/> 新的快照（回退后未来作废）。</summary>
        void DiscardNewerThan(int index);

        /// <summary>清空环形缓冲。重开关卡 / 规则到期时调用。</summary>
        void ClearBuffer();

        // ------------------------------------------------------------------
        //  事件
        // ------------------------------------------------------------------

        /// <summary>恢复完成后触发（<see cref="Restore"/> 末尾）。订阅者：表现层 · 需要收尾的规则类。</summary>
        event Action OnRestored;
    }
}
