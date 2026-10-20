using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Enemy;
using RuleGame.Presentation.RuleWheel;
using RuleGame.UI.RuleWheel;
using RuleGame.Infrastructure.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>在工程副本录制真实规则事件与生产 Shader，不改动特效时钟或材质参数。</summary>
    public static class RuleCastVfxPreview
    {
        private const string Pending = "RuleGame.RuleVfx.Preview";
        private const string Folder = "Logs/RuleVfxPreview";
        private static readonly string[] Names = { "Player", "Bat", "Clock", "Rabbit", "Brick" };
        private static readonly EnemyKind[] Kinds = { EnemyKind.BatBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BrickBoss };
        private static int _clip, _frame, _lastFrame;
        private static bool _prepared, _cast, _cancelled;
        private static double _due, _start, _nextCapture;
        private static Camera _camera;
        private static EnemyActor _actor;
        private static RuleCastVfx _view;
        private static RenderTexture _target;
        private static Texture2D _pixels;
        private static Recording _recording;
        [Serializable] private sealed class Recording { public string name; public int fps = 24; public List<Frame> frames = new List<Frame>(); }
        [Serializable] private sealed class Frame { public string file; public double time; public string phase; public int visible; }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在工程副本录制。");
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/EnemyLabs/Test_Enemy_BatBoss.unity");
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
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
            _due = EditorApplication.timeSinceStartup + .65;
            EditorApplication.update += Tick;
        }
        private static void Setup()
        {
            var wheel = UnityEngine.Object.FindAnyObjectByType<RuleWheelPresenter>();
            if (wheel == null) throw new InvalidOperationException("缺少生产轮盘。");
            _camera = wheel.WorldCamera;
            foreach (var follow in _camera.GetComponents<MonoBehaviour>())
                if ((follow.GetType().Namespace ?? "").StartsWith("RuleGame.")
                    || (follow.GetType().Namespace ?? "").StartsWith("Unity.Cinemachine")) follow.enabled = false;
            foreach (var actor in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                actor.enabled = false;
                foreach (var animator in actor.GetComponentsInChildren<EnemySpriteAnimator>()) animator.enabled = false;
                foreach (var effects in actor.GetComponents<EnemyVfxPresenter>()) effects.enabled = false;
                if (_clip > 0 && actor.Definition.Kind == Kinds[_clip - 1]) _actor = actor;
            }
            foreach (var shot in UnityEngine.Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None)) shot.gameObject.SetActive(false);
            foreach (var beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>(FindObjectsSortMode.None)) beam.gameObject.SetActive(false);
            wheel.Player.enabled = false;
            foreach (var animator in wheel.Player.GetComponentsInChildren<Animator>()) animator.enabled = false;
            foreach (var body in UnityEngine.Object.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None)) body.simulated = false;
            _view = _clip == 0 ? wheel.GetComponent<RuleCastVfx>() : _actor.GetComponent<RuleCastVfx>();
            if (_view == null) throw new InvalidOperationException("生产特效未自动接线。");
            Vector3 origin = _clip == 0 ? wheel.Player.transform.position : _actor.transform.position;
            _camera.transform.position = origin + new Vector3(0f, .5f, -10f);
            _camera.orthographicSize = 7.5f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(.026f, .034f, .065f);
            _target = new RenderTexture(960, 540, 24); _target.Create(); _camera.targetTexture = _target;
            _pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            Directory.CreateDirectory(Folder + "/" + Names[_clip]);
            _recording = new Recording { name = Names[_clip] };
            _frame = 0; _cast = _cancelled = false;
            _start = Time.unscaledTimeAsDouble; _nextCapture = 0;
            _prepared = true;
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_lastFrame == Time.frameCount || EditorApplication.timeSinceStartup < _due) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (!_prepared) Setup();
                double elapsed = Time.unscaledTimeAsDouble - _start;
                if (!_cast && elapsed >= .5)
                {
                    bool success = _clip == 0
                        ? RuleSystem.Instance.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player).Succeeded
                        : _actor.CastRule(AttributeId.Mass, RelationId.Enhance);
                    if (!success) throw new InvalidOperationException("实际规则请求失败。");
                    _cast = true;
                }
                if (!_cancelled && elapsed >= 2.3)
                { RuleSystem.Instance.Cancel(AttributeId.Mass); _cancelled = true; }
                if (elapsed >= _nextCapture)
                {
                    Capture(elapsed);
                    _nextCapture = elapsed + 1.0 / 24.0;
                }
                if (elapsed < 3.7) return;
                if (!_recording.frames.Any(f => f.phase == "Active" && f.visible == 0)
                    || !_recording.frames.Any(f => f.phase == "Return" && f.visible > 0))
                    throw new InvalidOperationException("录制缺少完全出屏或实际回吸阶段。");
                File.WriteAllText(Folder + "/" + Names[_clip] + "/frames.json", JsonUtility.ToJson(_recording, true));
                _camera.targetTexture = null;
                _target.Release(); UnityEngine.Object.DestroyImmediate(_target); UnityEngine.Object.DestroyImmediate(_pixels);
                Debug.Log("Recorded " + Names[_clip] + ": " + _frame + " actual URP frames.");
                if (++_clip >= Names.Length) { Finish(null); return; }
                _prepared = false; _actor = null;
                EditorSceneManager.LoadSceneInPlayMode("Assets/_Project/Scenes/EnemyLabs/Test_Enemy_"
                    + Kinds[_clip - 1].ToString() + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
                _due = EditorApplication.timeSinceStartup + .65;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Capture(double time)
        {
            var previous = RenderTexture.active;
            _camera.Render(); RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); _pixels.Apply();
            string file = "frame_" + (_frame++).ToString("D4") + ".png";
            File.WriteAllBytes(Folder + "/" + Names[_clip] + "/" + file, _pixels.EncodeToPNG());
            RenderTexture.active = previous;
            _recording.frames.Add(new Frame { file = file, time = time,
                phase = time < .5 ? "Before" : time < 1.38 ? "Expand" : time < 2.3 ? "Active" : time < 2.85 ? "Return" : "After",
                visible = _view.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.name == "Rule Star Halo" && r.enabled) });
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            if (error != null) Debug.LogError(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
