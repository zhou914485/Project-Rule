using System;
using System.Collections.Generic;
using System.IO;
using RuleGame.Development.Enemy;
using RuleGame.Gameplay.Enemy;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>真实预制体与物理步：远处追击、玩家迎面走、回巢堵路、初始贴身和薄墙遮挡。</summary>
    public static class HugBearApproachValidation
    {
        private const string Pending = "RuleGame.HugBearApproach";
        private static double _due;
        private static readonly List<string> Trace = new List<string>();
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在隔离工程运行。");
            Trace.Clear();
            EditorSceneManager.OpenScene(MinionGallerySceneSetup.ScenePath, OpenSceneMode.Single);
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
        private static void Run()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            EditorApplication.update -= Run;
            int passed = 0, failed = 0;
            try
            {
                var controls = UnityEngine.Object.FindAnyObjectByType<MinionGalleryControls>();
                foreach (var actor in controls.Enemies) actor.gameObject.SetActive(false);
                controls.Player.gameObject.SetActive(false);
                Physics2D.simulationMode = SimulationMode2D.Script;
                var bearPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinionArtSetup.PrefabPath(EnemyKind.HugBear));
                var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab");
                foreach (int mode in new[] { 0, 1, 2, 3, 4 })
                foreach (float height in new[] { .55f, 1.2f })
                foreach (float side in new[] { -1f, 1f })
                {
                    var player = UnityEngine.Object.Instantiate(playerPrefab, new Vector3(50f + side * 5f, height), Quaternion.identity).GetComponent<RuleGame.Gameplay.Player.PlayerController>();
                    player.enabled = false;
                    player.GetComponent<RuleGame.Gameplay.Combat.EntityDamageable>().SetInvulnerable(true);
                    var bear = UnityEngine.Object.Instantiate(bearPrefab, new Vector3(50f, .95f), Quaternion.identity).GetComponent<EnemyActor>();
                    bear.enabled = false; bear.Target = player.transform; bear.SendMessage("Start");
                    if (mode == 2)
                    {
                        // 追击中被外力推到回巢边缘，玩家位于回家的方向。
                        for (int j = 0; j < 70; j++) { bear.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); }
                        player.TeleportTo(new Vector2(50f + side * 7.0f, .55f));
                        bear.GetComponent<Rigidbody2D>().position = new Vector2(50f + side * 8.2f, .95f);
                        bear.transform.position = new Vector3(50f + side * 8.2f, .95f);
                    }
                    if (mode == 3) player.TeleportTo(new Vector2(50f + side * 1.3f, .55f));
                    var input = typeof(RuleGame.Gameplay.Player.PlayerController).GetField("_input", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
                    Physics2D.SyncTransforms(); bool hugged = false; float heldTime = 0f;
                    var body = bear.GetComponent<Collider2D>(); var target = player.GetComponent<Collider2D>();
                    GameObject wall = null;
                    if (mode == 4)
                    {
                        float targetOffset = target.bounds.center.x - player.transform.position.x;
                        float x = body.bounds.center.x + side * (body.bounds.extents.x + target.bounds.extents.x + .06f) - targetOffset;
                        player.TeleportTo(new Vector2(x, .55f));
                        wall = new GameObject("ContactBarrier") { layer = 8 };
                        wall.transform.position = new Vector3(body.bounds.center.x + side * (body.bounds.extents.x + .03f), 1.5f);
                        wall.AddComponent<BoxCollider2D>().size = new Vector2(.02f, 4f);
                        Physics2D.SyncTransforms();
                    }
                    bool grabbedThroughWall = false;
                    for (int i = 0; i < Mathf.CeilToInt(8f / Time.fixedDeltaTime); i++)
                    {
                        if (mode == 1)
                        {
                            input.GetType().GetField("_move", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(input, Vector2.left * side);
                            player.SendMessage("FixedUpdate");
                        }
                        bear.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms();
                        if (i % 24 == 0) Trace.Add($"mode={mode}, side={side}, height={height}, age={bear.Brain.SimulationAge:F2}, state={bear.Brain.StateName}, animation={bear.AnimationName}, gap={body.Distance(target).distance:F4}, bear={bear.transform.position}, player={player.transform.position}, intent={bear.Motor.Intent.x}, route={bear.Navigation.HasRoute}, held={player.MovementHeld}");
                        if (bear.Brain.StateName == "Hug" && player.MovementHeld) heldTime += Time.fixedDeltaTime;
                        else heldTime = 0f;
                        if (mode == 4 && (bear.Brain.StateName == "Hug" || player.MovementHeld)) { grabbedThroughWall = true; break; }
                        if (heldTime >= .75f) { hugged = true; break; }
                    }
                    if (mode == 4) hugged = !grabbedThroughWall;
                    if (hugged) passed++; else failed++;
                    Trace.Add($"Approach mode {mode}, side {side}, height {height}: {(hugged ? "PASS" : "FAIL")}");
                    if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
                    UnityEngine.Object.DestroyImmediate(bear.gameObject); UnityEngine.Object.DestroyImmediate(player.gameObject); Physics2D.SyncTransforms();
                }
            }
            catch (Exception error) { failed++; Trace.Add(error.ToString()); }
            string folder = Path.GetFullPath("Logs/HugBearApproach"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Results.txt"), $"Passed: {passed}\nFailed: {failed}\n" + string.Join("\n", Trace));
            SessionState.SetBool(Pending, false); EditorApplication.Exit(failed == 0 ? 0 : 1);
        }
    }
}
