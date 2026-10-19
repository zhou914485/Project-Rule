using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Rules;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.RuleWheel;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>副本内验证真实移动输入以及两级轮盘中途松开 Shift，不修改实际工程的输入设置。</summary>
    public static class RuleWheelCancellationValidation
    {
        private const string Pending = "RuleGame.Wheel.CancelValidation";
        private const string PreviewOnly = "RuleGame.Wheel.CancelPreview";
        private static PlayerController _player;
        private static RuleWheelPresenter _wheel;
        private static Keyboard _keyboard;
        private static Mouse _mouse;
        private static RuleDef _costFixture;
        private static float _originalCost;
        private static RenderTexture _surface;
        private static int _step, _cycle, _frames, _passed;
        private static float _x, _energy;
        private static double _due;
        private static double _startupDeadline;
        private static bool _errors, _recording;
        private static int _captureCount;
        private static double _captureDue;
        private static string Output => Environment.GetEnvironmentVariable("RULE_WHEEL_OUTPUT") ?? "Logs/RuleWheelArt";

        public static void Run() => Start(false);
        public static void Preview() => Start(true);

        private static void Start(bool preview)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            try
            {
                Directory.CreateDirectory(Output);
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/SampleScene.unity");
                RuleWheelArtSetup.Configure();
                RuleWheelLabBuilder.Build();
                EditorSceneManager.OpenScene(RuleWheelLabBuilder.ScenePath);
                SessionState.SetBool(Pending, true);
                SessionState.SetBool(PreviewOnly, preview);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    if (!stack.Contains("UnityEditor.Search.SearchDatabase")) _errors = true;
            };
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Begin(); };
            if (EditorApplication.isPlaying) Begin();
        }

        private static void Begin()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // The legacy lab has a solid dummy at x=3. Keep this input test's
            // short A/D path clear without changing the saved scene.
            GameObject.Find("Dummy")?.SetActive(false);
            GameObject.Find("TestBox")?.SetActive(false);
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            _wheel = UnityEngine.Object.FindAnyObjectByType<RuleWheelPresenter>();
            _keyboard = InputSystem.AddDevice<Keyboard>(); _keyboard.MakeCurrent();
            _mouse = InputSystem.AddDevice<Mouse>(); _mouse.MakeCurrent();
            _costFixture = AssetDatabase.LoadAssetAtPath<RuleDef>("Assets/_Project/Data/Rules/GravityRevert.asset");
            _originalCost = _costFixture.ActivationCost;
            _costFixture.ActivationCost = 7f; // Play-only positive cost; never saved.
            _energy = _player.GetComponent<EntityStatus>().Energy;
            _surface = new RenderTexture(1280, 720, 24); _surface.Create();
            Camera.main.targetTexture = _surface;
            _wheel.GetComponent<UIDocument>().panelSettings.targetTexture = _surface;
            _due = Time.unscaledTimeAsDouble + 1.0;
            _startupDeadline = EditorApplication.timeSinceStartup + 10.0;
            EditorApplication.update += Step;
        }

        private static void Step()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_recording && Time.unscaledTimeAsDouble >= _captureDue && _captureCount < 80)
            {
                Screenshot($"motion_{_captureCount++:000}.png");
                _captureDue = Time.unscaledTimeAsDouble + 1.0 / 24.0;
            }
            if (Time.unscaledTimeAsDouble < _due) return;
            // 新场景首次字体/渲染初始化会阻塞编辑器；开始操作前等真实物理接地，不按墙钟猜。
            if (_cycle == 0 && _step == 0 && !_player.IsGrounded && EditorApplication.timeSinceStartup < _startupDeadline) return;
            try
            {
                Key move = _cycle % 2 == 0 ? Key.D : Key.A;
                float direction = _cycle % 2 == 0 ? 1f : -1f;
                switch (_step++)
                {
                    case 0:
                        if (_cycle == 0) Check(_player.IsGrounded, "复制场景里的玩家正常落地");
                        if (SessionState.GetBool(PreviewOnly, false))
                        {
                            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.LeftShift));
                            _step = 2;
                            break;
                        }
                        _x = _player.transform.position.x;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(move));
                        break;
                    case 1:
                        Check((_player.transform.position.x - _x) * direction > 0.1f, "正常移动输入未响应");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(move, Key.LeftShift));
                        break;
                    case 2:
                        Check(_wheel.Stage == RuleSelectionStage.Attribute && Mathf.Approximately(Time.timeScale, RuleWheelPresenter.DefaultSelectionScale), "Shift 未打开属性轮盘");
                        if (SessionState.GetBool(PreviewOnly, false))
                        {
                            Screenshot("attribute.png");
                            Finish(!_errors, "深色场景 HUD 对比度预览完成。");
                            return;
                        }
                        if (_cycle % 2 == 1)
                        {
                            _wheel.SelectAttributeIndex(0);
                            Check(_wheel.Stage == RuleSelectionStage.Relation, "悬停没有展开关系轮盘");
                        }
                        if (_cycle == 0) Screenshot("attribute.png");
                        if (_cycle == 1) Screenshot("relation.png");
                        _x = _player.transform.position.x;
                        _frames = Time.frameCount;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(move));
                        break;
                    case 3:
                        Check(!_wheel.IsOpen && Mathf.Approximately(Time.timeScale, 1f)
                            && Mathf.Approximately(TimeManager.Instance.TimeScale, 1f),
                            "松开 Shift 没恢复 UI / 时间倍率");
                        Check(Time.frameCount > _frames && (_player.transform.position.x - _x) * direction > 0.1f,
                            "取消选择后帧循环或移动仍被锁住");
                        Check(Mathf.Approximately(_player.GetComponent<EntityStatus>().Energy, _energy), "取消选择错误扣费");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        break;
                    case 4:
                        Check(!_wheel.WorldCamera.GetComponent<RuleSelectionCamera>().IsFocused, "取消后未归还队友跟随摄像机");
                        if (++_cycle < 4) { _step = 0; break; }
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                        break;
                    case 5:
                        _recording = true; _captureDue = Time.unscaledTimeAsDouble;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.LeftShift));
                        break;
                    case 6:
                        Check(_wheel.IsOpen && !_wheel.Fan.Mirrored, "朝右的新一轮打开成功");
                        CheckArt(_wheel.Fan, 6);
                        CheckArt(_wheel.RelationFan, 5);
                        Check(_wheel.Fan.Query<VisualElement>(className: "wheel-paper").First().resolvedStyle.opacity > .99f,
                            "极慢游戏时间下纸牌仍按真实时间完成展开");
                        _wheel.SelectAttributeIndex(0);
                        break;
                    case 7:
                        _wheel.SelectRelationIndex(0);
                        HoverRelation(0);
                        Check(_wheel.Fan.Query<VisualElement>(className: "wheel-paper").First().resolvedStyle.scale.value.x > 1f,
                            "悬停属性牌轻微抬起放大");
                        break;
                    case 8:
                        Screenshot("art-relation-right.png");
                        Check(_wheel.RelationFan.Query<VisualElement>(className: "wheel-glow").First().resolvedStyle.opacity > .15f,
                            "悬停关系牌亮起金色光边");
                        _energy = _player.GetComponent<EntityStatus>().Energy;
                        Check(_wheel.ConfirmSelection(), "手绘牌可以确认真实规则");
                        Check(!_wheel.IsOpen && Mathf.Approximately(Time.timeScale, 1f), "释放反馈立即归还游戏时间");
                        Check(Mathf.Approximately(_player.GetComponent<EntityStatus>().Energy, _energy - 7f), "真实规则释放恰好结算一次七点能量");
                        _due = Time.unscaledTimeAsDouble + .06;
                        return;
                    case 9:
                        Screenshot("art-release.png");
                        Check(_wheel.TryOpen(), "淡出期间可立即重新打开轮盘");
                        Check(!_wheel.GetComponent<UIDocument>().rootVisualElement.Q("wheelRoot").ClassListContains("hidden"),
                            "上一次淡出不会隐藏新打开的轮盘");
                        _wheel.Close();
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.A));
                        break;
                    case 10:
                        _recording = false;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.A, Key.LeftShift));
                        break;
                    case 11:
                        Check(_wheel.Fan.Mirrored, "向左时使用镜像手绘牌");
                        _wheel.SelectAttributeIndex(0);
                        _wheel.SelectRelationIndex(0);
                        HoverRelation(0);
                        break;
                    case 12:
                        Screenshot("art-relation-left.png");
                        Check(_wheel.Fan.Query<VisualElement>(className: "wheel-paper").First().resolvedStyle.scale.value.x < 0f,
                            "镜像只作用于美术纸牌");
                        Check(_wheel.Fan.Query<Label>(className: "wheel-word").First().resolvedStyle.scale.value.x >= 0f,
                            "镜像时文字保持正向");
                        for (int i = 0; i < 6; i++)
                            Check(_wheel.Fan.IndexAt(_wheel.Fan.OptionPoint(i)) == i, "镜像纸牌命中位置与显示一致：" + i);
                        _energy = _player.GetComponent<EntityStatus>().Energy;
                        Check(_wheel.ConfirmSelection(), "已生效规则可通过手绘牌取消");
                        Check(Mathf.Approximately(_player.GetComponent<EntityStatus>().Energy, _energy), "取消已生效规则不扣能量");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        break;
                    case 13:
                        Check(!_wheel.IsOpen && !_wheel.WorldCamera.GetComponent<RuleSelectionCamera>().IsFocused, "动效结束不残留输入和镜头接管");
                        Check(_wheel.GetComponent<UIDocument>().rootVisualElement.Q("wheelRoot").ClassListContains("hidden"), "关闭动效最终隐藏轮盘");
                        Finish(!_errors, "真实输入取消、手绘贴图、慢时间动效、左右镜像、释放扣费、免费取消与快速重开验证完成。");
                        return;
                }
                _due = Time.unscaledTimeAsDouble + 0.35;
            }
            catch (Exception error) { Finish(false, $"cycle={_cycle}, step={_step}, position={_player.transform.position}, startX={_x}, velocity={_player.Velocity}, frames={Time.frameCount-_frames}, timeScale={Time.timeScale}, managerScale={(TimeManager.Instance != null ? TimeManager.Instance.TimeScale : -1f)}\n{error}"); }
        }

        private static void HoverRelation(int index)
        {
            Vector2 panel = _wheel.RelationFan.LocalToWorld(_wheel.RelationFan.OptionPoint(index));
            Vector2 root = _wheel.GetComponent<UIDocument>().rootVisualElement.contentRect.size;
            var screen = new Vector2(panel.x / root.x * _surface.width, (1f - panel.y / root.y) * _surface.height);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });
        }

        private static void CheckArt(RuleFan fan, int expected)
        {
            var cards = fan.Query<VisualElement>(className: "wheel-paper").ToList();
            Check(cards.Count == expected, "美术纸牌数量：" + expected);
            foreach (VisualElement card in cards)
            {
                Texture2D texture = card.resolvedStyle.backgroundImage.texture;
                Check(texture != null && AssetDatabase.GetAssetPath(texture).StartsWith("Assets/_Project/UI/Art/RuleWheel/"),
                    "纸牌引用真实交付贴图");
            }
        }

        private static void Check(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
            _passed++;
        }
        private static void Screenshot(string name)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _surface;
            var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Output, name), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels);
        }
        private static void Finish(bool success, string detail)
        {
            if (_costFixture != null) _costFixture.ActivationCost = _originalCost;
            EditorApplication.update -= Step;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "result.json"), JsonUtility.ToJson(new Report { Success = success, Passed = _passed, Detail = detail }, true));
            Debug.Log("[Wheel Cancel] " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }
        [Serializable] private sealed class Report { public bool Success; public int Passed; public string Detail; }
    }
}
