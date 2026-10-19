using UnityEngine;
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
            DontDestroyOnLoad(gameObject);

            // 全局时间权威先立起来。规则系统（LevelRuntime 上）会复用它，
            // 所以分区加载卸载不会把暂停状态 / 倍率冲掉。
            TimeManager.Initialize();

            LevelFlow = new LevelFlow();
            LevelFlow.OnRestartRequested += HandleRestartRequested;

            if (_streamer == null) _streamer = GetComponent<WorldStreamer>();
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

            Instance = null;
            TimeManager.Reset();
        }
    }
}
