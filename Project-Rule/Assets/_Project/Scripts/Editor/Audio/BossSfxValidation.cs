using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    [InitializeOnLoad]
    internal static class BossSfxValidation
    {
        private const string Request = "Temp/BossSfx.request";
        private const string Pending = "RuleGame.BossSfxValidation";
        private static readonly List<string> Results = new List<string>();
        private static readonly List<SfxCue> Played = new List<SfxCue>();
        private static readonly Dictionary<string, EnemyActor> Actors = new Dictionary<string, EnemyActor>();
        private static GameObject _root;
        private static SfxPlayer _audio;
        private static double _due;
        private static bool _paused;

        static BossSfxValidation()
        {
            EditorApplication.playModeStateChanged += StateChanged;
            if (File.Exists(Request)) EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/规则游戏/音频/验证 Boss 扩展音效（PlayMode）")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play Mode。");
            if (File.Exists(Request)) File.Delete(Request);
            BossSfxSetup.Install();
            Results.Clear();
            var bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
            string before = EditorJsonUtility.ToJson(bank);
            BossSfxSetup.EnsureEntries(bank);
            Check(EditorJsonUtility.ToJson(bank) == before, "重复安装保留全部已有配置");
            foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
            {
                if ((int)cue < (int)SfxCue.BossRuleCast) continue;
                var clip = bank.Find(cue)?.Clips.FirstOrDefault();
                Check(clip != null && AudioWorkbenchWindow.Name(cue).Contains("Boss"), cue + " 素材及中文工作台名称存在");
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                var pcm = new float[clip.samples * clip.channels];
                Check(clip.GetData(pcm, 0) && pcm.Any(sample => Mathf.Abs(sample) > .00001f), cue + " 可读非零 PCM");
            }
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // 域重载清空静态集合，EditMode 结果先写入文件，PlayMode 继续追加。
                Results.Clear();
                _due = EditorApplication.timeSinceStartup + .4;
                EditorApplication.update += Begin;
            }
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                Directory.CreateDirectory("Logs/BossSfx");
                File.WriteAllLines("Logs/BossSfx/Validation.txt", Results);
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Pending, false);
                AudioWorkbenchWindow.ShowBoss();
            }
        }

        private static void Begin()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            EditorApplication.update -= Begin;
            try
            {
                _paused = TimeManager.Instance?.IsPaused == true;
                TimeManager.Instance?.SetPaused(false);
                _root = new GameObject("BossSfxValidation_Temporary");
                _audio = SfxPlayer.Active;
                if (_audio == null)
                {
                    var go = new GameObject("Audio"); go.SetActive(false); go.transform.SetParent(_root.transform);
                    var listener = new GameObject("Listener", typeof(AudioListener)); listener.transform.SetParent(go.transform);
                    _audio = go.AddComponent<SfxPlayer>();
                    var serialized = new SerializedObject(_audio);
                    serialized.FindProperty("_bank").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
                    serialized.ApplyModifiedPropertiesWithoutUndo(); go.SetActive(true);
                }
                Played.Clear(); _audio.OnPlayed += OnPlayed;
                var target = new GameObject("DistantTarget"); target.transform.SetParent(_root.transform); target.transform.position = new Vector3(1000, 0);
                var listenerObject = UnityEngine.Object.FindAnyObjectByType<AudioListener>();
                var origin = listenerObject != null ? listenerObject.transform.position : Vector3.zero;
                Actors.Clear();
                foreach (string name in new[] { "BrickBoss", "ClockBoss", "RabbitBoss", "BatBoss", "BrickMinion" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + name + ".prefab");
                    var instance = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity, _root.transform);
                    var actor = instance.GetComponentInChildren<EnemyActor>(); actor.Target = target.transform;
                    Check(actor.GetComponents<EnemySfxPresenter>().Length == 1, name + " 真实预制体已有唯一音效表现");
                    Actors[name] = actor;
                }
                _due = EditorApplication.timeSinceStartup + .3;
                EditorApplication.update += Verify;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void OnPlayed(SfxCue cue) => Played.Add(cue);
        private static void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException(text);
            Results.Add("PASS " + text);
        }
        private static void Expect(SfxCue cue, Action trigger)
        {
            _audio.ClearWorld(); Played.Clear(); trigger();
            Check(Played.Count == 1 && Played[0] == cue, cue + " 真实表现事件仅播放一次");
            Check(_audio.GetComponentsInChildren<AudioSource>().Any(source => source.isPlaying && source.volume > 0
                && _audio.Bank.Find(cue).Clips.Contains(source.clip)), cue + " 已进入实际 AudioSource");
        }

        private static void Verify()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            EditorApplication.update -= Verify;
            try
            {
                TimeManager.Instance?.SetPaused(false);
                foreach (var actor in Actors.Values) { Check(actor.Brain != null, actor.name + " 脑初始化成功"); actor.enabled = false; }
                var brick = Actors["BrickBoss"]; var clock = Actors["ClockBoss"];
                var rabbit = Actors["RabbitBoss"]; var bat = Actors["BatBoss"];
                Expect(SfxCue.BrickBossWindup, () => brick.Cue("HammerGround", .8f));
                Expect(SfxCue.BrickBossSlam, () => brick.AreaAttack(1f));
                Expect(SfxCue.BrickBossThrow, () => brick.Shoot(Vector2.right));
                Expect(SfxCue.BrickBossSplit, () => brick.Cue("Split", 1));
                Expect(SfxCue.ClockBossCharge, () => { clock.Cue("Charge", 1); clock.Cue("Charge", 1); });
                Expect(SfxCue.ClockBossBarrageWarning, () => clock.EmitFeedback(EnemyFeedbackKind.BarrageWarning, clock.transform.position, Vector2.up));
                Expect(SfxCue.ClockBossBeam, () => clock.EmitFeedback(EnemyFeedbackKind.BeamSpawn, clock.transform.position, Vector2.up));
                Expect(SfxCue.ClockBossRewind, () => clock.Cue("Rewind", 1));
                Expect(SfxCue.ClockBossStun, () => clock.Cue("Stun", 1));
                Expect(SfxCue.RabbitBossBurrow, () => rabbit.Cue("Hide", 1));
                Expect(SfxCue.RabbitBossEmerge, () => rabbit.Cue("Pop", 1));
                Expect(SfxCue.RabbitBossShot, () => rabbit.Shoot(Vector2.right));
                Expect(SfxCue.BatBossWindup, () => bat.Cue("DashWindup", 1));
                Expect(SfxCue.BatBossDash, () => { bat.Cue("Dash", 1); bat.Cue("Dash", 1); });
                Expect(SfxCue.BatBossFlap, () => bat.Cue("WingSpread", 1));
                Expect(SfxCue.BatBossRoar, () => bat.Cue("Roar", 1));
                Expect(SfxCue.BossPhaseChange, () => bat.Cue("Weak", 1));
                Expect(SfxCue.BossRuleCast, () => brick.EmitFeedback(EnemyFeedbackKind.RuleCast, brick.transform.position, Vector2.up));
                Expect(SfxCue.BossSummon, () => brick.EmitFeedback(EnemyFeedbackKind.Summon, brick.transform.position, Vector2.up));
                Expect(SfxCue.EnemyMelee, () => Actors["BrickMinion"].AreaAttack(1));
                _audio.ClearWorld(); Played.Clear();
                object snapshot = rabbit.CaptureSnapshotState(); rabbit.RestoreSnapshotState(snapshot);
                Check(Played.Count == 0 && !rabbit.IsRestoringSnapshot, "快照恢复不会重播动作音效且清理恢复标志");
                _audio.ClearWorld(); Played.Clear();
                TimeManager.Instance?.SetPaused(true); bat.Cue("DashWindup", 1);
                Check(Played.Count == 0, "游戏暂停时 Boss 提示不穿透世界暂停");
                Finish(null);
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Begin; EditorApplication.update -= Verify;
            if (error != null) Results.Add("FAIL " + error);
            if (_audio != null) { _audio.OnPlayed -= OnPlayed; _audio.ClearWorld(); }
            TimeManager.Instance?.SetPaused(_paused);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            Directory.CreateDirectory("Logs/BossSfx"); File.AppendAllLines("Logs/BossSfx/Validation.txt", Results);
            Debug.Log("Boss 音效定向验证：" + (error == null ? "通过" : "失败，请查看 Logs/BossSfx/Validation.txt"));
            EditorApplication.isPlaying = false;
        }
    }
}
