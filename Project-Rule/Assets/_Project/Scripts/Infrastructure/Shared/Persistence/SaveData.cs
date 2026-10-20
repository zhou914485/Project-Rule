using System;
using System.Collections.Generic;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 存档的全部内容（接口文档 §4.4，策划案 §十）。
    /// </summary>
    /// <remarks>
    /// <para><b>只持久化小数据</b>：已解锁词条、跨关卡激活规则、激活历史、关卡进度、玩家数值。</para>
    /// <para>🔴 <b>绝不包含任何 Transform / Rigidbody / 物理状态</b>（约束 C5）。
    /// 重开关卡时世界回到设计初态，再把 <see cref="ActiveRuleIds"/> 重新应用一遍 ——
    /// 天然避免「存档被玩坏」。</para>
    /// </remarks>
    [Serializable]
    public class SaveData
    {
        /// <summary>
        /// 存档格式版本。当前为 1。
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>v3.0 §4.4 未列出该字段</b>：字段增删迟早发生（删属性 / 加系统），
        /// 没有版本号就只能靠「字段缺失给默认值」瞎猜。读取旧档时按版本迁移。
        /// <b>待团队确认后并入冻结文档。</b>
        /// </remarks>
        public int Version = CurrentVersion;

        /// <summary>当前存档格式版本。结构不兼容地变更时 +1，并在 SaveSystem 里写迁移。</summary>
        public const int CurrentVersion = 1;

        /// <summary>已解锁的属性词条（枚举名，如 "Gravity"）。</summary>
        public List<string> UnlockedAttributes = new List<string>();

        /// <summary>已解锁的关系词条（枚举名，如 "Revert"）。</summary>
        public List<string> UnlockedRelations = new List<string>();

        /// <summary>已领取的宝箱 / 龙珠编号（<c>CityRewardSource.SourceId</c>）。</summary>
        /// <remarks>奖励是永久的，箱子是场景物件：不存这一串，读档后同一个箱子能再领一次。
        /// 场景里的箱子也靠它把自己画回「已开」。</remarks>
        public List<string> ClaimedSourceIds = new List<string>();

        /// <summary>生命上限的累计奖励增量（宝箱）。0 = 没领过。</summary>
        /// <remarks>存在玩家账本里而不是 <see cref="EntityStatusData"/>：后者是「那一刻的血条」，
        /// 每次回溯都会整体覆盖，而成长必须跨回溯保留。</remarks>
        public float MaxHealthBonus;

        /// <summary>能量上限的累计奖励增量（宝箱）。0 = 没领过。</summary>
        public float MaxEnergyBonus;

        /// <summary>跨关卡仍激活的规则 Id（如 "gravity.revert"）。重开关卡后重新应用。</summary>
        public List<string> ActiveRuleIds = new List<string>();

        /// <summary>规则激活历史，供 NPC 回顾式台词引用。</summary>
        public List<RuleEventRecord> History = new List<RuleEventRecord>();

        /// <summary>当前关卡号。</summary>
        public int CurrentLevel = 1;

        /// <summary>已通关的关卡号列表。</summary>
        public List<int> ClearedLevels = new List<int>();

        /// <summary>玩家的能量 / 生命 / 持有物。无玩家实体时为 null。</summary>
        public EntityStatusData PlayerStatus;

        /// <summary>最近一次存档点的编号（<c>PlayerSpawnPoint.Id</c>）。位置由场景里的点提供，只存编号。</summary>
        public string RespawnId;

        /// <summary>
        /// 存档点所在的分区场景路径（如 <c>Assets/_Project/Scenes/Start/Level_Center.unity</c>）。
        /// </summary>
        /// <remarks>续档时要先把这个分区加载出来，那个 <c>PlayerSpawnPoint</c> 才存在。
        /// 只存路径，仍然不碰坐标（约束 C5）。</remarks>
        public string RespawnScene;

        /// <summary>
        /// 世界状态：可交互场景组件 / 敌人的状态（开关按下 / 门碎掉 / 剑拔出 / 敌人已死 …）。
        /// </summary>
        /// <remarks>⚠️ 只是键值对，<b>不含任何 Transform / 物理状态</b>（约束 C5）。键的写法见
        /// <see cref="WorldStateStore.KeyOf"/>。</remarks>
        public List<WorldStateRecord> WorldStates = new List<WorldStateRecord>();
    }
}
