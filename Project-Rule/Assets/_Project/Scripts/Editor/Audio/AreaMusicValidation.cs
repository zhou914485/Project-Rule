using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Audio
{
    /// <summary>真实音频 / DSP / 音量 / 分区生命周期验证；退出后恢复原场景。</summary>
    [InitializeOnLoad]
    public static class AreaMusicValidation
    {
        private const string Pending = "RuleGame.AreaMusicValidation.Pending";
        private const string SceneState = "RuleGame.AreaMusicValidation.SceneState";
        private static readonly List<string> Results = new List<string>();
        private static MusicDirector _director;
        private static Transform _focus;
        private static MusicProfile _bricks, _clock, _fluffy;
        private static MusicRegion _brickRegion, _clockRegion, _fluffyRegion;
        private static UnityEngine.SceneManagement.Scene _preloaded;
        private static int _step, _sample;
        private static double _due, _deadline, _firstStart;
        private static bool _running;

        [Serializable]
        private sealed class Setup { public SceneSetup[] Scenes; }

        static AreaMusicValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        [MenuItem("Tools/规则游戏/音频/③ 验证背景音乐（PlayMode）")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景，验证后会自动恢复。");
            SessionState.SetString(SceneState, JsonUtility.ToJson(new Setup { Scenes = EditorSceneManager.GetSceneManagerSetup() }));
            SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Pending, false);
            string setup = SessionState.GetString(SceneState, "");
            if (!string.IsNullOrEmpty(setup)) EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(setup).Scenes);
        }

        private static MusicProfile Profile(string name) => AssetDatabase.LoadAssetAtPath<MusicProfile>(MusicSetup.DataFolder + "/" + name + "_MusicProfile.asset");

        private static void Begin()
        {
            Results.Clear();
            _step = 0; _running = true;
            _deadline = EditorApplication.timeSinceStartup + 75d;
            try
            {
                Application.runInBackground = true;
                TimeManager.Initialize();
                new GameObject("MusicValidation_Listener", typeof(AudioListener));
                _bricks = Profile("Bricks"); _clock = Profile("Clockwork"); _fluffy = Profile("Fluffy");
                Check(_bricks != null && _clock != null && _fluffy != null, "三个真实音乐配置均可加载");
                Check(_bricks.Intro != null && _clock.Intro != null && _fluffy.Intro == null, "两组前奏与毛绒整曲配置正确");
                foreach (MusicProfile profile in new[] { _bricks, _clock, _fluffy })
                {
                    Check(profile.IsPlayable && profile.Loop.channels == 2 && profile.Loop.frequency == 44100,
                        profile.name + " 双声道 44.1 kHz 循环可播放");
                    var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.Loop));
                    Check(importer.defaultSampleSettings.loadType == AudioClipLoadType.Streaming, profile.name + " 长曲流式加载");
                }
                var runtime = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/LevelRuntime.prefab");
                Check(runtime.GetComponent<MusicDirector>() != null, "LevelRuntime 已接入音乐组件");
                var gameRoot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/GameRoot.prefab");
                Check(gameRoot.GetComponentsInChildren<MusicDirector>(true).Length == 1, "GameRoot 嵌套预制体继承且只有一个音乐组件");
                _focus = new GameObject("MusicValidation_Focus").transform;
                _brickRegion = MakeRegion("Bricks", 0f);
                _clockRegion = MakeRegion("Clockwork", 20f);
                _fluffyRegion = MakeRegion("Fluffy", 40f);
                var host = new GameObject("MusicValidation_Director");
                host.SetActive(false);
                _director = host.AddComponent<MusicDirector>();
                var serialized = new SerializedObject(_director);
                serialized.FindProperty("_defaultProfile").objectReferenceValue = _bricks;
                serialized.FindProperty("_focus").objectReferenceValue = _focus;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                host.SetActive(true);
                _due = EditorApplication.timeSinceStartup + 1.3d;
                EditorApplication.update += Tick;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static MusicRegion MakeRegion(string area, float x)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MusicSetup.PrefabFolder + "/" + area + "_MusicRegion.prefab");
            var go = UnityEngine.Object.Instantiate(prefab);
            go.transform.position = new Vector3(x, 0f, 0f);
            MusicRegion region = go.GetComponent<MusicRegion>();
            var serialized = new SerializedObject(region);
            serialized.FindProperty("_size").vector2Value = new Vector2(20f, 30f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return region;
        }

        private static AudioSource Source(AudioClip clip) => _director.GetComponentsInChildren<AudioSource>().First(s => s.clip == clip);
        private static double Now => EditorApplication.timeSinceStartup;

        private static void Tick()
        {
            if (!_running) return;
            if (Now > _deadline) { Fail(new TimeoutException("音乐验证超时，step=" + _step)); return; }
            if (Now < _due) return;
            try
            {
                switch (_step++)
                {
                    case 0:
                        Check(_director.CurrentProfile == _bricks, "开局按玩家位置播放积木曲");
                        Check(Source(_bricks.Intro).timeSamples > 0, "真实积木音频 前奏正在推进");
                        Check(Source(_bricks.Loop).timeSamples == 0, "前奏期间循环已排程但未提前发声");
                        Check(Math.Abs(_director.ScheduledLoopStart - _director.ScheduledStart
                            - (double)_bricks.Intro.samples / _bricks.Intro.frequency) < 0.00001d, "前奏与循环按精确采样时长衔接");
                        _firstStart = _director.ScheduledStart;
                        _sample = Source(_bricks.Intro).timeSamples;
                        TimeManager.Instance.SetPaused(true);
                        _due = Now + 0.4d;
                        break;
                    case 1:
                        Check(Mathf.Abs(_director.PauseGain - 0.35f) < 0.01f, "暂停平滑降低到 35% 音量");
                        Check(Source(_bricks.Intro).timeSamples > _sample + 2000, "暂停时真实音乐继续播放");
                        Check(Mathf.Approximately(Time.timeScale, 1f), "暂停音频没有改动游戏时间倍率");
                        TimeManager.Instance.SetPaused(false);
                        TimeManager.Instance.PushScale(TimeSource.Rule, 0.1f);
                        _due = Now + 0.4d;
                        break;
                    case 2:
                        Check(Mathf.Abs(_director.PauseGain - 1f) < 0.01f, "慢时间规则下仍按真实时间恢复音量");
                        Check(Source(_bricks.Intro).pitch == 1f, "时间规则不改变音乐音高");
                        TimeManager.Instance.RemoveScale(TimeSource.Rule);
                        _preloaded = SceneManager.CreateScene("MusicValidation_Preload");
                        var preloadedRegion = MakeRegion("Fluffy", 100f);
                        SceneManager.MoveGameObjectToScene(preloadedRegion.gameObject, _preloaded);
                        _focus.position = new Vector3(10.6f, 0f, 0f);
                        _due = Now + 0.5d;
                        break;
                    case 3:
                        Check(_director.CurrentProfile == _bricks && _director.ScheduledStart == _firstStart,
                            "相邻区域预加载和边界内 0.6 米抖动不换曲、不重播前奏");
                        _focus.position = Vector3.zero;
                        _due = Now + Math.Max(0.1d, _director.ScheduledLoopStart - AudioSettings.dspTime + 0.25d);
                        break;
                    case 4:
                        Check(!Source(_bricks.Intro).isPlaying && Source(_bricks.Loop).timeSamples > 0,
                            "真实积木前奏结束后循环已接上");
                        Check(Mathf.Abs(Source(_bricks.Loop).volume - _bricks.Volume) < .001f,
                            "积木循环实际声源使用配置音量 " + _bricks.Volume.ToString("0.00"));
                        Source(_bricks.Loop).timeSamples = _bricks.Loop.samples - _bricks.Loop.frequency / 4;
                        _due = Now + 0.7d;
                        break;
                    case 5:
                        Check(Source(_bricks.Loop).isPlaying && Source(_bricks.Loop).timeSamples < _bricks.Loop.frequency * 2,
                            "真实积木长曲到尾后回到循环开头");
                        _focus.position = new Vector3(20f, 0f, 0f);
                        _due = Now + 1.3d;
                        break;
                    case 6:
                        Check(_director.CurrentProfile == _clock && _director.IsTransitioning, "进入发条区域开始淡化");
                        Check(Source(_bricks.Loop).volume > 0f && Source(_clock.Intro).volume > 0f,
                            "跨区淡化期间新旧音乐同时具有非零音量");
                        Check(Source(_clock.Intro).timeSamples > 0, "真实发条前奏开始播放");
                        _due = Now + Math.Max(0.1d, _director.ScheduledLoopStart - AudioSettings.dspTime + 0.25d);
                        break;
                    case 7:
                        Check(!_director.IsTransitioning && Source(_clock.Loop).timeSamples > 0
                            && !Source(_clock.Intro).isPlaying, "真实发条前奏接循环且旧积木声源已释放");
                        Check(Mathf.Abs(Source(_clock.Loop).volume - _clock.Volume) < .001f,
                            "发条循环实际声源使用配置音量 " + _clock.Volume.ToString("0.00"));
                        Check(!_director.GetComponentsInChildren<AudioSource>().Any(s => s.clip == _bricks.Loop), "淡化完成无残留旧曲声源");
                        _focus.position = new Vector3(40f, 0f, 0f);
                        _due = Now + 2.5d;
                        break;
                    case 8:
                        Check(_director.CurrentProfile == _fluffy && Source(_fluffy.Loop).timeSamples > 0,
                            "进入毛绒区直接播放真实整曲循环");
                        Check(Mathf.Abs(Source(_fluffy.Loop).volume - _fluffy.Volume) < .001f,
                            "毛绒循环实际声源使用配置音量 " + _fluffy.Volume.ToString("0.00"));
                        Source(_fluffy.Loop).timeSamples = _fluffy.Loop.samples - _fluffy.Loop.frequency / 4;
                        _due = Now + 0.7d;
                        break;
                    case 9:
                        Check(Source(_fluffy.Loop).timeSamples < _fluffy.Loop.frequency * 2 && Source(_fluffy.Loop).isPlaying,
                            "毛绒整曲能够循环回绕");
                        _focus.position = Vector3.zero;
                        _due = Now + 2.5d;
                        break;
                    case 10:
                        Check(_director.CurrentProfile == _bricks && _director.ScheduledLoopStart == _director.ScheduledStart,
                            "返回已访问区域直接进入循环，前奏不重播");
                        _focus.position = new Vector3(20f, 0f, 0f);
                        _due = Now + 1d;
                        break;
                    case 11:
                        _focus.position = new Vector3(40f, 0f, 0f);
                        _due = Now + 0.4d;
                        break;
                    case 12:
                        _focus.position = Vector3.zero;
                        _due = Now + 2.7d;
                        break;
                    case 13:
                        Check(_director.CurrentProfile == _bricks && !_director.IsTransitioning,
                            "连续快速切三个区域后收敛到玩家最终所在区域");
                        Check(_director.GetComponentsInChildren<AudioSource>().Length == 4, "反复切曲始终仅使用四个声源");
                        _clockRegion.gameObject.SetActive(false);
                        _focus.position = new Vector3(20f, 0f, 0f);
                        _due = Now + 0.5d;
                        break;
                    case 14:
                        Check(_director.CurrentProfile == _bricks, "已停用或卸载的区域不参与音乐归属，回退默认曲");
                        SceneManager.UnloadSceneAsync(_preloaded);
                        _director.enabled = false;
                        _due = Now + 0.2d;
                        break;
                    case 15:
                        Check(_director.GetComponentsInChildren<AudioSource>().Length == 0, "停用组件释放所有声源与排程");
                        TimeManager.Instance.SetPaused(true);
                        _director.enabled = true;
                        _due = Now + 1d;
                        break;
                    case 16:
                        Check(_director.CurrentProfile == _bricks && Mathf.Abs(_director.PauseGain - 0.35f) < 0.01f,
                            "暂停期间重新启用仍正确绑定音乐与压低状态");
                        Check(_director.GetComponentsInChildren<AudioSource>().Length == 4, "重新启用无声源或订阅累积");
                        Check(_director.ScheduledStart == _director.ScheduledLoopStart, "重新启用不会重复已听过的前奏");
                        Finish(true);
                        break;
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Results.Add("PASS " + description);
        }

        private static void Fail(Exception ex)
        {
            Results.Add("FAIL step=" + _step + " " + ex);
            Debug.LogException(ex);
            Finish(false);
        }

        private static void Finish(bool passed)
        {
            _running = false;
            EditorApplication.update -= Tick;
            TimeManager.Instance?.SetPaused(false);
            TimeManager.Instance?.RemoveScale(TimeSource.Rule);
            Directory.CreateDirectory("Logs/AreaMusic");
            string summary = "passed=" + Results.Count(r => r.StartsWith("PASS")) + " failed=" + (passed ? 0 : 1);
            File.WriteAllLines("Logs/AreaMusic/Validation.txt", Results.Concat(new[] { summary }));
            Debug.Log("[AreaMusicValidation] " + summary);
            EditorApplication.isPlaying = false;
        }
    }
}
