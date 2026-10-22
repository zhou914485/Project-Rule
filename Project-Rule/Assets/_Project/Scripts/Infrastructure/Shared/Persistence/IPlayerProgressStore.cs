using System.Collections.Generic;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 玩家**永久成长**的读写边界：已领取的宝箱编号 + 生命 / 能量的上限增量。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="IRuleUnlockStore"/> 同一条理由：<see cref="SaveData"/> 里得有这几项，
    /// 但存档层不能凭空造一个玩家组件（那是 Gameplay 层的事），所以只定义<b>它读谁、写谁</b>的接缝。
    /// 玩家侧的 <c>PlayerProgress</c> 实现本接口，<c>PersistenceDriver</c> 按接口找它。</para>
    ///
    /// <para><b>为什么上限增量要存</b>：<c>EntityStatus</c> 上的 <c>_healthMax</c> 是预制体里的设计初值，
    /// 宝箱的增量是运行时状态。不落盘的话，读档回来血条分母缩回 100，
    /// 而宝箱又因为「已领取」不再发第二次 —— 玩家永久丢进度。</para>
    ///
    /// <para><b>为什么领取记录也要存</b>：奖励是永久效果，箱子是场景物件。只存上限、不存记录，
    /// 读档后同一个箱子还能再领一次（上限会翻倍叠加）。记录住在这份账本里，
    /// 场景里的箱子靠它在 <c>Start</c> 里把自己画成「已开」。</para>
    ///
    /// <para>🔴 只有小数据（字符串 + 两个浮点），**不含任何 Transform / 物理状态**（约束 C5）。</para>
    /// </remarks>
    public interface IPlayerProgressStore
    {
        /// <summary>已领取的宝箱 / 龙珠编号（<c>CityRewardSource.SourceId</c>）。</summary>
        IReadOnlyCollection<string> ClaimedSourceIds { get; }

        /// <summary>生命上限的累计奖励增量（0 = 没有领过生命宝箱）。</summary>
        float MaxHealthBonus { get; }

        /// <summary>能量上限的累计奖励增量（0 = 没有领过能量宝箱）。</summary>
        float MaxEnergyBonus { get; }

        /// <summary>
        /// 已解锁的能力位（<c>PlayerAbility</c> 的整数位标记，0 = 全锁）。
        /// </summary>
        /// <remarks>用 <c>int</c> 而不是枚举：本接口在 Infrastructure 层，不认识 Gameplay 的 <c>PlayerAbility</c>。</remarks>
        int UnlockedAbilities { get; }

        /// <summary>
        /// 读档后整体替换：领取记录 + 上限增量。
        /// </summary>
        /// <remarks>实现方负责把增量回灌给玩家的 <c>EntityStatus</c>（存的是绝对值，不是叠加量）。</remarks>
        void RestoreProgress(IReadOnlyCollection<string> claimedSourceIds, float healthBonus, float energyBonus);

        /// <summary>读档后整体覆盖能力位（整数位标记；0 = 全锁）。</summary>
        /// <remarks>回灌不发「刚解锁」事件 —— 读档不是获得，不该弹解锁黑幕。</remarks>
        void RestoreAbilities(int unlockedAbilities);
    }
}
