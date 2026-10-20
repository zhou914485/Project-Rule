using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Travel
{
    /// <summary>
    /// 玩家的**车票**：记着哪些车站已经解锁。解锁状态住在玩家身上，不挂在车站上。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么在玩家身上</b>：与「能力 = 实体自己的序列化字段」同一条原则 ——
    /// 车站是场景物件，换个玩家 / 重开一局就该是另一套进度；解锁属于玩家，不属于地图。
    /// 也顺手避开了「场景卸载后车站被销毁，解锁记录跟着没了」。</para>
    /// <para><b>不写盘</b>：只广播 <see cref="Unlocked"/>。将来的存档只需要存
    /// <see cref="UnlockedIds"/> 这一串编号（约束 C5：不序列化物理状态），
    /// 位置与名字仍由场景里的车站提供。</para>
    /// <para><b>按需创建</b>：本组件由 <see cref="TrainStation"/> / <see cref="TrainTravelService"/>
    /// 在第一次需要时挂到玩家身上（<see cref="Ensure"/>），不用去改共享的 LevelRuntime 预制体。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrainPass : MonoBehaviour
    {
        private readonly HashSet<string> _unlocked = new HashSet<string>();

        /// <summary>刚解锁一座车站（参数是编号）。存档系统落地后接这里。</summary>
        public event Action<string> Unlocked;

        /// <summary>已解锁的车站编号（存档要存的就是这一串）。</summary>
        public IReadOnlyCollection<string> UnlockedIds => _unlocked;

        /// <summary>这座车站解锁了吗。</summary>
        public bool IsUnlocked(string stationId)
            => !string.IsNullOrEmpty(stationId) && _unlocked.Contains(stationId);

        /// <summary>解锁一座车站。<b>返回 false = 之前就解锁过</b>（不会重复广播）。</summary>
        public bool Unlock(string stationId)
        {
            if (string.IsNullOrEmpty(stationId)) return false;
            if (!_unlocked.Add(stationId)) return false;

            Unlocked?.Invoke(stationId);
            SaveUnlocked();
            return true;
        }

        /// <summary>调试 / 重开用：清空所有解锁记录。</summary>
        public void ResetAll()
        {
            _unlocked.Clear();
            SaveUnlocked();
        }

        // ==================================================================
        //  存档：已解锁车站（挂在玩家身上，用固定键，不走场景路径）
        // ==================================================================

        private const string SaveKey = "global/trainpass#unlocked";

        private void Start()
        {
            // 读档恢复：把存档里的车站重新解锁（走 Unlock，订阅方照常收到事件）。
            if (!RuleGame.Infrastructure.Persistence.WorldStateStore.TryGet(SaveKey, out string saved) || string.IsNullOrEmpty(saved)) return;

            string[] ids = saved.Split(',');
            for (int i = 0; i < ids.Length; i++)
            {
                if (!string.IsNullOrEmpty(ids[i])) Unlock(ids[i]);
            }
        }

        private void SaveUnlocked()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string id in _unlocked)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id);
            }
            RuleGame.Infrastructure.Persistence.WorldStateStore.Set(SaveKey, sb.ToString());
        }

        /// <summary>找玩家身上的车票；没有就挂一个。</summary>
        public static TrainPass Ensure(PlayerController player)
        {
            if (player == null) return null;

            TrainPass pass = player.GetComponent<TrainPass>();
            if (pass == null) pass = player.gameObject.AddComponent<TrainPass>();
            return pass;
        }

        /// <summary>场景里找一张车票（没有返回 null，不会创建）。</summary>
        public static TrainPass Find()
            => UnityEngine.Object.FindAnyObjectByType<TrainPass>();
    }
}
