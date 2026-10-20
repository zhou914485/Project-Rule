using System;
using System.Collections;
using System.Collections.Generic;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Interaction;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Persistence;
using RuleGame.UI.Launch;
using RuleGame.UI.RuleWheel;
using RuleGame.UI.Train;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.UI.Pause
{
    /// <summary>
    /// ESC 暂停菜单：继续 / 回到上一个存档点 / 回到主菜单。
    /// </summary>
    /// <remarks>
    /// <para><b>ESC 是「逐层返回」，不是「到处都能开暂停」</b>：本组件执行序 <c>-500</c>，
    /// 早于规则轮盘 / 火车面板 / 对话这些默认序（0）的层 —— 它先看一眼「这一帧 ESC 归谁」，
    /// 有人开着就直接让路，自己去处理关闭。反过来（跑在它们后面）就会出现「关掉轮盘的同一次 ESC
    /// 又弹出暂停菜单」。</para>
    /// <para><b>暂停只走 TimeManager</b>（停物理步进 + 停规则时钟），绝不碰 <c>Time.timeScale</c>；
    /// 进入时先记旧值、退出时恢复，避免把轮盘 / 存档过场的暂停态一起放掉。</para>
    /// <para><b>危险动作默认焦点在「取消」</b>：连按回车永远先取消。</para>
    /// </remarks>
    [DefaultExecutionOrder(-500)]
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class PauseMenuPresenter : MonoBehaviour
    {
        private enum ConfirmKind
        {
            None,
            Checkpoint,
            MainMenu,
        }

        [Tooltip("玩家的 PlayerController；留空则运行时自动找。")]
        [SerializeField] private PlayerController _player;

        [Tooltip("主菜单（回到主菜单时把它叫回来）；留空则运行时自动找。")]
        [SerializeField] private MainMenuPresenter _mainMenu;

        [Tooltip("黑场实现（Presentation 里的 SwingCheckpointFadeScreen）。留空则运行时自己找；没有黑场也能回档。")]
        [SerializeField] private MonoBehaviour _fadeBehaviour;

        private VisualElement _viewport;
        private VisualElement _artboard;
        private VisualElement _shade;
        private VisualElement _mainPanel;
        private VisualElement _confirmPanel;
        private VisualElement _saveCard;
        private Button _continueButton;
        private Button _checkpointButton;
        private Button _mainMenuButton;
        private Button _cancelButton;
        private Button _confirmButton;
        private Label _checkpointDesc;
        private Label _confirmEyebrow;
        private Label _confirmTitle;
        private Label _confirmCopy;
        private Label _saveTitle;
        private Label _saveMeta;

        private readonly List<Button> _buttons = new List<Button>();

        private EntityStatus _status;
        private EntityDamageable _damageable;
        private RuleWheelPresenter _wheel;
        private TrainTravelPanel _train;
        private AbilityUnlockOverlay _abilityScreen;
        private IDisposable _inputLease;
        private ConfirmKind _confirmKind;
        private Button _focusReturn;
        private bool _ownsPause;
        private bool _pausedBefore;
        private bool _statusPausedBefore;
        private bool _busy;
        private bool _bound;
        private IScreenFade _fade;

        /// <summary>面板是否开着。</summary>
        public bool IsOpen => _viewport != null && !_viewport.ClassListContains("pause-hidden");

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void OnEnable()
        {
            Bind();
        }

        /// <summary>
        /// 取元素 + 挂回调。放在可重入的方法里：UIDocument 与本组件同物体时，
        /// OnEnable 的先后不保证，rootVisualElement 可能还没建好（那就等 Start 再取一次）。
        /// </summary>
        private void Bind()
        {
            if (_bound) return;

            UIDocument document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null) return;

            _bound = true;
            _viewport = root.Q("pauseViewport");
            _artboard = root.Q("pauseArtboard");
            _shade = root.Q("pauseShade");
            _mainPanel = root.Q("pausePanel");
            _confirmPanel = root.Q("confirmPanel");
            _saveCard = root.Q("saveCard");

            _continueButton = root.Q<Button>("continueButton");
            _checkpointButton = root.Q<Button>("checkpointButton");
            _mainMenuButton = root.Q<Button>("mainMenuButton");
            _cancelButton = root.Q<Button>("cancelButton");
            _confirmButton = root.Q<Button>("confirmButton");

            _checkpointDesc = root.Q<Label>("checkpointDesc");
            _confirmEyebrow = root.Q<Label>("confirmEyebrow");
            _confirmTitle = root.Q<Label>("confirmTitle");
            _confirmCopy = root.Q<Label>("confirmCopy");
            _saveTitle = root.Q<Label>("saveTitle");
            _saveMeta = root.Q<Label>("saveMeta");

            if (_viewport == null || _continueButton == null || _confirmPanel == null)
            {
                Debug.LogError("[PauseMenu] PauseMenu.uxml 缺少必需元素（pauseViewport / continueButton / confirmPanel）。", this);
                enabled = false;
                return;
            }

            _continueButton.clicked += Close;
            _checkpointButton.clicked += ShowCheckpointConfirm;
            _mainMenuButton.clicked += ShowMainMenuConfirm;
            _cancelButton.clicked += CancelConfirm;
            _confirmButton.clicked += AcceptConfirm;

            RegisterFocus(_continueButton);
            RegisterFocus(_checkpointButton);
            RegisterFocus(_mainMenuButton);
            RegisterFocus(_cancelButton);
            RegisterFocus(_confirmButton);

            _viewport.RegisterCallback<GeometryChangedEvent>(ViewportChanged);
            _viewport.EnableInClassList("pause-hidden", true);
        }

        private void Start()
        {
            Bind();

            if (_player == null) _player = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (_mainMenu == null) _mainMenu = FindAnyObjectByType<MainMenuPresenter>(FindObjectsInactive.Include);
            _fade = _fadeBehaviour as IScreenFade;
            if (_fade == null) _fade = FindScreenFade();
            if (_player != null)
            {
                _status = _player.GetComponent<EntityStatus>();
                _damageable = _player.GetComponent<EntityDamageable>();
            }
        }

        private void OnDisable()
        {
            ReleasePause();
            _bound = false;

            if (_continueButton != null) _continueButton.clicked -= Close;
            if (_checkpointButton != null) _checkpointButton.clicked -= ShowCheckpointConfirm;
            if (_mainMenuButton != null) _mainMenuButton.clicked -= ShowMainMenuConfirm;
            if (_cancelButton != null) _cancelButton.clicked -= CancelConfirm;
            if (_confirmButton != null) _confirmButton.clicked -= AcceptConfirm;

            for (int i = 0; i < _buttons.Count; i++) UnregisterFocus(_buttons[i]);
            _buttons.Clear();

            _viewport?.UnregisterCallback<GeometryChangedEvent>(ViewportChanged);
        }

        // ==================================================================
        //  输入
        // ==================================================================

        private void Update()
        {
            if (_viewport == null || _busy) return;
            if (Keyboard.current == null && Gamepad.current == null) return;

            bool pressed = Keyboard.current?.escapeKey.wasPressedThisFrame == true
                || Gamepad.current?.startButton.wasPressedThisFrame == true;
            if (!pressed) return;

            // 别的层开着：这一帧的 ESC 归它们，本组件让路（它们会自己关）。
            if (AnyOverlayOpen()) return;

            if (IsOpen)
            {
                if (_confirmKind == ConfirmKind.None) Close();
                else CancelConfirm();
                return;
            }

            if (!CanOpen()) return;
            Open();
        }

        /// <summary>先看能不能开：主菜单阶段 / 死亡中 / 世界没跑起来都不开。</summary>
        private bool CanOpen()
        {
            if (GameRoot.Instance == null || !GameRoot.Instance.HasStarted) return false;
            if (_damageable != null && _damageable.IsDead) return false;
            return true;
        }

        /// <summary>
        /// 这一帧是否被别的 UI 层占着。**只查，不关** —— 关由它们自己处理。
        /// </summary>
        private bool AnyOverlayOpen()
        {
            DialogueHost host = DialogueHost.Active;
            if (host != null && host.IsPlaying) return true;

            if (_wheel == null) _wheel = FindAnyObjectByType<RuleWheelPresenter>();
            if (_wheel != null && _wheel.IsOpen) return true;

            if (_train == null) _train = FindAnyObjectByType<TrainTravelPanel>();
            if (_train != null && _train.IsVisible) return true;

            // 解锁黑幕自己拿着暂停，而且它把 ESC 当「继续」消费：
            // 这里不让路的话，同一次 ESC 会先弹出暂停菜单、再把黑幕关掉。
            if (_abilityScreen == null) _abilityScreen = FindAnyObjectByType<AbilityUnlockOverlay>();
            if (_abilityScreen != null && _abilityScreen.IsShowing) return true;

            return false;
        }

        // ==================================================================
        //  开 / 关
        // ==================================================================

        /// <summary>打开暂停菜单：暂停两层 + 收走玩法输入 + 刷新存档点信息。</summary>
        public void Open()
        {
            if (IsOpen) return;

            _status ??= _player != null ? _player.GetComponent<EntityStatus>() : null;
            _damageable ??= _player != null ? _player.GetComponent<EntityDamageable>() : null;

            if (TimeManager.Instance != null)
            {
                _pausedBefore = TimeManager.Instance.IsPaused;
                TimeManager.Instance.SetPaused(true);
            }

            if (_status != null)
            {
                _statusPausedBefore = _status.IsPaused;
                _status.SetPaused(true);
            }

            _inputLease = _player != null ? _player.BlockGameplayInput() : null;
            _ownsPause = true;

            _confirmKind = ConfirmKind.None;
            _confirmPanel.EnableInClassList("pause-hidden", true);
            _mainPanel.EnableInClassList("pause-hidden", false);

            RefreshCheckpointState();

            _viewport.EnableInClassList("pause-hidden", false);
            _viewport.schedule.Execute(() => _continueButton?.Focus());
        }

        /// <summary>关闭并恢复进入前的暂停状态。</summary>
        public void Close()
        {
            if (!IsOpen) return;

            _confirmKind = ConfirmKind.None;
            _confirmPanel.EnableInClassList("pause-hidden", true);
            _mainPanel.EnableInClassList("pause-hidden", false);
            _viewport.EnableInClassList("pause-hidden", true);

            ReleasePause();
        }

        private void ReleasePause()
        {
            _inputLease?.Dispose();
            _inputLease = null;

            if (!_ownsPause) return;
            _ownsPause = false;

            if (TimeManager.Instance != null) TimeManager.Instance.SetPaused(_pausedBefore);
            if (_status != null) _status.SetPaused(_statusPausedBefore);
        }

        // ==================================================================
        //  主面板
        // ==================================================================

        private void RefreshCheckpointState()
        {
            SaveSystem save = PersistenceDriver.Instance != null ? PersistenceDriver.Instance.Save : null;
            string id = save != null ? save.LatestRespawnId : null;
            bool has = !string.IsNullOrEmpty(id);

            _checkpointButton.SetEnabled(has);
            _checkpointButton.focusable = has;                 // 键盘导航跳过禁用项
            _checkpointDesc.text = has
                ? "存档点 · " + id
                : "还没有存档点 —— 打一下秋千存档点就会记录";
        }

        // ==================================================================
        //  确认态
        // ==================================================================

        private void ShowCheckpointConfirm()
        {
            SaveSystem save = PersistenceDriver.Instance != null ? PersistenceDriver.Instance.Save : null;
            if (save == null || string.IsNullOrEmpty(save.LatestRespawnId)) return;

            _focusReturn = _checkpointButton;
            _confirmEyebrow.text = "CHECKPOINT";
            _confirmTitle.text = "回到上一个存档点？";
            _confirmCopy.text = "会回到存档点当时的状态：血量、已拼出的规则、机关的开关都会还原。";
            _confirmButton.text = "回到存档点";
            _confirmButton.RemoveFromClassList("is-danger");
            _saveTitle.text = save.LatestRespawnId;
            _saveMeta.text = PersistenceDriver.Instance.Flow != null
                ? "第 " + PersistenceDriver.Instance.Flow.CurrentLevel + " 关"
                : "存档点";
            _saveCard.RemoveFromClassList("pause-hidden");
            ShowConfirm(ConfirmKind.Checkpoint);
        }

        private void ShowMainMenuConfirm()
        {
            _focusReturn = _mainMenuButton;
            _confirmEyebrow.text = "MAIN MENU";
            _confirmTitle.text = "回到主菜单？";
            _confirmCopy.text = "本局从存档点之后的进度会丢失。下次从主菜单「读取存档」，还会回到上一个存档点。";
            _confirmButton.text = "回到主菜单";
            _confirmButton.AddToClassList("is-danger");
            _saveCard.AddToClassList("pause-hidden");
            ShowConfirm(ConfirmKind.MainMenu);
        }

        private void ShowConfirm(ConfirmKind kind)
        {
            _confirmKind = kind;
            _mainPanel.EnableInClassList("pause-hidden", true);
            _confirmPanel.EnableInClassList("pause-hidden", false);
            _viewport.schedule.Execute(() => _cancelButton?.Focus());   // 危险操作默认落在「取消」
        }

        private void CancelConfirm()
        {
            if (_confirmKind == ConfirmKind.None) return;

            _confirmKind = ConfirmKind.None;
            _confirmPanel.EnableInClassList("pause-hidden", true);
            _mainPanel.EnableInClassList("pause-hidden", false);
            _viewport.schedule.Execute(() => _focusReturn?.Focus());
        }

        private void AcceptConfirm()
        {
            ConfirmKind kind = _confirmKind;
            if (kind == ConfirmKind.None) return;

            CancelConfirm();

            if (kind == ConfirmKind.Checkpoint) ReturnToCheckpoint();
            else ReturnToMainMenu();
        }

        // ==================================================================
        //  回到上一个存档点（快照回滚）
        // ==================================================================

        /// <summary>
        /// 回到上一个存档点 = **内存快照回滚**，不是读盘。
        /// </summary>
        /// <remarks>
        /// <para>存档驱动在落盘那一刻顺手拍了一张存档点快照（<see cref="SnapshotSystem.CaptureCheckpointSnapshot"/>），
        /// 这里直接 <c>TryRestoreCheckpointSnapshot()</c> —— 玩家 / 敌人 / 机关 / 规则表 / 剩余时间 / 血量
        /// 一起回到那一帧，**不读盘、不重载分区**。</para>
        /// <para><b>兜底</b>：存档点所在分区已经卸掉（快照里的实体销毁了，回滚只能还原半个世界）
        /// 或者根本没有快照（从盘里续的档），退回「读盘 + 重载分区」。</para>
        /// <para>全程盖黑场（<see cref="IScreenFade"/>），面板自己先收起来 —— 纸面板不该浮在黑场上。</para>
        /// </remarks>
        private void ReturnToCheckpoint()
        {
            if (_busy) return;
            StartCoroutine(RollbackRoutine());
        }

        private IEnumerator RollbackRoutine()
        {
            _busy = true;
            // 面板与遮罩都收起来：黑场要盖住整张画面，纸面板 / 蓝黑遮罩不该浮在上面
            _artboard.EnableInClassList("pause-hidden", true);
            _shade?.EnableInClassList("pause-hidden", true);

            if (_fade != null) yield return _fade.FadeTo(1f);

            bool restored = TrySnapshotRollback();
            if (!restored) yield return SaveReloadRoutine();

            // 黑场里多停一小会儿：分区重载 / 落人之后画面不至于「一亮就抖」
            yield return new WaitForSecondsRealtime(0.15f);

            _artboard.EnableInClassList("pause-hidden", false);
            _shade?.EnableInClassList("pause-hidden", false);
            _busy = false;
            Close();                                  // 解冻

            if (_fade != null) yield return _fade.FadeTo(0f);
        }

        /// <summary>快照回滚。跨分区 / 没有快照时返回 false，交给读盘兜底。</summary>
        private bool TrySnapshotRollback()
        {
            SnapshotSystem snapshots = SnapshotSystem.Instance;
            if (snapshots == null || !snapshots.HasCheckpointSnapshot) return false;

            // 存档点分区已经卸掉：快照里那些实体全是空引用，回滚等于只搬了玩家。
            SaveSystem save = PersistenceDriver.Instance != null ? PersistenceDriver.Instance.Save : null;
            string scene = save != null ? save.LatestRespawnScene : null;
            WorldStreamer streamer = GameRoot.Instance != null ? GameRoot.Instance.Streamer : null;
            if (streamer != null && !string.IsNullOrEmpty(scene) && !streamer.IsSceneLoaded(scene)) return false;

            if (!snapshots.TryRestoreCheckpointSnapshot()) return false;

            // 快照把刚体位姿写回去了，但控制器 / 移动器里的旧速度还在 —— 用工程标准的
            // TeleportTo 收尾（清速度 + SyncTransforms + 对齐 transform），否则解冻第一帧会被推走。
            if (_player != null && _player.TryGetComponent(out Rigidbody2D body))
            {
                _player.TeleportTo(body.position);
            }

            return true;
        }

        /// <summary>
        /// 兜底：读盘 + 分区重载。世界状态（机关开关等）在 <c>WorldStateStore</c> 里，
        /// 分区重新加载时才会被组件取回；落人交给流式加载器现成的续档路径，不自己搬人。
        /// </summary>
        private IEnumerator SaveReloadRoutine()
        {
            SaveSystem save = PersistenceDriver.Instance != null ? PersistenceDriver.Instance.Save : null;
            SaveData data = save != null ? save.Load() : null;
            if (data == null) yield break;

            save.Apply(data);
            LoadContext.SetFromSave(data);

            WorldStreamer streamer = GameRoot.Instance != null ? GameRoot.Instance.Streamer : null;
            if (streamer == null) yield break;

            streamer.ReloadAll();
            yield return WaitForLanding(streamer);
        }

        /// <summary>
        /// 等世界卸干净、重新拉起来、把人放下。超时也放行（卡在黑屏比落点偏一点更糟）。
        /// </summary>
        private IEnumerator WaitForLanding(WorldStreamer streamer)
        {
            float start = UnityEngine.Time.unscaledTime;
            float deadline = start + 4f;
            bool sawEmpty = false;

            while (UnityEngine.Time.unscaledTime < deadline)
            {
                if (streamer.LoadedCount == 0) sawEmpty = true;

                // 卸完了、又拉起至少一片、而且没有正在进行的加载 → 落人逻辑会在下一次 Refresh 执行
                if (sawEmpty && streamer.LoadedCount > 0 && !streamer.IsLoading) break;

                // 本来就没有分区（测试场景）：别干等 4 秒
                if (!sawEmpty && !streamer.IsLoading && UnityEngine.Time.unscaledTime - start > 0.5f) break;

                yield return null;
            }

            // 再给 ApplyStartSpawn 两个 Refresh 的机会（它挂在 WorldStreamer.Update 上）
            yield return null;
            yield return null;
        }

        /// <summary>
        /// 没配引用时自己找一个黑场实现（Boot 常驻场景里的 SwingCheckpointFadeScreen）。
        /// 只扫一次，找不到就按「没有黑场」处理 —— 回档本身照常工作。
        /// </summary>
        private static IScreenFade FindScreenFade()
        {
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IScreenFade fade) return fade;
            }

            return null;
        }

        // ==================================================================
        //  回到主菜单
        // ==================================================================

        /// <summary>
        /// 先停世界（Gameplay 层只负责停流式加载 + 卸分区），再把主菜单叫回来（UI 层）。
        /// </summary>
        private void ReturnToMainMenu()
        {
            Close();

            if (_mainMenu == null) _mainMenu = FindAnyObjectByType<MainMenuPresenter>(FindObjectsInactive.Include);

            GameRoot.Instance?.StopForMenu();

            if (_mainMenu != null) _mainMenu.ShowMenu();
            else Debug.LogWarning("[PauseMenu] 找不到 MainMenuPresenter，世界已停但主菜单没有出现。", this);
        }

        // ==================================================================
        //  焦点 / 缩放
        // ==================================================================

        private void RegisterFocus(Button button)
        {
            if (button == null) return;
            button.RegisterCallback<FocusInEvent>(HandleFocusIn);
            button.RegisterCallback<FocusOutEvent>(HandleFocusOut);
            _buttons.Add(button);
        }

        private void UnregisterFocus(Button button)
        {
            if (button == null) return;
            button.UnregisterCallback<FocusInEvent>(HandleFocusIn);
            button.UnregisterCallback<FocusOutEvent>(HandleFocusOut);
        }

        private static void HandleFocusIn(FocusInEvent evt)
            => (evt.target as VisualElement)?.AddToClassList("is-focused");

        private static void HandleFocusOut(FocusOutEvent evt)
            => (evt.target as VisualElement)?.RemoveFromClassList("is-focused");

        private void ViewportChanged(GeometryChangedEvent evt)
        {
            if (_artboard == null) return;

            float scale = Mathf.Min(evt.newRect.width / 1920f, evt.newRect.height / 1080f);
            _artboard.style.scale = new Scale(new Vector3(scale, scale, 1f));
            _artboard.style.translate = new Translate(
                (evt.newRect.width - 1920f * scale) * .5f,
                (evt.newRect.height - 1080f * scale) * .5f);
        }
    }
}