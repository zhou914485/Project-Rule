using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.LevelFlow;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Persistence
{
    /// <summary>
    /// 存档系统的场景引导：装配 <see cref="SaveSystem"/>、启动时读档恢复、持有关卡流程。
    /// </summary>
    /// <remarks>
    /// <para>挂在常驻场景对象上（与 <c>RuleSystemDriver</c> 同一物体或并列均可），
    /// 执行顺序 -90，保证规则系统（-100）先就绪。</para>
    /// <para><b>LevelFlow 的当前所有者</b>：场景系统尚未落地，关卡流程纯 C# 实例
    /// 由本驱动创建并通过 <see cref="Flow"/> 暴露；终点触发器 / 场景系统接入前，
    /// 外部通过 <c>PersistenceDriver.Instance.Flow</c> 调用
    /// <c>NotifyGoalReached()</c> 等。场景系统落地后所有权移交，存档改为订阅外部实例。</para>
    /// </remarks>
    [DefaultExecutionOrder(-90)]
    public class PersistenceDriver : MonoBehaviour
    {
        /// <summary>当前场景的引导实例。由 Awake 设置、OnDestroy 清空。</summary>
        public static PersistenceDriver Instance { get; private set; }

        // ------------------------------------------------------------------
        //  可选接线（Unity 不能序列化接口字段，用 MonoBehaviour 承接后 As 转换）
        // ------------------------------------------------------------------

        [Tooltip("玩家的 EntityStatus 组件。不接则存档不保存玩家数值（测试 / 主菜单场景）。")]
        [SerializeField] private MonoBehaviour _playerStatusBehaviour;

        [Tooltip("玩家解锁词条存储（实现 IRuleUnlockStore 的组件）。解锁系统落地前留空。")]
        [SerializeField] private MonoBehaviour _unlockStoreBehaviour;

        /// <summary>关卡流程状态机。当前由本驱动持有，见类注释。</summary>
        public LevelFlow Flow { get; private set; }

        /// <summary>存档系统实例（同时是 NPC 台词的 IDialogueHistorySource）。</summary>
        public SaveSystem Save { get; private set; }

        private void Awake()
        {
            IRuleSystem rules = RuleSystem.Instance;
            if (rules == null)
            {
                Debug.LogError("[PersistenceDriver] 规则系统尚未初始化（RuleSystemDriver 应先 Awake），存档系统不启动。", this);
                return;
            }

            var player = _playerStatusBehaviour as IEntityStatus;
            if (_playerStatusBehaviour != null && player == null)
                Debug.LogWarning("[PersistenceDriver] 配置的玩家状态对象未实现 IEntityStatus，按未接线处理。", this);

            var unlocks = _unlockStoreBehaviour as IRuleUnlockStore;
            if (_unlockStoreBehaviour != null && unlocks == null)
                Debug.LogWarning("[PersistenceDriver] 配置的解锁存储对象未实现 IRuleUnlockStore，按未接线处理。", this);

            Flow = new LevelFlow();
            Save = new SaveSystem(rules, Flow, new FileSaveStorage(), player, unlocks);
            Instance = this;

            // 启动即读档恢复。无档返回 null，Apply(null) = 干净初态（世界保持设计初态）。
            // 这一步会抑制自动存档，避免加载过程中的关卡事件把半成品写回磁盘。
            SaveData data = Save.Load();
            Save.Apply(data);

            if (data == null) Debug.Log("[PersistenceDriver] 未找到存档，以新游戏启动。");
            else Debug.Log($"[PersistenceDriver] 存档已恢复：第 {data.CurrentLevel} 关，" +
                           $"激活规则 {data.ActiveRuleIds?.Count ?? 0} 条，历史 {data.History?.Count ?? 0} 条。");
        }

        private void OnDestroy()
        {
            Save?.Dispose();
            Save = null;
            Flow = null;
            if (Instance == this) Instance = null;
        }
    }
}
