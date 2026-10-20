namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 存档系统（接口文档 §4.4）。
    /// </summary>
    /// <remarks>
    /// <para>四个方法的分工：</para>
    /// <list type="bullet">
    /// <item><term><see cref="Capture"/></term><description>从各运行时系统<b>采集</b>当前进度到内存（不碰磁盘）。</description></item>
    /// <item><term><see cref="Apply"/></term><description>把内存数据<b>写回</b>各运行时系统（重开关卡后恢复世界）。</description></item>
    /// <item><term><see cref="Save"/></term><description>Capture + 序列化写盘（自动存档：每关开头 / 结尾）。</description></item>
    /// <item><term><see cref="Load"/></term><description>读盘反序列化。无档 / 坏档返回 null（= 新游戏），不抛异常。</description></item>
    /// </list>
    /// <para><b>自动存档，不做手动存档</b>（策划案 §十）。单存档位，按关卡覆盖。</para>
    /// </remarks>
    public interface ISaveSystem
    {
        /// <summary>采集当前世界的进度数据（不写盘）。</summary>
        SaveData Capture();

        /// <summary>
        /// 把存档内容重新应用到运行时系统。
        /// </summary>
        /// <remarks>
        /// 流程：进度 → 解锁词条 → 玩家状态 → 跨关卡激活规则（重新 <c>Request</c> 一遍，
        /// 让重力 / 摩擦等引擎效果真正生效）。<b>应用期间不写历史、不触发自动存档</b>，
        /// 否则重放会污染「激活历史」。
        /// </remarks>
        void Apply(SaveData data);

        /// <summary>采集并写入磁盘。</summary>
        void Save();

        /// <summary>从磁盘读取。无存档或存档损坏时返回 null。</summary>
        SaveData Load();
    }
}
