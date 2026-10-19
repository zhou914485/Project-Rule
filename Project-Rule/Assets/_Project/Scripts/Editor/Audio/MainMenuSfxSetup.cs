using System;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    public static class MainMenuSfxSetup
    {
        [MenuItem("Tools/规则游戏/音频/接入主界面按钮音效")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            SfxBank bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
            if (bank == null) throw new InvalidOperationException("缺少共用音效配置。");
            EnsureEntries(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
        }

        internal static void EnsureEntries(SfxBank bank)
        {
            var serialized = new SerializedObject(bank);
            SerializedProperty entries = serialized.FindProperty("_entries");
            Add(entries, bank, SfxCue.MenuClick, SfxCue.WheelPreview, .35f);
            Add(entries, bank, SfxCue.MenuBack, SfxCue.WheelBack, .28f);
            Add(entries, bank, SfxCue.MenuConfirm, SfxCue.WheelConfirm, .42f);
            if (serialized.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(bank);
        }

        private static void Add(SerializedProperty entries, SfxBank bank, SfxCue cue, SfxCue source, float volume)
        {
            if (bank.Find(cue) != null) return;
            SfxBank.Entry reference = bank.Find(source);
            if (reference?.Clips == null || reference.Clips.Length == 0)
                throw new InvalidOperationException("缺少界面音效来源：" + source);
            int index = entries.arraySize++;
            SerializedProperty item = entries.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("Cue").intValue = (int)cue;
            item.FindPropertyRelative("Volume").floatValue = volume;
            item.FindPropertyRelative("Pitch").vector2Value = Vector2.one;
            item.FindPropertyRelative("MinInterval").floatValue = .035f;
            item.FindPropertyRelative("MaxVoices").intValue = 3;
            item.FindPropertyRelative("Priority").intValue = 64;
            SerializedProperty clips = item.FindPropertyRelative("Clips");
            clips.arraySize = reference.Clips.Length;
            for (int i = 0; i < clips.arraySize; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = reference.Clips[i];
        }
    }
}
