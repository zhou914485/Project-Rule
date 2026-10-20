using System;
using System.IO;
using System.Linq;
using RuleGame.Core.Rules;
using RuleGame.Editor.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.RuleWheel;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>在隔离工程中检查真实轮盘/敌方施法、生命周期与 URP 渲染。</summary>
    public static class RuleCastVfxValidation
    {
        private const string Pending = "RuleGame.StarHalo.Validation";
        private const string Folder = "Logs/RuleStarHalo";
        private static RuleWheelPresenter _wheel;
        private static RuleCastVfx _playerView;
        private static EnemyActor _enemy, _other;
        private static RuleCastVfx _enemyView;
        private static int _step, _passed, _lastFrame;
        private static double _due, _deadline;
        private static float _radius, _age;
        private static RuleDef _fixture;
        private static RenderTexture _target;
        private static Camera _camera;
        private static bool _runtimeError;
        private static RuleGame.Infrastructure.Snapshot.Snapshot _snapshot;

        [MenuItem("RuleGame/Rule Wheel/Create Star Halo Material")]
        public static void EnsureMaterial()
        {
            const string root = "Assets/_Project/Resources/RuleCastVfx";
            if (!AssetDatabase.IsValidFolder(root)) AssetDatabase.CreateFolder("Assets/_Project/Resources", "RuleCastVfx");
            var shader = Shader.Find("RuleGame/Rules/StarHalo");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("星环 Shader 编译失败");
            if (AssetDatabase.LoadAssetAtPath<Material>(root + "/StarHalo.mat") == null)
                AssetDatabase.CreateAsset(new Material(shader) { name = "StarHalo" }, root + "/StarHalo.mat");
            AssetDatabase.SaveAssets();
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在可丢弃的完整工程副本运行。");
            EnsureMaterial();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Test_RuleWheel.unity");
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Begin(); };
        }
        private static void Begin()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(Folder);
            Application.logMessageReceived += (message, stack, type) =>
            {
                if ((type == LogType.Error || type == LogType.Exception) && !stack.Contains("UnityEditor.Search"))
                    _runtimeError = true;
            };
            _deadline = EditorApplication.timeSinceStartup + 120;
            _due = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += Tick;
        }
        private static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException("FAIL: " + name);
            _passed++; Debug.Log("PASS: " + name);
        }
        private static SpriteRenderer[] Rings(RuleCastVfx view) => view.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(r => r.name == "Rule Star Halo" && r.enabled).ToArray();
        private static float Property(SpriteRenderer renderer, string name)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block.GetFloat(name);
        }
        private static void Wait(float seconds) => _due = EditorApplication.timeSinceStartup + seconds;
        private static EnemyActor Make(EnemyKind kind, Vector3 position)
        {
            string subfolder = (int)kind >= (int)EnemyKind.StoneGuard ? "FieldMinions/" : "";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + subfolder + kind + ".prefab");
            var actor = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<EnemyActor>();
            actor.enabled = false;
            actor.Definition = UnityEngine.Object.Instantiate(actor.Definition);
            actor.Definition.Energy = 999;
            actor.SendMessage("Start");
            actor.GetComponent<RuleCastVfx>().SendMessage("Start");
            return actor;
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("星环验收超时 step=" + _step);
                switch (_step++)
                {
                    case 0:
                        _wheel = UnityEngine.Object.FindAnyObjectByType<RuleWheelPresenter>();
                        Check(_wheel != null && _wheel.isActiveAndEnabled, "真实轮盘场景正常启动");
                        _playerView = _wheel.GetComponent<RuleCastVfx>();
                        Check(_playerView != null, "玩家现有光球自动接入");
                        foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
                        {
                            var actor = Make(kind, new Vector3(100, 0));
                            Check(actor.GetComponents<RuleCastVfx>().Length == 1, kind + " 原生产 Prefab 自动且只接入一次");
                            var palette = actor.GetComponent<RuleGame.Presentation.Enemy.EnemyVfxPresenter>()?.Profile;
                            if (palette != null)
                            {
                                Check(actor.CastRule(AttributeId.Mass, RelationId.Enhance), kind + " 实际施法成功");
                                var properties = new MaterialPropertyBlock();
                                Rings(actor.GetComponent<RuleCastVfx>())[0].GetPropertyBlock(properties);
                                Check(properties.GetColor("_Accent") == palette.Accent, kind + " 规则特效与原招式配色一致");
                                RuleSystem.Instance.Cancel(AttributeId.Mass);
                            }
                            UnityEngine.Object.DestroyImmediate(actor.gameObject);
                        }
                        _enemy = Make(EnemyKind.ClockBoss, _wheel.Player.transform.position + Vector3.right * 7);
                        _other = Make(EnemyKind.PlushSeal, _wheel.Player.transform.position + Vector3.right * 30);
                        _enemyView = _enemy.GetComponent<RuleCastVfx>();
                        Check(Rings(_playerView).Length == 0, "未释放时没有常驻光环");
                        Check(_wheel.TryOpen(), "真实输入入口打开规则轮盘");
                        Wait(.45f); break;
                    case 1:
                        // 保留打开状态用于实际手部光球锚点校验，直接确认会被键盘自动松开逻辑关闭。
                        if (!_wheel.IsOpen) Check(_wheel.TryOpen(), "轮盘可重新打开");
                        _wheel.SelectAttributeIndex((int)AttributeId.Mass);
                        _wheel.SelectRelationIndex((int)RelationId.Enhance);
                        Check(_wheel.ConfirmSelection(), "真实轮盘确认质量增强成功");
                        Check(Rings(_playerView).Length == 1 && Rings(_enemyView).Length == 0, "只为实际玩家释放生成一圈");
                        var orb = _wheel.GetComponent<RuleSelectionOrbView>();
                        Check(Vector3.Distance(Rings(_playerView)[0].transform.position, orb.CastOrigin) < .02f,
                            "扩散原点与交付动作的手部光球重合");
                        Check(Rings(_playerView)[0].bounds.size.x > 30f, "神圣光潮覆盖三十米以上世界画布");
                        Wait(.22f); break;
                    case 2:
                        _radius = Property(Rings(_playerView)[0], "_Radius");
                        Check(_radius > .01f && _radius < .72f, "释放光环从核心向外展开");
                        Capture("Player_Expand");
                        TimeManager.Instance.SetPaused(true);
                        _age = Property(Rings(_playerView)[0], "_Age");
                        Wait(.2f); break;
                    case 3:
                        Check(Mathf.Approximately(_age, Property(Rings(_playerView)[0], "_Age")), "正式暂停冻结旋转/星屑/光环时钟");
                        TimeManager.Instance.SetPaused(false);
                        Wait(1.25f); break;
                    case 4:
                        Check(Rings(_playerView).Length == 0, "主角展开后整套光潮完全离开画面");
                        var playerRing = _playerView.GetComponentsInChildren<SpriteRenderer>(true).First(r => r.name == "Rule Star Halo");
                        Check(Mathf.Abs(Property(playerRing, "_Radius") - .72f) < .005f, "规则仍生效时记住画外回吸起点");
                        Capture("Player_Sustain");
                        Check(_enemy.CastRule(AttributeId.Mass, RelationId.Weaken), "敌人通过真实服务覆盖玩家质量规则");
                        Check(Rings(_enemyView).Length == 1 && Rings(_other.GetComponent<RuleCastVfx>()).Length == 0,
                            "敌方光环只落在实际施法者，不误用最近敌人");
                        Check(Property(Rings(_playerView)[0], "_Collapse") == 0, "旧规则收到收尾事件");
                        Wait(.25f); break;
                    case 5:
                        Check(Property(Rings(_playerView)[0], "_Radius") < .72f, "覆盖时旧光环向内回收");
                        Check(Property(Rings(_enemyView)[0], "_Radius") > .1f, "覆盖时新敌方光环同步展开");
                        var block = new MaterialPropertyBlock(); Rings(_enemyView)[0].GetPropertyBlock(block);
                        Check(block.GetColor("_Accent") == _enemy.GetComponent<RuleGame.Presentation.Enemy.EnemyVfxPresenter>().Profile.Accent, "闹钟规则光潮直接使用既有黄色特效配色");
                        Capture("Enemy_Expand");
                        Wait(1.15f); break;
                    case 6:
                        Check(Rings(_playerView).Length == 0, "旧规则回收后不留残影");
                        Capture("Enemy_Sustain");
                        Check(RuleSystem.Instance.Cancel(AttributeId.Mass), "实际主动取消敌方规则");
                        Wait(.32f); break;
                    case 7:
                        Check(Property(Rings(_enemyView)[0], "_Radius") < .72f && Property(Rings(_enemyView)[0], "_Collapse") > .1f,
                            "失效将整个符文光环向能量核收束");
                        Capture("Enemy_Return");
                        Wait(.65f); break;
                    case 8:
                        Check(Rings(_enemyView).Length == 0, "失效完整回收且渲染停止");
                        _fixture = RuleVerifyHarness.MakeDef(AttributeId.Friction, RelationId.Weaken, .5f);
                        RuleSystem.Initialize(new RuleVerifyHarness.TestCatalog(new[] { _fixture }));
                        SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
                        _playerView.SendMessage("Start"); _enemyView.SendMessage("Start");
                        Check(RuleSystem.Instance.Request(AttributeId.Friction, RelationId.Weaken, RuleSource.Player).Succeeded, "短时规则正常生效");
                        RuleSystem.Instance.Tick(.6f);
                        Wait(1.0f); break;
                    case 9:
                        Check(Rings(_playerView).Length == 0, "自然到期也完整回收");
                        RuleSystem.Instance.Request(AttributeId.Friction, RelationId.Weaken, RuleSource.Player);
                        _snapshot = SnapshotSystem.Instance.Capture();
                        _snapshot.Entities.Clear();
                        RuleSystem.Instance.Cancel(AttributeId.Friction);
                        SnapshotSystem.Instance.Restore(_snapshot);
                        Check(Rings(_playerView).Length == 0,
                            "快照恢复保持画外状态，不重放冲击");
                        _playerView.enabled = false;
                        Check(Rings(_playerView).Length == 0, "停用清理旧效果并解除订阅");
                        _playerView.enabled = true;
                        RuleSystem.Instance.Cancel(AttributeId.Friction);
                        for (int i = 0; i < 40; i++)
                            RuleSystem.Instance.Request(AttributeId.Friction, RelationId.Weaken, RuleSource.Player);
                        Check(_playerView.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.name == "Rule Star Halo") <= 12,
                            "连续四十次覆盖对象数量仍有界");
                        _playerView.enabled = false;
                        Check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("RuleCastVfx/StarHalo").shader), "实际 URP 渲染后 Shader 无编译错误");
                        Check(!_runtimeError, "无新增运行时错误");
                        _playerView.enabled = true;
                        RuleSystem.Initialize(new RuleVerifyHarness.TestCatalog(RuleVerifyHarness.MakeDefs(AttributeId.Time, 10f)));
                        SnapshotRoster.Initialize();
                        SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
                        _playerView.SendMessage("Start");
                        Check(!RuleSystem.Instance.Request(AttributeId.Time, RelationId.Revert, RuleSource.Player).Succeeded
                            && Rings(_playerView).Length == 0, "时间反转无快照时失败且不播放特效");
                        SnapshotSystem.Instance.CaptureToBuffer();
                        Check(RuleSystem.Instance.Request(AttributeId.Time, RelationId.Revert, RuleSource.Player).Succeeded
                            && Rings(_playerView).Length == 1, "时间反转实际恢复快照后仍播放一次释放光潮");
                        Wait(2.2f); break;
                    case 10:
                        Check(Rings(_playerView).Length == 0, "瞬时时间反转自动回吸，不遗留永久光潮");
                        _playerView.enabled = false;
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Capture(string name)
        {
            if (_camera == null)
            {
                var go = new GameObject("StarHaloValidationCamera"); _camera = go.AddComponent<Camera>();
                _camera.orthographic = true; _camera.orthographicSize = 4.2f;
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color(.018f, .023f, .052f);
                _target = new RenderTexture(1280, 900, 24); _target.Create(); _camera.targetTexture = _target;
            }
            bool enemy = name.StartsWith("Enemy");
            _camera.transform.position = (enemy ? _enemy.transform.position : _wheel.Player.transform.position) + new Vector3(0, .3f, -10);
            _camera.Render(); var previous = RenderTexture.active; RenderTexture.active = _target;
            var image = new Texture2D(1280, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
            Color[] pixels = image.GetPixels();
            var visible = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                .Where(r => r.name == "Rule Star Halo" && r.enabled).ToArray();
            foreach (var ring in visible) ring.enabled = false;
            _camera.Render(); image.ReadPixels(new Rect(0, 0, 1280, 900), 0, 0); image.Apply();
            Color[] baseline = image.GetPixels();
            foreach (var ring in visible) ring.enabled = true;
            int changed = pixels.Where((c, i) => c.r - baseline[i].r + c.g - baseline[i].g + c.b - baseline[i].b > .12f).Count();
                        if (name.EndsWith("Sustain")) Check(changed == 0, name + " 规则持续时没有任何残留光圈像素");
            else Check(changed > 1000, name + " URP 中神圣光潮实际可见");
            if (name.EndsWith("Expand"))
            {
                Check(changed > pixels.Length * .2f, name + " 扩散圣光至少覆盖画面五分之一");
                Check(pixels.Count(c => Mathf.Max(c.r, c.g, c.b) > .82f) > 20000,
                    name + " 使用大面积厚重辉光而非细线");
                if (!enemy) Check(pixels.Count(c => c.r > .8f && Mathf.Abs(c.r-c.g) < .045f && Mathf.Abs(c.g-c.b) < .045f) > 20000,
                    "玩家光潮以纯白为主");
            }
            RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image);
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            TimeManager.Instance?.SetPaused(false);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Folder + "/Results.txt", "Passed: " + _passed + "\n" + (error ?? "Failed: 0"));
            if (error != null) Debug.LogError(error);
            Debug.Log("RuleCastVfxValidation: " + _passed + " passed; " + (error == null ? "0 failed" : error));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
