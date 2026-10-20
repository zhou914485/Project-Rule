using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 世界状态仓库：可交互组件把自己「现在是什么样」写进来，存档整体带走。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不是一个组件列表</b>：世界是分区流式加载的（<see cref="RuleGame.Infrastructure.LevelFlow.PartitionCatalog"/>），
    /// 存档时玩家只站在一个分区里，别的分区根本没加载 —— 枚举「现在场景里的组件」会丢掉
    /// 玩家在别处开过的门。所以状态存在这张全局表里：<b>组件改变时写入</b>，卸载后依然保留。</para>
    /// <para><b>读档顺序</b>：<c>SaveSystem.Apply</c> 先把档里的记录灌回本表，再由各组件在
    /// <c>Start</c> 里自己查表恢复 —— 分区什么时候被加载出来，就什么时候恢复，不要求一次全在场上。</para>
    /// <para>本类<b>不持有任何场景对象引用</b>：只有字符串，跨分区卸载不失效。</para>
    /// </remarks>
    public static class WorldStateStore
    {
        private static readonly Dictionary<string, string> States = new Dictionary<string, string>(128);

        /// <summary>
        /// 存档是否已经灌进来过。
        /// </summary>
        /// <remarks>
        /// <para>组件在 <c>OnEnable</c> / <c>Awake</c> 里经常先写一遍默认值（<c>SetOpen(false)</c>
        /// 之类）。那些写入发生在读档之前，会把存档里的状态冲成默认值。</para>
        /// <para>所以<b>只有 <see cref="MarkLoaded"/> 之后才允许写</b>：<c>SaveSystem.Apply</c>
        /// 结束时打开它。读档之前的写入一律丢弃，组件在 <c>Start</c> 里自己查表恢复。</para>
        /// </remarks>
        public static bool IsLoaded { get; private set; }

        /// <summary>读档流程结束（无论有没有档）：此后组件的写入才生效。</summary>
        public static void MarkLoaded() => IsLoaded = true;

        /// <summary>键的稳定写法：<c>场景名/层级路径#槽位</c>。层级路径带兄弟序号，重名对象也能区分。</summary>
        /// <remarks>组件改名 / 挪父级会让键变化 —— 那时旧状态自然失效，不会串到别的对象上。</remarks>
        public static string KeyOf(Component owner, string slot)
        {
            if (owner == null) return slot;

            var sb = new System.Text.StringBuilder(64);
            sb.Append(owner.gameObject.scene.name).Append('/');
            AppendPath(sb, owner.transform);
            sb.Append('#').Append(slot);
            return sb.ToString();
        }

        private static void AppendPath(System.Text.StringBuilder sb, Transform t)
        {
            if (t.parent != null)
            {
                AppendPath(sb, t.parent);
                sb.Append('/');
            }

            sb.Append(t.name).Append('[').Append(t.GetSiblingIndex()).Append(']');
        }

        /// <summary>写一条状态；值不变时不重复写（写入很频繁，省掉无意义的字典操作）。</summary>
        public static void Set(string key, string value)
        {
            if (!IsLoaded) return;          // 读档前的默认值写入：丢弃，见 IsLoaded 的说明
            if (string.IsNullOrEmpty(key)) return;
            if (States.TryGetValue(key, out string old) && old == value) return;
            States[key] = value;
        }

        /// <summary>读一条状态。没有记录返回 false。</summary>
        public static bool TryGet(string key, out string value)
        {
            value = null;
            return !string.IsNullOrEmpty(key) && States.TryGetValue(key, out value);
        }

        /// <summary>读一个布尔状态；没有记录时返回 <paramref name="fallback"/>。</summary>
        public static bool GetBool(string key, bool fallback = false)
            => TryGet(key, out string v) ? v == "1" : fallback;

        /// <summary>写一个布尔状态。</summary>
        public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

        /// <summary>清空（新游戏 / 读档前）。同时把「已就绪」关掉，等 Apply 结束再打开。</summary>
        public static void Clear()
        {
            States.Clear();
            IsLoaded = false;
        }

        /// <summary>导出成可序列化的列表（存盘用）。</summary>
        public static List<WorldStateRecord> Export()
        {
            var list = new List<WorldStateRecord>(States.Count);
            foreach (KeyValuePair<string, string> kv in States)
                list.Add(new WorldStateRecord { Key = kv.Key, Value = kv.Value });
            return list;
        }

        /// <summary>从存档灌回（读档用）。先清空，避免上一局残留。</summary>
        public static void Import(List<WorldStateRecord> records)
        {
            States.Clear();
            if (records == null) return;

            for (int i = 0; i < records.Count; i++)
            {
                WorldStateRecord r = records[i];
                if (r == null || string.IsNullOrEmpty(r.Key)) continue;
                States[r.Key] = r.Value;
            }
        }

        /// <summary>当前记录条数（验证 / 调试用）。</summary>
        public static int Count => States.Count;
    }
}
