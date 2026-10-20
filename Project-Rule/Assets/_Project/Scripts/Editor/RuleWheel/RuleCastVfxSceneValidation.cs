using System;
using System.IO;
using System.Linq;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.RuleWheel;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>原样加载生产白盒/正式运行预制体，验证漏接线和超视野扩散；不在测试里补挂视图。</summary>
    public static class RuleCastVfxSceneValidation
    {
        private const string Pending = "RuleGame.RuleVfx.SceneRegression";
        private static readonly string[] Scenes = {
            "Assets/_Project/Scenes/EnemyLabs/Test_Enemy_BatBoss.unity",
            "Assets/_Project/Scenes/EnemyLabs/Test_Enemy_ClockBoss.unity",
            "Assets/_Project/Scenes/MinionLabs/Test_Minion_Gallery.unity",
            "Assets/_Project/Scenes/MinionLabs/Test_Minion_DiscoTop.unity" };
        private const string Folder = "Logs/RuleVfxSceneRegression";
        private static int _scene, _step, _passed, _frame;
        private static double _due, _deadline;
        private static RuleWheelPresenter _wheel;
        private static RuleCastVfx _view;
        private static Keyboard _keyboard;
        private static float _radius;
        private static Camera _camera;
        private static string _name;
        private static SpriteRenderer[] Visible => _view.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(r => r.name == "Rule Star Halo" && r.enabled).ToArray();
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在工程副本运行。");
            ResolveScenes();
            EditorSceneManager.OpenScene(Scenes[0]);
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Begin(); };
        }
        private static void ResolveScenes()
        {
            // 进入 Play 会重载静态字段；在进入前后都解析实际存在的测试场景。
            if (!File.Exists(Scenes[2])) Scenes[2] = "Assets/_Project/Scenes/EnemyLabs/Test_Enemy_RabbitBoss.unity";
            if (!File.Exists(Scenes[3])) Scenes[3] = "Assets/_Project/Scenes/EnemyLabs/Test_Enemy_BrickBoss.unity";
        }
        private static void Begin()
        {
            ResolveScenes();
            Application.runInBackground = true;
            Directory.CreateDirectory(Folder);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>(); _keyboard.MakeCurrent();
            _deadline = EditorApplication.timeSinceStartup + 150;
            _due = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Tick;
        }
        private static void Check(bool ok, string text)
        { if (!ok) throw new InvalidOperationException("FAIL: " + _name + " / " + text); _passed++; Debug.Log("PASS: " + _name + " / " + text); }
        private static void Wait(float time) => _due = EditorApplication.timeSinceStartup + time;
        private static float Radius(SpriteRenderer renderer)
        { var p = new MaterialPropertyBlock(); renderer.GetPropertyBlock(p); return p.GetFloat("_Radius") * renderer.bounds.size.x * .5f; }
        private static float FurthestCorner(Camera camera, Vector3 origin)
        {
            float depth = Vector3.Dot(origin - camera.transform.position, camera.transform.forward), distance = 0;
            for (int i = 0; i < 4; i++)
                distance = Mathf.Max(distance, Vector2.Distance(origin, camera.ViewportToWorldPoint(new Vector3(i & 1, i >> 1, depth))));
            return distance;
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_frame == Time.frameCount || EditorApplication.timeSinceStartup < _due) return;
            _frame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("正式场景验收超时");
                switch (_step++)
                {
                    case 0:
                        _name = SceneManager.GetActiveScene().name;
                        _wheel = UnityEngine.Object.FindAnyObjectByType<RuleWheelPresenter>();
                        Check(_wheel != null && _wheel.isActiveAndEnabled, "原样场景的规则轮盘可用");
                        _view = _wheel.GetComponent<RuleCastVfx>();
                        Check(_view != null && _view.isActiveAndEnabled, "玩家无需托球组件或人工补线即可接入特效");
                        foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None)) enemy.enabled = false;
                        _camera = _wheel.WorldCamera;
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.LeftShift));
                        Wait(.4f); break;
                    case 1:
                        Check(_wheel.IsOpen, "真实 Shift 输入打开轮盘");
                        _wheel.SelectAttributeIndex((int)AttributeId.Mass);
                        _wheel.SelectRelationIndex((int)RelationId.Enhance);
                        Check(_wheel.ConfirmSelection(), "真实轮盘释放成功");
                        Check(Visible.Length == 1, "玩家成功释放立即有白色光潮");
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                        Wait(.24f); break;
                    case 2:
                        Check(Visible.Length == 1, "归还输入后光潮仍向外扩散");
                        _radius = Radius(Visible[0]);
                        Capture("Expand");
                        // 模拟大白盒视野、超宽屏与离心位置；不能仅针对先前的小相机硬编码半径。
                        foreach (var follow in _camera.GetComponents<MonoBehaviour>())
                            if ((follow.GetType().Namespace ?? "").StartsWith("RuleGame.")
                                || (follow.GetType().Namespace ?? "").StartsWith("Unity.Cinemachine")) follow.enabled = false;
                        _camera.orthographicSize = 18f; _camera.aspect = 2.4f;
                        _camera.transform.position += Vector3.left * 12f;
                        Wait(1.35f); break;
                    case 3:
                        Check(Visible.Length == 0, "展开完成后光潮彻底离开画面，不留驻留圆圈");
                        RuleSystem.Instance.Cancel(AttributeId.Mass);
                        Check(Visible.Length == 1, "失效从画外开始回收");
                        float worldRadius = Radius(Visible[0]);
                        Check(worldRadius > FurthestCorner(_camera, Visible[0].transform.position) * 2.5f,
                            "所有延迟光圈和宽辉光均超过最远屏幕角点");
                        _radius = worldRadius;
                        Wait(.32f); break;
                    case 4:
                        Check(Visible.Length == 1 && Radius(Visible[0]) < _radius * .35f, "光圈从画外向核心收束");
                        Capture("Return");
                        Wait(.6f); break;
                    case 5:
                        Check(Visible.Length == 0, "失效收束后完全清理");
                        if (++_scene < Scenes.Length)
                        {
                            EditorSceneManager.LoadSceneInPlayMode(Scenes[_scene], new LoadSceneParameters(LoadSceneMode.Single));
                            _step = 0; Wait(1.2f); break;
                        }
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Capture(string suffix)
        {
            var previous = _camera.targetTexture;
            var target = new RenderTexture(1280, 720, 24); target.Create(); _camera.targetTexture = target;
            var active = RenderTexture.active; _camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Folder + "/" + _name + "_" + suffix + ".png", image.EncodeToPNG());
            RenderTexture.active = active; _camera.targetTexture = previous;
            UnityEngine.Object.DestroyImmediate(image); target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/Results.txt", "Passed: " + _passed + "\n" + (error ?? "Failed: 0"));
            if (error != null) Debug.LogError(error);
            Debug.Log("RuleCastVfxSceneValidation: " + _passed + " passed; " + (error ?? "0 failed"));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
