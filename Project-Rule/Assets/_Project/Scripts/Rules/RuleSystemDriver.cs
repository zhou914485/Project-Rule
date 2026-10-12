using UnityEngine;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 规则系统的启动器与心跳。
    /// </summary>
    /// <remarks>
    /// 挂在常驻场景对象上（或做成跨场景的 Prefab）。
    /// 负责两件事：<b>初始化单例</b>、<b>每帧 Tick</b>。
    /// </remarks>
    [DefaultExecutionOrder(-100)]   // 比其他脚本先 Awake
    public class RuleSystemDriver : MonoBehaviour
    {
        [Tooltip("规则目录资产，放在 Assets/_Project/Data/Rules/")]
        [SerializeField] private RuleCatalog _catalog;

        /// <summary>
        /// 设置暂停状态。转发给 <see cref="TimeManager"/>。
        /// </summary>
        /// <remarks>
        /// <para>暂停的<b>唯一真相</b>在 <c>ITimeManager</c>（2026-10-12 决策 3）：
        /// 它同时做两件事 —— <c>Physics2D.simulationMode = Script</c> 停物理步进、
        /// <see cref="ITimeManager.DeltaTime"/> 归零停规则时钟。旧的 <c>IsPaused</c>
        /// 布尔量已删除。</para>
        /// <para>⚠️ <b>过渡期转发器</b>：现有调用方（<c>P3DebugOverlay</c>）仍在调这里。
        /// 拼装界面接线时应直接调 <c>TimeManager.Instance.SetPaused</c>，届时删掉本方法。</para>
        /// </remarks>
        public void SetPaused(bool paused) => TimeManager.Instance?.SetPaused(paused);

        private void Awake()
        {
            // 对象注册表先起来：实体在 OnEnable 里会来找它。
            WorldRegistry.Initialize();

            // 时间管理器在规则系统之前：下面 Update 的规则时钟要读它的 delta。
            TimeManager.Initialize();

            if (_catalog == null)
            {
                Debug.LogError("[RuleSystemDriver] 没有指定 RuleCatalog，规则系统无法初始化。", this);
                return;
            }
            RuleSystem.Initialize(_catalog);

            // 快照系统依赖规则系统与注册表，最后初始化。
            // RuleSystem 初始化失败（没配目录）时跳过，避免半可用的快照系统。
            if (RuleSystem.Instance != null)
                SnapshotSystem.Initialize(RuleSystem.Instance, WorldRegistry.Instance);
        }

        private void Update()
        {
            // 暂停由两层组成（策划案 §3.1，禁止 Time.timeScale = 0）：
            //   ① 物理层：ITimeManager.SetPaused 切 Physics2D.simulationMode = Script
            //   ② 时钟层：DeltaTime 暂停时返回 0 —— 本帧什么都不 Tick，规则时限不再流失
            float dt = TimeManager.Instance != null ? TimeManager.Instance.DeltaTime : Time.deltaTime;
            if (dt <= 0f) return;
            RuleSystem.Instance?.Tick(dt);
        }

        private void OnDestroy()
        {
            // 让下次进 Play / 切场景重新初始化
            RuleSystem.Reset();
            WorldRegistry.Reset();
            TimeManager.Reset();
            SnapshotSystem.Reset();
        }
    }
}
