using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Interaction;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.Comic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.UI.Launch
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuPresenter : MonoBehaviour
    {
        [SerializeField] private GameRoot _gameRoot;
        [SerializeField] private PlayerController _player;
        private readonly Dictionary<Button, Action> _bindings = new();
        private VisualElement _viewport, _artboard, _modal;
        private Label _title, _copy;
        private Slider _volume;
        private Button _confirm, _returnFocus;
        private EntityStatus _status;
        private IDisposable _inputLease;
        private bool _started, _initialized, _pausedBeforeMenu, _statusPausedBeforeMenu, _ownsPause;

        public bool HasStarted => _started;
        public event Action<Button> ButtonActivated;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _viewport = root.Q("menuViewport");
            _artboard = root.Q("menuArtboard");
            _modal = root.Q("modalShade");
            _title = root.Q<Label>("modalTitle");
            _copy = root.Q<Label>("modalCopy");
            _volume = root.Q<Slider>("volumeSlider");
            _confirm = root.Q<Button>("quitConfirmButton");

            // 世界已经在跑（例如 Boot 被附加加载了第二份 / 菜单被重新启用）：先收起，绝不 EnterMenu ——
            // 它会 SetPaused(true)，把正在播的存档过场和玩法一起冻住。
            // 判据用静态单例而不是序列化引用：重复那份 Boot 的 GameRoot 会自毁，
            // 序列化引用会变成 null，只有 GameRoot.Instance 还指向真正在跑的那个。
            // ⚠️ 这一步必须在下面的配置校验之前：重复那份可能校验失败直接 return，
            //    那时菜单已经挂在屏幕上了。
            if (GameRoot.Instance != null && GameRoot.Instance.HasStarted) _started = true;
            _viewport?.EnableInClassList("hidden", _started);

            if (_gameRoot == null || _player == null || _viewport == null || _volume == null)
            {
                Debug.LogError("[MainMenu] 请配置 GameRoot、玩家和 MainMenu.uxml。", this);
                enabled = false;
                return;
            }
            Bind(root, "startButton", StartGame);
            Bind(root, "loadButton", () => ShowDialog("读取存档", "存档功能尚未接入。请返回主界面，选择「开始游戏」。", "loadButton"));
            Bind(root, "aboutButton", () => ShowDialog("关于我们", "《规则游戏》\n2D 平台动作解谜\n\n用「属性 × 关系」改变世界的物理法则。\n26TapTap 聚光灯 GameJam · 主题：涌现", "aboutButton"));
            Bind(root, "settingsButton", () => ShowDialog("设置", "调整游戏总音量", "settingsButton", true));
            Bind(root, "quitButton", () => ShowDialog("退出游戏", "确定要退出游戏吗？", "quitButton", false, true));
            Bind(root, "modalCloseButton", CloseDialog);
            Bind(root, "modalBackButton", CloseDialog);
            Bind(root, "quitConfirmButton", QuitGame);
            _volume.SetValueWithoutNotify(AudioListener.volume);
            _volume.RegisterValueChangedCallback(VolumeChanged);
            _viewport.RegisterCallback<GeometryChangedEvent>(ViewportChanged);
            _viewport.EnableInClassList("hidden", _started);
            if (_initialized && !_started) EnterMenu();
        }

        private void Start()
        {
            _initialized = true;
            if (GameRoot.Instance != null && GameRoot.Instance.HasStarted) _started = true;
            if (!_started) EnterMenu();
        }

        private void EnterMenu()
        {
            _status = _player.GetComponent<EntityStatus>();

            // ⚠️ Boot 重建 LevelRuntime 时，菜单会被重新启用一次 —— 那一刻
            // TimeManager / 玩家状态可能还不存在（规则系统由常驻宿主创建）。
            // 没有世界就没有可暂停的东西，直接跳过，别让 NRE 打断菜单初始化。
            if (TimeManager.Instance == null || _status == null)
            {
                _ownsPause = false;
                return;
            }

            _pausedBeforeMenu = TimeManager.Instance.IsPaused;
            _statusPausedBeforeMenu = _status.IsPaused;
            TimeManager.Instance.SetPaused(true);
            _status.SetPaused(true);
            _inputLease = ((IRuleSelectionInputGate)_player).BlockGameplayInput();
            _ownsPause = true;
            _viewport.schedule.Execute(() => _viewport.Q<Button>("startButton").Focus());
        }

        public void StartGame()
        {
            if (_started || !_initialized) return;
            _started = true;
            _viewport.AddToClassList("hidden");
            ReleasePause();

            // 开场漫画：播完（或被跳过）才真的开始加载世界。没有序列时 TryPlayIntro 返回 false，照旧直接开局。
            ComicPlayer comic = ComicPlayer.EnsureInstance();
            if (!comic.TryPlayIntro(_gameRoot.StartStreaming)) _gameRoot.StartStreaming();
        }

        /// <summary>
        /// 从游戏里回到主菜单：把标题界面叫回来，并重新接管暂停与输入。
        /// </summary>
        /// <remarks>
        /// 世界侧（停流式加载 / 卸分区）由 <c>GameRoot.StopForMenu()</c> 负责 —— 那一层看不见本类，
        /// 由 UI 层依次调用，依赖方向仍然是 UI → Gameplay。
        /// </remarks>
        public void ShowMenu()
        {
            if (!_initialized) return;

            _started = false;
            _modal?.AddToClassList("hidden");
            _artboard?.SetEnabled(true);
            _viewport?.RemoveFromClassList("hidden");
            EnterMenu();
        }

        private void Bind(VisualElement root, string name, Action action)
        {
            Button button = root.Q<Button>(name);
            Action activated = () =>
            {
                ButtonActivated?.Invoke(button);
                action();
            };
            button.clicked += activated;
            _bindings.Add(button, activated);
        }

        private void ShowDialog(string title, string copy, string origin, bool volume = false, bool quit = false)
        {
            _returnFocus = _viewport.Q<Button>(origin);
            _title.text = title;
            _copy.text = copy;
            _volume.EnableInClassList("hidden", !volume);
            _confirm.EnableInClassList("hidden", !quit);
            _artboard.SetEnabled(false);
            _modal.RemoveFromClassList("hidden");
            _viewport.Q<Button>("modalBackButton").Focus();
        }

        private void CloseDialog()
        {
            _modal.AddToClassList("hidden");
            _artboard.SetEnabled(true);
            _returnFocus?.Focus();
        }

        private void QuitGame()
        {
            if (Application.isEditor)
            {
                _copy.text = "编辑器中不退出应用。构建后的游戏会在确认后关闭。";
                _confirm.AddToClassList("hidden");
                return;
            }
            Application.Quit();
        }

        private void Update()
        {
            if (_started || _modal.ClassListContains("hidden")) return;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true
                || Gamepad.current?.buttonEast.wasPressedThisFrame == true) CloseDialog();
        }

        private void ViewportChanged(GeometryChangedEvent evt)
        {
            float scale = Mathf.Min(evt.newRect.width / 1920f, evt.newRect.height / 1080f);
            _artboard.style.scale = new Scale(new Vector3(scale, scale, 1f));
            _artboard.style.translate = new Translate((evt.newRect.width - 1920f * scale) * .5f,
                (evt.newRect.height - 1080f * scale) * .5f);
        }

        private void VolumeChanged(ChangeEvent<float> evt) => AudioListener.volume = evt.newValue;

        private void ReleasePause()
        {
            _inputLease?.Dispose();
            _inputLease = null;
            if (!_ownsPause) return;
            TimeManager.Instance?.SetPaused(_pausedBeforeMenu);
            if (_status != null) _status.SetPaused(_statusPausedBeforeMenu);
            _ownsPause = false;
        }

        private void OnDisable()
        {
            ReleasePause();
            foreach (KeyValuePair<Button, Action> binding in _bindings) binding.Key.clicked -= binding.Value;
            _bindings.Clear();
            _volume?.UnregisterValueChangedCallback(VolumeChanged);
            _viewport?.UnregisterCallback<GeometryChangedEvent>(ViewportChanged);
        }
    }
}
