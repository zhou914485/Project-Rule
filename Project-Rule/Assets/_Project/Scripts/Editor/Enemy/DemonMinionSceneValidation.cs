using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;

namespace RuleGame.Editor.Enemy
{
    /// <summary>实际保存的场景、原画和移动掩体的 Play Mode 验证。</summary>
    public static class DemonMinionSceneValidation
    {
        private const string Pending = "RuleGame.DemonMinionScene";
        private static float _due;
        private static double _deadline;
        private static EnemyActor _trackedTop;
        private static bool _topMoved;
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene(DemonMinionSetup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod] private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                _topMoved = false;
                foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyActor>())
                    if (enemy.Definition.Kind == EnemyKind.SpinningTop) _trackedTop = enemy;
                _due = Time.time + 2f; _deadline = EditorApplication.timeSinceStartup + 60f; EditorApplication.update += Verify;
            };
        }
        private static void Verify()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_trackedTop != null && Mathf.Abs(_trackedTop.transform.position.x - 7f) > .3f) _topMoved = true;
            if (Time.time < _due && EditorApplication.timeSinceStartup < _deadline) return;
            EditorApplication.update -= Verify;
            int passed = 0; string error = null;
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passed++; }
            try
            {
                var actors = UnityEngine.Object.FindObjectsByType<EnemyActor>();
                Check(actors.Length == 2, "集中场景不是两种新怪");
                EnemyActor disco = null, top = null;
                foreach (var actor in actors)
                {
                    Check(actor.Brain != null && actor.Target != null && actor.Combat != null, "自动接线失效");
                    Check(actor.GetComponentInChildren<EnemySpriteAnimator>().GetComponent<SpriteRenderer>().sprite != null, "原画没有显示");
                    if (actor.Definition.Kind == EnemyKind.DiscoBall) disco = actor;
                    if (actor.Definition.Kind == EnemyKind.SpinningTop) top = actor;
                }
                Check(disco != null && top != null, "场景敌人类型有误");
                Check(Vector2.Distance(disco.transform.position, new Vector2(-5f, 5.5f)) < .001f, "悬挂球移动了");
                Check(top.AnimationName == "Spin" && _topMoved, $"陀螺没有旋转平移：state={top.Brain.StateName}, animation={top.AnimationName}, position={top.transform.position}, t={Time.time}, dt={Time.fixedDeltaTime}");
                var cover = UnityEngine.Object.FindAnyObjectByType<MovingPlatform>();
                Check(cover != null && Vector2.Distance(cover.transform.position, cover.StartPoint) > .3f, "遮挡平台没有真实移动");
                Check(disco.GetComponentInChildren<DiscoRadiationView>().GetComponent<MeshFilter>().sharedMesh.vertexCount > 250, "场景缺少完整辐射光区");
                var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                Check(player.GetComponent<EntityStatus>().Health > 0f, "玩家无法开始测试");
                Directory.CreateDirectory("Logs/DemonMinions");
                // 用独立预览相机显示完整试验区，不修改实际玩家相机或保存场景。
                var camera = new GameObject("ScenePreview").AddComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 8f; camera.transform.position = new Vector3(0f, 5f, -10f);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.19f, .2f, .25f);
                var target = new RenderTexture(1600, 900, 24); var previous = RenderTexture.active;
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                var image = new Texture2D(1600, 900, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                File.WriteAllBytes("Logs/DemonMinions/DiscoTopScene.png", image.EncodeToPNG());
                camera.targetTexture = null; RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(camera.gameObject);
                TimeManager.Instance.SetPaused(true);
            }
            catch (Exception exception) { error = exception.ToString(); }
            Directory.CreateDirectory("Logs/DemonMinions");
            File.WriteAllText("Logs/DemonMinions/Scene.txt", $"Passed: {passed}\nFailed: {(error == null ? 0 : 1)}\n{error}");
            SessionState.SetBool(Pending, false); EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
