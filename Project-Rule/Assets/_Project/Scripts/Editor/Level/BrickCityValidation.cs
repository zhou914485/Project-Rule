using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Development.BrickCity;

namespace RuleGame.Editor.Level
{
    /// <summary>只针对本批机关的承载、快照、伤害与奖励接口；批处理使用独立白盒，不重跑敌人套件。</summary>
    public static class BrickCityValidation
    {
        private const string Pending = "BrickCity.Validation.Stage";
        private static double _started;
        private static readonly List<string> Results = new List<string>();
        private static int _failed;
        private static PlayerController _player;
        private static BrickCityLabControls _controls;
        private static Snapshot _baseline;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("该入口只供批处理使用；手动打开白盒即可。");
            if (!File.Exists(BrickCityBuilder.ScenePath)) BrickCityBuilder.Build();
            EditorSceneManager.OpenScene(BrickCityBuilder.ScenePath);
            SessionState.SetInt(Pending, 1); Subscribe(); EditorApplication.isPlaying = true;
        }
        public static void RunHammerBatch()
        {
            SessionState.SetString(Pending + ".Only", "hammer and fuse rewind"); RunBatch();
        }
        [InitializeOnLoadMethod]
        private static void Resume() { if (SessionState.GetInt(Pending, 0) != 0) Subscribe(); }
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= OnPlay;
            EditorApplication.playModeStateChanged += OnPlay;
            if (SessionState.GetInt(Pending, 0) == 3)
                EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); };
        }
        private static void OnPlay(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetInt(Pending, 0) == 1)
            { _started = EditorApplication.timeSinceStartup; EditorApplication.update += WaitForReady; }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetInt(Pending, 0) == 3) Exit();
        }
        private static void Exit()
        {
            int code = SessionState.GetInt(Pending + ".Exit", 1);
            SessionState.SetInt(Pending, 0); SessionState.SetString(Pending + ".Only", ""); EditorApplication.Exit(code);
        }
        private static void WaitForReady()
        {
            _controls = UnityEngine.Object.FindAnyObjectByType<BrickCityLabControls>();
            if (_controls != null && _controls.Ready)
            {
                EditorApplication.update -= WaitForReady; SessionState.SetInt(Pending, 2); Run();
            }
            else if (EditorApplication.timeSinceStartup - _started > 30)
            {
                EditorApplication.update -= WaitForReady; _failed++; Results.Add("FAIL bootstrap: no initial snapshot"); Finish();
            }
        }
        private static T Find<T>(Predicate<T> predicate = null) where T : Component
        {
            foreach (var item in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include))
                if (predicate == null || predicate(item)) return item;
            throw new InvalidOperationException("Missing " + typeof(T).Name);
        }
        private static void Case(string name, Action test)
        {
            string only = SessionState.GetString(Pending + ".Only", "");
            if (only.Length > 0 && only != name) return;
            try { SnapshotSystem.Instance.Restore(_baseline); test(); Results.Add("PASS " + name); }
            catch (Exception error) { _failed++; Results.Add("FAIL " + name + ": " + error.Message); }
        }
        private static void Check(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
        private static void Tick(Component component, int steps, bool simulate = false)
        {
            for (int i = 0; i < steps; i++) { component.SendMessage("FixedUpdate"); if (simulate) Physics2D.Simulate(Time.fixedDeltaTime); }
        }
        private static void PutPlayer(Vector2 position)
        {
            // 通过现有暂停分支清理移动，避免测试夹带上一项的速度。
            var status = _player.GetComponent<EntityStatus>();
            status.SetPaused(true); _player.SendMessage("FixedUpdate"); status.SetPaused(false);
            _player.GetComponent<Rigidbody2D>().position = position; Physics2D.SyncTransforms();
        }
        private static void Run()
        {
            _player = _controls.Interactor.GetComponent<PlayerController>(); _player.enabled = false; _controls.enabled = false;
            _baseline = SnapshotSystem.Instance.Capture();
            var mode = Physics2D.simulationMode; Physics2D.simulationMode = SimulationMode2D.Script;
            try
            {
                Case("dual levers", () =>
                {
                    var lift = Find<CityPathMotor>(x => x.CarryRiders && !x.AutoReturn);
                    var levers = UnityEngine.Object.FindObjectsByType<CitySwitch>();
                    var linked = new List<CitySwitch>(); foreach (var item in levers) if (item.FollowMotor == lift) linked.Add(item);
                    Check(linked.Count == 2, "lift must have two linked levers");
                    linked[0].Interact(_player); Check(lift.TargetEnd, "first lever failed");
                    linked[1].Interact(_player); Check(!lift.TargetEnd, "second lever must read shared target state");
                });
                Case("lift carries up and down", () =>
                {
                    var lift = Find<CityPathMotor>(x => x.CarryRiders && !x.AutoReturn);
                    var shape = lift.GetComponent<Collider2D>(); var playerShape = _player.GetComponent<Collider2D>();
                    PutPlayer(new Vector2(lift.Body.position.x, shape.bounds.max.y + playerShape.bounds.extents.y + .015f));
                    for (int i = 0; i < 8; i++) { _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    Vector2 start = _player.GetComponent<Rigidbody2D>().position; float liftStart = lift.Body.position.y;
                    lift.SetOn(true);
                    for (int i = 0; i < 40; i++) { lift.SendMessage("FixedUpdate"); _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    float moved = lift.Body.position.y - liftStart;
                    float rider = _player.GetComponent<Rigidbody2D>().position.y - start.y;
                    Check(moved > 1f && Mathf.Abs(moved - rider) < .16f, "up lift=" + moved + " rider=" + rider);
                    lift.SetOn(false);
                    for (int i = 0; i < 40; i++) { lift.SendMessage("FixedUpdate"); _player.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    Check(Mathf.Abs(_player.GetComponent<Rigidbody2D>().position.y - start.y) < .18f, "rider did not follow descent");
                });
                Case("gate blocks safely", () =>
                {
                    var gate = Find<CityPathMotor>(x => x.BlockActors); PutPlayer(new Vector2(13f, .8f));
                    gate.SetOn(true); Tick(gate, 120, true);
                    PutPlayer(new Vector2(gate.Body.position.x, .8f)); gate.SetOn(false); Tick(gate, 120, true);
                    Check(gate.GetComponent<Collider2D>().bounds.min.y >= _player.GetComponent<Collider2D>().bounds.max.y - .04f,
                        "closing gate penetrated rider");
                });
                Case("bubble jump and rewind", () =>
                {
                    var bubble = Find<CityBubble>(); var bounds = bubble.GetComponent<Collider2D>().bounds;
                    PutPlayer(new Vector2(bounds.center.x, bounds.max.y + _player.GetComponent<Collider2D>().bounds.extents.y + .01f));
                    var saved = SnapshotSystem.Instance.Capture();
                    var jumped = typeof(PlayerController).GetField("OnJumped", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_player) as Action;
                    jumped?.Invoke(); Check(bubble.Broken && !bubble.GetComponent<Collider2D>().enabled, "jump did not consume support bubble");
                    SnapshotSystem.Instance.Restore(saved); Check(!bubble.Broken && bubble.GetComponent<Collider2D>().enabled, "bubble was not restored");
                });
                Case("marble accepts processed impulse", () =>
                {
                    var ball = Find<CityMarble>(); ball.Body.linearVelocity = Vector2.zero;
                    ball.ApplyDamage(new DamageInfo { Attacker = _player, Amount = 10f, Knockback = 6f });
                    Physics2D.Simulate(Time.fixedDeltaTime);
                    Check(ball.Body.linearVelocity.magnitude > 1f, "dynamic impulse lost");
                });
                Case("contact damage and one-shot refill", () =>
                {
                    var zone = Find<CityDamageZone>(x => x.name == "CityThorns");
                    var status = _player.GetComponent<EntityStatus>(); PutPlayer(zone.transform.position);
                    float before = status.Health; zone.SendMessage("FixedUpdate");
                    Check(status.Health < before, "contact did not reach CombatSystem");
                    float after = status.Health; zone.SendMessage("FixedUpdate"); Check(status.Health == after, "contact ignored cooldown");
                    var refill = Find<CityRefill>(); status.TryConsumeEnergy(50f); float energy = status.Energy;
                    float expected = Mathf.Min(energy + refill.Amount, status.EnergyMax);
                    // 走「攻击盒 / 飞刀」那条路，不是 CityInteractor 的 E。
                    ((IContactSwitch)refill).ImpactByContact(_player.transform.position);
                    Check(Mathf.Approximately(status.Energy, expected), "refill amount wrong: " + status.Energy);
                    Check(!refill.Available && !refill.GetComponent<Collider2D>().enabled, "one-shot refill was not consumed");
                    ((IContactSwitch)refill).ImpactByContact(_player.transform.position);
                    Check(Mathf.Approximately(status.Energy, expected), "consumed refill accepted a second hit");
                });
                Case("hammer and fuse rewind", () =>
                {
                    var hammer = Find<CityHammer>(); var zone = hammer.GetComponent<CityDamageZone>();
                    for (int i = 0; i < 300 && !zone.Armed; i++) Tick(hammer, 1);
                    Check(zone.Armed, "strike phase never armed");
                    var saved = SnapshotSystem.Instance.Capture();
                    for (int i = 0; i < 300 && zone.Armed; i++) Tick(hammer, 1);
                    Check(!zone.Armed, "strike did not recover");
                    SnapshotSystem.Instance.Restore(saved); Check(zone.Armed, "active strike state not restored");
                    var bomb = Find<CityBomb>(); bomb.SetOn(true); Tick(bomb, 15);
                    var fuse = SnapshotSystem.Instance.Capture(); Tick(bomb, 50);
                    Check(!bomb.GetComponent<Collider2D>().enabled, "bomb did not finish");
                    SnapshotSystem.Instance.Restore(fuse); Check(bomb.GetComponent<Collider2D>().enabled, "fuse rewind failed");
                });
                Case("track bomb patrols and detonates on contact", () =>
                {
                    // 白盒里没有就现造一个：这一项只验组件行为，不依赖场景摆位（重跑生成器后就用场景里那个）。
                    var bomb = UnityEngine.Object.FindAnyObjectByType<CityTrackBomb>();
                    if (bomb == null)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrickCityBuilder.Folder + "/CityTrackBomb.prefab");
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        instance.transform.position = new Vector2(31f, .8f);
                        instance.GetComponent<CityTrackBomb>().Combat = Find<CombatSystem>();
                        bomb = instance.GetComponent<CityTrackBomb>();
                    }
                    var track = bomb.GetComponent<TrackMotor>();
                    var shape = bomb.GetComponent<Collider2D>();
                    PutPlayer(new Vector2(-3f, .85f));   // 先让人走开，免得巡逻途中误触
                    Vector2 start = bomb.transform.position;
                    for (int i = 0; i < 25; i++) { track.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                    Check(Vector2.Distance(bomb.transform.position, start) > .3f, "patrol did not move");

                    var armed = SnapshotSystem.Instance.Capture();
                    var status = _player.GetComponent<EntityStatus>();
                    PutPlayer(bomb.transform.position);
                    float before = status.Health;
                    bomb.SendMessage("FixedUpdate");
                    Check(!shape.enabled, "contact did not detonate");
                    Check(status.Health < before, "explosion did not reach CombatSystem");

                    SnapshotSystem.Instance.Restore(armed);
                    Check(shape.enabled, "detonation was not rewound");
                    PutPlayer(new Vector2(-3f, .85f));
                });
                Case("checkpoint and permanent reward", () =>
                {
                    // 落点 = 存档点：攻击命中才记录（这里直接调 Arrive 模拟），死亡后回这里。
                    var point = Find<PlayerSpawnPoint>();
                    var controller = _player.GetComponent<PlayerController>();
                    Check(point.Arrive() && controller.HasRespawn && controller.RespawnId == point.Id,
                          "checkpoint was not recorded");
                    Vector2 saved = _player.GetComponent<Rigidbody2D>().position;
                    PutPlayer(new Vector2(73f, .8f));
                    Check(controller.Respawn() && Vector2.Distance(_player.GetComponent<Rigidbody2D>().position, saved) < .01f, "respawn failed");
                    var chest = Find<CityRewardSource>(x => !x.PickupVisual); var before = SnapshotSystem.Instance.Capture();
                    Check(chest.Interact(_player), "demo receiver did not accept reward");
                    // 战斗力那条奖励已删（2026-10-20），演示计数只剩生命 / 能量。
                    int count = _controls.HealthTokens + _controls.EnergyTokens; SnapshotSystem.Instance.Restore(before);
                    Check(!chest.Available && !chest.Interact(_player) && _controls.HealthTokens + _controls.EnergyTokens == count, "rewind duplicated permanent reward");
                });
            }
            finally { Physics2D.simulationMode = mode; Finish(); }
        }
        private static void Finish()
        {
            Directory.CreateDirectory("Library/BossFixReview");
            string report = SessionState.GetString(Pending + ".Only", "").Length > 0 ? "CityChecks-retry.txt" : "CityChecks.txt";
            File.WriteAllLines("Library/BossFixReview/" + report, Results);
            Debug.Log("[BrickCityChecks] " + (Results.Count - _failed) + " passed / " + _failed + " failed");
            SessionState.SetInt(Pending + ".Exit", _failed == 0 ? 0 : 1); SessionState.SetInt(Pending, 3);
            EditorApplication.isPlaying = false;
        }
    }
}
