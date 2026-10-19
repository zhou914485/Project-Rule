using System;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    public static class BossSfxSetup
    {
        internal const string Folder = "Assets/_Project/Audio/SFX/Boss/";
        internal static string DefaultClipPath(SfxCue cue)
        {
            // 保留工作台里已经确认的共用关系，不生成无人引用的重复素材。
            if (cue == SfxCue.BossRuleCast) cue = SfxCue.BossPhaseChange;
            if (cue == SfxCue.RabbitBossEmerge) cue = SfxCue.RabbitBossBurrow;
            return Folder + cue + ".wav";
        }

        [MenuItem("Tools/规则游戏/音频/接入 Boss 扩展音效（保留已有配置）")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play Mode。");
            var bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
            if (bank == null) throw new InvalidOperationException("缺少音效配置，请先运行音频菜单④。");
            EnsureEntries(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
            AudioWorkbenchWindow.ShowBoss();
        }

        internal static void EnsureEntries(SfxBank bank)
        {
            var serialized = new SerializedObject(bank);
            var entries = serialized.FindProperty("_entries");
            foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
            {
                if (cue < SfxCue.BossRuleCast || bank.Find(cue) != null) continue;
                string path = DefaultClipPath(cue);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) throw new InvalidOperationException("缺少 Boss 素材：" + path);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                var sample = importer.defaultSampleSettings;
                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                sample.compressionFormat = AudioCompressionFormat.PCM;
                sample.preloadAudioData = true;
                importer.defaultSampleSettings = sample; importer.loadInBackground = false;
                importer.SaveAndReimport();
                var item = entries.GetArrayElementAtIndex(entries.arraySize++);
                item.FindPropertyRelative("Cue").intValue = (int)cue;
                item.FindPropertyRelative("Volume").floatValue = cue == SfxCue.BrickBossSlam ? .58f : .42f;
                item.FindPropertyRelative("Pitch").vector2Value = new Vector2(.98f, 1.02f);
                item.FindPropertyRelative("MinInterval").floatValue = .16f;
                item.FindPropertyRelative("MaxVoices").intValue = 2;
                // 预警与阶段提示优先于普通弹幕；仍使用世界声源池和暂停规则。
                item.FindPropertyRelative("Priority").intValue = cue == SfxCue.BossPhaseChange
                    || cue == SfxCue.ClockBossBarrageWarning || cue == SfxCue.BatBossWindup ? 48 : 80;
                var clips = item.FindPropertyRelative("Clips"); clips.arraySize = 1;
                clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            }
            if (serialized.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(bank);
        }
    }
}
