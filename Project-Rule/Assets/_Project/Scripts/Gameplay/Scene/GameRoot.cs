using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.LevelFlow;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 常驻宿主：跨分区存活的系统装配点。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么必须有它</b>：世界是分区流式加载的（<see cref="WorldStreamer"/>），
    /// 分区场景会被反复加载与卸载。玩家 / 相机 / 规则系统 / 进度状态机都不能跟着分区走，
    /// 否则玩家每跨一个分区就被重建一次。</para>
    /// <para><b>它持有什么</b>：<see cref="ILevelFlow"/> · <see cref="WorldStreamer"/> ·
    /// <c>TimeManager</c> · 以及同物体上的 <c>LevelRuntime</c>（Systems / Player / Camera / RuleWheel）。</para>
    /// <para><b>执行顺序</b>：<c>DefaultExecutionOrder(-1000)</c> 保证它早于
    /// <c>RuleSystemDriver</c>（-100）建好 <c>TimeManager</c>，后者只会复用不会重建。</para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class GameRoot : MonoBehaviour
    {
        /// <summary>当前常驻宿主。没有它说明不是从 Boot 场景启动的。</summary>
        public static GameRoot Instance { get; private set; }

        [Tooltip("世界流式加载组件；留空则在本物体上找")]
        [SerializeField] private WorldStreamer _streamer;

        /// <summary>跨分区的进度状态机。</summary>
        public ILevelFlow LevelFlow { get; private set; }

        /// <summary>世界流式加载入口。</summary>
        public WorldStreamer Streamer => _streamer;

        /// <summary>
        /// 世界是否已经开始（玩家点过「开始游戏」/ 自动启动）。
        /// </summary>
        /// <remarks>给「后续才被启用的 UI」判「这是不是一局已经在跑的游戏」——主菜单用它避免
        /// 重复加载 Boot 时在玩到一半弹出来把游戏暂停。</remarks>
        public bool HasStarted => _started;

        private bool _started;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameRoot] 场景里已有常驻宿主，销毁重复的一份。", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_streamer == null) _streamer = GetComponent<WorldStreamer>();

            // 启动场景（Boot）不是世界分区：**必须在 DontDestroyOnLoad 之前**把路径交给流式加载器，
            // 之后 gameObject.scene 就变成 DontDestroyOnLoad、拿不到 Boot 的路径了。
            // 不交的话，分区表里一旦有人把 Boot 登记进去，流式加载就会卸载 Boot（主菜单 / 传送 / 黑场一起没）
            // 再附加加载第二份（第二个主菜单在玩到一半暂停游戏）。
            if (_streamer != null) _streamer.SetBootstrapScene(gameObject.scene.path);

            DontDestroyOnLoad(gameObject);

            // 全局时间权威先立起来。规则系统（LevelRuntime 上）会复用它，
            // 所以分区加载卸载不会把暂停状态 / 倍率冲掉。
            TimeManager.Initialize();

            LevelFlow = new LevelFlow();
            LevelFlow.OnRestartRequested += HandleRestartRequested;

            // 交给基础设施层的静态入口：存档驱动（Persistence 层，看不见 Gameplay）从这里取真流程。
            LevelFlowHub.Set(LevelFlow);

            if (_streamer == null) Debug.LogError("[GameRoot] 缺少 WorldStreamer 组件，世界不会加载。", this);
            // 启动入口尚未放行时，不让 Update 提前加载分区。
            if (_streamer != null) _streamer.enabled = false;

            // TODO(P1 存档)：读档后调用 LevelFlow.SetProgress(current, cleared)，
            // 并让 WorldStreamer 从存档记录的出生点开局。
        }

        /// <summary>开始世界流式加载。由 Boot 场景的 <see cref="BootFlowStarter"/> 调用。</summary>
        public void StartStreaming()
        {
            if (_started) return;
            _started = true;

            if (_streamer != null) _streamer.enabled = true;
            _streamer?.Refresh();

            // 正式开局：把能力锁上、规则只留 Gravity × Revert。
            // 调试场景通常不经过这里，所以那边仍然是全解锁（见 PlayerProgress 的开发期开关）。
            //
            // ⚠️ 续档时**不能**调 StartNewGame：它会把刚读回来的解锁词条清空（存档白读）。
            PlayerProgress progress = GetComponentInChildren<PlayerProgress>(true);
            bool continuing = RuleGame.Infrastructure.Persistence.LoadContext.ContinueGame;
            if (progress != null && !continuing) progress.StartNewGame();

            // 开局即「一关开始」：进度状态机立起来，UI / 存档可以订阅。
            // 分区切换本身不发这个事件 —— 玩家走过边界不该被当成「又开了一关」。
            LevelFlow.StartLevel(LevelFlow.CurrentLevel);
        }

        /// <summary>重开：把在场分区全部卸掉重载。世界回到设计初态靠这个，不靠切场景。</summary>
        private void HandleRestartRequested(int level)
        {
            if (_streamer == null) return;

            _streamer.ReloadAll();
            LevelFlow.StartLevel(level);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;

            if (LevelFlow != null) LevelFlow.OnRestartRequested -= HandleRestartRequested;

            LevelFlowHub.Reset();
            Instance = null;
            TimeManager.Reset();
        }
    }
}
