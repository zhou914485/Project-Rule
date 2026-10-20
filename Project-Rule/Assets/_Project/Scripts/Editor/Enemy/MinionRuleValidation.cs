using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
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
    /// <summary>12 个真实预制体 × 30 个规则格位：验证物理、战斗、时钟与世界回溯的实际结果。</summary>
    public static class MinionRuleValidation
    {
        private const string Pending = "RuleGame.MinionRuleValidation";
        private const string CombatPending = "RuleGame.MinionRuleCombatValidation";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<string> Results = new List<string>();
        private static readonly List<GameObject> Owned = new List<GameObject>();
        private static EnemyActor _enemy, _ally;
        private static PlayerController _player;
        private static CombatSystem _combat;
        private static int _passed, _failed;
        private static readonly EnemyKind[] Kinds = { EnemyKind.BrickMinion, EnemyKind.BatMinion,
            EnemyKind.StoneGuard, EnemyKind.ExplosiveBlock, EnemyKind.BrickSoldier, EnemyKind.SpringToy,
            EnemyKind.ClockMouse, EnemyKind.ClockBomb, EnemyKind.PlushBird, EnemyKind.HugBear,
            EnemyKind.PlushSeal, EnemyKind.ChargingCube };
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离工程运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
        }
        public static void PreparePlacementBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离工程运行。");
            foreach (var kind in new[] { EnemyKind.BrickMinion, EnemyKind.BatMinion })
            {
                string path = "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try { root.GetComponent<EnemyActor>().UseSpawnRelativeArena = true; PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
        }
        public static void RunCombatBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离工程运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(CombatPending, true); EditorApplication.isPlaying = true;
        }
        private static void RunSharedCombat()
        {
            TimeManager.Initialize();
            string report = RuleGame.Editor.Rules.DamageProcessorVerifier.Run();
            Directory.CreateDirectory("Logs/MinionRules"); File.WriteAllText("Logs/MinionRules/SharedCombat.txt", report);
            SessionState.SetBool(CombatPending, false); EditorApplication.Exit(report.Contains("✗") ? 1 : 0);
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || (!SessionState.GetBool(Pending, false) && !SessionState.GetBool(CombatPending, false))) return;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) { if (SessionState.GetBool(CombatPending, false)) RunSharedCombat(); else Run(); } };
        }
        private static void Run()
        {
            TimeManager.Initialize(); WorldRegistry.Initialize(); SnapshotRoster.Initialize();
            RuleSystem.Initialize(AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Rules/RuleCatalog.asset"));
            SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
            Physics2D.simulationMode = SimulationMode2D.Script;
            _combat = new GameObject("RuleCombat").AddComponent<CombatSystem>();
            var ground = new GameObject("RuleGround") { layer = 8 };
            ground.transform.position = new Vector3(0f, -.5f); ground.AddComponent<BoxCollider2D>().size = new Vector2(200f, 1f);
            foreach (var kind in Kinds)
            {
                foreach (AttributeId attribute in Enum.GetValues(typeof(AttributeId)))
                foreach (RelationId relation in Enum.GetValues(typeof(RelationId)))
                    Case($"{kind}/{attribute}/{relation}", () => Matrix(kind, attribute, relation));
                Case(kind + "/直接拖放与自动接线", () => DropIn(kind));
                Case(kind + "/计时与动作独立/规则切换", () => Clocks(kind));
                Case(kind + "/质量轻重反转", () => MassInversion(kind));
            }
            Case("专属状态在摩擦反转下收尾", SpecialStates);
            Case("跳跃高度随重力改变", JumpGravity);
            Case("锁动作期间仍响应世界重力", LockedPhysics);
            Case("时间失效的持续伤害与冷却", TimedBehaviors);
            Case("因果失效的敌人实际命中与自然回能", SharedRecovery);
            RuleSystem.Instance.ClearAll();
            string folder = Path.GetFullPath("Logs/MinionRules"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Results.txt"), $"Passed: {_passed}\nFailed: {_failed}\nMatrix: 12 x 6 x 5 = 360\n" + string.Join("\n", Results));
            SessionState.SetBool(Pending, false); EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
        private static void Case(string name, Action action)
        {
            try { action(); _passed++; Results.Add("PASS " + name); }
            catch (Exception error) { _failed++; Results.Add("FAIL " + name + ": " + error); }
            finally
            {
                RuleSystem.Instance.ClearAll(); _combat.Processor.Tick();
                foreach (var shot in Object.FindObjectsByType<EnemyProjectile>()) Object.DestroyImmediate(shot.gameObject);
                foreach (var go in Owned) if (go != null) Object.DestroyImmediate(go);
                Owned.Clear(); Physics2D.SyncTransforms(); SnapshotSystem.Instance.ClearBuffer();
            }
        }
        private static EnemyActor Spawn(EnemyKind kind, Vector2 position)
        {
            string path = kind == EnemyKind.BrickMinion || kind == EnemyKind.BatMinion
                ? "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab" : MinionArtSetup.PrefabPath(kind);
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), position, Quaternion.identity).GetComponent<EnemyActor>();
            Owned.Add(actor.gameObject); actor.enabled = false; actor.Target = _player.transform; actor.Combat = _combat;
            actor.SendMessage("Start"); actor.Status.InitializeVitals(200f, 100f);
            actor.ArenaX = new Vector2(-90f, 90f); actor.ArenaY = new Vector2(-30f, 40f);
            actor.GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.None;
            Force(actor, "Walk"); actor.GetComponent<Collider2D>().enabled = true;
            return actor;
        }
        private static void Setup(EnemyKind kind)
        {
            RuleSystem.Instance.ClearAll();
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab"), new Vector3(5f, 1f), Quaternion.identity).GetComponent<PlayerController>();
            _player.enabled = false; Owned.Add(_player.gameObject); _player.GetComponent<EntityStatus>().InitializeVitals(200f, 100f);
            var damageable = _player.GetComponent<EntityDamageable>();
            var serialized = new SerializedObject(damageable);
            serialized.FindProperty("_invincibleSeconds").floatValue = 0f; serialized.ApplyModifiedPropertiesWithoutUndo();
            _enemy = Spawn(kind, new Vector2(0f, 10f));
            _ally = Spawn(kind, new Vector2(1.5f, 10f));
            foreach (var actor in new[] { _enemy, _ally }) actor.Status.ApplyHealthDelta(-100f);
            _player.GetComponent<EntityStatus>().ApplyHealthDelta(-100f); Physics2D.SyncTransforms();
        }
        private static void Force(EnemyActor actor, string state) => typeof(EnemyBrain).GetMethod("Go", Hidden).Invoke(actor.Brain, new object[] { state });
        private static void Pose(EnemyActor actor, Vector2 position)
        {
            actor.GetComponent<Rigidbody2D>().position = position; actor.transform.position = position;
            actor.Motor.Stop(); Physics2D.SyncTransforms();
        }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Near(float actual, float expected, string message, float tolerance = .035f)
            => Check(Mathf.Abs(actual - expected) <= tolerance, $"{message}: {actual:F4} != {expected:F4}");
        private static void Request(AttributeId attribute, RelationId relation)
            => Check(RuleSystem.Instance.Request(attribute, relation, RuleSource.Player).Succeeded, "规则请求失败");
        private static void Matrix(EnemyKind kind, AttributeId attribute, RelationId relation)
        {
            Setup(kind);
            switch (attribute)
            {
                case AttributeId.Gravity:
                    Request(attribute, relation);
                    for (int i = 0; i < 10; i++) { _enemy.Motor.Tick(.02f); Physics2D.Simulate(.02f); }
                    float expected = (relation == RelationId.Revert ? 9.81f : relation == RelationId.Nullify ? 0f
                        : relation == RelationId.Enhance ? -19.62f : relation == RelationId.Weaken ? -2.943f : -9.81f);
                    if (_enemy.IsFlying) expected += 9.81f;
                    Near(_enemy.Motor.Velocity.y, expected * _enemy.Definition.GravityScale * .2f, "真实竖直速度");
                    break;
                case AttributeId.Mass:
                    Request(attribute, relation);
                    var affected = _enemy.GetComponent<RuleAffectedBody>();
                    float mass = affected.BaseMass;
                    float scale = relation == RelationId.Enhance ? 3f : relation == RelationId.Weaken ? .3f : relation == RelationId.Nullify ? .05f : 1f;
                    Near(_enemy.GetComponent<Rigidbody2D>().mass, relation == RelationId.Revert ? 1f / mass : mass * scale, "刚体质量");
                    _enemy.Motor.AddImpulse(Vector2.right);
                    Near(_enemy.Motor.CaptureImpulse().x, 1f / Mathf.Max(.1f, _enemy.GetComponent<Rigidbody2D>().mass), "真实击退响应");
                    var late = Spawn(kind, new Vector2(25f, 10f));
                    Near(late.GetComponent<Rigidbody2D>().mass, _enemy.GetComponent<Rigidbody2D>().mass, "规则期间新生成实体");
                    break;
                case AttributeId.Friction:
                    Pose(_ally, new Vector2(40f, 10f));
                    Request(attribute, relation);
                    // 飞行实体没有地面摩擦，保持悬浮；地面实体检查实际行走、制动与材质。
                    Pose(_enemy, new Vector2(0f, _enemy.IsFlying ? 10f : _enemy.Definition.BodySize.y * .5f + .015f));
                    _enemy.Motor.Tick(.02f); Physics2D.Simulate(.02f);
                    _enemy.Motor.Intent = Vector2.right;
                    for (int i = 0; i < 40; i++) { _enemy.Motor.Tick(.02f); Physics2D.Simulate(.02f); }
                    if (_enemy.IsFlying) Check(_enemy.Motor.Velocity.x > 0f, "飞行控制被地面摩擦错误接管");
                    else if (relation == RelationId.Nullify) Near(_enemy.Motor.Velocity.x, 0f, "无摩擦不能凭行走起步");
                    else
                    {
                        float speed = _enemy.Definition.MoveSpeed * (relation == RelationId.Enhance ? 1f / 3f : relation == RelationId.Revert ? -1f : 1f);
                        Near(_enemy.Motor.Velocity.x, speed, "行走速度/方向", .09f);
                    }
                    if (!_enemy.IsFlying && relation == RelationId.Nullify)
                    {
                        _enemy.Motor.Restore(Vector2.right * 2f, Vector2.zero); _enemy.Motor.Stop();
                        for (int i = 0; i < 20; i++) { _enemy.Motor.Tick(.02f); Physics2D.Simulate(.02f); }
                        Near(_enemy.Motor.Velocity.x, 2f, "无摩擦保留滑行动量");
                    }
                    break;
                case AttributeId.Time:
                    TimeCell(relation); break;
                case AttributeId.Damage:
                    Request(attribute, relation);
                    _combat.Processor.Tick();
                    _combat.ApplyDamage(_player, _enemy, 10f, 0f);
                    float damage = relation == RelationId.Nullify || relation == RelationId.Constant ? 0f
                        : relation == RelationId.Revert ? -10f : relation == RelationId.Enhance ? 20f : 5f;
                    Near(_enemy.Status.Health, 100f - damage, "敌人实际生命结算");
                    if (relation == RelationId.Constant)
                    { RuleSystem.Instance.Cancel(attribute); _combat.Processor.Tick(); Near(_enemy.Status.Health, 90f, "挂账结束结算"); }
                    break;
                case AttributeId.Causality:
                    CausalityCell(relation); break;
            }
        }
        private static void TimeCell(RelationId relation)
        {
            float hp = _enemy.Status.Health, age = _enemy.Brain.SimulationAge;
            Vector2 position = _enemy.transform.position;
            if (relation == RelationId.Revert) SnapshotSystem.Instance.CaptureToBuffer();
            if (relation != RelationId.Revert) Request(AttributeId.Time, relation);
            _enemy.Brain.Tick(.1f * TimeManager.Instance.TimeScale);
            _enemy.Status.ApplyHealthDelta(-5f); Pose(_enemy, position + Vector2.right * 2f);
            if (relation == RelationId.Revert || relation == RelationId.Constant)
            {
                if (relation == RelationId.Revert) Request(AttributeId.Time, relation);
                else Check(RuleSystem.Instance.TryRewind(), "定点回溯失败");
                Near(_enemy.Status.Health, hp, "回溯恢复生命");
                Near(_enemy.GetComponent<Rigidbody2D>().position.x, position.x, "回溯恢复位置");
                Near(_enemy.Brain.SimulationAge, age, "回溯恢复状态时钟");
                Near(_enemy.Brain.MechanicsAge, age, "回溯恢复冷却时钟");
            }
            else
            {
                float elapsed = .1f * (relation == RelationId.Enhance ? 2f : relation == RelationId.Weaken ? .5f : 1f);
                Near(_enemy.Brain.SimulationAge, age + elapsed, "动作时钟/世界倍率");
                Near(_enemy.Brain.MechanicsAge, relation == RelationId.Nullify ? 0f : elapsed, "机制时钟");
            }
        }
        private static void CausalityCell(RelationId relation)
        {
            Request(AttributeId.Causality, relation);
            if (relation == RelationId.Nullify)
            {
                _enemy.Status.TryConsumeEnergy(50f); _ally.Status.TryConsumeEnergy(50f); _player.GetComponent<EntityStatus>().TryConsumeEnergy(50f);
                _combat.RestoreEnergy(_enemy.Status, 5f);
                Near(_enemy.Status.Energy, 55f, "敌人回能"); Near(_player.GetComponent<EntityStatus>().Energy, 55f, "敌人回能共享玩家"); Near(_ally.Status.Energy, 55f, "恢复共享同伴");
                bool healed = false, damaged = false;
                for (int i = 0; i < 24; i++)
                {
                    foreach (var actor in new[] { _enemy, _ally }) { Force(actor, "Walk"); actor.Status.InitializeVitals(200, 100); actor.Status.ApplyHealthDelta(-100); typeof(EnemyActor).GetField("_invulnerability", Hidden).SetValue(actor, 0f); }
                    var playerStatus = _player.GetComponent<EntityStatus>(); playerStatus.InitializeVitals(200, 100); playerStatus.ApplyHealthDelta(-100);
                    UnityEngine.Random.InitState(831 + i); _combat.ApplyDamage(_player, _enemy, 2f, 0f);
                    float total = _enemy.Status.Health + _ally.Status.Health + playerStatus.Health;
                    Check(Mathf.Abs(total - 298f) < .05f || Mathf.Abs(total - 296f) < .05f || Mathf.Abs(total - 306f) < .05f, "随机倍率/对象/全体治疗错误 " + total);
                    healed |= total > 300f; damaged |= total < 300f;
                }
                Check(healed && damaged, "没有覆盖随机治疗和随机伤害");
                return;
            }
            _combat.ApplyDamage(_player, _enemy, 2f, 0f);
            Near(_enemy.Status.Health, relation == RelationId.Revert ? 100f : relation == RelationId.Weaken ? 99f : 98f, "因果目标/倍率");
            Near(_player.GetComponent<EntityStatus>().Health, relation == RelationId.Revert ? 98f : 100f, "因果反转落到发起方");
            Near(_ally.Status.Health, relation == RelationId.Enhance ? 98f : 100f, "因果扩散同伴");
        }
        private static void Clocks(EnemyKind kind)
        {
            Setup(kind); Request(AttributeId.Time, RelationId.Nullify);
            float animation = _enemy.AnimationElapsed;
            _enemy.Brain.Tick(.5f);
            Near(_enemy.Brain.MechanicsAge, 0f, "失效期间冷却仍推进");
            Check(_enemy.AnimationElapsed > animation || _enemy.Brain.StateName != "Walk", "失效错误停止动画");
            RuleSystem.Instance.Cancel(AttributeId.Time); _enemy.Brain.Tick(.2f);
            Near(_enemy.Brain.MechanicsAge, .2f, "取消失效后未接续");
            var saved = _enemy.CaptureSnapshotState(); _enemy.Brain.Tick(.3f); _enemy.RestoreSnapshotState(saved);
            Near(_enemy.Brain.MechanicsAge, .2f, "快照没有恢复机制时钟");
        }
        private static void MassInversion(EnemyKind kind)
        {
            Setup(kind);
            var affected = _enemy.GetComponent<RuleAffectedBody>(); var body = _enemy.GetComponent<Rigidbody2D>();
            // 真实预制体默认质量可能为 1；用质量基准探针覆盖轻/重两侧，随后恢复。
            var field = typeof(RuleAffectedBody).GetField("_baseMass", Hidden);
            foreach (float mass in new[] { .5f, 2f })
            { field.SetValue(affected, mass); Request(AttributeId.Mass, RelationId.Revert); Near(body.mass, 1f / mass, "轻重互换"); }
        }
        private static void SpecialStates()
        {
            foreach (var pair in new[] { (EnemyKind.ExplosiveBlock, "Armed"), (EnemyKind.ClockMouse, "Hide"),
                (EnemyKind.PlushBird, "Fly"), (EnemyKind.PlushSeal, "Slide"), (EnemyKind.HugBear, "Hug") })
            {
                Setup(pair.Item1); Request(AttributeId.Friction, RelationId.Revert); Force(_enemy, pair.Item2);
                if (pair.Item1 == EnemyKind.HugBear) _player.TeleportTo((Vector2)_enemy.transform.position + Vector2.right * .9f);
                _enemy.Brain.Tick(.1f);
                Check(_enemy.Brain.StateName == pair.Item2, pair + " 被共用反转逻辑覆盖");
                if (pair.Item1 == EnemyKind.PlushBird) Check(Mathf.Abs(_enemy.Motor.Intent.y) > .001f, "飞行丢失高度控制");
                if (pair.Item1 == EnemyKind.ExplosiveBlock)
                {
                    Request(AttributeId.Time, RelationId.Nullify); _enemy.Brain.Tick(3f);
                    Check(_enemy.Brain.StateName == "Armed", "引信应停止计时");
                    RuleSystem.Instance.Cancel(AttributeId.Time); _enemy.Brain.Tick(_enemy.Definition.MinionFuseDuration);
                    Check(_enemy.Brain.StateName == "Explode", "引信未在恢复后继续");
                }
                foreach (var go in Owned) if (go != null) Object.DestroyImmediate(go); Owned.Clear();
            }
        }
        private static void DropIn(EnemyKind kind)
        {
            Setup(kind);
            _player.TeleportTo(new Vector2(85f, 1f));
            string path = kind == EnemyKind.BrickMinion || kind == EnemyKind.BatMinion
                ? "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab" : MinionArtSetup.PrefabPath(kind);
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), new Vector3(80f, 1f), Quaternion.identity).GetComponent<EnemyActor>();
            Owned.Add(actor.gameObject); actor.enabled = false; actor.SendMessage("Start");
            Check(actor.Brain != null && actor.Target == _player.transform && actor.Combat != null, "放置后未自动接好玩家与战斗服务");
            Check(actor.GetComponent<RuleAffectedBody>().IsRegistered && actor.GetComponentInChildren<Hurtbox>(true) != null, "规则或受击箱未接入");
            Near((actor.ArenaX.x + actor.ArenaX.y) * .5f, 80f + actor.ArenaOffset.x, "活动范围仍围绕世界原点");
            // 同一预制体由 Boss 召唤时，应继续继承 Boss 的边界，不能反过来覆盖父场地。
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), new Vector3(80f, 1f), Quaternion.identity);
            var child = root.GetComponent<EnemyActor>(); Owned.Add(root); child.enabled = false;
            child.Summoner = actor; child.ArenaX = new Vector2(60f, 100f); child.ArenaY = new Vector2(-5f, 30f);
            child.SendMessage("Start");
            Check(child.ArenaX == new Vector2(60f, 100f) && child.ArenaY == new Vector2(-5f, 30f), "召唤物覆盖了继承的场地范围");
        }
        private static void LockedPhysics()
        {
            Setup(EnemyKind.ExplosiveBlock); Force(_enemy, "Armed");
            Request(AttributeId.Time, RelationId.Nullify); Request(AttributeId.Gravity, RelationId.Revert);
            float initial = _enemy.transform.position.y;
            for (int i = 0; i < 20; i++) { _enemy.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
            Check(_enemy.transform.position.y > initial + .2f && _enemy.Brain.StateName == "Armed", "引信锁位抵消了反转重力，或时间失效时发生爆炸");
        }
        private static void TimedBehaviors()
        {
            Setup(EnemyKind.HugBear);
            _player.TeleportTo((Vector2)_enemy.transform.position + Vector2.right * .9f);
            Force(_enemy, "Hug"); _enemy.Brain.Tick(.02f);
            float health = _player.GetComponent<EntityStatus>().Health;
            Request(AttributeId.Time, RelationId.Nullify);
            float animation = _enemy.AnimationElapsed;
            _enemy.Brain.Tick(4f);
            Check(_enemy.Brain.StateName == "Hug", "拥抱持续时间在失效期间消耗");
            Near(_player.GetComponent<EntityStatus>().Health, health, "持续伤害在失效期间仍触发");
            Check(_enemy.AnimationElapsed > animation + 3f, "时间失效停止了拥抱动画");
            _enemy.Status.BeginCooldown(AttributeId.Gravity, RelationId.Weaken, 2f);
            _enemy.Status.SendMessage("Update");
            Near(_enemy.Status.GetCooldownRemaining(AttributeId.Gravity, RelationId.Weaken), 2f, "规则施法冷却仍推进");
            RuleSystem.Instance.Cancel(AttributeId.Time); _enemy.Brain.Tick(1.1f);
            Check(_player.GetComponent<EntityStatus>().Health < health, "恢复时间后拥抱不再结算");
            Force(_enemy, "Hurt"); Request(AttributeId.Time, RelationId.Nullify); _enemy.Brain.Tick(2f);
            Check(_enemy.Brain.StateName == "Hurt", "受击硬直在时间失效期间被提前清除");
            RuleSystem.Instance.Cancel(AttributeId.Time); _enemy.Brain.Tick(.5f);
            Check(_enemy.Brain.StateName != "Hurt", "恢复后硬直未正常解除");
        }
        private static void SharedRecovery()
        {
            Setup(EnemyKind.PlushSeal); Request(AttributeId.Causality, RelationId.Nullify);
            foreach (var status in new[] { _enemy.Status, _ally.Status, _player.GetComponent<EntityStatus>() }) status.TryConsumeEnergy(50f);
            _enemy.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime);
            float expected = 50f + _enemy.Definition.EnergyRegen * Time.fixedDeltaTime;
            Near(_enemy.Status.Energy, expected, "敌人自然恢复");
            Near(_player.GetComponent<EntityStatus>().Energy, expected, "自然恢复未共享玩家");
            Near(_ally.Status.Energy, expected, "自然恢复未共享同伴");
            float before = _player.GetComponent<EntityStatus>().Energy;
            _combat.ApplyDamage(_enemy, _player.GetComponent<EntityDamageable>(), 1f, 0f);
            float gain = _player.GetComponent<EntityStatus>().Energy - before;
            Check(gain >= 4f, "敌人攻击未触发共享回能");
            Near(_enemy.Status.Energy, _player.GetComponent<EntityStatus>().Energy, "命中恢复未共享敌人");
            RuleSystem.Instance.Cancel(AttributeId.Causality);
            _combat.RestoreEnergy(_enemy.Status, 1f);
            Near(_player.GetComponent<EntityStatus>().Energy, before + gain, "规则结束后仍在共享恢复");
        }
        private static void JumpGravity()
        {
            Setup(EnemyKind.SpringToy);
            float[] heights = new float[3]; int index = 0;
            foreach (var relation in new[] { RelationId.Enhance, RelationId.Constant, RelationId.Weaken })
            {
                Request(AttributeId.Gravity, relation); Pose(_enemy, new Vector2(0, _enemy.Definition.BodySize.y * .5f + .015f));
                _enemy.Motor.Tick(.02f); Physics2D.Simulate(.02f); _enemy.Motor.Jump(1.5f);
                float initial = _enemy.transform.position.y, maximum = initial;
                for (int i = 0; i < 100; i++)
                { _enemy.Motor.Tick(.02f); Physics2D.Simulate(.02f); maximum = Mathf.Max(maximum, _enemy.transform.position.y); }
                heights[index++] = maximum - initial;
            }
            Check(heights[0] < heights[1] * .7f && heights[2] > heights[1] * 2f, "跳跃顶点不随重力改变 " + string.Join(",", heights));
        }
    }
}
