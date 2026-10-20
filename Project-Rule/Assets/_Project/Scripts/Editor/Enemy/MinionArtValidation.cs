using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuleGame.Editor.Enemy
{
    /// <summary>在完整工程副本运行，验证正式预制体的动画、暂停、地下隐藏、死亡和回溯。</summary>
    public static class MinionArtValidation
    {
        private const string Pending = "RuleGame.MinionArtValidation";
        private static EnemyActor[] _actors;
        private static Sprite[] _paused;
        private static Sprite[] _first;
        private static int _passed, _stage;
        private static float _due, _lastSimulationTime;
        private static double _deadline;
        private static string Folder => Path.GetFullPath("Logs/MinionArt");

        public static void PrepareAndRunBatch()
        {
            if (MinionArtSetup.Kinds.Any(kind => AssetDatabase.LoadAssetAtPath<GameObject>(MinionArtSetup.PrefabPath(kind)) == null)) MinionArtSetup.Apply();
            var definitions = MinionArtSetup.Kinds.Select(kind => AssetDatabase.LoadAssetAtPath<GameObject>(MinionArtSetup.PrefabPath(kind)).GetComponent<EnemyActor>().Definition).ToArray();
            var before = definitions.Select(CombatSignature).ToArray();
            MinionArtSetup.Apply();
            var paths = MinionArtSetup.Kinds.Select(MinionArtSetup.PrefabPath).Concat(Directory.GetFiles(MinionArtSetup.DataFolder, "*_Animations.asset")).ToArray();
            var generated = paths.Select(File.ReadAllText).ToArray();
            MinionArtSetup.Apply();
            for (int i = 0; i < paths.Length; i++)
                if (File.ReadAllText(paths[i]) != generated[i]) throw new InvalidOperationException("重复接入改变了资源：" + paths[i]);
            for (int i = 0; i < definitions.Length; i++)
                if (CombatSignature(definitions[i]) != before[i]) throw new InvalidOperationException("接入覆盖了战斗参数");
            Debug.Log("[MinionArtValidation] 导入幂等且生命、伤害、移动、冷却等战斗配置保持不变（只延长交付死亡动作）。");
            RunBatch();
        }

        private static string CombatSignature(EnemyDefinition definition)
            => Regex.Replace(EditorJsonUtility.ToJson(definition), "\"Timings\":\\[[^\\]]*\\]", "\"Timings\":[]");

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
        }
        private static EnemySpriteAnimator View(EnemyActor actor) => actor.GetComponentInChildren<EnemySpriteAnimator>();
        private static SpriteRenderer Body(EnemyActor actor) => View(actor).GetComponent<SpriteRenderer>();
        private static EnemySpriteSequence Sequence(EnemyActor actor, string action)
            => Array.Find(View(actor).AnimationSet.Sequences, sequence => sequence.Action == action);
        private static void Begin()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                TimeManager.Initialize(); WorldRegistry.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                Physics2D.simulationMode = SimulationMode2D.Script;
                _actors = new EnemyActor[MinionArtSetup.Kinds.Length];
                _first = new Sprite[_actors.Length]; _paused = new Sprite[_actors.Length];
                for (int i = 0; i < _actors.Length; i++)
                {
                    var kind = MinionArtSetup.Kinds[i];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinionArtSetup.PrefabPath(kind));
                    var actor = UnityEngine.Object.Instantiate(prefab, new Vector3(i * 3f - 6f, 1.3f, 0f), Quaternion.identity).GetComponent<EnemyActor>();
                    _actors[i] = actor;
                    actor.enabled = false;
                    actor.Definition = UnityEngine.Object.Instantiate(actor.Definition);
                    actor.Definition.MinionMouseCooldown = .1f;
                    actor.Target = new GameObject("Target_" + kind).transform;
                    actor.Target.position = actor.transform.position + Vector3.left;
                    actor.SendMessage("Start");
                    Check(actor.GetComponent<FieldMinionView>() == null, kind + " 已替换占位表现");
                    Check(View(actor).transform.localScale == Vector3.one && Body(actor).color == Color.white, kind + " 固定比例及原色");
                    Check(actor.GetComponent<BoxCollider2D>().size == actor.Definition.BodySize, kind + " 身体碰撞尺寸保留");
                    Check(actor.GetComponent<EnemyHitFeedback>() != null, kind + " 保留受伤反馈接入");
                    Check(View(actor).AnimationSet.Sequences.All(s => s.Frames.Length > 0 && s.Frames.All(sprite => sprite != null)), kind + " 动画引用完整");
                    if (kind == EnemyKind.ClockMouse) actor.Brain.Tick(.2f);
                    else actor.Cue(kind == EnemyKind.PlushBird ? "Fly" : "Walk", .8f, true);
                    actor.Status.SetPaused(true);
                    View(actor).SendMessage("LateUpdate");
                    _first[i] = Body(actor).sprite;
                    actor.Status.SetPaused(false);
                }
                _lastSimulationTime = Time.time;
                _due = Time.time + .2f;
                _deadline = EditorApplication.timeSinceStartup + 45;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error); }
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("小怪美术验证超时");
                float dt = Time.time - _lastSimulationTime; _lastSimulationTime = Time.time;
                if (dt > 0f)
                    foreach (var actor in _actors)
                        if (actor.gameObject.activeSelf) actor.Brain.Tick(dt);
                if (Time.time < _due) return;
                if (_stage == 0)
                {
                    for (int i = 0; i < _actors.Length; i++)
                    {
                        var actor = _actors[i];
                        if (actor.Definition.Kind != EnemyKind.ExplosiveBlock)
                            Check(Body(actor).sprite != _first[i], actor.Definition.Kind + " 实际逐帧播放");
                        actor.Status.SetPaused(true); _paused[i] = Body(actor).sprite;
                    }
                    _stage++; _due = Time.time + .25f;
                }
                else if (_stage == 1)
                {
                    for (int i = 0; i < _actors.Length; i++)
                    {
                        var actor = _actors[i];
                        Check(Body(actor).sprite == _paused[i], actor.Definition.Kind + " 暂停冻结动画");
                        actor.Status.SetPaused(false);
                        var snapshot = actor.CaptureSnapshotState();
                        actor.Cue("Dead", .2f);
                        actor.RestoreSnapshotState(snapshot);
                        View(actor).SendMessage("LateUpdate");
                        Check(Body(actor).sprite != null && actor.gameObject.activeSelf, actor.Definition.Kind + " 快照恢复动画与存活");
                        if (actor.Definition.Kind == EnemyKind.ClockMouse)
                        {
                            // 真实 Brain 的 Idle → Pop → Walk → Hide → Idle。
                            Check(Body(actor).enabled, "发条鼠冒出时可见");
                            actor.Brain.Tick(actor.AnimationDuration + .01f);
                            Check(actor.Brain.StateName == "Walk", "发条鼠冒出后行走");
                            actor.Brain.Tick(actor.Definition.MinionMouseRange * 4f / actor.Definition.MoveSpeed + 2.01f);
                            actor.Brain.Tick(actor.AnimationDuration + .01f);
                            View(actor).SendMessage("LateUpdate");
                            Check(actor.Brain.StateName == "Idle" && !Body(actor).enabled, "发条鼠回到地下隐藏");
                            actor.Brain.Tick(.2f); View(actor).SendMessage("LateUpdate");
                            Check(Body(actor).enabled, "发条鼠再次冒出恢复显示");
                        }
                    }
                    CaptureGallery();
                    foreach (var actor in _actors)
                    {
                        actor.Status.ApplyHealthDelta(-actor.Status.Health);
                        actor.Brain.Tick(.01f);
                        if (actor.Definition.Kind == EnemyKind.ExplosiveBlock)
                        {
                            Check(actor.Brain.StateName == "Armed", "自爆方块致命伤先进入引信状态");
                            actor.Brain.Tick(actor.Definition.MinionFuseDuration + .01f);
                            Check(actor.AnimationName == "Explode", "引信结束播放交付爆炸序列");
                        }
                        else Check(actor.AnimationName == "Dead", actor.Definition.Kind + " 致命伤播放死亡序列");
                        actor.Cue(actor.AnimationName, Mathf.Max(.4f, Sequence(actor, actor.AnimationName).Duration));
                    }
                    _stage++; _due = Time.time + _actors.Max(actor => actor.AnimationDuration) + .15f;
                }
                else
                {
                    foreach (var actor in _actors)
                    {
                        var sequence = Sequence(actor, actor.AnimationName);
                        View(actor).SendMessage("LateUpdate");
                        Check(Body(actor).sprite == sequence.Frames[sequence.Frames.Length - 1], actor.Definition.Kind + " 死亡/爆炸末帧可播放");
                        actor.gameObject.SetActive(false); actor.gameObject.SetActive(true);
                        actor.Cue("Idle", 1f, true); View(actor).SendMessage("LateUpdate");
                        Check(Body(actor).sprite == Sequence(actor, "Idle").Frames[0], actor.Definition.Kind + " 再启用时无残留死亡帧");
                    }
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }
        private static void CaptureGallery()
        {
            Check(GraphicsSettings.currentRenderPipeline != null, "预览使用项目 URP 管线");
            foreach (var actor in _actors)
            {
                actor.Cue(actor.Definition.Kind == EnemyKind.PlushBird ? "Fly" : actor.Definition.Kind == EnemyKind.BatMinion ? "Walk" : "Idle", 1f, true);
                actor.Status.SetPaused(true); View(actor).SendMessage("LateUpdate");
            }
            var camera = new GameObject("MinionGalleryCamera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(3f, 2f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.15f, .18f, .23f);
            var target = new RenderTexture(1600, 700, 24);
            var image = new Texture2D(1600, 700, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            camera.targetTexture = target; RenderTexture.active = target;
            camera.Render(); image.ReadPixels(new Rect(0, 0, 1600, 700), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Folder, "IntegratedMinions.png"), image.EncodeToPNG());
            RenderTexture.active = previous; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
            foreach (var actor in _actors) actor.Status.SetPaused(false);
        }
        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
            _passed++; Debug.Log("[MinionArtValidation] PASS " + message);
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "Results.txt"), $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\n{error}");
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
