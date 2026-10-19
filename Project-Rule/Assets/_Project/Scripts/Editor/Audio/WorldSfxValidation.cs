using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Audio
{
    [InitializeOnLoad]
    public static class WorldSfxValidation
    {
        private const string Pending = "RuleGame.WorldSfxValidation";
        private static readonly List<string> Results = new List<string>();
        private static readonly int[] Counts = new int[Enum.GetValues(typeof(SfxCue)).Length];
        private static GameObject _root;
        private static SfxPlayer _audio;
        private static CombatSystem _combat;
        private static PlayerController _player;
        private static EnemyActor _wood, _metal, _soft, _second;
        private static CitySwitch _switch;
        private static GravityBoulder _boulder;
        private static CityBomb _bomb;
        private static CityTrackBomb _track;
        private static BreakableGate _gate;
        private static Snapshot _snapshot;
        private static Transform _target;
        private static Mouse _mouse, _oldMouse;
        private static Keyboard _keyboard, _oldKeyboard;
        private static int _step, _impacts, _deaths, _switchOn, _switchOff;
        private static object _switchInitialState;
        private static double _due, _deadline;
        private static bool _running;
        [Serializable] private sealed class Setup { public SceneSetup[] Scenes; }
        static WorldSfxValidation() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("Tools/规则游戏/音频/⑦ 验证敌人与机关音效（PlayMode）")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play Mode。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景。");
            SessionState.SetString(Pending + ".Scenes", JsonUtility.ToJson(new Setup { Scenes = EditorSceneManager.GetSceneManagerSetup() }));
            SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Pending, false);
            EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(SessionState.GetString(Pending + ".Scenes", "")).Scenes);
        }
        private static double Now => EditorApplication.timeSinceStartup;
        private static int Count(SfxCue cue) => Counts[(int)cue];
        private static T Prefab<T>(string path, Vector3 position) where T : Component
        {
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + path + ".prefab"));
            root.transform.position = position;
            return root.GetComponentInChildren<T>(true);
        }
        private static EnemyActor Enemy(string path, Vector3 position)
        {
            var actor = Prefab<EnemyActor>("Enemies/" + path, position);
            actor.Target = _target;
            return actor;
        }
        private static void Begin()
        {
            Results.Clear(); Array.Clear(Counts, 0, Counts.Length); _step = 0; _running = true; _deadline = Now + 60d;
            try
            {
                Application.runInBackground = true;
                _oldMouse = Mouse.current; _oldKeyboard = Keyboard.current;
                _mouse = InputSystem.AddDevice<Mouse>(); _keyboard = InputSystem.AddDevice<Keyboard>();
                var ground = new GameObject("WorldSfx_Ground", typeof(BoxCollider2D));
                ground.layer = 8; ground.transform.position = Vector3.down;
                ground.GetComponent<BoxCollider2D>().size = new Vector2(200f, 2f);
                _root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/LevelRuntime.prefab"));
                _audio = _root.GetComponent<SfxPlayer>(); _combat = _root.GetComponentInChildren<CombatSystem>();
                _player = _root.GetComponentInChildren<PlayerController>();
                _player.TeleportTo(new Vector2(-7f, 1.5f));
                _audio.OnPlayed += cue => Counts[(int)cue]++;
                _target = new GameObject("OutOfAggroTarget").transform; _target.position = new Vector3(1000f, 0f, 0f);
                _wood = Enemy("BrickMinion", new Vector3(-2f, 2f));
                _second = Enemy("BrickMinion", new Vector3(0f, 2f));
                _metal = Enemy("FieldMinions/SpringToy", new Vector3(2f, 2f));
                _soft = Enemy("FieldMinions/HugBear", new Vector3(4f, 2f));
                _switch = Prefab<CitySwitch>("BrickCity/CityLever", new Vector3(-15f, 1f));
                _switchInitialState = _switch.CaptureSnapshotState();
                _bomb = Prefab<CityBomb>("BrickCity/CityBomb", new Vector3(8f, 1f));
                _bomb.Fuse = .1f; _bomb.Damage = 0f; _bomb.Combat = _combat;
                _track = Prefab<CityTrackBomb>("BrickCity/CityTrackBomb", new Vector3(9f, 1f));
                _track.DetonateOnContact = false; _track.Damage = 0f; _track.Combat = _combat;
                foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
                    Check(_audio.Bank.Find(cue)?.Clips.All(c => c != null) == true, cue + " 素材引用可加载");
                Check(_audio.Bank.Find(SfxCue.SwordLight).Clips.All(c => c.length < .25f
                    && AssetDatabase.GetAssetPath(c).Contains("Swishes/")), "轻击已替换为独立短破风素材");
                Check(_audio.Bank.Find(SfxCue.SwordHeavy).Clips.All(c => c.length < .3f
                    && AssetDatabase.GetAssetPath(c).Contains("Swishes/")), "重击使用单独较厚破风素材");
                int prefabs = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Enemies" }))
                    foreach (EnemyActor actor in AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)).GetComponentsInChildren<EnemyActor>(true))
                    { Check(actor.GetComponents<EnemySfxPresenter>().Length == 1, actor.name + " 敌人预制体只接入一个音效表现"); prefabs++; }
                Check(prefabs == 15, "全部十五种真实敌人预制体已接线，包含召唤物");
                _due = Now + .8d; EditorApplication.update += Tick;
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void Tick()
        {
            if (!_running) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Now > _deadline) { Fail(new TimeoutException("WorldSfx step=" + _step)); return; }
            if (Now < _due) return;
            try
            {
                switch (_step++)
                {
                    case 0:
                        Check(SfxPlayer.Active == _audio && _wood.Brain != null, "常驻池与真实敌人运行初始化成功");
                        _audio.ClearWorld();
                        _wood.Cue("Attack", .3f); _wood.Attack(1f); _wood.Attack(1f);
                        Check(Count(SfxCue.EnemyMelee) == 1, "持续攻击判定窗只通知一次攻击声");
                        _second.Cue("Attack", .3f); _second.Attack(1f);
                        Check(Count(SfxCue.EnemyMelee) == 2, "同帧第二只敌人发声不被第一只的限频吞掉");
                        _due = Now + .18d; break;
                    case 1:
                        _wood.Attack(1f, true);
                        Check(Count(SfxCue.EnemyMelee) == 3, "下一次真实攻击脉冲可以再次发声");
                        _wood.Shoot(Vector2.up);
                        Check(Count(SfxCue.EnemyShot) == 1, "真实投射物生成触发发射声");
                        _wood.Beam(90f, .1f, .05f);
                        Check(Count(SfxCue.EnemyBeam) == 1, "真实光束生成触发蓄能攻击声");
                        foreach (EnemyProjectile shot in UnityEngine.Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None)) shot.gameObject.SetActive(false);
                        foreach (EnemyBeam beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>(FindObjectsSortMode.None)) beam.gameObject.SetActive(false);
                        _combat.ApplyDamage(null, _wood, 1f, 0f);
                        Check(Count(SfxCue.EnemyHitWood) == 1, "真实积木敌人扣血触发木质受击声");
                        _combat.ApplyDamage(null, _wood, 1f, 0f);
                        _combat.ApplyDamage(null, _wood, -1f, 0f);
                        Check(Count(SfxCue.EnemyHitWood) == 1, "敌人无敌帧与治疗不误播受击声");
                        _combat.ApplyDamage(null, _metal, 1f, 0f);
                        _combat.ApplyDamage(null, _soft, 1f, 0f);
                        Check(Count(SfxCue.EnemyHitMetal) == 1 && Count(SfxCue.EnemyHitSoft) == 1, "金属与毛绒敌人具有独立材质受击声");
                        _due = Now + .25d; break;
                    case 2:
                        _snapshot = SnapshotSystem.Instance.Capture();
                        _combat.ApplyDamage(null, _wood, 9999f, 0f);
                        Check(Count(SfxCue.EnemyDeathWood) == 1, "致命扣血当时播放死亡声");
                        Check(Count(SfxCue.EnemyHitWood) == 1, "致命一击不叠加普通受击声");
                        _due = Now + .2d; break;
                    case 3:
                        Check(_wood.Defeated && Count(SfxCue.EnemyDeathWood) == 1, "随后 OnDefeated 不重复播放同一次死亡声");
                        _deaths = Count(SfxCue.EnemyDeathWood);
                        SnapshotSystem.Instance.Restore(_snapshot);
                        Check(!_wood.Defeated && Count(SfxCue.EnemyDeathWood) == _deaths, "真实快照复活不播放死亡或受击声");
                        _due = Now + .2d; break;
                    case 4:
                        _combat.ApplyDamage(null, _wood, 9999f, 0f);
                        Check(Count(SfxCue.EnemyDeathWood) == 2, "回溯复活后再次真实击杀仍可播放死亡声");
                        _combat.ApplyDamage(null, _metal, 9999f, 0f);
                        _combat.ApplyDamage(null, _soft, 9999f, 0f);
                        Check(Count(SfxCue.EnemyDeathMetal) == 1 && Count(SfxCue.EnemyDeathSoft) == 1,
                            "可重组金属与毛绒小怪倒地时播放对应材质声");
                        _switch.RestoreSnapshotState(_switchInitialState);
                        _audio.ClearWorld();
                        _switchOn = Count(SfxCue.SwitchOn); _switchOff = Count(SfxCue.SwitchOff);
                        _switch.SetOn(true); _switch.PressByContact(); _switch.PressByContact();
                        Check(Count(SfxCue.SwitchOn) == _switchOn + 1, "机关开关真实切换只播一次，持续接触不连响 count=" + Count(SfxCue.SwitchOn));
                        _switch.SetOn(false); Check(Count(SfxCue.SwitchOff) == _switchOff + 1, "机关关闭有独立关闭声");
                        _due = Now + .2d; break;
                    case 5:
                        object saved = _switch.CaptureSnapshotState();
                        _switch.SetOn(true);
                        int on = Count(SfxCue.SwitchOn);
                        _switch.RestoreSnapshotState(saved);
                        Check(!_switch.IsOn && Count(SfxCue.SwitchOn) == on && Count(SfxCue.SwitchOff) == _switchOff + 1,
                            "机关快照直接恢复开关状态不播放切换声");
                        var drop = new GameObject("FallingStoneSfx", typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(StoneBody));
                        drop.transform.position = new Vector3(6f, 3f);
                        WorldSfxBinder.Install(SceneManager.GetActiveScene());
                        Check(drop.GetComponents<MechanismSfxPresenter>().Length == 1, "流式绑定器能补齐场景内嵌机关");
                        _impacts = Count(SfxCue.ImpactStone);
                        _due = Now + 1.2d; break;
                    case 6:
                        Check(Count(SfxCue.ImpactStone) == _impacts + 1, "真实 Dynamic 石头落地碰撞播放一次石质撞击声");
                        _impacts = Count(SfxCue.ImpactStone); _due = Now + .4d; break;
                    case 7:
                        Check(Count(SfxCue.ImpactStone) == _impacts, "物体静止接触地面不持续响碰撞声");
                        _boulder = Prefab<GravityBoulder>("BoulderSwordMechanism", new Vector3(0f, 6f));
                        _boulder.Arm(); _boulder.Arm();
                        Check(Count(SfxCue.ImpactStone) == _impacts + 1, "巨石震裂事件幂等，只播一次裂纹撞击");
                        _boulder.Shatter();
                        Check(Count(SfxCue.BreakStone) == 1 && !_boulder.gameObject.activeSelf,
                            "巨石真实碎裂并停用后，破碎声由常驻池承接");
                        _boulder.Shatter(); Check(Count(SfxCue.BreakStone) == 1, "重复碎裂调用不重复播声");
                        _audio.ClearWorld();
                        int explosionsBeforeFuse = Count(SfxCue.Explosion);
                        _bomb.SetOn(true);
                        Check(Count(SfxCue.Explosion) == explosionsBeforeFuse, "点燃引信时不提前播放爆炸声");
                        _due = Now + .3d; break;
                    case 8:
                        Check(Count(SfxCue.Explosion) == 1, "引信炸弹实际消耗并关闭形状后播放爆裂声");
                        Check(_root.GetComponentsInChildren<AudioSource>().Any(source => source.isPlaying && _audio.Bank.Find(SfxCue.Explosion).Clips.Contains(source.clip)),
                            "实际爆炸使用当前配置的爆炸素材播放");
                        _bomb.SetOn(true); _due = Now + .2d; break;
                    case 9:
                        Check(Count(SfxCue.Explosion) == 1, "已消耗炸弹重复点火不重复发声");
                        _track.SetOn(true); _track.SetOn(true);
                        Check(Count(SfxCue.Explosion) == 2, "轨道炸弹真实引爆只通知一次");
                        _audio.ClearWorld();
                        var gate = new GameObject("BreakableSoundGate", typeof(BoxCollider2D), typeof(BreakableGate));
                        gate.layer = 8;
                        gate.transform.position = _player.transform.position + Vector3.right * .75f;
                        gate.GetComponent<BoxCollider2D>().size = new Vector2(.4f, 2f);
                        _gate = gate.GetComponent<BreakableGate>();
                        WorldSfxBinder.Install(SceneManager.GetActiveScene());
                        InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = 1 });
                        _due = Now + .35d; break;
                    case 10:
                        InputSystem.QueueStateEvent(_mouse, new MouseState());
                        Check(_gate.IsBroken && Count(SfxCue.BreakWood) == 1, "真实玩家挥刀破门触发一次木质碎裂声");
                        _audio.ClearWorld();
                        Vector3 listener = _root.GetComponentInChildren<AudioListener>().transform.position;
                        Check(!_audio.PlayAt(SfxCue.ImpactMetal, listener + Vector3.right * 1000f, _switch), "远处分区机关不播放全音量声音");
                        Check(_audio.PlayAt(SfxCue.ImpactMetal, listener + Vector3.right * 2f, _switch), "近处机关允许按位置播放");
                        Check(!_audio.PlayAt(SfxCue.ImpactMetal, listener + Vector3.right * 2f, _switch), "同一发声物体具有独立限频");
                        TimeManager.Instance.SetPaused(true);
                        int off = Count(SfxCue.SwitchOff);
                        _switch.SetOn(false);
                        Check(Count(SfxCue.SwitchOff) == off, "暂停时不接受新增世界机关声");
                        TimeManager.Instance.SetPaused(false);
                        _due = Now + .3d; break;
                    case 11:
                        _second.gameObject.SetActive(false);
                        Check(Count(SfxCue.EnemyDeathWood) == 2, "对象停用不冒充敌人死亡");
                        Check(_root.GetComponentsInChildren<AudioSource>().Length == 13, "敌人与机关复用固定音效池，不为每次事件创建声源");
                        Finish(true); break;
                }
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void Check(bool value, string description)
        { if (!value) throw new InvalidOperationException(description); Results.Add("PASS " + description); }
        private static void Fail(Exception error) { Results.Add("FAIL step=" + _step + " " + error); Debug.LogException(error); Finish(false); }
        private static void Finish(bool passed)
        {
            _running = false; EditorApplication.update -= Tick;
            TimeManager.Instance?.SetPaused(false);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            _oldMouse?.MakeCurrent(); _oldKeyboard?.MakeCurrent();
            string summary = "passed=" + Results.Count(r => r.StartsWith("PASS")) + " failed=" + (passed ? 0 : 1);
            Directory.CreateDirectory("Logs/WorldSfx");
            File.WriteAllLines("Logs/WorldSfx/Validation.txt", Results.Concat(new[] { summary }));
            Debug.Log("[WorldSfxValidation] " + summary); EditorApplication.isPlaying = false;
        }
    }
}
