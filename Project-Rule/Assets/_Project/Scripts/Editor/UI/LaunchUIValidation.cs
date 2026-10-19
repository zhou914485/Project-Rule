using System;
using System.Collections.Generic;
using System.IO;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.Launch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace RuleGame.Editor.UI
{
    /// <summary>真实 Boot 场景的 UI 冒烟验证；不保存 Play Mode 中的状态。</summary>
    public static class LaunchUIValidation
    {
        private const string Pending = "RuleGame.LaunchUI.Validation";
        private const string Fixture = "RuleGame.LaunchUI.Fixture";
        private static readonly List<string> Checks = new();
        private static readonly List<string> Errors = new();
        private static MainMenuPresenter _menu;
        private static PlayerVitalsPresenter _hud;
        private static PlayerController _player;
        private static EntityStatus _status;
        private static Keyboard _keyboard;
        private static RenderTexture _surface;
        private static VisualElement _menuUI, _hudUI;
        private static EntityStatusData _saved;
        private static Vector3 _initialPosition;
        private static int _step;
        private static double _due, _deadline;
        private static float _volumeBefore;
        private static bool _reportedWorld;
        private static string Output => Path.GetFullPath("Logs/LaunchUI");

        public static void RunBatch()
        {
            SessionState.SetBool(Fixture, false);
            Run();
        }

        public static void RunFixtureBatch()
        {
            SessionState.SetBool(Fixture, true);
            Run();
        }

        private static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("此入口只用于批处理验证。");
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene(LaunchUISetup.BootPath);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            Application.logMessageReceived += Log;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
            if (EditorApplication.isPlaying) Begin();
        }

        private static void Begin()
        {
            try
            {
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                _menu = UnityEngine.Object.FindAnyObjectByType<MainMenuPresenter>();
                _hud = UnityEngine.Object.FindAnyObjectByType<PlayerVitalsPresenter>();
                _player = GameRoot.Instance.GetComponentInChildren<PlayerController>();
                _status = _player.GetComponent<EntityStatus>();
                if (SessionState.GetBool(Fixture, false))
                {
                    // 原 Boot 出生点超出地图。仅在 Play 中用预置玩家位置验证 UI，
                    // 不把关卡摆放问题混入 UI 验证，也不写回用户场景。
                    foreach (PlayerSpawnPoint point in UnityEngine.Object.FindObjectsByType<PlayerSpawnPoint>())
                        if (point.Id == PlayerSpawnPoint.DefaultId) point.transform.position = _player.transform.position;
                }
                _menuUI = _menu.GetComponent<UIDocument>().rootVisualElement;
                _hudUI = _hud.GetComponent<UIDocument>().rootVisualElement;
                _keyboard = InputSystem.AddDevice<Keyboard>();
                _keyboard.MakeCurrent();
                _volumeBefore = AudioListener.volume;
                Surface(1920, 1080);
                _due = EditorApplication.timeSinceStartup + 1.5;
                _deadline = EditorApplication.timeSinceStartup + 90;
                EditorApplication.update += Step;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        private static void Step()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup > _deadline)
            {
                Finish(false, $"验证超时。step={_step}, loaded={GameRoot.Instance?.Streamer.LoadedCount}, loading={GameRoot.Instance?.Streamer.IsLoading}, position={_player?.transform.position}");
                return;
            }
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                switch (_step++)
                {
                    case 0:
                        Check(_menu.enabled && _hud.enabled, "正式菜单和 HUD 已启用、引用完整");
                        Check(!GameRoot.Instance.Streamer.enabled && GameRoot.Instance.Streamer.LoadedCount == 0,
                            "开始前未加载地图分区");
                        Check(TimeManager.Instance.IsPaused && _status.IsPaused && Mathf.Approximately(Time.timeScale, 1f),
                            "菜单冻结物理及玩家状态，timeScale 保持 1");
                        _initialPosition = _player.transform.position;
                        Check(_menuUI.Q("menuArtboard").worldBound.Contains(_menuUI.Q<Button>("startButton").worldBound.center),
                            "开始按钮位于画面内");
                        Capture("Menu_1920x1080.png");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D, Key.LeftShift));
                        break;
                    case 1:
                        Check(Vector3.Distance(_initialPosition, _player.transform.position) < .001f, "菜单期间输入和重力不移动玩家");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        Submit("loadButton");
                        break;
                    case 2:
                        Check(ModalOpen() && _menuUI.Q<Label>("modalCopy").text.Contains("尚未接入"), "读取存档显示真实功能状态");
                        Submit("startButton");
                        Check(!_menu.HasStarted, "弹窗期间底层开始按钮不响应");
                        Submit("modalBackButton");
                        Submit("settingsButton");
                        _menuUI.Q<Slider>("volumeSlider").value = .37f;
                        Check(Mathf.Abs(AudioListener.volume - .37f) < .001f, "设置中的音量滑块控制实际总音量");
                        break;
                    case 3:
                        Capture("Settings_1920x1080.png");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
                        break;
                    case 4:
                        Check(!ModalOpen(), "Esc 关闭弹窗");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        for (int i = 0; i < 3; i++) { Submit("aboutButton"); Submit("modalBackButton"); }
                        Check(!ModalOpen() && !_menu.HasStarted, "关于弹窗反复开关不启动世界");
                        Submit("quitButton");
                        Check(ModalOpen() && !_menuUI.Q<Button>("quitConfirmButton").ClassListContains("hidden"), "退出需二次确认");
                        Submit("modalBackButton");
                        Surface(1280, 1024);
                        break;
                    case 5:
                        Check(Mathf.Abs(_menuUI.Q("menuArtboard").worldBound.width / _menuUI.Q("menuArtboard").worldBound.height - 16f / 9f) < .01f,
                            "5:4 屏幕保持开始原画 16:9 比例");
                        Capture("Menu_1280x1024.png");
                        Surface(2560, 1080);
                        break;
                    case 6:
                        Check(_menuUI.Q("menuViewport").worldBound.Contains(_menuUI.Q<Button>("quitButton").worldBound.center),
                            "超宽屏退出按钮仍在画面内");
                        Capture("Menu_2560x1080.png");
                        Surface(1920, 1080);
                        break;
                    case 7:
                        _menuUI.Q<Button>("startButton").Focus();
                        // Headless captures use a RenderTexture panel; exercise the UI's
                        // navigation submit event rather than the native window input route.
                        Submit("startButton");
                        break;
                    case 8:
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        Check(_menu.HasStarted && GameRoot.Instance.Streamer.enabled, "UI 导航确认启动真实世界加载");
                        Check(!TimeManager.Instance.IsPaused && !_status.IsPaused && Mathf.Approximately(Time.timeScale, 1f),
                            "开始后恢复物理与状态时钟");
                        Check(_menuUI.Q("menuViewport").ClassListContains("hidden"), "开始菜单退出渲染与拾取");
                        _due = EditorApplication.timeSinceStartup + 2;
                        return;
                    case 9:
                        if (!_reportedWorld)
                        {
                            _reportedWorld = true;
                            Debug.Log($"[LaunchUI World] loaded={GameRoot.Instance.Streamer.LoadedCount}, loading={GameRoot.Instance.Streamer.IsLoading}, position={_player.transform.position}, frames={Time.frameCount}");
                        }
                        if (GameRoot.Instance.Streamer.LoadedCount == 0 || GameRoot.Instance.Streamer.IsLoading)
                        { _step--; _due = EditorApplication.timeSinceStartup + .5; return; }
                        Check(GameRoot.Instance.Streamer.LoadedCount > 0, "开始后实际加载地图分区");
                        Check(!string.IsNullOrEmpty(_player.RespawnId), "开局出生点写入玩家复活点");
                        Check(_player.IsGrounded && _status.Health > 0f, "出生后稳定站立在真实地面且玩家存活");
                        _saved = _status.ToData();
                        TimeManager.Instance.SetPaused(true);
                        _status.SetPaused(true);
                        _status.ApplyHealthDelta(-_status.HealthMax * .5f);
                        _status.TryConsumeEnergy(_status.EnergyMax * .75f);
                        break;
                    case 10:
                        Check(Mathf.Abs(FillRatio("healthClip") - .5f) < .01f,
                            "实际扣血后生命条显示一半");
                        Check(Mathf.Abs(FillRatio("energyClip") - .25f) < .01f,
                            "实际耗能后能量条显示四分之一");
                        Check(_hudUI.Query<VisualElement>(className: "vital-division").ToList().Count == 18,
                            "原图生命条和能量条各显示九条分隔线");
                        Capture("Gameplay_HalfHealth.png");
                        _status.ApplyData(_saved);
                        break;
                    case 11:
                        Check(Mathf.Abs(FillRatio("healthClip") - 1f) < .01f,
                            "快照恢复后生命条同步回满");
                        _hud.enabled = false;
                        _status.ApplyHealthDelta(-_status.HealthMax * .25f);
                        _hud.enabled = true;
                        _hudUI = _hud.GetComponent<UIDocument>().rootVisualElement;
                        break;
                    case 12:
                        Check(Mathf.Abs(FillRatio("healthClip") - .75f) < .01f,
                            "HUD 反复启用后读取最新生命值");
                        _status.ApplyData(_saved);
                        _status.ApplyHealthDelta(-_status.HealthMax);
                        _status.TryConsumeEnergy(_status.EnergyMax);
                        break;
                    case 13:
                        Check(_hudUI.Q("healthClip").resolvedStyle.width < 1f && _hudUI.Q("energyClip").resolvedStyle.width < 1f,
                            "死亡和零能量时彩色填充清零，灰色边框保留");
                        Capture("Gameplay_EmptyVitals.png");
                        _status.ApplyData(_saved);
                        _status.SetPaused(false);
                        TimeManager.Instance.SetPaused(false);
                        Check(Errors.Count == 0, "运行期间无新增 Error/Exception");
                        Finish(true, "Boot 菜单、弹窗、UI 导航确认、Esc、比例适配、分区启动和事件血条验证完成。");
                        return;
                }
                _due = EditorApplication.timeSinceStartup + .4;
            }
            catch (Exception error) { Finish(false, $"step={_step}\n{error}"); }
        }

        private static bool ModalOpen() => !_menuUI.Q("modalShade").ClassListContains("hidden");

        private static float FillRatio(string name)
        {
            VisualElement clip = _hudUI.Q(name);
            return clip.resolvedStyle.width / clip.parent.contentRect.width;
        }

        private static void Submit(string name)
        {
            Button button = _menuUI.Q<Button>(name);
            using NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            button.SendEvent(evt);
        }

        private static void Surface(int width, int height)
        {
            RenderTexture old = _surface;
            _surface = new RenderTexture(width, height, 24);
            _surface.Create();
            Camera.main.targetTexture = _surface;
            foreach (UIDocument doc in UnityEngine.Object.FindObjectsByType<UIDocument>()) doc.panelSettings.targetTexture = _surface;
            if (old != null) { old.Release(); UnityEngine.Object.DestroyImmediate(old); }
        }

        private static void Capture(string name)
        {
            RenderTexture old = RenderTexture.active;
            RenderTexture.active = _surface;
            var pixels = new Texture2D(_surface.width, _surface.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0);
            pixels.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
        }

        private static void Check(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
            Checks.Add(description);
        }

        private static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                if (!stack.Contains("UnityEditor.Search.SearchDatabase")) Errors.Add(message);
        }

        private static void Finish(bool success, string detail)
        {
            EditorApplication.update -= Step;
            SessionState.SetBool(Pending, false);
            AudioListener.volume = _volumeBefore;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "result.json"), JsonUtility.ToJson(new Report
            { Success = success, Passed = Checks.Count, Detail = detail, UsesRuntimeSpawnFixture = SessionState.GetBool(Fixture, false), Checks = Checks.ToArray(), Errors = Errors.ToArray() }, true));
            Debug.Log($"[LaunchUI Validation] passed={Checks.Count}, success={success}: {detail}");
            EditorApplication.Exit(success ? 0 : 1);
        }

        [Serializable]
        private sealed class Report
        {
            public bool Success;
            public bool UsesRuntimeSpawnFixture;
            public int Passed;
            public string Detail;
            public string[] Checks, Errors;
        }
    }
}
