using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Audio
{
    public static class AudioAuditionValidation
    {
        private static readonly Type Util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        private static readonly MethodInfo Playing = Util.GetMethod("IsPreviewClipPlaying");
        private static readonly MethodInfo Position = Util.GetMethod("GetPreviewClipPosition");
        private static readonly FieldInfo Temporary = typeof(AudioAudition).GetField("_temporary", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly List<string> Results = new List<string>();
        private static double _due;
        private static float _position;
        private static int _step;
        private static string _scene;
        private static AudioClip Preview => (AudioClip)Temporary.GetValue(null);

        [MenuItem("Tools/规则游戏/音频/⑨ 验证参数试听输出")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            EditorApplication.update -= Tick; Results.Clear();
            _scene = SceneManager.GetActiveScene().path;
            try
            {
                var bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
                foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
                {
                    AudioClip clip = bank.Find(cue).Clips[0];
                    AudioAudition.PlayProcessed(clip, .4f, 1f);
                    Check(Rms(Preview) > 0.00001d, cue + " 进入原生预览后样本仍有能量");
                }
                AudioClip sword = bank.Find(SfxCue.SwordLight).Clips[0];
                AudioAudition.PlayProcessed(sword, 1f, 1f);
                double full = Rms(Preview);
                AudioClip previous = Preview;
                AudioAudition.PlayProcessed(sword, .5f, 1f);
                Check(previous == null, "连续试听销毁上一份临时音频");
                double half = Rms(Preview);
                Check(Math.Abs(half / full - .5d) < .001d, "半音量参数使实际波形能量幅度减半");
                AudioAudition.PlayProcessed(sword, .5f, 1.5f);
                Check(Preview.samples == Mathf.CeilToInt(sword.samples / 1.5f), "音高 1.5 倍同时改变实际播放时长");
                AudioAudition.PlayProcessed(sword, 0f, 1f);
                Check(Rms(Preview) == 0d, "零音量有意输出静音");
                int variant = 0;
                foreach (AudioClip hurt in bank.Find(SfxCue.Hurt).Clips)
                {
                    AudioAudition.PlayProcessed(hurt, .4f, 1f);
                    Check(Rms(Preview) > .00001d, "玩家受击变体 " + (++variant) + " 具有非零试听输出");
                }
                AudioClip longClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Project/Audio/SFX/Rpg80/spell_fire_02.ogg");
                AudioAudition.PlayProcessed(longClip, .1f, .5f);
                _step = 0; _due = EditorApplication.timeSinceStartup + .12d;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (_step++ == 0)
                {
                    Check((bool)Playing.Invoke(null, null) && Rms(Preview) > .00001d, "经过编辑器帧和 DSP 更新后依然播放非零波形");
                    _position = (float)Position.Invoke(null, null);
                    _due = EditorApplication.timeSinceStartup + .2d;
                }
                else
                {
                    float now = (float)Position.Invoke(null, null);
                    Check(now > _position + .05f, "实际播放位置持续前进（" + _position.ToString("0.000") + " → " + now.ToString("0.000") + " 秒）");
                    AudioAudition.Stop();
                    Check(Preview == null && !(bool)Playing.Invoke(null, null), "停止释放临时音频并结束原生预览");
                    Check(Resources.FindObjectsOfTypeAll<AudioClip>().All(c => c.name != "AudioWorkbench_Preview"), "连续试听结束后没有遗留动态 Clip");
                    Check(SceneManager.GetActiveScene().path == _scene, "验证不切换或写入当前场景");
                    Finish(true);
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private static double Rms(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) throw new InvalidOperationException("无法读回试听输出。");
            return Math.Sqrt(data.Select(x => (double)x * x).Average());
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Results.Add("PASS " + message);
        }
        private static void Fail(Exception error) { Results.Add("FAIL " + error); Debug.LogException(error); Finish(false); }
        private static void Finish(bool success)
        {
            EditorApplication.update -= Tick; AudioAudition.Stop();
            string summary = "passed=" + Results.Count(r => r.StartsWith("PASS")) + " failed=" + (success ? 0 : 1);
            Directory.CreateDirectory("Logs/AudioAudition");
            File.WriteAllLines("Logs/AudioAudition/Validation.txt", Results.Concat(new[] { summary }));
            Debug.Log("[AudioAuditionValidation] " + summary);
        }

    }
}
