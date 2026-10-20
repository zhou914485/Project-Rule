using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RuleGame.Presentation.Enemy;
using RuleGame.Infrastructure.Snapshot;
using UnityEngine.Rendering;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>在可丢弃的工程副本中进入 Play Mode，验证真实组件与 HFSM，不保存生产场景。</summary>
    public static class BossCombatValidation
    {
        private const string Pending = "RuleGame.Enemy.CombatValidation";
        private const string Tactics = "RuleGame.Enemy.TacticsValidation";
        private const string RabbitTactics = "RuleGame.Enemy.RabbitTacticsValidation";
        private const string ClockHands = "RuleGame.Enemy.ClockHandsValidation";
        private const string ClockRewind = "RuleGame.Enemy.ClockRewindValidation";
        private const string SnapshotCamera = "RuleGame.Enemy.SnapshotCameraValidation";
        private static readonly List<EnemyActor> Actors = new List<EnemyActor>();
        private static readonly List<Action<EnemyActor>> Cases = new List<Action<EnemyActor>>();
        private static int _passed, _failed;
        private static double _deadline;
        private static bool _tactics;
        private static string Output => Environment.GetEnvironmentVariable("RULE_BOSS_OUTPUT")
            ?? Path.GetFullPath(Path.Combine(Application.dataPath, _tactics ? "../Logs/BossTacticsValidation.txt" : "../Logs/BossCombatValidation.txt"));

        public static void RunRabbitTacticsBatch()
        {
            SessionState.SetBool(RabbitTactics, true);
            RunBatch();
        }

        public static void RunClockHandsBatch()
        {
            SessionState.SetBool(ClockHands, true);
            RunBatch();
        }

        public static void RunClockRewindBatch()
        {
            SessionState.SetBool(ClockRewind, true);
            RunBatch();
        }

        public static void RunSnapshotCameraBatch()
        {
            SessionState.SetBool(SnapshotCamera, true);
            RunBatch();
        }

        public static void RunTacticsBatch()
        {
            SessionState.SetBool(Tactics, true);
            RunBatch();
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本中运行。");
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

        private static void Begin()
        {
            TimeManager.Initialize(); WorldRegistry.Initialize();
            if (SessionState.GetBool(ClockRewind, false) || SessionState.GetBool(ClockHands, false))
                RuleSystem.Initialize(new TimeRevertCatalog());
            else RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
            _deadline = EditorApplication.timeSinceStartup + 30;
            _tactics = SessionState.GetBool(Tactics, false);
            if (SessionState.GetBool(RabbitTactics, false))
            {
                AddRabbitTacticsCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (SessionState.GetBool(ClockHands, false))
            {
                AddClockHandCases();
                AddClockRewindCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (SessionState.GetBool(ClockRewind, false))
            {
                AddClockRewindCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (SessionState.GetBool(SnapshotCamera, false))
            {
                AddSnapshotCameraCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            if (_tactics)
            {
                AddTacticsCases();
                EditorApplication.update += AwaitReady;
                return;
            }
            Add(EnemyKind.BrickBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "HammerPlayer", "积木近距锤击"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 5); Step(a); Check(a.Brain.StateName == "Advance", "积木中距主动压近"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 8); Step(a); Check(a.Brain.StateName == "Throw", "积木远距投掷"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 11); Step(a); Check(a.Brain.StateName == "Walk" && a.Motor.Intent.x > 0, "超射程追击"); });
            Add(EnemyKind.BrickBoss, a => { Target(a, 2, 5); Step(a); Check(a.Brain.StateName == "Throw", "高处目标使用投掷"); });
            Add(EnemyKind.BrickBoss, a =>
            {
                Cooldown(a); Target(a, 1); Step(a);
                Check(a.Motor.Intent.x < 0 && a.Facing > 0, "冷却时面向玩家后撤");
                Target(a, 7); Step(a);
                Check(a.Motor.Intent.x > 0 && a.Brain.StateName == "Walk", "冷却时靠近远处玩家");
                Step(a, a.Definition.AttackCooldown + 0.01f);
                Check(a.Brain.StateName == "Throw", "冷却结束重新按距离选招");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                Cooldown(a); a.transform.position = new Vector3(-11f, 0, 0); Target(a, 1); Step(a);
                Check(a.Motor.Intent.x == 0, "后撤受场地边界约束");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                Cooldown(a); Target(a, 4); Step(a); Target(a, 3.3f); Step(a);
                Check(a.Brain.StateName == "Walk", "进入较窄停止区前继续移动");
                Target(a, 3.1f); Step(a); Target(a, 3.35f); Step(a);
                Check(a.Brain.StateName == "Idle" && a.Motor.Intent == Vector2.zero, "距离边缘不反复启停");
            });
            Add(EnemyKind.BatBoss, a => { Target(a, 2); Step(a); Check(a.Brain.StateName == "Slash", "蝙蝠近身优先斩击，不先逃走"); });
            Add(EnemyKind.BatBoss, a => { Target(a, 6); Step(a); Check(a.Brain.StateName == "Fly" && a.Motor.Intent.sqrMagnitude > 0, "蝙蝠先空中换位"); });
            Add(EnemyKind.BatBoss, a => { Target(a, 11); Step(a); Check(a.Brain.StateName == "Fly" && a.Motor.Intent.x > 0, "蝙蝠远距飞行追击"); });
            Add(EnemyKind.BatBoss, a => { Target(a, 2, 6); Step(a); Check(a.Brain.StateName != "Slash", "蝙蝠不向高差过大目标空斩"); });
            Add(EnemyKind.BatBoss, a =>
            {
                Target(a, 6, 4); AdvanceTo(a, "Windup");
                Check(a.Brain.Phase == 1, "蝙蝠一阶段解锁俯冲");
                Check(a.AnimationName == "DashWindup", "俯冲预警使用独立短展翅序列");
                Step(a, 0.2f);
                Check(a.Brain.StateName == "Windup", "俯冲预警不会提前结束");
                Target(a, -6, 4); Step(a, 0.31f); Step(a);
                Check(a.Brain.StateName == "Dash" && a.Motor.Intent.x > 0 && a.Facing > 0, "俯冲保持预警时锁定的路线和朝向");
                Vector2 direction = a.Motor.Intent; object saved = a.CaptureSnapshotState();
                Step(a, 0.2f); a.RestoreSnapshotState(saved); Step(a);
                Check(a.Brain.StateName == "Dash" && a.Motor.Intent == direction, "快照恢复锁定的俯冲路线");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                Target(a, 6, 4); AdvanceTo(a, "Windup"); a.Brain.OnHit(30);
                Check(a.Brain.StateName == "Hurt", "达到破韧门槛可打断俯冲蓄力");
                Step(a, 0.46f);
                Check(a.Brain.StateName == "Evade", "蝙蝠从硬直恢复后主动脱离");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                Target(a, 6, 4); a.Status.ApplyHealthDelta(-334); Step(a); Step(a, 2.6f);
                AdvanceTo(a, "Windup");
                Check(a.Brain.Phase == 2 && a.AnimationDuration < a.Definition.BatDashWindup, "后续阶段缩短俯冲预警");
                object saved = a.CaptureSnapshotState(); float duration = a.AnimationDuration;
                Step(a, 1); a.RestoreSnapshotState(saved);
                Check(Mathf.Approximately(a.AnimationDuration, duration), "恢复快照保留阶段对应的攻击节奏");
            });
            Add(EnemyKind.ClockBoss, a => { Target(a, 2); Step(a, 2.4f); Check(a.Brain.StateName == "JumpShake", "发条贴近且冲撞就绪时响铃反击"); });
            Add(EnemyKind.ClockBoss, a =>
            {
                Target(a, 5); Step(a);
                Check(a.Brain.StateName == "Idle", "发条保持激光作战距离");
                Target(a, 11); Step(a);
                Check(a.Motor.Intent.x > 0, "发条远处追击");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 2); Step(a, 0.3f); Step(a, 0.8f);
                Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0, "兔子近身藏身撤退");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 11); Step(a, .3f); Step(a, 2.1f);
                Step(a, a.Definition.BossRestDuration + .01f); Step(a);
                Check(a.Motor.Intent.x > 0, "兔子超射程靠近");
            });
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
                Add(kind, a =>
                {
                    a.Brain.OnHit(10);
                    Check(a.Brain.StateName != "Hurt", a.Definition.Kind + " 普通单次命中不打断");
                    a.Brain.OnHit(10); a.Brain.OnHit(10);
                    Check(a.Brain.StateName == "Hurt", a.Definition.Kind + " 累计伤害触发硬直");
                    Check(a.AnimationDuration <= 0.45f, a.Definition.Kind + " 硬直时长上限");
                    Step(a, 0.46f); a.Brain.OnHit(100);
                    Check(a.Brain.StateName != "Hurt", a.Definition.Kind + " 恢复后冷却禁止再打断");
                    Step(a, 4.01f); a.Brain.OnHit(10);
                    Check(a.Brain.StateName != "Hurt", a.Definition.Kind + " 冷却期间不积攒硬直");
                    a.Brain.OnHit(20);
                    Check(a.Brain.StateName == "Hurt", a.Definition.Kind + " 冷却结束可再次破韧");
                });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(20); Step(a, 1.6f); a.Brain.OnHit(10);
                Check(a.Brain.StateName != "Hurt", "久未命中清空累计伤害");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(10); object snapshot = a.CaptureSnapshotState();
                a.Brain.OnHit(20); a.RestoreSnapshotState(snapshot); a.Brain.OnHit(10);
                Check(a.Brain.StateName != "Hurt", "快照恢复累计硬直进度");
                a.Brain.OnHit(10); snapshot = a.CaptureSnapshotState();
                Step(a, 5); a.RestoreSnapshotState(snapshot); Step(a, 0.46f); a.Brain.OnHit(100);
                Check(a.Brain.StateName != "Hurt", "快照恢复硬直冷却");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(30); Step(a, 0.46f); float age = a.Brain.SimulationAge;
                a.Status.SetPaused(true); a.Brain.Tick(10);
                Check(a.Brain.SimulationAge == age, "暂停冻结 AI 与硬直时钟");
                a.Status.SetPaused(false); a.Brain.OnHit(100);
                Check(a.Brain.StateName != "Hurt", "暂停未消耗硬直冷却");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                float health = a.Status.Health;
                a.ApplyDamage(new DamageInfo { Amount = 10 });
                Check(a.Status.Health == health - 10 && a.Brain.StateName != "Hurt", "伤害入口正常扣血且单次不硬直");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(30); Step(a, 0.46f); float health = a.Status.Health;
                a.ApplyDamage(new DamageInfo { Amount = 10 });
                Check(a.Status.Health == health - 10 && a.Brain.StateName != "Hurt", "硬直冷却期间正常扣血");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                Target(a, 11); Step(a); a.Target.gameObject.SetActive(false); Step(a);
                Check(a.Brain.StateName == "Idle" && a.Motor.Intent == Vector2.zero, "目标停用后停止追击");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Brain.OnHit(20); a.Status.ApplyHealthDelta(-260); Step(a);
                Check(a.Brain.StateName == "Split", "阶段分裂优先于普通行为");
                Step(a, 1.1f); a.Brain.OnHit(10);
                Check(a.Brain.StateName != "Hurt", "阶段过渡清空累计伤害");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                a.Status.ApplyHealthDelta(-334); Step(a);
                a.Status.ApplyHealthDelta(-333); Step(a);
                Check(a.Brain.StateName == "Stun" && a.Brain.DamageLocked, "发条阶段眩晕窗口仍有效");
                a.Brain.OnHit(100);
                Check(a.Brain.StateName == "Stun", "普通硬直不覆盖阶段眩晕");
                Step(a, a.Definition.ClockCoreWindow + 0.01f); Step(a, 1);
                Check(a.Brain.Phase == 2 && Mathf.Approximately(a.Status.Health, 600), "发条窗口失败仍回溯自身生命");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                a.Status.ApplyHealthDelta(-334); Step(a);
                Check(a.Brain.StateName == "Weak", "蝙蝠阶段虚弱仍有效");
                a.Brain.OnHit(100); Step(a, 2.6f);
                Check(a.Brain.Phase == 2 && a.Brain.StateName != "Hurt", "阶段虚弱不被普通受击覆盖");
            });
            Add(EnemyKind.BrickMinion, a =>
            {
                a.Brain.OnHit(1); Check(a.Brain.StateName != "Hurt", "小怪轻击未达破韧阈值时保持动作");
                a.Brain.OnHit(a.Definition.MinionStaggerThreshold);
                Check(a.Brain.StateName == "Hurt", "小怪达到配置阈值后保留受击");
            });
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
                Add(kind, a =>
                {
                    a.Definition.BossRestDuration = 1.8f;
                    Target(a, 3); AdvanceTo(a, "Rest");
                    string label = a.Definition.Kind.ToString();
                    bool bat = a.Definition.Kind == EnemyKind.BatBoss;
                    Check(bat ? a.Motor.Intent.y > 0f : a.Motor.Intent == Vector2.zero && a.Motor.Velocity == Vector2.zero,
                        label + (bat ? " 完成攻击后上升至休息航线" : " 完成一套攻击后停下休息"));
                    a.Combat = new GameObject("RestCombat_" + label).AddComponent<RuleGame.Gameplay.Combat.CombatSystem>();
                    int attacks = 0;
                    a.Feedback += cue =>
                    {
                        if (cue.Kind == EnemyFeedbackKind.Strike || cue.Kind == EnemyFeedbackKind.ProjectileSpawn
                            || (cue.Kind == EnemyFeedbackKind.BeamSpawn && kind != EnemyKind.ClockBoss) || cue.Kind == EnemyFeedbackKind.RuleCast
                            || cue.Kind == EnemyFeedbackKind.Summon) attacks++;
                    };
                    Target(a, 0.1f);
                    for (int i = 0; i < 10; i++) Step(a);
                    Check(a.Brain.StateName == "Rest" && attacks == 0 && (bat || a.Motor.Intent == Vector2.zero),
                        label + " 玩家贴近也不提前进攻或施法");
                    float remaining = a.Brain.RestRemaining;
                    a.Status.SetPaused(true); Step(a, 10); a.Status.SetPaused(false);
                    Check(Mathf.Approximately(a.Brain.RestRemaining, remaining), label + " 暂停冻结休息计时");
                    object saved = a.CaptureSnapshotState(); Step(a, 0.3f); a.RestoreSnapshotState(saved);
                    Check(a.Brain.StateName == "Rest" && Mathf.Approximately(a.Brain.RestRemaining, remaining),
                        label + " 快照恢复休息进度");
                    if (bat)
                    {
                        Step(a); Vector2 intent = a.Motor.Intent;
                        a.RestoreSnapshotState(saved); Step(a);
                        Check(a.Motor.Intent == intent, "快照恢复高空休息巡飞路线与方向");
                        a.RestoreSnapshotState(saved);
                    }
                    float health = a.Status.Health;
                    a.ApplyDamage(new DamageInfo { Amount = 10f });
                    Check(a.Status.Health == health - 10f && a.Brain.StateName == "Rest", label + " 休息中可以正常承受反击");
                    a.Brain.OnHit(30f); Step(a, .46f);
                    Check(a.Brain.StateName == "Rest" && (bat || a.Motor.Intent == Vector2.zero), label + " 破韧恢复不会跳过剩余休息");
                    float left = a.Brain.RestRemaining;
                    Step(a, left - .05f);
                    Check(a.Brain.StateName == "Rest" && attacks == 0, label + " 保留完整休息窗口");
                    Step(a, .06f);
                    Check(a.Brain.StateName != "Rest", label + " 到时恢复行动");
                    Target(a, 3);
                    bool clock = kind == EnemyKind.ClockBoss;
                    int wait = Mathf.CeilToInt((a.Definition.ClockChargeCooldown + a.Duration("JumpShake") + 2f) / .02f);
                    for (int i = 0; i < wait && attacks == 0 && !(clock && a.Brain.StateName == "Charge"); i++) Step(a);
                    Check(attacks > 0 || (clock && a.Brain.StateName == "Charge"), label + " 休息及独立技能冷却后能重新发动攻击");
                });
            Add(EnemyKind.ClockBoss, a =>
            {
                Target(a, 2); AdvanceTo(a, "Rest");
                a.ClockBeams.Tick(a.Definition.ClockHourInterval);
                Check(ClockBeamCount(a) == 1 && a.Brain.StateName == "Rest", "身体休息期间激光保持持续扫射");
            });
            Add(EnemyKind.BatBoss, a =>
            {
                a.Definition.BossRestDuration = 2.5f;
                Target(a, 3); AdvanceTo(a, "Rest");
                Step(a, 2.3f);
                Check(a.Brain.StateName == "Rest", "Inspector 配置可延长蝙蝠休息时间");
                a.Status.ApplyHealthDelta(-334); Step(a);
                Check(a.Brain.StateName == "Weak", "阶段过渡优先于普通休息");
            });
            EditorApplication.update += AwaitReady;
        }

        private static void AddRabbitCase(Action<EnemyActor> test)
            => Add(EnemyKind.RabbitBoss, test, config =>
            {
                var delivered = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/RabbitBoss_Definition.asset");
                if (delivered == null) throw new InvalidOperationException("缺少已交付兔子定义");
                config.Health = 100f; config.Energy = 0f;
                config.BodySize = delivered.BodySize;
                config.Timings = (EnemyActionTiming[])delivered.Timings.Clone();
            });
        private static void RabbitFlip(EnemyActor actor)
        {
            actor.Status.ApplyHealthDelta(actor.Definition.Health * .5f - actor.Status.Health);
            Step(actor); Step(actor, actor.AnimationDuration + .01f);
        }
        private static void AddRabbitTacticsCases()
        {
            AddRabbitCase(a =>
            {
                a.Definition.Health = 600f;
                a.Status.InitializeVitals(600f, 0f);
                Target(a, 6);
                a.Status.ApplyHealthDelta(-250f); Step(a);
                Check(a.Brain.Phase == 1 && !a.Defeated, "提高总生命后兔子一阶段仍持续到半血");
                a.Status.ApplyHealthDelta(-50f); Step(a);
                Check(a.Brain.StateName == "Flip", "兔子 600 生命在 300 生命翻转");
                a.Status.ApplyHealthDelta(119f - a.Status.Health); Step(a);
                Check(a.Defeated, "兔子退场阈值按最大生命的 20% 变化");
            });
            AddRabbitCase(a =>
            {
                a.Definition.RabbitLobCount = 3; a.Definition.RabbitGroundFanCount = 3;
                a.Definition.RabbitGroundArcStagger = 2.4f;
                int low = 0, arcs = 0; float minRise = float.MaxValue, maxRise = 0f;
                bool lowOrigin = true;
                a.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    var shot = (EnemyProjectile)cue.Subject;
                    if (shot.Gravity <= 0f)
                    {
                        low++;
                        lowOrigin &= shot.transform.position.y < a.transform.position.y - .5f;
                    }
                    else
                    {
                        arcs++;
                        minRise = Mathf.Min(minRise, shot.Velocity.y);
                        maxRise = Mathf.Max(maxRise, shot.Velocity.y);
                    }
                };
                Target(a, 6); Step(a, a.Definition.RabbitRepositionDuration + .01f); Step(a, 1.51f);
                Check(low == 3 && arcs == 3 && lowOrigin, "同次出手同时覆盖近地快弹与上抛落弹");
                Check(maxRise > minRise * 1.4f, "交错弧顶使用不同起飞速度，落弹不会同时清空");
            });
            AddRabbitCase(a =>
            {
                a.Definition.RabbitGroundVolleyCount = 3;
                Target(a, 6);
                int shots = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                Step(a, a.Definition.RabbitRepositionDuration + .01f);
                Check(a.Brain.StateName == "Throw", "兔子开场直接站位出招，不等待完整冒出动画");
                Step(a, 1.51f);
                object saved = a.CaptureSnapshotState();
                Target(a, 1.4f);
                for (int i = 0; i < 2; i++)
                {
                    Step(a, a.AnimationDuration - a.Brain.ActionElapsed + .01f);
                    Step(a, 1.51f);
                }
                Step(a, a.AnimationDuration - a.Brain.ActionElapsed + .01f);
                Check(shots == 3 && a.Brain.StateName == "Rest", "贴近不取消连投，三次完整投掷后统一休息");
                a.RestoreSnapshotState(saved);
                int before = shots;
                for (int i = 0; i < 2; i++)
                {
                    Step(a, a.AnimationDuration - a.Brain.ActionElapsed + .01f);
                    Step(a, 1.51f);
                }
                Step(a, a.AnimationDuration - a.Brain.ActionElapsed + .01f);
                Check(shots == before + 2 && a.Brain.StateName == "Rest", "回溯恢复剩余连投次数，不跳过或补满整套攻击");
            });
            AddRabbitCase(a =>
            {
                Target(a, 14); int shots = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                Step(a, a.AnimationDuration + .01f); Step(a, .1f);
                Check(a.Brain.StateName == "Reposition" && a.Motor.Intent.x > 0f, "兔子远处先主动追近，不站桩播放空投掷");
                Step(a, a.Definition.RabbitRepositionDuration + .01f);
                Check(a.Brain.StateName == "Reposition" && shots == 0, "目标仍超射程时持续换位，不进入攻击休息死循环");
            });
            AddRabbitCase(a =>
            {
                Target(a, 6); EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Step(a, a.AnimationDuration + .01f); Target(a, 8); Step(a, .8f);
                Target(a, -8); Step(a, .4f);
                Check(shot == null, "原画出手前留出可躲避的锁定窗口");
                Step(a, .31f);
                Check(shot != null && shot.Velocity.x > 0f && shot.Velocity.y > 0f && shot.VisualIndex == 0,
                    "低弧弹珠先向上抛向锁定位置，锁定后不追踪玩家换边");
            });
            AddRabbitCase(a =>
            {
                Target(a, 6, 4); EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Step(a, a.AnimationDuration + .01f); Step(a, 1.51f);
                Check(shot != null && shot.VisualIndex == 1 && shot.Gravity > .5f && shot.Velocity.y > 0f,
                    "高处目标改用跳棋高抛，不只按固定轮次换弹");
            });
            AddRabbitCase(a =>
            {
                a.transform.position = new Vector3(10.8f, 2f, 0f); Target(a, -2);
                AdvanceTo(a, "HideThrow");
                Check(a.Brain.StateName == "HideThrow", "墙角无撤退空间时原地反击，不倒退穿过玩家");
                EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Step(a, 5.14f);
                Check(shot != null && shot.VisualIndex == 1 && shot.Velocity.x < 0f,
                    "墙角反击完整冒出后向玩家高抛");
            });
            AddRabbitCase(a =>
            {
                Target(a, 2); AdvanceTo(a, "Burrow"); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0f, "近身先缩回再用空帽跳跃撤退");
                object saved = a.CaptureSnapshotState(); Target(a, -2); Step(a);
                Check(a.Motor.Intent.x < 0f, "撤退路线不随玩家穿越而抽搐换向");
                Target(a, 2); Step(a, a.Definition.RabbitBurrowDuration + .01f);
                Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Throw", "长冒出动画结束后保留撤退冷却，避免无限藏帽不反击");
                a.RestoreSnapshotState(saved); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0f && !a.IsFlying,
                    "快照恢复地面撤退路线与移动模式");
            });
            AddRabbitCase(a =>
            {
                a.transform.position = new Vector3(0f, 2f, 0f);
                var body = a.GetComponent<Rigidbody2D>(); body.position = a.transform.position;
                a.GetComponent<BoxCollider2D>().size = a.Definition.BodySize;
                Target(a, 6); RabbitFlip(a);
                Check(a.Brain.StateName == "AirReposition" && a.IsFlying && !a.Definition.Flying,
                    "翻转后启用实例飞行，不改共享定义或只有飞行外观");
                var visual = new GameObject("RabbitVisual"); visual.SetActive(false);
                visual.transform.SetParent(a.transform, false); visual.transform.localPosition = Vector3.down * 1.5f;
                var renderer = visual.AddComponent<SpriteRenderer>();
                var animator = visual.AddComponent<EnemySpriteAnimator>(); animator.Actor = a;
                animator.AnimationSet = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>("Assets/_Project/Data/Enemies/RabbitBoss_Animations.asset");
                visual.SetActive(true); animator.SendMessage("LateUpdate");
                var flight = Array.Find(animator.AnimationSet.Sequences, sequence => sequence.Action == "Flight");
                Check(flight.NativeFrameRate && flight.FrameRate == 8f && Array.IndexOf(flight.Frames, renderer.sprite) >= 0,
                    "翻转换侧使用交付的原生八帧飞行序列");
                var snapshots = SnapshotsFor(a); var saved = snapshots.Capture();
                Vector2 start = body.position;
                var floor = new GameObject("RabbitFlightFloor"); floor.layer = 8;
                floor.transform.position = new Vector3(0f, 0f, 0f);
                floor.AddComponent<BoxCollider2D>().size = new Vector2(40f, 1f);
                a.PlayerMask = 0; Physics2D.SyncTransforms();
                SimulationMode2D previousMode = Physics2D.simulationMode;
                try
                {
                    Physics2D.simulationMode = SimulationMode2D.Script;
                    for (int i = 0; i < 20; i++) { Step(a); a.Motor.Tick(.02f); Physics2D.Simulate(.02f); }
                    Check(body.position.y > start.y + .6f && body.position.y < a.ArenaY.y - a.Definition.BodySize.y * .5f,
                        "真实物理升空换位且留在场地高度范围");
                }
                finally { Physics2D.simulationMode = previousMode; }
                snapshots.Restore(saved); Step(a); Vector2 route = a.Motor.Intent;
                Target(a, -6); Step(a);
                Check(a.IsFlying && a.Brain.StateName == "AirReposition" && Vector2.Dot(route, a.Motor.Intent) > .99f,
                    "飞行快照恢复后保持锁定路线，不瞬移追踪换边");
                a.Target.gameObject.SetActive(false); Step(a);
                Check(a.Motor.Intent == Vector2.zero, "丢失目标停止飞行追击");
            });
            AddRabbitCase(a =>
            {
                a.transform.position = new Vector3(0f, 4f, 0f);
                a.GetComponent<Rigidbody2D>().position = a.transform.position;
                a.GetComponent<BoxCollider2D>().size = a.Definition.BodySize;
                Physics2D.SyncTransforms();
                Target(a, 6); RabbitFlip(a); Target(a, 5, -2);
                Step(a, a.Definition.RabbitAirMoveDuration + .01f);
                var velocities = new List<Vector2>(); var variants = new List<int>();
                a.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    var shot = (EnemyProjectile)cue.Subject; velocities.Add(shot.Velocity); variants.Add(shot.VisualIndex);
                };
                Step(a, .36f);
                Check(velocities.Count == 3 && variants.TrueForAll(index => index == 2), "第二阶段用鸭子打三发扇形压制");
                Check(Mathf.Abs(velocities[0].x - velocities[2].x) > 2f, "斜向攻击时三发仍有角度展开，不重叠为一条线");
                Step(a, a.Definition.RabbitBurstInterval + .01f);
                Check(velocities.Count == 6, "散射仅按配置发两轮，保留间隔");
                Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "Rest" && a.Motor.Intent == Vector2.zero && a.IsFlying,
                    "空中散射结束悬停休息，不连续追打");
                float health = a.Status.Health;
                a.ApplyDamage(new DamageInfo { Amount = 10f });
                Check(a.Status.Health == health - 10f && a.Brain.StateName == "Rest", "空中休息仍可被玩家反击，没有新增无敌");
                float age = a.Brain.SimulationAge; object saved = a.CaptureSnapshotState();
                a.Status.SetPaused(true); Step(a, 5); a.Status.SetPaused(false);
                Check(a.Brain.SimulationAge == age, "暂停冻结兔子换位和散射时钟");
                Step(a, a.Brain.RestRemaining + .01f); Step(a, .1f); a.RestoreSnapshotState(saved);
                Check(a.IsFlying && a.Brain.StateName == "Rest", "快照恢复飞行阶段及剩余喘息");
            });
        }

        private static void AddClockHandCase(Action<EnemyActor> test)
            => Add(EnemyKind.ClockBoss, test, config =>
            {
                config.ClockHourInterval = 2f;
                config.ClockMinuteInterval = 1.2f;
                config.ClockSecondInterval = .6f;
            });
        private static void ClockPhaseTwo(EnemyActor actor)
        {
            actor.Status.ApplyHealthDelta(666f - actor.Status.Health);
            Step(actor);
        }
        private static void ClockOpenCore(EnemyActor actor)
        {
            var core = new GameObject("ClockValidationCore");
            core.AddComponent<BoxCollider2D>();
            core.AddComponent<SpriteRenderer>();
            actor.WeakPoint = core.AddComponent<EnemyWeakPoint>();
            actor.WeakPoint.Owner = actor;
            actor.Status.ApplyHealthDelta(330f - actor.Status.Health);
            Step(actor);
        }
        private static ClockHandView CreateClockHandView(EnemyActor actor)
        {
            var go = new GameObject("ClockHands"); go.transform.SetParent(actor.transform, false); go.SetActive(false);
            var view = go.AddComponent<ClockHandView>(); view.Actor = actor;
            view.Hands = new Transform[3];
            for (int i = 0; i < 3; i++)
            {
                view.Hands[i] = new GameObject("Hand" + i).transform;
                view.Hands[i].SetParent(go.transform);
            }
            go.SetActive(true);
            view.SendMessage("LateUpdate");
            return view;
        }
        private static void AddClockHandCases()
        {
            AddClockHandCase(a =>
            {
                var config = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/ClockBoss_Definition.asset");
                Check(config.ClockHourRotationPeriod == 24f && config.ClockMinuteRotationPeriod == 12f && config.ClockSecondRotationPeriod == 6f,
                    "真实时钟配置默认改为 24/12/6 秒一圈");
                a.ClockBeams.Tick(.1f);
                Check(ClockBeamCount(a) == 0, "开场先等待停火间隔，不立即常驻激光");
                var view = CreateClockHandView(a);
                Check(view.Hands[2].gameObject.activeSelf && !view.Hands[1].gameObject.activeSelf, "一阶段仅显示时针");
                ClockPhaseTwo(a); ClockOpenCore(a);
                a.WeakPoint.ApplyDamage(new DamageInfo { Amount = a.Definition.ClockCoreHealth });
                Step(a); Step(a, .61f); view.SendMessage("LateUpdate");
                Check(a.Brain.Phase == 3 && view.Hands[0].gameObject.activeSelf, "三阶段才解锁秒针");
                for (int hand = 0; hand < 3; hand++)
                {
                    a.ClockBeams.Restore(new ClockBeamEmitter(a).Capture());
                    a.ClockBeams.Tick(a.ClockBeams.RotationPeriod(hand) * .25f);
                    view.SendMessage("LateUpdate");
                    EnemyBeam beam = ClockBeamFor(a, hand);
                    Vector2 needle = view.Hands[2 - hand].TransformDirection(hand == 1 ? Vector3.down : Vector3.up);
                    Check(beam != null && Vector2.Dot(beam.Direction, Vector2.right) > .9999f && Vector2.Dot(needle, beam.Direction) > .9999f,
                        hand + " 号针三点方向与短扫射光束一致");
                    a.ClockBeams.Tick(a.ClockBeams.RotationPeriod(hand) * .25f);
                    Check(Vector2.Dot(beam.Direction, Vector2.down) > .9999f, hand + " 号针六点方向向下");
                }
            });
            AddClockHandCase(a =>
            {
                int spawns = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) spawns++; };
                AdvanceClock(a, 1.98f); Check(ClockBeamCount(a) == 0, "停火计时结束前完全没有光束");
                AdvanceClock(a, .04f); EnemyBeam beam = ClockBeamFor(a, 0);
                Check(beam != null && beam.Age < beam.WarningDuration, "到期先进入预警");
                var profile = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<EnemyVfxProfile>("Assets/_Project/Data/Enemies/Vfx/ClockBoss_Vfx.asset"));
                var material = new Material(Shader.Find("Sprites/Default")); profile.BeamMaterial = profile.BeamCoreMaterial = material;
                var view = beam.GetComponent<EnemyBeamView>();
                if (view == null) view = beam.gameObject.AddComponent<EnemyBeamView>();
                view.Configure(profile); beam.SendMessage("LateUpdate"); view.SendMessage("LateUpdate");
                var core = beam.transform.Find("BeamCore").GetComponent<LineRenderer>();
                Check(!core.enabled, "预警阶段只有细线，没有伤害核心");
                AdvanceClock(a, .72f); beam.SendMessage("LateUpdate"); view.SendMessage("LateUpdate");
                Check(core.enabled && core.startWidth > 0f && beam.Age >= beam.WarningDuration, "预警后开启有限时长激光");
                float progress = a.ClockBeams.HandProgress(0);
                AdvanceClock(a, .36f);
                Check(!beam.gameObject.activeSelf && ClockBeamCount(a) == 0, "0.35 秒发射结束后光束真正消失");
                AdvanceClock(a, 1.8f);
                Check(ClockBeamCount(a) == 0 && spawns == 1 && a.ClockBeams.HandProgress(0) > progress,
                    "消失后保留完整停火窗口，指针仍缓慢旋转");
                AdvanceClock(a, .3f);
                Check(ClockBeamCount(a) == 1 && spawns == 2, "停火结束后下一轮重新预警，不积压常驻光束");
            });
            AddClockHandCase(a =>
            {
                a.ClockBeams.Tick(a.ClockBeams.HandInterval(0)); EnemyBeam beam = ClockBeamFor(a, 0);
                Vector2 direction = beam.Direction; float progress = a.ClockBeams.HandProgress(0);
                Target(a, -8, 3); a.FaceDirection(Vector2.left);
                Check(beam.Direction == direction, "玩家换边和 Boss 转身不改变激光方向");
                a.Definition.ClockHourInterval = .3f;
                a.ClockBeams.Tick(.5f);
                Check(Mathf.Abs(a.ClockBeams.HandProgress(0) - progress - .5f / 24f) < .001f,
                    "缩短发射间隔不会加快指针转速");
                a.Target.gameObject.SetActive(false);
                Check(beam.gameObject.activeSelf && beam.Direction != direction, "短扫射期间没有目标也继续沿针转动");
            });
            AddClockHandCase(a =>
            {
                Target(a, 2); Step(a, 2.4f); CheckSweepDuring(a, "JumpShake");
                Step(a, a.AnimationDuration + .01f); CheckSweepDuring(a, "Charge");
                Step(a, a.Definition.ClockChargeDuration + .01f); CheckSweepDuring(a, "Rest");
                a.Brain.OnHit(30f); CheckSweepDuring(a, "Hurt");
                ClockPhaseTwo(a); ClockOpenCore(a); CheckSweepDuring(a, "Stun");
                a.WeakPoint.ApplyDamage(new DamageInfo { Amount = a.Definition.ClockCoreHealth }); Step(a); CheckSweepDuring(a, "Recover");
            });
            AddClockHandCase(a =>
            {
                ClockPhaseTwo(a); ClockOpenCore(a); Step(a, a.Definition.ClockCoreWindow + .01f);
                CheckSweepDuring(a, "Rewind");
            });
            AddClockHandCase(a =>
            {
                a.ClockBeams.Tick(1f); object cooldownState = a.CaptureSnapshotState();
                a.ClockBeams.Tick(1f); Check(ClockBeamCount(a) == 1, "等待结束生成一束光束");
                a.RestoreSnapshotState(cooldownState);
                Check(ClockBeamCount(a) == 0, "回溯到等待阶段清除未来光束");
                a.ClockBeams.Tick(.98f); Check(ClockBeamCount(a) == 0, "快照恢复停火剩余时间");
                a.ClockBeams.Tick(.04f); EnemyBeam beam = ClockBeamFor(a, 0);
                for (int i = 0; i < 10; i++) beam.SendMessage("FixedUpdate");
                object actorState = a.CaptureSnapshotState(), beamState = beam.CaptureSnapshotState();
                Vector2 direction = beam.Direction; float age = beam.Age;
                a.ClockBeams.Tick(.4f); beam.SendMessage("FixedUpdate");
                a.RestoreSnapshotState(actorState); beam.RestoreSnapshotState(beamState);
                Check(beam.Direction == direction && beam.Age == age, "快照同步恢复角度与短脉冲预警进度");
                a.Status.SetPaused(true); a.ClockBeams.Tick(2); beam.SendMessage("FixedUpdate"); a.Status.SetPaused(false);
                Check(beam.Direction == direction && beam.Age == age, "单体暂停冻结指针和光束寿命");
                TimeManager.Instance.SetPaused(true); a.ClockBeams.Tick(2); beam.SendMessage("FixedUpdate"); TimeManager.Instance.SetPaused(false);
                Check(beam.Direction == direction && beam.Age == age, "全局暂停冻结指针和光束寿命");
                a.Status.ApplyHealthDelta(-a.Status.Health); Step(a); beam.SendMessage("FixedUpdate");
                Check(a.Defeated && !beam.gameObject.activeSelf, "死亡立即结束当前短扫射");
            });
            AddClockHandCase(a =>
            {
                a.transform.position = new Vector3(100f, 100f, 0f); a.PlayerMask = 1 << 10;
                a.Definition.ClockHourRotationPeriod = 8f;
                a.Combat = new GameObject("SweepCombat").AddComponent<RuleGame.Gameplay.Combat.CombatSystem>();
                GameObject target = a.Target.gameObject; target.layer = 10; target.SetActive(false);
                var status = target.AddComponent<RuleGame.Gameplay.Player.EntityStatus>();
                var damageable = target.AddComponent<RuleGame.Gameplay.Combat.EntityDamageable>();
                typeof(RuleGame.Gameplay.Combat.EntityDamageable).GetField("_faction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(damageable, Faction.Player);
                typeof(RuleGame.Gameplay.Combat.EntityDamageable).GetField("_invincibleSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(damageable, 0f);
                typeof(RuleGame.Gameplay.Combat.EntityDamageable).GetField("_combat", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(damageable, a.Combat);
                target.AddComponent<BoxCollider2D>().size = Vector2.one * .2f; target.SetActive(true); status.InitializeVitals(100f, 0f);
                // 重建实例计时，使测试设置的 8 秒/圈从十二点起算。
                a.ClockBeams.Restore(new ClockBeamEmitter(a).Capture());
                Target(a, 2f); Physics2D.SyncTransforms();
                a.ClockBeams.Tick(2f); EnemyBeam beam = ClockBeamFor(a, 0);
                for (int i = 0; i < 34; i++) beam.SendMessage("FixedUpdate");
                Check(status.Health == 100f, "预警细线不会造成伤害");
                for (int i = 0; i < 2; i++) beam.SendMessage("FixedUpdate");
                Check(status.Health == 90f, "短扫射沿针尖方向实际命中玩家");
                beam.SendMessage("FixedUpdate"); Check(status.Health == 90f, "短扫射不会每个物理帧重复扣血");
                a.ClockBeams.Tick(2f);
                for (int i = 0; i < 30; i++) beam.SendMessage("FixedUpdate");
                Check(status.Health == 90f && !beam.gameObject.activeSelf, "转离并消失后不再造成伤害");
                EnemyBeam pulse = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) pulse = (EnemyBeam)cue.Subject; };
                EnemyBeam.Spawn(a, 0f, .2f, .3f);
                Check(pulse == beam && !pulse.FollowsClockHand && pulse.Direction == Vector2.right,
                    "池复用为普通光束时清除指针绑定");
                for (int i = 0; i < 40; i++) pulse.SendMessage("FixedUpdate");
                Check(!pulse.gameObject.activeSelf, "普通光束仍按原寿命结束");
            });
        }
        private static EnemyBeam ClockBeamFor(EnemyActor actor, int hand)
        {
            foreach (EnemyBeam beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>())
                if (beam.Source == actor && beam.ClockHand == hand && beam.gameObject.activeSelf) return beam;
            return null;
        }
        private static int ClockBeamCount(EnemyActor actor)
        {
            int count = 0;
            foreach (EnemyBeam beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>())
                if (beam.Source == actor && beam.FollowsClockHand && beam.gameObject.activeSelf) count++;
            return count;
        }
        private static void AdvanceClock(EnemyActor actor, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Time.fixedDeltaTime);
            for (int frame = 0; frame < frames; frame++)
            {
                actor.ClockBeams.Tick(Time.fixedDeltaTime);
                foreach (EnemyBeam beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>())
                    if (beam.Source == actor) beam.SendMessage("FixedUpdate");
            }
        }
        private static void CheckSweepDuring(EnemyActor actor, string state)
        {
            Check(actor.Brain.StateName == state, "进入身体状态 " + state);
            actor.ClockBeams.Restore(new ClockBeamEmitter(actor).Capture());
            actor.ClockBeams.Tick(actor.ClockBeams.HandInterval(0));
            EnemyBeam beam = ClockBeamFor(actor, 0); Vector2 before = beam.Direction, intent = actor.Motor.Intent;
            actor.ClockBeams.Tick(.15f);
            Check(beam != null && beam.Direction != before && actor.Brain.StateName == state && actor.Motor.Intent == intent,
                state + " 期间短扫射独立计时且方向随针变化");
        }

        private sealed class TimeRevertCatalog : IRuleCatalog
        {
            private readonly RuleDef _definition = AssetDatabase.LoadAssetAtPath<RuleDef>("Assets/_Project/Data/Rules/TimeRevert.asset");
            public RuleDef Get(AttributeId attribute, RelationId relation)
                => attribute == AttributeId.Time && relation == RelationId.Revert ? _definition : null;
        }
        private static void AddClockRewindCases()
        {
            foreach (bool available in new[] { true, false })
                Add(EnemyKind.ClockBoss, a =>
                {
                    a.Definition.RuleAttribute = AttributeId.Time;
                    a.Definition.RuleRelation = RelationId.Revert;
                    var snapshots = SnapshotsFor(a);
                    SnapshotSystem.SetInstanceForTesting(snapshots);
                    var body = a.GetComponent<Rigidbody2D>();
                    a.GetComponent<BoxCollider2D>().size = a.Definition.BodySize;
                    Vector2 oldPosition = new Vector2(-6f, 2f);
                    body.position = oldPosition;
                    a.transform.position = oldPosition;
                    if (available)
                    {
                        snapshots.CaptureToBuffer();
                        body.position = new Vector2(-2f, 2f);
                        a.transform.position = body.position;
                        snapshots.CaptureToBuffer();
                    }
                    Vector2 currentPosition = new Vector2(4f, 2f);
                    body.position = currentPosition;
                    a.transform.position = currentPosition;
                    Target(a, 2f);
                    Step(a, 2.4f);
                    Check(a.Brain.StateName == "JumpShake", "时间反转配置下仍先响铃预警");
                    float energy = a.Status.Energy;
                    Step(a, a.AnimationDuration + .01f);
                    Check(a.Brain.StateName == "Charge" && body.position == currentPosition,
                        "有无历史快照都从响铃结束的当前位置起跑");
                    Check(a.Status.Energy == energy, "乱跑不隐式施法或扣除规则能量");
                    Step(a);
                    Check(a.Motor.Intent.x > 0f && a.Facing > 0f, "冲撞使用本次预警锁定的方向");
                    var floor = new GameObject("ClockChargeFloor"); floor.layer = 8;
                    floor.transform.position = new Vector3(0f, currentPosition.y - a.Definition.BodySize.y * .5f - .5f, 0f);
                    floor.AddComponent<BoxCollider2D>().size = new Vector2(60f, 1f);
                    a.PlayerMask = 0;
                    Physics2D.SyncTransforms();
                    SimulationMode2D previousMode = Physics2D.simulationMode;
                    try
                    {
                        Physics2D.simulationMode = SimulationMode2D.Script;
                        for (int i = 0; i < 15; i++)
                        {
                            Vector2 previous = body.position;
                            Step(a); a.Motor.Tick(.02f); Physics2D.Simulate(.02f);
                            if (Vector2.Distance(body.position, previous) > .3f)
                                throw new InvalidOperationException("冲撞位移不连续：" + previous + " -> " + body.position);
                        }
                        Check(body.position.x > currentPosition.x && a.Brain.StateName == "Charge",
                            "真实物理冲撞逐步向前移动，没有跳到历史坐标");
                    }
                    finally { Physics2D.simulationMode = previousMode; }
                    Check(snapshots.TryPeekSecondLast(out _) == available, "冲撞不会消耗或清空时间回溯缓冲");
                    Vector2 beforeCast = body.position;
                    bool cast = a.CastRule();
                    Check(available ? cast && body.position == oldPosition && a.Status.Energy < energy
                        : !cast && body.position == beforeCast && a.Status.Energy == energy,
                        "显式释放时间反转仍遵守原有回溯与扣费语义");
                });
        }

        private sealed class SnapshotBody : IRuleAffected
        {
            public Rigidbody2D Body { get; }
            public Collider2D[] Colliders { get; }
            public SnapshotBody(EnemyActor actor)
            {
                Body = actor.GetComponent<Rigidbody2D>();
                Colliders = actor.GetComponents<Collider2D>();
            }
        }
        private static SnapshotSystem SnapshotsFor(EnemyActor actor)
        {
            // 快照系统重构后收的是「快照名册」而不是活动实体注册表：两者服务不同消费者，
            // 名册的成员资格 = 生命周期（禁用中也留在册）。
            var roster = new SnapshotRoster();
            roster.Register(new SnapshotBody(actor));
            return new SnapshotSystem(RuleSystem.Instance, roster);
        }
        private static void AddSnapshotCameraCases()
        {
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
            {
                Add(kind, a =>
                {
                    var snapshots = SnapshotsFor(a);
                    var alive = snapshots.Capture();
                    string initial = a.Brain.StateName;
                    float health = kind == EnemyKind.RabbitBoss ? a.Definition.Health * .4f : kind == EnemyKind.ClockBoss ? 330f : 650f;
                    a.Status.ApplyHealthDelta(health - a.Status.Health);
                    Step(a);
                    string transition = kind == EnemyKind.BrickBoss ? "Split" : kind == EnemyKind.ClockBoss ? "Stun"
                        : kind == EnemyKind.RabbitBoss ? "Flip" : "Weak";
                    Check(a.Brain.StateName == transition, kind + " 进入阶段过渡");
                    float elapsed = a.Brain.ActionElapsed;
                    var saved = snapshots.Capture();
                    snapshots.CaptureToBuffer();
                    Check(saved.Entities.Count == 1, kind + " 阶段过渡中可采集实体和时间缓冲");
                    Step(a, .1f);
                    snapshots.Restore(saved);
                    Check(a.Brain.StateName == transition && Mathf.Approximately(a.Brain.ActionElapsed, elapsed),
                        kind + " 恢复阶段过渡及计时");
                    Step(a, .02f);
                    snapshots.Restore(alive);
                    Check(a.Brain.StateName == initial && a.Status.Health == a.Definition.Health,
                        kind + " 过渡后可回溯 Alive 动作与生命");
                });
                Add(kind, a =>
                {
                    var snapshots = SnapshotsFor(a);
                    a.Status.ApplyHealthDelta(-a.Status.Health);
                    Step(a);
                    Check(a.Brain.StateName == "Dead", kind + " 进入死亡");
                    var saved = snapshots.Capture();
                    snapshots.CaptureToBuffer();
                    Check(saved.Entities.Count == 1, kind + " 死亡中可采集实体和时间缓冲");
                    Step(a, 2f);
                    snapshots.Restore(saved);
                    Check(a.gameObject.activeSelf && a.Defeated && a.Brain.StateName == "Dead",
                        kind + " 回溯死亡动作且不重开 Alive");
                });
            }
            Add(EnemyKind.BrickMinion, _ => CheckCameraRelease(false));
            Add(EnemyKind.BrickMinion, _ => CheckCameraRelease(true));
        }
        private static void CheckCameraRelease(bool disable)
        {
            var go = new GameObject("CameraImpulseValidation");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                go.transform.position = new Vector3(0f, 0f, -10f);
                var impulse = go.AddComponent<BossCameraImpulse>();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var before = typeof(BossCameraImpulse).GetMethod("BeforeRender", flags);
                var after = typeof(BossCameraImpulse).GetMethod("AfterRender", flags);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 position = go.transform.position;
                    BossCameraImpulse.Pulse(.3f, .2f);
                    before.Invoke(impulse, new object[] { default(ScriptableRenderContext), camera });
                    Check(Vector3.Distance(camera.worldToCameraMatrix.MultiplyPoint3x4(position), Vector3.zero) > .01f,
                        "震屏在渲染阶段生效");
                    if (disable) impulse.enabled = false;
                    else after.Invoke(impulse, new object[] { default(ScriptableRenderContext), camera });
                    go.transform.position += new Vector3(3f, 2f, 0f);
                    Check(camera.worldToCameraMatrix.MultiplyPoint3x4(go.transform.position).sqrMagnitude < .00001f,
                        disable ? "停用震屏后视图继续跟随摄像机位置" : "震屏结束后视图继续跟随摄像机位置");
                    impulse.enabled = true;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static void AddTacticsCases()
        {
            Add(EnemyKind.BrickBoss, a =>
            {
                Target(a, 5); Step(a); Step(a);
                Check(a.Brain.StateName == "Advance" && a.Motor.Intent.x > 0 && a.Motor.SpeedMultiplier > 1f, "积木主动快步压近");
                Target(a, -6); Step(a);
                Check(a.Brain.StateName == "HammerGround", "玩家绕后时积木停步重新预警地面重击");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                EnemyProjectile shot = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                Target(a, 8); Step(a); Target(a, -8); Step(a, a.AnimationDuration * .65f);
                Check(shot != null && shot.Velocity.x > 0f, "积木投掷锁定起手时目标，玩家换边可以躲避");
            });
            Add(EnemyKind.BrickBoss, a =>
            {
                a.Status.ApplyHealthDelta(-260); Step(a); Step(a, 1.1f);
                Target(a, 2); Step(a); Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "HammerGround", "积木分裂阶段解锁锤人接地面重击");
                Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "Rest", "积木有限连招结束后进入休息");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                EnemyBeam beam = null;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) beam = (EnemyBeam)cue.Subject; };
                Target(a, 6); Step(a, a.Definition.ClockHourInterval + .01f);
                Check(beam != null && Vector2.Dot(beam.Direction, a.ClockBeams.HandDirection(0)) > .999f && beam.WarningDuration >= .7f, "发条沿指针方向扫射并保留启动预警");
                Vector2 direction = beam.Direction; Target(a, -6);
                Check(beam.Direction == direction, "玩家换边不会让光束瞄准玩家");
                Step(a, .1f);
                Check(beam.Direction != direction, "激光预警期间也跟随指针转动");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                Target(a, 2); Step(a, 2.4f); Target(a, -6);
                Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Charge" && a.Motor.Intent.x > 0 && a.Facing > 0, "发条冲撞保持响铃时锁定方向");
                a.transform.position = new Vector3(11.9f, 0, 0); Step(a);
                Check(a.Brain.StateName == "Rest", "发条到边界停止冲撞，不来回折返追杀");
            });
            Add(EnemyKind.ClockBoss, a =>
            {
                int beams = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.BeamSpawn) beams++; };
                Target(a, 2); Step(a, 2.4f);
                Check(a.Brain.StateName == "JumpShake", "身体独立按冷却响铃");
                a.ClockBeams.Tick(a.Definition.ClockHourInterval);
                Check(beams >= 1 && a.Brain.StateName == "JumpShake", "响铃期间激光独立发射");
                Step(a, a.AnimationDuration + .01f); Step(a, a.Definition.ClockChargeDuration + .01f);
                Check(a.Brain.StateName == "Rest", "身体冲撞后保留喘息窗口");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                Target(a, 2); Step(a, .21f); Step(a); Step(a, a.AnimationDuration + .01f); Step(a);
                Check(a.Brain.StateName == "Burrow" && a.Motor.Intent.x < 0 && a.Motor.SpeedMultiplier > 1, "兔子被逼近时藏身撤退");
                var visual = new GameObject("BurrowVisual"); visual.SetActive(false);
                var renderer = visual.AddComponent<SpriteRenderer>();
                var animator = visual.AddComponent<RuleGame.Presentation.Enemy.EnemySpriteAnimator>(); animator.Actor = a;
                visual.SetActive(true); animator.SendMessage("LateUpdate");
                Check(renderer.color.a < .5f, "白盒也用半透明显示藏身状态");
                object saved = a.CaptureSnapshotState(); Target(a, -8); Step(a);
                Check(a.Motor.Intent.x < 0, "藏身路线不随玩家换边反复抖动");
                a.RestoreSnapshotState(saved); Step(a, a.Definition.RabbitBurrowDuration + .01f); animator.SendMessage("LateUpdate");
                Check(a.Brain.StateName == "Pop" && renderer.color.a == 1f, "藏身结束重新现身，外观恢复");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                a.transform.position = new Vector3(-10, 0, 0); Target(a, 2);
                Step(a, .21f); Step(a); Step(a, a.AnimationDuration + .01f);
                Check(a.Brain.StateName == "HideThrow", "兔子被堵在墙角时改用高抛反击");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                float first = -1f, second = -1f;
                a.Feedback += cue =>
                {
                    if (cue.Kind != EnemyFeedbackKind.ProjectileSpawn) return;
                    if (first < 0f) first = ((EnemyProjectile)cue.Subject).Gravity;
                    else second = ((EnemyProjectile)cue.Subject).Gravity;
                };
                Target(a, 6); Step(a, .21f); Step(a, .75f); Step(a, 1.3f);
                Step(a, a.Definition.BossRestDuration + .01f); Step(a, .65f); Step(a, .75f);
                Check(first > 0 && second > first, "兔子休息后切换快弹与高抛，避免重复同一招");
            });
            Add(EnemyKind.RabbitBoss, a =>
            {
                int shots = 0;
                a.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                Target(a, 6); a.Status.ApplyHealthDelta(a.Definition.Health * .5f - a.Status.Health); Step(a); Step(a, .81f);
                Step(a, .36f); Step(a, .36f); Step(a, .4f);
                Check(shots == 6 && a.Brain.StateName == "Rest", "兔子翻转后两轮三发散射，结束后休息");
            });
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss })
                Add(kind, a =>
                {
                    a.Definition.BossRestDuration = 1.8f; Target(a, 6); AdvanceTo(a, "Rest");
                    Step(a, .4f);
                    Check(a.Brain.StateName == "Rest" && a.Motor.Intent == Vector2.zero, kind + " 新战术保留完整休息");
                    object saved = a.CaptureSnapshotState(); float remaining = a.Brain.RestRemaining;
                    a.Status.SetPaused(true); Step(a, 10); a.Status.SetPaused(false); Step(a, .2f); a.RestoreSnapshotState(saved);
                    Check(Mathf.Approximately(a.Brain.RestRemaining, remaining), kind + " 新战术兼容暂停与快照");
                });
        }

        private static void Add(EnemyKind kind, Action<EnemyActor> test, Action<EnemyDefinition> configure = null)
        {
            var config = ScriptableObject.CreateInstance<EnemyDefinition>();
            config.Kind = kind; config.Health = 1000; config.HurtCooldown = 4;
            config.BossRestDuration = .5f;
            config.Flying = kind == EnemyKind.BatBoss;
            config.Timings = new[]
            {
                new EnemyActionTiming { Action = "Hurt", Duration = 2 },
                new EnemyActionTiming { Action = "HammerPlayer", Duration = 1 },
                new EnemyActionTiming { Action = "Pop", Duration = 0.2f },
                new EnemyActionTiming { Action = "Split", Duration = 1 }
            };
            configure?.Invoke(config);
            var go = new GameObject("BossValidation_" + Actors.Count); go.layer = 9; go.SetActive(false);
            var actor = go.AddComponent<EnemyActor>(); actor.Definition = config;
            actor.Target = new GameObject("Target_" + Actors.Count).transform;
            Target(actor, 5); actor.GetComponent<RuleGame.Gameplay.Player.EntityStatus>().SetPaused(true);
            go.SetActive(true); Actors.Add(actor); Cases.Add(test);
        }

        private static void AwaitReady()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            foreach (EnemyActor actor in Actors)
                if (actor.Brain == null)
                {
                    if (EditorApplication.timeSinceStartup > _deadline) Finish("等待组件 Start 超时");
                    return;
                }
            EditorApplication.update -= AwaitReady;
            for (int i = 0; i < Actors.Count; i++)
            {
                EnemyActor actor = Actors[i]; actor.enabled = false; actor.Status.SetPaused(false);
                try { Cases[i](actor); }
                catch (Exception error) { _failed++; Debug.LogError("Case " + i + ": " + error); }
            }
            Finish(null);
        }

        private static void Cooldown(EnemyActor actor)
        {
            Target(actor, 2); Step(actor); Step(actor, actor.AnimationDuration + 0.01f);
            Step(actor, actor.Definition.BossRestDuration + .01f);
        }
        private static void Target(EnemyActor actor, float x, float y = 0)
            => actor.Target.position = actor.transform.position + new Vector3(x, y, 0);
        private static void Step(EnemyActor actor, float dt = 0.02f)
        {
            if (TimeManager.Instance?.IsPaused == true || actor.Status.IsPaused) return;
            actor.Brain.Tick(dt);
            actor.ClockBeams?.Tick(dt);
        }
        private static void AdvanceTo(EnemyActor actor, string state)
        {
            for (int i = 0; i < 2000 && actor.Brain.StateName != state; i++) Step(actor);
            Check(actor.Brain.StateName == state, "能够进入 " + state);
        }
        private static void Check(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException(name);
            _passed++; Debug.Log("PASS: " + name);
        }
        private static void Finish(string error)
        {
            SessionState.SetBool(Pending, false);
            SessionState.SetBool(Tactics, false);
            SessionState.SetBool(SnapshotCamera, false);
            SessionState.SetBool(ClockRewind, false);
            SessionState.SetBool(ClockHands, false);
            SessionState.SetBool(RabbitTactics, false);
            if (error != null) { _failed++; Debug.LogError(error); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output)));
            File.WriteAllText(Output, $"Passed: {_passed}\nFailed: {_failed}\n{error}\n");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
    }
}
