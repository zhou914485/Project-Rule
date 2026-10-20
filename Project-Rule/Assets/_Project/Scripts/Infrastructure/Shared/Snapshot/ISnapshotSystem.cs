using System;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 快照系统：为「时间恒定」「时间反转」提供统一的世界状态保存 / 恢复能力（接口文档 §3.2）。
    /// </summary>
    /// <remarks>
    /// <para><b>恢复语义 = 世界回到那一刻</b>：实体位置 / 速度 / 动画 / 状态 / 激活状态、
    /// 规则表（含剩余时间）、属性管理器值全部写回，随后<b>重建规则效果</b>
    /// （<c>IRuleSystem.ReapplyActiveEffects()</c>）—— 因为恢复不触发任何规则事件，
    /// 重力这类写在引擎上的效果必须在这里补回来。</para>
    /// <para>⚠️ <b>恢复不触发任何规则系统事件</b>（<c>OnRuleExpired</c> 等与
    /// <c>IRuleTable.RestoreSlots</c> 的契约一致）—— 需要在恢复后收尾的模块订阅 <see cref="OnRestored"/>。</para>
    /// <para>⚠️ <b>对象一律「禁用」而非销毁</b> —— 这是激活状态能恢复的前提（架构 D15）。</para>
    /// <para>⚠️ <b>重开关卡不走快照</b>：重开 = 场景重载 + 存档重放规则（架构 §6.1，2026-10-14 定）。</para>
    /// </remarks>
    public interface ISnapshotSystem
    {
        // ------------------------------------------------------------------
        //  单点快照
        // ------------------------------------------------------------------

        /// <summary>采集当前世界状态。采集范围 = 快照名册（含禁用中的对象）。</summary>
        Snapshot Capture();

        /// <summary>
        /// 把快照内容写回，使世界回到该时刻。写完全部状态、并重建规则效果后才触发 <see cref="OnRestored"/>。
        /// </summary>
        /// <param name="mode">
        /// 恢复范围。默认 <see cref="RestoreMode.Full"/>（关卡存档点）；
        /// 时间系能力传 <see cref="RestoreMode.KeepEnergy"/> —— 回溯不退能量。
        /// </param>
        void Restore(Snapshot snap, RestoreMode mode = RestoreMode.Full);

        // ------------------------------------------------------------------
        //  环形缓冲（时间反转）
        // ------------------------------------------------------------------

        /// <summary>采集并压入环形缓冲，返回该快照的序号（单调递增，不受裁剪影响）。</summary>
        int CaptureToBuffer();

        /// <summary>只读倒数第二张快照。缓冲不足两张时返回 false。</summary>
        /// <remarks>「只读倒数第二张」是时间反转的首选回溯目标：保证至少能回到 5 秒前。</remarks>
        bool TryPeekSecondLast(out Snapshot snap);

        /// <summary>
        /// 只读最旧的一张快照。缓冲为空时返回 false。
        /// </summary>
        /// <remarks>
        /// <b>反转的兜底</b>：缓冲不足两张时退到这一张 —— 开局第一帧垫的那张至少保证
        /// 有一次回溯可用，否则开局前 5 秒内反转永远是 <c>NoResource</c>。
        /// </remarks>
        bool TryPeekOldest(out Snapshot snap);

        /// <summary>丢弃所有比 <paramref name="index"/> 新的快照（回退后未来作废）。</summary>
        void DiscardNewerThan(int index);

        /// <summary>清空环形缓冲。重开关卡 / 规则到期时调用。</summary>
        void ClearBuffer();

        // ------------------------------------------------------------------
        //  定点快照（时间恒定）
        // ------------------------------------------------------------------

        /// <summary>
        /// 采集并固定一张快照，返回句柄（&gt; 0）。
        /// </summary>
        /// <remarks>
        /// <para><b>何时 Pin 由规则类决定，存储归快照系统</b>（2026-10-14 定）——
        /// 规则类不再自己持有 <see cref="Snapshot"/>。</para>
        /// <para>定点槽<b>不参与快照内容</b>，所以一次环形缓冲回溯不会把它冲掉。</para>
        /// </remarks>
        int Pin();

        /// <summary>恢复到定点快照。句柄不匹配或没有定点时返回 false。</summary>
        bool TryRestorePinned(int handle, RestoreMode mode = RestoreMode.Full);

        /// <summary>解除定点。句柄不匹配时静默忽略。</summary>
        void Unpin(int handle);

        // ------------------------------------------------------------------
        //  存档点快照（暂停菜单「回到上一个存档点」）
        // ------------------------------------------------------------------

        /// <summary>是否持有一张存档点快照。</summary>
        bool HasCheckpointSnapshot { get; }

        /// <summary>
        /// 采集并固定一张「存档点」快照（覆盖上一张）。由存档驱动在落盘之后调用。
        /// </summary>
        /// <remarks>
        /// <para>与时间恒定的 <see cref="Pin"/> <b>各占一个槽位</b> —— 时间规则 Pin 的时候不能把存档点快照挤掉。</para>
        /// <para>采集范围仍是快照名册（当前已加载的分区）。跨分区的世界不在里面，所以回滚前
        /// 要确认存档点所在分区还在场，不在场就退回读档（调用方负责判断）。</para>
        /// </remarks>
        bool CaptureCheckpointSnapshot();

        /// <summary>回到存档点快照。没有快照时返回 false（调用方退回读盘）。</summary>
        bool TryRestoreCheckpointSnapshot(RestoreMode mode = RestoreMode.Full);

        /// <summary>丢弃存档点快照（新开一局 / 回到主菜单时）。</summary>
        void ClearCheckpointSnapshot();

        // ------------------------------------------------------------------
        //  事件
        // ------------------------------------------------------------------

        /// <summary>恢复完成后触发（<see cref="Restore"/> 末尾）。订阅者：表现层 · 需要收尾的规则类。</summary>
        event Action OnRestored;
    }
}
