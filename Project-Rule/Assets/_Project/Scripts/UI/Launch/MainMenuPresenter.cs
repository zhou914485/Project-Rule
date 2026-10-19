using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Interaction;
using RuleGame.Infrastructure.Time;
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
            if (!_started) EnterMenu();
        }

        private void EnterMenu()
        {
            _status = _player.GetComponent<EntityStatus>();
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
            _gameRoot.StartStreaming();
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
