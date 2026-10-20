using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>以真实预制体检查感知、冷却响应、锁向和动作时钟。</summary>
    public static class MinionDecisionValidation
    {
        private const string Pending = "RuleGame.MinionDecisionValidation";
        private static int _passed, _failed, _fixture;
        private static double _due;
        private static readonly List<string> Failures = new List<string>();
        private static readonly List<GameObject> Owned = new List<GameObject>();
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离工程运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                _due = EditorApplication.timeSinceStartup + .5; EditorApplication.update += Run;
            };
        }
        private static EnemyActor Spawn(EnemyKind kind, float targetX = 2f)
        {
            var center = new Vector3(_fixture++ * 30f, 3f);
            var target = new GameObject("DecisionTarget"); Owned.Add(target);
            target.transform.position = center + Vector3.right * targetX;
            target.AddComponent<BoxCollider2D>().size = new Vector2(.7f, 1.2f);
            bool boss = kind == EnemyKind.BrickBoss || kind == EnemyKind.ClockBoss || kind == EnemyKind.RabbitBoss || kind == EnemyKind.BatBoss;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(boss ? "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab" : MinionArtSetup.PrefabPath(kind));
            var actor = UnityEngine.Object.Instantiate(prefab, center, Quaternion.identity).GetComponent<EnemyActor>();
            Owned.Add(actor.gameObject); actor.enabled = false;
            actor.Definition = UnityEngine.Object.Instantiate(actor.Definition);
            actor.Definition.Health = 500f;
            actor.Definition.MinionNavigationEnabled = false;
            actor.Definition.MinionSenseRange = 12f; actor.Definition.MinionLeashRange = 14f;
            actor.Target = target.transform; actor.UseSpawnRelativeArena = false;
            actor.ArenaX = new Vector2(center.x - 15f, center.x + 15f); actor.ArenaY = new Vector2(-5f, 15f);
            actor.SendMessage("Start"); Physics2D.SyncTransforms(); return actor;
        }
        private static EnemySpriteAnimator View(EnemyActor actor) => actor.GetComponentInChildren<EnemySpriteAnimator>();
        private static SpriteRenderer Body(EnemyActor actor) => View(actor).GetComponent<SpriteRenderer>();
        private static EnemySpriteSequence Sequence(EnemyActor actor, string action)
            => Array.Find(View(actor).AnimationSet.Sequences, sequence => sequence.Action == action);
        private static void Step(EnemyActor actor, float duration)
        {
            int count = Mathf.CeilToInt(duration / .02f);
            for (int i = 0; i < count; i++) { actor.Brain.Tick(Mathf.Min(.02f, duration - i * .02f)); View(actor).SendMessage("LateUpdate"); }
        }
        private static void FlyStep(EnemyActor actor, float duration)
        {
            for (int i = 0; i < Mathf.CeilToInt(duration / .02f); i++)
            {
                actor.Brain.Tick(.02f); actor.Motor.Tick(.02f);
                Physics2D.Simulate(.02f); Physics2D.SyncTransforms(); View(actor).SendMessage("LateUpdate");
            }
        }
        private static void Case(string name, Action action)
        {
            try { action(); }
            catch (Exception error) { _failed++; Failures.Add(name + ": " + error.Message); }
            finally
            {
                foreach (var shot in UnityEngine.Object.FindObjectsByType<EnemyProjectile>()) UnityEngine.Object.DestroyImmediate(shot.gameObject);
                foreach (var go in Owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                Owned.Clear();
            }
        }
        private static void Run()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            EditorApplication.update -= Run;
            try
            {
                TimeManager.Initialize(); WorldRegistry.Initialize(); RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                Physics2D.simulationMode = SimulationMode2D.Script;
                Case("贴身墙体", () =>
                {
                    var actor = Spawn(EnemyKind.StoneGuard, 1.5f);
                    var wall = new GameObject("OpaqueWall"); Owned.Add(wall); wall.layer = LayerMask.NameToLayer("Ground");
                    wall.transform.position = actor.transform.position + Vector3.right * .9f;
                    wall.AddComponent<BoxCollider2D>().size = new Vector2(.15f, 5f); Physics2D.SyncTransforms();
                    Step(actor, .1f); Check(actor.Brain.StateName != "Attack", "贴身仍检查墙体，不能隔墙起手");
                    wall.SetActive(false); Physics2D.SyncTransforms(); Step(actor, .1f);
                    Check(actor.Brain.StateName == "Attack", "移除遮挡后及时攻击");
                });
                Case("守卫冷却", () =>
                {
                    var actor = Spawn(EnemyKind.StoneGuard, 1.2f); Step(actor, .02f);
                    Check(actor.Brain.StateName == "Attack", "守卫近战正常起手");
                    Step(actor, actor.AnimationDuration + .04f);
                    Check(actor.Brain.StateName == "Ignore", "保留原有攻击冷却");
                    actor.Target.position = actor.transform.position + Vector3.right * 4f; Step(actor, .1f);
                    Check(actor.Motor.Intent.x > .1f && actor.AnimationName == "Walk", "冷却期间仍跟进可见目标并播放移动动作");
                    Check(actor.Brain.StateName == "Ignore", "跟进不能绕过冷却连打");
                });
                Case("冷却破韧", () =>
                {
                    var actor = Spawn(EnemyKind.StoneGuard, 1.2f); Step(actor, .02f);
                    Step(actor, actor.AnimationDuration + .04f);
                    Check(actor.Brain.StateName == "Ignore", "破韧测试进入攻击冷却");
                    actor.ApplyDamage(new DamageInfo { Amount = actor.Definition.MinionStaggerThreshold + 1f });
                    Check(actor.Brain.StateName == "Hurt", "攻击冷却不免疫破韧，正常进入受击状态");
                    Step(actor, actor.AnimationDuration + .1f);
                    Check(actor.Brain.StateName != "Attack" && actor.Brain.StateName != "Hurt", "受击恢复仍保留攻击冷却，不能提前出手");
                });
                Case("鸟攻击锁向", () =>
                {
                    var actor = Spawn(EnemyKind.PlushBird, -2f); EnemyProjectile shot = null;
                    actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                    Step(actor, .02f); Check(actor.Brain.StateName == "Attack", "鸟进入攻击");
                    Step(actor, actor.AnimationDuration * .35f);
                    actor.Target.position = actor.transform.position + Vector3.right * 2f;
                    Step(actor, actor.AnimationDuration * .35f);
                    Check(actor.Facing < 0f && shot != null && shot.Velocity.x < 0f, "锁向后身体朝向和射击方向一致");
                });
                Case("鸟间距稳定", () =>
                {
                    var actor = Spawn(EnemyKind.PlushBird, 2f);
                    actor.Definition.AttackCooldown = 100f; actor.Definition.AttackRange = 10f; actor.Definition.MinionPreferredRange = 4f;
                    Step(actor, .02f); Step(actor, actor.AnimationDuration + .02f);
                    Action<float> gap = x => { actor.Target.position = actor.transform.position + Vector3.right * x; Step(actor, .02f); };
                    gap(4.65f); Check(actor.Motor.Intent.x > 0f, "鸟在目标过远时靠近");
                    gap(4.3f); Check(actor.Motor.Intent.x > 0f, "进入缓冲区仍完成靠近");
                    gap(4.1f); Check(Mathf.Abs(actor.Motor.Intent.x) < .001f, "达到理想间距后悬停");
                    gap(4.3f); Check(Mathf.Abs(actor.Motor.Intent.x) < .001f, "小范围距离波动不反复启停");
                    gap(3.35f); Check(actor.Motor.Intent.x < 0f, "过近时后撤");
                    gap(3.9f); Check(Mathf.Abs(actor.Motor.Intent.x) < .001f, "后撤达到间距后停止");
                });
                Case("海豹滑行", () =>
                {
                    var actor = Spawn(EnemyKind.PlushSeal, -2f);
                    for (int i = 0; i < 50 && actor.Brain.StateName != "Slide"; i++) Step(actor, .02f);
                    Check(actor.Brain.StateName == "Slide" && actor.Facing < 0f, "海豹按目标方向准备滑行");
                    Check(actor.AnimationName == "Idle" && actor.Motor.Intent == Vector2.zero, "滑行前摇保持蓄势，不提前播放位移动作");
                    Step(actor, .3f);
                    Check(actor.AnimationName == "Slide" && actor.AnimationLoop && actor.Motor.Intent.x < 0f, "启动滑行时同步循环滑行动作");
                    var snapshot = actor.CaptureSnapshotState(); var sprite = Body(actor).sprite;
                    Step(actor, .25f); actor.RestoreSnapshotState(snapshot); View(actor).SendMessage("LateUpdate");
                    Check(actor.AnimationName == "Slide" && Body(actor).sprite == sprite, "滑行快照恢复准确动作与帧");
                    Step(actor, actor.Definition.MinionSlideDuration + .1f);
                    Check(actor.Brain.StateName == "Wander", "滑行按配置时长结束，恢复决策");
                });
                Case("蝙蝠朝向", () =>
                {
                    var actor = Spawn(EnemyKind.BatMinion, -3f); Step(actor, .02f);
                    actor.Target.position = actor.transform.position + Vector3.right * 3f; Step(actor, .02f);
                    Check(actor.Facing > 0f && actor.Motor.Intent.x > 0f, "蝙蝠追击换侧时及时转向");
                    actor.Target = null; Step(actor, actor.Definition.MinionTargetMemory + .1f);
                    Check(actor.Brain.StateName == "Idle" && actor.Motor.Intent.sqrMagnitude < .001f && actor.AnimationName == "Fly", "蝙蝠回到出生点后悬停拍翼，不在半空倒挂");
                });
                Case("动画权威时钟", () =>
                {
                    var actor = Spawn(EnemyKind.PlushSeal, 2f);
                    typeof(EnemyBrain).GetMethod("Go", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(actor.Brain, new object[] { "BellySlap" });
                    actor.Cue("BellySlap", .8f);
                    Step(actor, .41f); var frames = Sequence(actor, "BellySlap").Frames;
                    Check(Body(actor).sprite == frames[frames.Length / 2], "动画精确跟随逻辑推进，不依赖渲染帧率");
                    var snapshot = actor.CaptureSnapshotState(); var sprite = Body(actor).sprite;
                    for (int i = 0; i < 20; i++) View(actor).SendMessage("LateUpdate");
                    Check(Body(actor).sprite == sprite, "仅重复渲染不会自行推进攻击帧");
                    actor.Status.SetPaused(true); Step(actor, .3f);
                    Check(Body(actor).sprite == sprite, "实体暂停冻结逻辑与动画");
                    actor.Status.SetPaused(false); Step(actor, .2f); actor.RestoreSnapshotState(snapshot); View(actor).SendMessage("LateUpdate");
                    Check(actor.AnimationName == "BellySlap" && Body(actor).sprite == sprite, "回溯恢复状态内切换的动画与精确进度");
                });
                Case("抱熊战斗站姿", () =>
                {
                    var actor = Spawn(EnemyKind.HugBear, 1.05f);
                    Step(actor, actor.Duration("Stand") + .1f);
                    Check(actor.Brain.StateName == "Hug", "抱熊完成站起并进入拥抱");
                    actor.ApplyDamage(new DamageInfo { Amount = 1f }); Step(actor, .02f);
                    Check(actor.Brain.StateName == "Chase" && actor.AnimationName != "Idle", "挣脱后的拥抱冷却保持站姿，不切坐姿待机");
                    Check(Sequence(actor, "Ready") != null && Body(actor).sprite == Sequence(actor, "Ready").Frames[0], "站定使用完整站立帧");
                    // Actor 的命中保护由真实物理步推进，不能只推进 Brain 的动作时间。
                    for (int i = 0; i < Mathf.CeilToInt(.15f / Time.fixedDeltaTime); i++) actor.SendMessage("FixedUpdate");
                    actor.ApplyDamage(new DamageInfo { Amount = actor.Definition.MinionStaggerThreshold + 1f });
                    Check(actor.Brain.StateName == "Hurt", "抱熊正常进入破韧受击");
                    Step(actor, actor.AnimationDuration + .04f);
                    Check(actor.Brain.StateName == "Chase" && actor.AnimationName == "Ready", "战斗受击恢复直接站立接战，不重坐再起身");
                    var snapshot = actor.CaptureSnapshotState(); Step(actor, .1f); actor.RestoreSnapshotState(snapshot); Step(actor, .02f);
                    Check(actor.AnimationName == "Ready", "回溯保留交战站姿");
                    actor.Target.gameObject.SetActive(false); Step(actor, .06f);
                    Check(actor.Brain.StateName == "Sit", "失去目标且回到出生点才坐下");
                    actor.Target.gameObject.SetActive(true); Step(actor, .02f);
                    Check(actor.Brain.StateName == "Stand", "坐下过程中发现目标及时重新站起");
                });
                Case("小鸟立体飞行", () =>
                {
                    var actor = Spawn(EnemyKind.PlushBird, 4f); actor.Definition.AttackRange = .1f;
                    float start = actor.transform.position.y;
                    actor.Target.position += Vector3.up * 2f; FlyStep(actor, .5f);
                    Check(actor.transform.position.y > start + .3f, "目标升高时小鸟通过真实运动上升");
                    float high = actor.transform.position.y;
                    actor.Target.position = new Vector3(actor.Target.position.x, start - 3f); FlyStep(actor, .5f);
                    Check(actor.transform.position.y < high - .2f, "目标降低时小鸟通过真实运动下降");
                    Check(actor.AnimationName == "Fly", "升降保持原有拍翼动画");
                });
                Case("小鸟巡飞和高度边界", () =>
                {
                    var actor = Spawn(EnemyKind.PlushBird, 3f); actor.Definition.AttackRange = .1f;
                    var target = actor.Target; actor.Target = null; Step(actor, .6f);
                    Check(Mathf.Abs(actor.Motor.Intent.y) > .1f, "无目标巡飞也有上下浮动");
                    actor.Target = target; target.position += Vector3.up * 4f;
                    actor.ArenaY = new Vector2(-5f, actor.transform.position.y + .8f);
                    float top = actor.ArenaY.y - actor.Definition.BodySize.y * .5f;
                    FlyStep(actor, 2f);
                    Check(actor.transform.position.y <= top, "追高处目标时保留身体高度并限制在活动范围内");
                });
                foreach (var kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
                    Case(kind + " 共用动画", () =>
                    {
                        var actor = Spawn(kind, 6f); Step(actor, .12f);
                        Check(Body(actor).sprite != null, kind + " 实际状态有有效动画");
                        var snapshot = actor.CaptureSnapshotState(); var sprite = Body(actor).sprite;
                        actor.Status.SetPaused(true); Step(actor, .4f);
                        Check(Body(actor).sprite == sprite, kind + " 暂停保持动作帧");
                        actor.Status.SetPaused(false); Step(actor, .2f); actor.RestoreSnapshotState(snapshot); View(actor).SendMessage("LateUpdate");
                        Check(Body(actor).sprite == sprite, kind + " 快照恢复精确动画帧");
                    });
            }
            catch (Exception error) { _failed++; Failures.Add(error.ToString()); }
            var folder = Path.GetFullPath("Logs/MinionDecisions"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Results.txt"), $"Passed: {_passed}\nFailed: {_failed}\n" + string.Join("\n", Failures));
            foreach (var failure in Failures) Debug.LogWarning("[MinionDecisions] FAIL " + failure);
            SessionState.SetBool(Pending, false); EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            _passed++; Debug.Log("[MinionDecisions] PASS " + message);
        }
    }
}
