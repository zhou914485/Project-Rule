using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    /// <summary>素材更换的定向检查；不进入 Play，不保存场景，不重跑战斗接入测试。</summary>
    [InitializeOnLoad]
    internal static class BossOnlineValidation
    {
        private const string Request = "Temp/BossOnline.request";
        [Serializable] private sealed class Manifest { public Record[] items; }
        [Serializable] private sealed class Record
        {
            public string cue, output_sha256;
            public float seconds, rms;
        }

        static BossOnlineValidation()
        {
            if (File.Exists(Request)) EditorApplication.delayCall += Run;
        }

        [MenuItem("Tools/规则游戏/音频/验证 Boss 网上重选素材（不进入 Play）")]
        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play Mode。");
            if (File.Exists(Request)) File.Delete(Request);
            var results = new List<string>();
            try
            {
                string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
                string json = File.ReadAllText(Path.Combine(repository, "Doc/AudioSources/BossSfxManifest.json"));
                var records = JsonUtility.FromJson<Manifest>("{\"items\":" + json + "}").items;
                if (records.Length != 19) throw new InvalidOperationException("Boss 清单应包含19项。");
                var bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
                foreach (var record in records)
                {
                    var cue = (SfxCue)Enum.Parse(typeof(SfxCue), record.cue);
                    string path = BossSfxSetup.DefaultClipPath(cue);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    var entry = bank.Find(cue);
                    if (clip == null || entry == null) throw new InvalidOperationException("缺少素材或配置：" + cue);
                    if (entry.Clips.Length != 1 || entry.Clips[0] != clip)
                    {
                        throw new InvalidOperationException("当前配置已手动更换，请更新选材清单后再验证：" + cue);
                    }
                    string hash;
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(path)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (hash != record.output_sha256 || Mathf.Abs(clip.length-record.seconds) > .001f)
                        throw new InvalidOperationException("素材哈希或 Unity 导入时长不匹配：" + cue);
                    if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                    var samples = new float[clip.samples*clip.channels];
                    if (!clip.GetData(samples, 0) || samples.Any(float.IsNaN)) throw new InvalidOperationException("PCM 不可读："+cue);
                    double rms = Math.Sqrt(samples.Sum(value => (double)value*value)/samples.Length);
                    if (rms < .001 || Math.Abs(rms-record.rms) > .001 || samples.Max(value => Mathf.Abs(value)) > .86f)
                        throw new InvalidOperationException("导入 PCM 与清单能量不匹配或超出峰值上限："+cue);
                    AudioAudition.PlayProcessed(clip, entry.Volume*.85f, 1f);
                    var preview = (AudioClip)typeof(AudioAudition).GetField("_temporary",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                    var output = new float[preview.samples*preview.channels]; preview.GetData(output,0);
                    if (!output.Any(value => Mathf.Abs(value)>.00001f)) throw new InvalidOperationException("编辑器试听静音："+cue);
                    results.Add("PASS " + cue + " 新文件哈希、Unity导入PCM、配置引用及原生试听输出一致");
                }
                results.Add("PASS 19 项 Boss 素材检查完成；未进入 Play 或保存场景");
                AudioWorkbenchWindow.ShowBoss();
            }
            catch (Exception error) { results.Add("FAIL " + error); }
            finally
            {
                AudioAudition.Stop();
                Directory.CreateDirectory("Logs/BossOnline"); File.WriteAllLines("Logs/BossOnline/Validation.txt",results);
                Debug.Log("Boss 网上选材验证："+(results.Any(line=>line.StartsWith("FAIL")) ? "失败" : "通过"));
            }
        }
    }
}
