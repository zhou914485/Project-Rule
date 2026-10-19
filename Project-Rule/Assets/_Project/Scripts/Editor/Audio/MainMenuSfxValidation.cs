using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Audio;
using RuleGame.UI.Launch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Audio
{
    [InitializeOnLoad]
    public static class MainMenuSfxValidation
    {
        private const string Pending = "RuleGame.MainMenuSfxValidation";
        private static readonly List<string> Results = new List<string>();
        private static MainMenuPresenter _menu;
        private static SfxPlayer _audio;
        private static VisualElement _ui;
        private static int _step, _played;
        private static double _due, _deadline;
        private static SfxCue _last;

        static MainMenuSfxValidation()
        {
            EditorApplication.playModeStateChanged += StateChanged;
            if (File.Exists("Temp/MainMenuSfx.request")) EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/规则游戏/音频/验证主界面按钮音效")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (File.Exists("Temp/UiSfxRestore/Undo.request"))
            {
                EditorApplication.delayCall += Run;
                return;
            }
            if (File.Exists("Temp/MainMenuSfx.request")) File.Delete("Temp/MainMenuSfx.request");
            MainMenuSfxSetup.Install();
            Scene active = SceneManager.GetActiveScene();
            SessionState.SetString(Pending + ".Active", active.name);
            Scene boot = SceneManager.GetSceneByPath(RuleGame.Editor.UI.LaunchUISetup.BootPath);
            bool opened = !boot.IsValid() || !boot.isLoaded;
            SessionState.SetBool(Pending + ".Opened", opened);
            if (opened) EditorSceneManager.OpenScene(RuleGame.Editor.UI.LaunchUISetup.BootPath, OpenSceneMode.Additive);
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".Background", Application.runInBackground);
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Application.runInBackground = true;
                Results.Clear(); _step = 0; _played = 0; _audio = null;
                _deadline = EditorApplication.timeSinceStartup + 25d;
                _due = EditorApplication.timeSinceStartup + .2d;
                EditorApplication.update += Tick;
            }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Pending, false);
            Application.runInBackground = SessionState.GetBool(Pending + ".Background", false);
            if (SessionState.GetBool(Pending + ".Opened", false))
            {
                Scene boot = SceneManager.GetSceneByPath(RuleGame.Editor.UI.LaunchUISetup.BootPath);
                if (boot.IsValid() && boot.isLoaded) EditorSceneManager.CloseScene(boot, true);
            }
            Scene previous = SceneManager.GetSceneByName(SessionState.GetString(Pending + ".Active", ""));
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }

        private static void Played(SfxCue cue) { _played++; _last = cue; }
        private static void Check(bool condition, string text)
        {
            if (!condition) throw new InvalidOperationException(text);
            Results.Add("PASS " + text);
        }

        private static void Submit(string name, SfxCue cue)
        {
            Button button = _ui.Q<Button>(name);
            int count = _played;
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
            Check(_played == count + 1 && _last == cue, name + " 实际按钮确认仅发声一次"
                + "（新增=" + (_played - count) + "，Enabled=" + button.enabledInHierarchy
                + "，游戏时间=" + Time.unscaledTime + "）");
            Check(_audio.GetComponentsInChildren<AudioSource>().Any(source => source.isPlaying
                && source.ignoreListenerPause && source.volume > 0f && _audio.Bank.Find(cue).Clips.Contains(source.clip)),
                name + " 使用界面声源实际播放");
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup > _deadline) { Finish(new TimeoutException("主界面音效验证超时")); return; }
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (_audio == null)
                {
                    _menu = UnityEngine.Object.FindAnyObjectByType<MainMenuPresenter>();
                    if (_menu == null || _menu.GetComponent<MainMenuSfxPresenter>() == null || SfxPlayer.Active == null) return;
                    _audio = SfxPlayer.Active; _ui = _menu.GetComponent<UIDocument>().rootVisualElement;
                    _audio.OnPlayed += Played;
                    Check(TimeManager.Instance.IsPaused, "正式主菜单暂停世界时仍可使用界面声音");
                    WorldSfxBinder.Install(_menu.gameObject.scene); WorldSfxBinder.Install(_menu.gameObject.scene);
                    Check(_menu.GetComponents<MainMenuSfxPresenter>().Length == 1, "重复接线不会增加音效组件");
                    foreach (SfxCue cue in new[] { SfxCue.MenuClick, SfxCue.MenuBack, SfxCue.MenuConfirm })
                    {
                        var entry = _audio.Bank.Find(cue);
                        Check(entry != null && SfxBank.IsInterface(cue), cue + " 已配置为暂停可用的界面通道");
                        AudioClip clip = entry.Clips[0]; clip.LoadAudioData();
                        var data = new float[clip.samples * clip.channels];
                        Check(clip.GetData(data, 0) && data.Any(sample => Mathf.Abs(sample) > .001f), cue + " PCM 非零");
                    }
                }
                switch (_step++)
                {
                    case 0: Submit("loadButton", SfxCue.MenuClick); break;
                    case 1: Submit("modalCloseButton", SfxCue.MenuBack); break;
                    case 2: Submit("aboutButton", SfxCue.MenuClick); break;
                    case 3: Submit("modalBackButton", SfxCue.MenuBack); break;
                    case 4: Submit("settingsButton", SfxCue.MenuClick); break;
                    case 5: Submit("modalBackButton", SfxCue.MenuBack); break;
                    case 6: Submit("quitButton", SfxCue.MenuClick); break;
                    case 7: Submit("quitConfirmButton", SfxCue.MenuConfirm); break;
                    case 8: Submit("modalBackButton", SfxCue.MenuBack); break;
                    case 9:
                        var sound = _menu.GetComponent<MainMenuSfxPresenter>();
                        sound.enabled = false; sound.enabled = true; sound.enabled = false; sound.enabled = true;
                        Submit("settingsButton", SfxCue.MenuClick);
                        break;
                    case 10: Submit("modalBackButton", SfxCue.MenuBack); break;
                    case 11:
                        Button disabled = _ui.Q<Button>("loadButton"); disabled.SetEnabled(false);
                        int count = _played;
                        using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = disabled; disabled.SendEvent(evt); }
                        Check(_played == count, "禁用按钮不播放点击声");
                        disabled.SetEnabled(true);
                        Check(!_audio.Play(SfxCue.EnemyMelee), "世界暂停仍拒绝世界音效");
                        break;
                    case 12:
                        Submit("startButton", SfxCue.MenuClick);
                        Check(_menu.HasStarted && _ui.Q("menuViewport").ClassListContains("hidden"), "开始按钮隐藏菜单前已经发声");
                        Finish(null); return;
                }
                _due = EditorApplication.timeSinceStartup + .14d;
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            if (_audio != null) _audio.OnPlayed -= Played;
            Directory.CreateDirectory("Logs/MainMenuSfx");
            File.WriteAllText("Logs/MainMenuSfx/Validation.txt", string.Join("\n", Results)
                + "\nPassed: " + Results.Count + "\nFailed: " + (error == null ? 0 : 1) + "\n" + error);
            if (error != null) Debug.LogException(error);
            EditorApplication.isPlaying = false;
        }
    }
}
