using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Verification
{
    /// <summary>
    /// 刺击回落（<see cref="SafeGroundMemory"/> + <see cref="RuleGame.Gameplay.Common.IHazardResponder"/>）的 Play 冒烟：
    /// **在隔离的空场景里跑**，跑完把原来打开的场景重新打开。
    /// </summary>
    /// <remarks>
    /// <para>测试台是**最小玩家**，不是 `LevelRuntime` 整机：只要 Rigidbody2D / CapsuleCollider2D /
    /// EntityStatus / EntityDamageable / PlayerController / 一个受击箱子物体。把整机拖进空场景会连带
    /// 相机 / UI / 音频 / 规则驱动，跑挂了很难判断是哪一块 —— 这里只要「会落地、会被刺打」。</para>
    /// <para>对着需求逐条验：① 静态瓦片地面算安全点、手摆碰撞体 / 带刚体的面不算；
    /// ② 刺格范围查得到、远处地面查不到；③ 落地后有安全点且就在脚下；
    /// ④ 被刺打中后回到安全点、伤害照常结算；⑤ 记录点的瓦片被抽掉后不再传送；
    /// ⑥ 没有记录点时什么都不做。</para>
    /// <para>⚠️ 不在关卡 / 测试场景里跑：工程关掉了 Scene Reload，运行期改动会落到编辑场景上
    /// （见仓库根 AGENTS.md 的事故记录）。</para>
    /// </remarks>
    public static class SpikeReturnValidation
    {
        private const string Pending     = "RuleGame.SpikeReturnValidation.Pending";
        private const string ReturnScene = "RuleGame.SpikeReturnValidation.Return";
        private const string InputActionsPath = "Assets/_Project/Settings/InputSystem_Actions.inputactions";
        private const string GroundTilePath  = "Assets/_Project/Art/Tiles/Ground_Logic.asset";
        private const string SpikeTilePath   = "Assets/_Project/Art/Tiles/Spike_Logic.asset";

        private static readonly string OutputPath =
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/SpikeReturnValidation.txt"));

        private static readonly List<string> Lines = new List<string>();
        private static readonly Vector3Int SpikeCell = new Vector3Int(3, 2, 0);

        private static GameObject _testGrid;
        private static Tilemap _map;
        private static HazardField _hazard;
        private static PlayerController _player;
        private static TileBase _groundTile;
        private static TileBase _spikeTile;

        private static Vector2 _spot;
        private static int _step;
        private static int _passed;
        private static float _due;
        private static float _watchdog;
        private static bool _returned;
        private static float _healthBefore;

        [MenuItem("规则游戏/验证/刺击回落（隔离场景 Play 冒烟）", false, 73)]
        public static void Run()
        {
            if (Application.isPlaying) { Debug.LogWarning("[刺击回落验证] 先退出 Play 再跑。"); return; }

            // 先把当前场景存盘，再换一张空场景 —— 绝不把运行期改动写回原场景。
            Scene current = SceneManager.GetActiveScene();
            if (current.isDirty) EditorSceneManager.SaveScene(current);
            SessionState.SetString(ReturnScene, current.path);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        /// <summary>批处理入口（不需要人点菜单，跑完自己退出）。</summary>
        public static void RunBatch()
        {
            if (Application.isPlaying) return;

            Scene current = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(current.path)) SessionState.SetString(ReturnScene, current.path);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        /// <remarks>无条件注册：进 Play 时域可能不重载，靠 <see cref="SessionState"/> 判断该不该跑。</remarks>
        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (SessionState.GetBool(Pending, false)) Begin();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Restore();
            }
        }

        // ==================================================================
        //  测试台
        // ==================================================================

        private static void Begin()
        {
            try
            {
                // 编辑器窗口失焦时 Play 循环会被节流 —— 不给这两条，Time.time 会一直停在 0.02。
                Application.runInBackground = true;
                EditorApplication.QueuePlayerLoopUpdate();
                Trace("Begin");

                // 关键：不拖 LevelRuntime 整机，手动把规则 / 时间 / 注册表拉起来（与纸飞机验证同一套）。
                if (TimeManager.Instance == null) TimeManager.Initialize();
                if (WorldRegistry.Instance == null) WorldRegistry.Initialize();
                if (RuleSystem.Instance == null)
                    RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                Trace("systems ready = " + (RuleSystem.Instance != null));

                _groundTile = AssetDatabase.LoadAssetAtPath<TileBase>(GroundTilePath);
                _spikeTile  = AssetDatabase.LoadAssetAtPath<TileBase>(SpikeTilePath);
                if (_groundTile == null) throw new InvalidOperationException("找不到 " + GroundTilePath);
                if (_spikeTile == null)  throw new InvalidOperationException("找不到 " + SpikeTilePath);

                TilemapCollider2D mapCollider = BuildWorld();
                Trace("tilemap ready");

                var combat = new GameObject("Combat").AddComponent<CombatSystem>();
                Trace("combat ready");

                var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
                if (input == null) throw new InvalidOperationException("找不到 " + InputActionsPath);
                BuildPlayer(input, combat);
                Trace("player ready");

                // ---- 静态判定（不需要落地） ----
                Check("静态瓦片地面算安全点", SafeGroundMemory.IsStaticTileGround(mapCollider));

                var plain = new GameObject("PlainGround").AddComponent<BoxCollider2D>();
                Check("手摆 BoxCollider2D 不算（单向平台同理）", !SafeGroundMemory.IsStaticTileGround(plain));

                var mover = new GameObject("MovingGround");
                mover.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                var moverBox = mover.AddComponent<BoxCollider2D>();
                Check("带刚体的面不算（移动平台 / 电梯）", !SafeGroundMemory.IsStaticTileGround(moverBox));

                Bounds spikeBounds = new Bounds(_map.GetCellCenterWorld(SpikeCell), new Vector3(0.3f, 0.3f, 0f));
                Check("刺格范围查得到刺", _hazard.Overlaps(spikeBounds));

                Bounds farBounds = new Bounds(new Vector3(-8f, 1f, 0f), new Vector3(0.3f, 0.3f, 0f));
                Check("远处地面查不到刺", !_hazard.Overlaps(farBounds));

                var empty = new GameObject("EmptySpot").AddComponent<BoxCollider2D>();
                Check("没有记录点时不回落",
                      !new SafeGroundMemory(empty).TryRecall(Vector2.up, 1 << 8, out _));

                _step = 1;
                _due = Time.time + 1.5f;                        // 等它落到地上并记下安全点
                _watchdog = Time.realtimeSinceStartup + 40f;
                EditorApplication.update += Tick;
                Trace("tick scheduled");
            }
            catch (Exception error) { Finish("Begin: " + error); }
        }

        /// <summary>一张 Logic 瓦片表：地面 + 一个刺格。返回地面碰撞体给白名单断言用。</summary>
        private static TilemapCollider2D BuildWorld()
        {
            _testGrid = new GameObject("SpikeReturnGrid");
            var grid = _testGrid.AddComponent<Grid>();
            grid.cellSize = new Vector3(0.5f, 0.5f, 0f);

            var mapGo = new GameObject("Logic") { layer = 8 };
            mapGo.transform.SetParent(_testGrid.transform, false);
            _map = mapGo.AddComponent<Tilemap>();
            var renderer = mapGo.AddComponent<TilemapRenderer>();
            renderer.enabled = false;                       // 只测碰撞与判定，不画
            var mapCollider = mapGo.AddComponent<TilemapCollider2D>();

            for (int x = -12; x <= 12; x++)
                _map.SetTile(new Vector3Int(x, 0, 0), _groundTile);
            _map.SetTile(SpikeCell, _spikeTile);
            _map.RefreshAllTiles();

            _hazard = mapGo.AddComponent<HazardField>();
            var so = new SerializedObject(_hazard);
            var hazards = so.FindProperty("_hazards");
            hazards.arraySize = 1;
            var entry = hazards.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("Tile").objectReferenceValue = _spikeTile;
            entry.FindPropertyRelative("Damage").floatValue = 10f;
            entry.FindPropertyRelative("Knockback").floatValue = 6f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return mapCollider;
        }

        /// <summary>最小玩家：身体 + 受击箱，没有整机的相机 / UI / 动画。</summary>
        private static void BuildPlayer(InputActionAsset input, CombatSystem combat)
        {
            var go = new GameObject("TestPlayer");
            go.SetActive(false);                             // 先关掉，字段设完再 Awake

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;

            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.76f, 1.19f);
            capsule.offset = new Vector2(0f, 0.108f);

            go.AddComponent<EntityStatus>();
            var damageable = go.AddComponent<EntityDamageable>();
            var controller = go.AddComponent<PlayerController>();

            var hurtGo = new GameObject("Hurtbox") { layer = 10 };
            hurtGo.transform.SetParent(go.transform, false);
            hurtGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            var hurtBox = hurtGo.AddComponent<BoxCollider2D>();
            hurtBox.size = new Vector2(0.57f, 0.89f);        // 约身体 0.75 倍
            hurtBox.isTrigger = true;
            hurtGo.AddComponent<Hurtbox>();

            var pso = new SerializedObject(controller);
            pso.FindProperty("_inputActions").objectReferenceValue = input;
            pso.FindProperty("_solidLayer").intValue = 1 << 8;
            pso.FindProperty("_combat").objectReferenceValue = combat;
            pso.ApplyModifiedPropertiesWithoutUndo();

            var dso = new SerializedObject(damageable);
            dso.FindProperty("_faction").enumValueIndex = (int)Faction.Player;
            dso.FindProperty("_combat").objectReferenceValue = combat;
            dso.ApplyModifiedPropertiesWithoutUndo();

            go.transform.position = new Vector3(0f, 2f, 0f);
            go.SetActive(true);

            _player = controller;
            _player.OnHazardReturned += () => _returned = true;
        }

        private static void Tick()
        {
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
                if (Time.realtimeSinceStartup > _watchdog) { Finish("等待超时（Play 循环没推进）"); return; }
                if (Time.time < _due) return;

                if (_step == 1)
                {
                    _step = 2;
                    Trace("step1 grounded=" + _player.IsGrounded + " hasSpot=" + _player.HasSafeGround
                          + " player=" + _player.transform.position);
                    Check("落地后有安全点", _player.IsGrounded && _player.HasSafeGround,
                          "grounded=" + _player.IsGrounded + " hasSpot=" + _player.HasSafeGround);
                    _spot = _player.SafeGroundPosition;
                    Check("安全点就在脚下", Vector2.Distance(_spot, _player.transform.position) < 0.2f,
                          "spot=" + _spot + " player=" + _player.transform.position);

                    _healthBefore = _player.GetComponent<EntityStatus>().Health;
                    _returned = false;
                    _player.TeleportTo(_map.GetCellCenterWorld(SpikeCell));   // 钻进刺格
                    _due = Time.time + 0.4f;
                    return;
                }

                if (_step == 2)
                {
                    _step = 3;
                    float hp = _player.GetComponent<EntityStatus>().Health;
                    Trace("step2 returned=" + _returned + " pos=" + _player.transform.position
                          + " spot=" + _spot + " hp=" + hp);
                    Check("被刺打中后回到安全点",
                          _returned && Vector2.Distance(_player.transform.position, _spot) < 0.15f,
                          "returned=" + _returned + " pos=" + _player.transform.position + " spot=" + _spot);
                    Check("刺的伤害照常结算", hp < _healthBefore, "hp " + _healthBefore + " → " + hp);

                    // 把安全点脚下的瓦片抽掉：复验应该失败，不再传送。
                    // ⚠️ 玩家的胶囊宽 0.76 > 一格 0.5，永远同时压着两格 —— 只抽一格，
                    //    旁边那格仍然支撑得住，复验会通过（实测过）。所以连抽三格。
                    Vector3Int cell = _map.WorldToCell(_spot - new Vector2(0f, 0.6f));
                    for (int dx = -1; dx <= 1; dx++)
                        _map.SetTile(new Vector3Int(cell.x + dx, cell.y, 0), null);
                    _map.RefreshAllTiles();
                    _due = Time.time + 0.8f;        // 等回落冷却过去 + 碰撞体重建
                    return;
                }

                if (_step == 3)
                {
                    _step = 4;
                    Vector2 before = _player.transform.position;
                    bool moved = _player.OnHazardContact(Vector2.zero);
                    Trace("step3 moved=" + moved + " pos=" + _player.transform.position);
                    Check("安全点瓦片没了就不传送",
                          !moved && Vector2.Distance(_player.transform.position, before) < 0.01f,
                          "moved=" + moved + " before=" + before + " after=" + _player.transform.position);
                    Finish(null);
                }
            }
            catch (Exception error) { Finish("Tick: " + error); }
        }

        /// <summary>把当前进度写进日志 —— 万一卡在 Play 里，至少能看到最后走到哪一步。</summary>
        private static void Trace(string message)
        {
            Lines.Add("[trace] " + message);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                File.WriteAllText(OutputPath, string.Join("\n", Lines));
            }
            catch { /* 日志失败不影响测试 */ }
        }

        // ==================================================================
        //  收尾
        // ==================================================================

        private static void Check(string label, bool ok, string detail = "")
        {
            Lines.Add((ok ? "✓ " : "✗ ") + label + (ok || string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
            if (ok) { _passed++; Debug.Log("[刺击回落验证] PASS " + label); }
            else Debug.LogError("[刺击回落验证] FAIL " + label + (string.IsNullOrEmpty(detail) ? "" : " —— " + detail));
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            if (_testGrid != null) UnityEngine.Object.Destroy(_testGrid);
            if (_player != null) UnityEngine.Object.Destroy(_player.gameObject);

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            int failed = Lines.FindAll(line => line.StartsWith("✗")).Count;
            Lines.Add(error != null ? "失败：" + error
                : failed == 0 ? "全部通过（" + _passed + " 项）" : "有 " + failed + " 项没过");
            File.WriteAllText(OutputPath, string.Join("\n", Lines));

            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode) { EditorApplication.Exit(failed == 0 ? 0 : 1); return; }

            EditorApplication.isPlaying = false;
        }

        private static void Restore()
        {
            string path = SessionState.GetString(ReturnScene, string.Empty);
            if (string.IsNullOrEmpty(path)) return;
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            SessionState.EraseString(ReturnScene);
        }
    }
}
