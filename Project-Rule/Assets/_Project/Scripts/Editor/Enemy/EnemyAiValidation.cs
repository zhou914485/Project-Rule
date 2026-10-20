using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RuleGame.Editor.Enemy
{
    /// <summary>真实预制体的战术行为回归；检查选择、预判、锁向、技能恢复与失效目标。</summary>
    public static class EnemyAiValidation
    {
        private const string Pending = "RuleGame.EnemyAI";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _passed, _failed;
        private static readonly List<string> Results = new List<string>();
        private static readonly List<Object> Owned = new List<Object>();
        private static EnemyActor _enemy;
        private static PlayerController _player;
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
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Run(); };
        }
        private static void Run()
        {
            TimeManager.Initialize(); WorldRegistry.Initialize(); SnapshotRoster.Initialize();
            RuleSystem.Initialize(AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Rules/RuleCatalog.asset"));
            SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
            Physics2D.simulationMode = SimulationMode2D.Script;
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                Case(kind + " 真实移动预判", () =>
                {
                    Spawn(kind, 4f);
                    var input = typeof(PlayerController).GetField("_input", Hidden).GetValue(_player);
                    input.GetType().GetField("_move", Hidden).SetValue(input, Vector2.right);
                    for (int i = 0; i < 20; i++) { _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    Check(_player.Velocity.x > 2f, "玩家没有通过真实移动起步");
                    var predicted = (Vector2)typeof(EnemyBrain).GetMethod("PredictTarget", Hidden).Invoke(_enemy.Brain, new object[] { .25f });
                    Check(predicted.x > _player.transform.position.x + .5f, "AI 没有读取 Kinematic 玩家的实际移动速度");
                });
                Case(kind + " 实际对战流程", () => Encounter(kind));
                Case(kind + " 死亡目标与快照", () =>
                {
                    Spawn(kind, 3f); var saved = _enemy.CaptureSnapshotState();
                    _player.GetComponent<EntityStatus>().ApplyHealthDelta(-2000f); Tick(.05f);
                    Check(!_enemy.HasTarget, "仍把已死亡玩家当成有效目标");
                    _enemy.RestoreSnapshotState(saved); Tick(.03f);
                    Check(_enemy.Brain.StateName != null, "恢复快照后状态失效");
                });
            }
            Case("真实身体接触与受击箱开关", ContactDamage);
            Case("海豹贴身选择滑行", () =>
            {
                Spawn(EnemyKind.PlushSeal, 1.2f); _enemy.Motor.Tick(Time.fixedDeltaTime); Physics2D.Simulate(Time.fixedDeltaTime);
                typeof(FieldMinionBrain).GetField("ReadyAt", Hidden).SetValue(_enemy.Brain, 0f);
                Tick(.04f); Check(_enemy.Brain.StateName == "Slide", "把玩家身体当墙，近身改成叫或发呆");
            });
            Case("海豹远处使用冰块", () =>
            {
                Spawn(EnemyKind.PlushSeal, 5.5f); typeof(FieldMinionBrain).GetField("ReadyAt", Hidden).SetValue(_enemy.Brain, 0f);
                Tick(.04f); Check(_enemy.Brain.StateName == "BellySlap", "远处首选没有直接威胁的嚎叫");
            });
            Case("海豹不重复无效施法", () =>
            {
                Spawn(EnemyKind.PlushSeal, 3f); RuleSystem.Instance.Request(AttributeId.Gravity, RelationId.Weaken, RuleSource.Player);
                typeof(FieldMinionBrain).GetField("Pattern", Hidden).SetValue(_enemy.Brain, 1);
                typeof(FieldMinionBrain).GetField("ReadyAt", Hidden).SetValue(_enemy.Brain, 0f);
                Tick(.04f); Check(_enemy.Brain.StateName != "Howl", "世界已处于重力削弱时仍重复嚎叫");
            });
            Case("海豹已有技能可衔接", () =>
            {
                Spawn(EnemyKind.PlushSeal, 3f); int shots = 0;
                _enemy.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                typeof(FieldMinionBrain).GetField("ReadyAt", Hidden).SetValue(_enemy.Brain, 0f);
                Tick(3.5f); Check(shots > 0 || _enemy.Brain.StateName == "BellySlap", "一次滑行后长时间站着等所有技能共同冷却");
            });
            Case("守卫不额外发呆", () =>
            {
                Spawn(EnemyKind.StoneGuard, 1.6f); _enemy.Definition.AttackCooldown = 1f;
                Force("Attack"); Tick(_enemy.AnimationDuration + .6f);
                Check(_enemy.Brain.StateName == "Attack", "攻击冷却结束后仍被 Ignore 的额外等待卡住");
            });
            Case("自爆方块朝可见目标巡进", () =>
            { Spawn(EnemyKind.ExplosiveBlock, -3f); Tick(.04f); Check(_enemy.Motor.Intent.x < 0, "发现玩家后仍背对玩家巡逻"); });
            Case("发条鼠按当前位置迎击", () =>
            {
                Spawn(EnemyKind.ClockMouse, 1.5f); _enemy.transform.position += Vector3.right * 3f; Physics2D.SyncTransforms();
                Force("Pop"); Tick(_enemy.AnimationDuration + .04f);
                Check(_enemy.Motor.Intent.x < 0, "按出生点而非当前位置选择方向，朝反方向跑");
            });
            Case("弹簧怪按高度选择跳跃", () =>
            {
                Spawn(EnemyKind.SpringToy, 4f); _player.TeleportTo((Vector2)_enemy.transform.position + new Vector2(4f, 2.5f));
                _enemy.Motor.Tick(Time.fixedDeltaTime); Physics2D.Simulate(Time.fixedDeltaTime); Tick(.04f);
                Check(Vector2.Dot(_enemy.Motor.Velocity, _enemy.Motor.Up) > Mathf.Sqrt(2f * 9.81f * _enemy.Definition.MinionHopHeight) + .3f, "高处目标仍机械选择低跳");
            });
            Case("蝙蝠召唤物主动接敌", () =>
            {
                Spawn(EnemyKind.BatMinion, 4f); _enemy.Summoner = _enemy; Tick(.04f);
                Check(_enemy.Motor.Intent.x > .8f, "目标可见却持续留在出生点盘旋");
            });
            Case("抱熊冷却期间有近身防守", () =>
            {
                Spawn(EnemyKind.HugBear, 1.05f); Force("Hug");
                _enemy.ApplyDamage(new RuleGame.Infrastructure.DamageInfo { Amount = 1f, Attacker = _player });
                var input = typeof(PlayerController).GetField("_input", Hidden).GetValue(_player);
                input.GetType().GetField("_attackPressed", Hidden).SetValue(input, true);
                _player.SendMessage("FixedUpdate");
                Check(_player.AttackComboIndex >= 0, "没有启动真实攻击动作");
                Tick(.22f); Check(_enemy.Brain.StateName == "Chase" && _enemy.Motor.Intent.x < 0f, "拥抱冷却时站在原地挨打");
                var saved = _enemy.CaptureSnapshotState(); Tick(.6f); _enemy.RestoreSnapshotState(saved); Tick(.02f);
                Check(_enemy.Motor.Intent.x < 0f, "快照未恢复防守时间窗");
            });
            Case("积木前摇跟踪后锁定", () => Windup(EnemyKind.BrickBoss, "HammerPlayer"));
            Case("时钟起跑前选择方向", () => Windup(EnemyKind.ClockBoss, "JumpShake"));
            Case("兔子开场可近身撤退", () =>
            { Spawn(EnemyKind.RabbitBoss, 1.5f); Tick(.04f); Check(_enemy.Brain.StateName == "Hide", "开场近身却必须等完整撤退冷却"); });
            Case("蝙蝠 Boss 支援不被冲刺饿死", () =>
            {
                Spawn(EnemyKind.BatBoss, 6f); typeof(EnemyBrain).GetProperty("Phase").SetValue(_enemy.Brain, 2);
                typeof(BatBossBrain).GetField("_nextDash", Hidden).SetValue(_enemy.Brain, 0f);
                typeof(BatBossBrain).GetField("_nextSummon", Hidden).SetValue(_enemy.Brain, 0f);
                typeof(BatBossBrain).GetField("_nextRule", Hidden).SetValue(_enemy.Brain, float.PositiveInfinity);
                Tick(.04f); Check(_enemy.Brain.StateName == "Roar", "每次都先冲刺，召唤永远轮不到");
            });
            Directory.CreateDirectory("Logs/EnemyAI");
            File.WriteAllText("Logs/EnemyAI/Results.txt", $"Passed: {_passed}\nFailed: {_failed}\n" + string.Join("\n", Results));
            SessionState.SetBool(Pending, false); EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
        private static void ContactDamage()
        {
            Spawn(EnemyKind.BrickMinion, 4f); _enemy.Definition.MoveSpeed = 0f;
            var damageable = _player.GetComponent<EntityDamageable>(); damageable.SetInvulnerable(false);
            var serialized = new SerializedObject(damageable); serialized.FindProperty("_invincibleSeconds").floatValue = 0f; serialized.ApplyModifiedPropertiesWithoutUndo();
            var body = _enemy.GetComponent<Collider2D>(); var playerBody = _player.GetComponent<Collider2D>();
            float x = body.bounds.max.x + playerBody.bounds.extents.x + .015f - (playerBody.bounds.center.x - _player.transform.position.x);
            _player.TeleportTo(new Vector2(x, .55f));
            float health = _player.GetComponent<EntityStatus>().Health;
            _enemy.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime);
            Check(_player.GetComponent<EntityStatus>().Health < health, "实际碰撞边缘仍无法命中内缩受击箱");
            health = _player.GetComponent<EntityStatus>().Health;
            _enemy.SendMessage("FixedUpdate"); Check(_player.GetComponent<EntityStatus>().Health == health, "接触冷却失效，逐物理步重复扣血");
            _player.GetComponentInChildren<Hurtbox>().SetHittable(false);
            for (int i = 0; i < 60; i++) _enemy.SendMessage("FixedUpdate");
            Check(_player.GetComponent<EntityStatus>().Health == health, "关闭受击箱后仍被身体接触命中");
            _player.GetComponentInChildren<Hurtbox>().SetHittable(true);
            var wall = new GameObject("ContactWall") { layer = 8 }; Owned.Add(wall);
            wall.transform.position = new Vector3(body.bounds.max.x + .007f, 1f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.01f, 3f); Physics2D.SyncTransforms();
            for (int i = 0; i < 60; i++) _enemy.SendMessage("FixedUpdate");
            Check(_player.GetComponent<EntityStatus>().Health == health, "隔着地形仍造成身体接触伤害");
        }
        private static void Encounter(EnemyKind kind)
        {
            Spawn(kind, 3f); int attacks = 0;
            _enemy.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn || cue.Kind == EnemyFeedbackKind.Strike || cue.Kind == EnemyFeedbackKind.AreaStrike || cue.Kind == EnemyFeedbackKind.BeamSpawn) attacks++; };
            _enemy.Combat.OnDamageApplied += hit => { if (ReferenceEquals(hit.Attacker, _enemy)) attacks++; };
            for (int i = 0; i < Mathf.CeilToInt(15f / Time.fixedDeltaTime) && _enemy.gameObject.activeSelf; i++)
            {
                if (i % 90 == 0)
                {
                    float gap = (i / 90) % 4 == 0 ? 3f : (i / 90) % 4 == 1 ? 1.05f : (i / 90) % 4 == 2 ? -2.3f : 5.5f;
                    _player.TeleportTo(new Vector2(Mathf.Clamp(_enemy.transform.position.x + gap, -18f, 18f), .55f));
                }
                _enemy.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime);
                RuleSystem.Instance.Tick(Time.fixedDeltaTime);
                Vector2 position = _enemy.transform.position;
                Check(!float.IsNaN(position.x) && !float.IsNaN(position.y) && _enemy.Brain.StateName != null, "对战中状态或位置失效");
                Check(_enemy.AnimationDuration > 0f && !string.IsNullOrEmpty(_enemy.AnimationName), "对战中丢失动作");
            }
            Check(attacks > 0, "15 秒内换边/近身/拉远都没有发起有效进攻，state=" + _enemy.Brain.StateName);
            Results.Add($"Encounter {kind}: {attacks} attack/damage cues");
        }
        private static void Windup(EnemyKind kind, string action)
        {
            Spawn(kind, 2f); Force(action);
            _player.TeleportTo((Vector2)_enemy.transform.position + Vector2.left * 2f); Tick(.08f);
            Check(_enemy.Facing < 0, "前摇一开始就完全锁死方向");
            Tick(_enemy.AnimationDuration * .5f);
            _player.TeleportTo((Vector2)_enemy.transform.position + Vector2.right * 2f); Tick(.02f);
            Check(_enemy.Facing < 0, "过了锁向点仍追着玩家转身");
        }
        private static void Spawn(EnemyKind kind, float gap)
        {
            var floor = new GameObject("AI Ground") { layer = 8 }; Owned.Add(floor);
            floor.transform.position = new Vector3(0, -.5f); floor.AddComponent<BoxCollider2D>().size = new Vector2(100, 1);
            var combat = new GameObject("AI Combat").AddComponent<CombatSystem>(); Owned.Add(combat.gameObject);
            var playerObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab"), new Vector3(gap, .55f), Quaternion.identity);
            Owned.Add(playerObject); _player = playerObject.GetComponent<PlayerController>(); _player.enabled = false;
            _player.GetComponent<EntityStatus>().InitializeVitals(2000f, 100f); _player.GetComponent<EntityDamageable>().SetInvulnerable(true);
            bool boss = kind == EnemyKind.BrickBoss || kind == EnemyKind.ClockBoss || kind == EnemyKind.RabbitBoss || kind == EnemyKind.BatBoss;
            string path = boss || kind == EnemyKind.BrickMinion || kind == EnemyKind.BatMinion
                ? "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab" : MinionArtSetup.PrefabPath(kind);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var config = Object.Instantiate(prefab.GetComponent<EnemyActor>().Definition); Owned.Add(config);
            config.MinionNavigationEnabled = false; config.MinionSenseRange = 12f; config.MinionLeashRange = 15f;
            var enemyObject = Object.Instantiate(prefab, new Vector3(0f, config.BodySize.y * .5f + .02f), Quaternion.identity);
            Owned.Add(enemyObject); _enemy = enemyObject.GetComponent<EnemyActor>(); _enemy.enabled = false;
            _enemy.Definition = config; _enemy.Target = _player.transform; _enemy.Combat = combat;
            _enemy.SendMessage("Start"); _enemy.ArenaX = new Vector2(-20, 20); _enemy.ArenaY = new Vector2(-5, 20);
            _enemy.GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.None;
            Physics2D.SyncTransforms();
        }
        private static void Tick(float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++) _enemy.Brain.Tick(.02f);
        }
        private static void Force(string action) => typeof(EnemyBrain).GetMethod("Go", Hidden).Invoke(_enemy.Brain, new object[] { action });
        private static void Check(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        private static void Case(string name, Action action)
        {
            try { action(); _passed++; Results.Add("PASS " + name); }
            catch (Exception error) { _failed++; Results.Add("FAIL " + name + ": " + error.Message); }
            finally
            {
                RuleSystem.Instance.ClearAll();
                foreach (var shot in Object.FindObjectsByType<EnemyProjectile>()) Object.DestroyImmediate(shot.gameObject);
                for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
                Owned.Clear(); Physics2D.SyncTransforms();
            }
        }
    }
}
