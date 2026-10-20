using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 不在 <see cref="IRuleAffected"/> 名册里的静态机关（按钮 / 门 / 平台 / 触发器等）
    /// 也能参加世界快照。成员资格 = 生命周期：<c>Awake</c> 登记、<c>OnDestroy</c> 注销。
    /// </summary>
    /// <remarks>
    /// <para>快照名册只收 <see cref="IRuleAffected"/>，那是「有刚体、受规则影响的实体」；
    /// 静态按钮 / 触发区没有刚体，但它们的开合、锁存、消失状态同样是「世界那一刻」的一部分。</para>
    /// <para>本注册表是**显式 opt-in**：只有真的实现了 <see cref="ISnapshotParticipant"/>
    /// 且登记进来的组件才会被采集，不改动已有的实体快照路径。</para>
    /// </remarks>
    public static class SnapshotParticipantRegistry
    {
        private static readonly List<ISnapshotParticipant> Participants = new List<ISnapshotParticipant>();
        private static readonly IReadOnlyList<ISnapshotParticipant> View = Participants.AsReadOnly();

        /// <summary>当前登记的静态参与者（只读视图）。</summary>
        public static IReadOnlyList<ISnapshotParticipant> All => View;

        public static void Register(ISnapshotParticipant participant)
        {
            if (participant == null) return;
            if (Participants.Contains(participant)) return;
            Participants.Add(participant);
        }

        public static void Unregister(ISnapshotParticipant participant)
        {
            if (participant == null) return;
            Participants.Remove(participant);
        }

        /// <summary>进 Play 前清空，避免上一局的静态引用漏到下一局。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Participants.Clear();

        /// <summary>剔除已经被销毁的参与者；采集前调用。</summary>
        public static void PruneDestroyed()
        {
            for (int i = Participants.Count - 1; i >= 0; i--)
            {
                if (Participants[i] is Object obj && obj == null)
                    Participants.RemoveAt(i);
            }
        }
    }
}
