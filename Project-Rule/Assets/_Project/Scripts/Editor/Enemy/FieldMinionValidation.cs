using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Enemy
{
    public static class FieldMinionValidation
    {
        private const string Pending = "RuleGame.FieldMinionValidation";
        private static int _passed, _failed;
        private static GameObject _root;
        private static EnemyActor _actor;
        private static EntityStatus _player;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于隔离工程。");
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
            TimeManager.Initialize(); WorldRegistry.Initialize();
            var catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Rules/RuleCatalog.asset");
            RuleSystem.Initialize(catalog);
            var previous = Physics2D.simulationMode; Physics2D.simulationMode = SimulationMode2D.Script;
            try
            {
                Case(EnemyKind.StoneGuard, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right;
                    Step(); Check(State == "Attack", "石头人巡逻时发现近处玩家后停下锤击");
                    float attackDuration = _actor.AnimationDuration;
                    float health = _player.Health; Step(attackDuration * _actor.Definition.ImpactFraction + .01f);
                    Check(_player.Health == health - 5, "锤击只经统一伤害结算造成表中五点伤害");
                    Step(attackDuration * (1f - _actor.Definition.ImpactFraction));
                    Check(State == "Ignore", "锤击后进入恢复并保留冷却");
                    Step(.1f); Check(State == "Ignore", "恢复初段不会立即连打");
                    WaitFor("Attack", _actor.Definition.AttackCooldown + .5f);
                    Check(State == "Attack", "攻击冷却完成后主动再次锤击");
                });
                Case(EnemyKind.BrickSoldier, () =>
                {
                    Kill(); Step(); Check(State == "Down" && !_actor.Brain.BodyEnabled && !_actor.Defeated, "积木士兵散架时禁用身体而不永久销毁");
                    _actor.Status.SetPaused(true); Step(20); _actor.Status.SetPaused(false);
                    Check(State == "Down" && _actor.Brain.ActionElapsed < .1f, "暂停不消耗七秒重组等待");
                    object saved = _actor.CaptureSnapshotState();
                    Step(7.01f); Check(State == "Rebirth", "七秒后开始重组");
                    Step(1); Check(State == "Patrol" && _actor.Status.Health == 7f && _actor.Brain.BodyEnabled, "重组完成恢复七点生命与巡逻");
                    _actor.RestoreSnapshotState(saved); Check(State == "Down" && !_actor.GetComponent<Collider2D>().enabled, "快照恢复散架与禁用的身体");
                });
                Case(EnemyKind.SpringToy, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 5;
                    for (int i = 0; i < 30; i++) PhysicalStep();
                    float start = _actor.transform.position.y;
                    for (int i = 0; i < 12; i++) PhysicalStep();
                    Check(_actor.GetComponent<Rigidbody2D>().position.y > start + .3f, "弹簧怪实际跳起追向玩家，使用真实碰撞移动");
                    _actor.Target.position = _actor.transform.position + Vector3.right * 5;
                    for (int i = 0; i < 150 && (!_actor.Motor.Grounded || _actor.Motor.Velocity.y > .1f); i++) PhysicalStep();
                    float highestVelocity = 0f;
                    for (int i = 0; i < 10; i++) { PhysicalStep(); highestVelocity = Mathf.Max(highestVelocity, _actor.Motor.Velocity.y); }
                    Check(highestVelocity <= .1f, "弹簧怪落地后才开始半秒等待，不用空中时间抵消冷却");
                    Kill(); Step(); Check(State == "Down", "弹簧怪按已确认需求进入可重组散架");
                    Step(7.1f); Step(1); Check(_actor.Status.Health == 20 && State == "Patrol", "弹簧怪重组后恢复二十点生命");
                });
                foreach (var kind in new[] { EnemyKind.ExplosiveBlock, EnemyKind.ClockBomb })
                    Case(kind, () =>
                    {
                        _actor.Target.position = _actor.transform.position + Vector3.right * 1.3f;
                        var extra = new GameObject("SecondPlayerCollider"); extra.transform.SetParent(_player.transform, false);
                        extra.AddComponent<BoxCollider2D>().size = new Vector2(.4f,.7f); extra.AddComponent<Hurtbox>(); Physics2D.SyncTransforms();
                        if (kind == EnemyKind.ClockBomb)
                        {
                            _actor.Target.position = _actor.transform.position + Vector3.right * 4; Step();
                            Check(State == "Chase", "发条炸弹见到玩家后主动冲刺");
                            Step(); Check(_actor.Motor.Intent.x > 0f && _actor.Motor.SpeedMultiplier > 1f, "主动冲刺使用独立速度倍率");
                            _actor.Target.position = _actor.transform.position + Vector3.right * 1.3f; Physics2D.SyncTransforms();
                        }
                        Kill(); Check(State == "Armed" && _actor.gameObject.activeSelf, kind + "受到致命伤仍先进入引信阶段");
                        float health = _player.Health; Step(.99f); Check(_player.Health == health, "引信期间不提前造成爆炸伤害");
                        object saved = _actor.CaptureSnapshotState();
                        Step(.02f); Step(.02f); Check(_player.Health == health - 20, "爆炸仅命中一次，同一玩家多个碰撞体不重复扣血");
                        Step(.1f); Check(_player.Health == health - 20, "爆炸收尾不会每帧重复伤害");
                        _actor.RestoreSnapshotState(saved); Check(State == "Armed", "快照能恢复剩余引信");
                    });
                Case(EnemyKind.ClockMouse, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 2;
                    Step(3.1f); Check(State == "Pop", "三格内玩家在冷却结束后触发钻出");
                    Step(.81f); Move(2); Step(); Move(-.1f); Step();
                    Check(State == "Walk", "第一次经过出生点仍继续移动");
                    Move(-8); Step(); Move(.1f); Step(); Check(State == "Hide", "第二次经过出生点才钻地");
                    Step(.81f); Check(State == "Idle" && !_actor.Brain.BodyEnabled && _actor.Brain.DamageLocked, "钻地后隐藏身体与伤害判定");
                    Step(2.9f); Check(State == "Idle", "钻地后的三秒冷却不会跳过");
                });
                Case(EnemyKind.PlushBird, () =>
                {
                    _actor.Target.position = new Vector3(20,3.5f,0);
                    float altitude = _actor.transform.position.y;
                    for (int i = 0; i < 35; i++) PhysicalStep();
                    Check(Mathf.Abs(_actor.GetComponent<Rigidbody2D>().position.y - altitude) < .6f && Mathf.Abs(_actor.transform.position.x) > .3f,
                        "玩具鸟无目标时在出生高度附近浮动巡飞");
                    _actor.Target.position = _actor.transform.position + Vector3.right * 4;
                    EnemyProjectile shot = null;
                    _actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shot = (EnemyProjectile)cue.Subject; };
                    Step(); Step(_actor.AnimationDuration * .35f);
                    _actor.Target.position = _actor.transform.position - Vector3.right * 4;
                    Step(_actor.AnimationDuration * .35f);
                    Check(shot != null && shot.Velocity.x > 0f, "法术球朝前摇锁定位置发射，锁定后换边不追踪");
                    Step(_actor.AnimationDuration * .35f); Check(State == "Fly", "法术球出手后回到立体巡飞");
                });
                Case(EnemyKind.HugBear, () =>
                {
                    var controller = _player.gameObject.AddComponent<PlayerController>(); controller.enabled = false;
                    _actor.Target.position = _actor.transform.position + Vector3.right * 1.05f;
                    Physics2D.SyncTransforms(); Step(); Check(State == "Stand", "抱抱熊看到玩家先起身");
                    Step(.81f); Step(); Check(State == "Hug" && controller.MovementHeld, "碰到玩家后拥抱，限制移动并保留攻击输入");
                    float health = _player.Health; Step(); Check(_player.Health == health - 20, "拥抱伤害按表中二十点与独立间隔结算");
                    object saved = _actor.CaptureSnapshotState();
                    _actor.ApplyDamage(new DamageInfo { Amount = 1 }); Check(!controller.MovementHeld, "玩家打中抱抱熊可立即挣脱");
                    _actor.RestoreSnapshotState(saved); Check(controller.MovementHeld && State == "Hug", "回溯到拥抱状态重新取得自己的移动限制");
                    _actor.enabled = true; _actor.gameObject.SetActive(false);
                    Check(!controller.MovementHeld, "停用或卸载抱抱熊不会残留移动限制");
                });
                Case(EnemyKind.PlushSeal, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 2;
                    WaitFor("Slide", 1f); Check(State == "Slide", "海豹近处优先选择有效滑行");
                    _actor.Target.position = _actor.transform.position + Vector3.right;
                    Physics2D.SyncTransforms(); float hp = _player.Health;
                    Step(_actor.Duration("Slide") * .35f + .01f);
                    Check(State == "Wander" && _player.Health == hp - 20, "滑行碰到玩家结算一次伤害后收招，不叠加接触伤害");
                    _actor.Target.position = _actor.transform.position + Vector3.right * 4;
                    Force("Howl"); float energy = _actor.Status.Energy;
                    Step(_actor.AnimationDuration * _actor.Definition.ImpactFraction + .01f);
                    Check(_actor.Status.Energy < energy, "嚎叫通过共享规则释放入口并支付能量");
                    Force("BellySlap");
                    int shots = 0; _actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) shots++; };
                    Step(_actor.AnimationDuration * _actor.Definition.ImpactFraction + .01f);
                    Check(shots == 3, "拍肚子在锁定位置上方生成有限三枚下落冰块");
                    Step(.02f); Check(shots == 3, "冰块命中时点只触发一次出手");
                });
                Case(EnemyKind.StoneGuard, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 4;
                    Step(); Check(State == "Chase" && _actor.Motor.Intent.x > 0f, "近战守卫发现射程外玩家会主动靠近");
                    var wall = new GameObject("SightBlock"); wall.transform.SetParent(_root.transform); wall.layer = 8;
                    wall.transform.position = _actor.transform.position + Vector3.right * 2;
                    wall.AddComponent<BoxCollider2D>().size = new Vector2(.3f,5f); Physics2D.SyncTransforms();
                    Step(.2f); Check(State == "Chase", "失去视线后短暂搜索最后已知位置");
                    Step(1.6f); Check(State == "Patrol", "搜索超时返回巡逻，不永久隔墙追踪");
                    UnityEngine.Object.DestroyImmediate(wall);
                    _actor.Target.position = _actor.transform.position + Vector3.right * 10;
                    _actor.Definition.MinionSenseRange = 20f;
                    Step(); Check(State == "Patrol", "看得到但超过出生点追击范围时不被无限拉走");
                });
                Case(EnemyKind.PlushSeal, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 20;
                    _actor.Brain.OnHit(10f); Check(State != "Hurt", "普通攻击有受伤反馈但未达破韧阈值时不硬直");
                    object saved = _actor.CaptureSnapshotState();
                    _actor.Brain.OnHit(10f); Check(State == "Hurt", "连续伤害达到阈值才打断动作");
                    Step(.26f); _actor.Brain.OnHit(24f);
                    Check(State != "Hurt", "硬直结束后的保护窗口阻止无限连控");
                    _actor.RestoreSnapshotState(saved); _actor.Brain.OnHit(10f);
                    Check(State == "Hurt", "快照同时恢复累计破韧与硬直冷却");
                });
                Case(EnemyKind.StoneGuard, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right;
                    Step(); Step(.1f);
                    _actor.Target.position = _actor.transform.position - Vector3.right;
                    Step(.05f); Check(_actor.Facing < 0f, "近战前摇早期会跟随玩家换边");
                    Step(.1f); _actor.Target.position = _actor.transform.position + Vector3.right;
                    Step(.02f); Check(_actor.Facing < 0f, "近战进入出手后锁定朝向，玩家可绕背躲避");
                    _actor.Brain.OnHit(24f); Check(State == "Attack", "已出手的攻击不会被普通连击强制重启");
                    Step(.22f); float hp = _player.Health; Step(.1f);
                    Check(_player.Health == hp, "锁定后的攻击不会瞬间翻身打中背后的玩家");
                });
                Case(EnemyKind.PlushBird, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right;
                    Step(); Step(); Check(_actor.Motor.Intent.x < 0f, "玩具鸟贴身时一边准备射击一边拉开距离");
                    _actor.Target.position = _actor.transform.position + Vector3.right * 6;
                    Step(); Check(_actor.Motor.Intent.x > 0f, "玩家离开理想射程时玩具鸟主动靠近");
                });
                Case(EnemyKind.PlushSeal, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 5.8f;
                    _actor.Status.TryConsumeEnergy(_actor.Status.Energy);
                    WaitFor("BellySlap", 1f); Check(State == "BellySlap", "海豹滑不到目标且无能量施法时选择有效的落冰攻击");
                });
                Case(EnemyKind.PlushSeal, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 3;
                    WaitFor("Slide", 1f); Step(.1f);
                    Check(State == "Slide" && _actor.Motor.Intent == Vector2.zero, "海豹滑行保留可辨认的蓄力前摇");
                    Step(.2f); Check(_actor.Motor.Intent.x > 0f, "蓄力结束后才沿起手方向冲出");
                    _actor.Target.position = _actor.transform.position - Vector3.right * 3;
                    Step(); Check(_actor.Motor.Intent.x > 0f, "滑行出手后不瞬间转向追踪");
                });
                Case(EnemyKind.ClockMouse, () =>
                {
                    _actor.Target.position = _actor.transform.position + Vector3.right * 2;
                    Step(3.1f); Step(.81f); Step(12f);
                    Check(State == "Hide", "发条鼠被地形阻止穿过出生点时有钻地兜底");
                });
            }
            finally
            {
                Physics2D.gravity = new Vector2(0,-9.81f); Physics2D.simulationMode = previous;
                SessionState.SetBool(Pending, false);
            }
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/FieldMinionValidation.txt", $"Passed: {_passed}\nFailed: {_failed}\n");
            EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
        private static string State => _actor.Brain.StateName;
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            _passed++; Debug.Log("PASS: " + message);
        }
        private static void Kill() => _actor.ApplyDamage(new DamageInfo { Amount = 1000 });
        private static void Step(float dt = .02f) { _actor.Brain.Tick(dt); Physics2D.SyncTransforms(); }
        private static void WaitFor(string state, float timeout)
        {
            for (int i = 0; i < Mathf.CeilToInt(timeout / .02f) && State != state; i++) Step();
        }
        private static void Force(string state) => typeof(EnemyBrain).GetMethod("Go", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_actor.Brain, new object[] { state });
        private static void PhysicalStep() { _actor.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f); }
        private static void Move(float x)
        {
            _actor.transform.position = new Vector2(x, _actor.transform.position.y);
            _actor.GetComponent<Rigidbody2D>().position = _actor.transform.position; Physics2D.SyncTransforms();
        }
        private static void Case(EnemyKind kind, Action test)
        {
            _root = new GameObject(kind + "Validation");
            try
            {
                Physics2D.gravity = new Vector2(0,-9.81f);
                var floor = new GameObject("Floor"); floor.transform.SetParent(_root.transform); floor.layer = 8;
                floor.transform.position = Vector2.down * .5f; floor.AddComponent<BoxCollider2D>().size = new Vector2(40,1);
                var player = new GameObject("Player"); player.SetActive(false); player.transform.SetParent(_root.transform);
                _player = player.AddComponent<EntityStatus>(); player.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                player.AddComponent<BoxCollider2D>().size = new Vector2(.7f,1.1f);
                var receiver = player.AddComponent<EntityDamageable>();
                var serialized = new SerializedObject(receiver); serialized.FindProperty("_faction").intValue = (int)Faction.Player;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var hurtbox = new GameObject("Hurtbox"); hurtbox.transform.SetParent(player.transform, false);
                hurtbox.AddComponent<BoxCollider2D>().size = new Vector2(.7f,1.1f); hurtbox.AddComponent<Hurtbox>();
                player.SetActive(true);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FieldMinionSetup.PrefabFolder + "/" + kind + ".prefab");
                _actor = UnityEngine.Object.Instantiate(prefab, _root.transform).GetComponent<EnemyActor>();
                _actor.enabled = false; _actor.Definition = UnityEngine.Object.Instantiate(_actor.Definition);
                _actor.transform.position = new Vector2(0, _actor.Definition.Flying ? 3.5f : _actor.Definition.BodySize.y * .5f + .02f);
                _actor.GetComponent<Rigidbody2D>().position = _actor.transform.position;
                _actor.Target = player.transform; player.transform.position = new Vector2(5,.65f);
                _actor.Combat = _root.AddComponent<CombatSystem>(); _actor.SendMessage("Start"); Physics2D.SyncTransforms();
                test();
            }
            catch (Exception error) { _failed++; Debug.LogError(kind + ": " + error); }
            finally
            {
                RuleSystem.Instance.ClearAll();
                foreach (var shot in UnityEngine.Object.FindObjectsByType<EnemyProjectile>()) UnityEngine.Object.DestroyImmediate(shot.gameObject);
                if (_actor != null) UnityEngine.Object.DestroyImmediate(_actor.Definition);
                UnityEngine.Object.DestroyImmediate(_root); Physics2D.SyncTransforms();
            }
        }
    }
}
