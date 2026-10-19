using System;
using System.IO;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    public static class WorldSfxSetup
    {
        private const string SoundFolder = "Assets/_Project/Audio/SFX/";
        private const string PrefabFolder = "Assets/_Project/Prefabs";
        public const string LightA = SoundFolder + "Swishes/SwordLight_A.wav";
        public const string LightB = SoundFolder + "Swishes/SwordLight_B.wav";
        public const string HeavyA = SoundFolder + "Swishes/SwordHeavy_A.wav";
        public const string HeavyB = SoundFolder + "Swishes/SwordHeavy_B.wav";

        [MenuItem("Tools/规则游戏/音频/⑥ 接入敌人与机关音效（保留已有配置）")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SoundFolder.TrimEnd('/') }))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            SfxBank bank = AssetDatabase.LoadAssetAtPath<SfxBank>(SfxSetup.BankPath);
            if (bank == null) throw new InvalidOperationException("请先运行音频菜单④。");
            var serialized = new SerializedObject(bank);
            var entries = serialized.FindProperty("_entries");
            Set(entries, SfxCue.SwordLight, 0.5f, 64, 0.98f, 1.04f, false, "Assets/_Project/Audio/SFX/Swishes/SwordLight_A.wav", "Assets/_Project/Audio/SFX/Swishes/SwordLight_B.wav");
            Set(entries, SfxCue.SwordHeavy, 0.58f, 64, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Swishes/SwordHeavy_A.wav", "Assets/_Project/Audio/SFX/Swishes/SwordHeavy_B.wav");
            Set(entries, SfxCue.EnemyMelee, 0.38f, 160, 0.98f, 1.04f, false, "Assets/_Project/Audio/SFX/Swishes/SwordLight_A.wav", "Assets/_Project/Audio/SFX/Swishes/SwordLight_B.wav");
            Set(entries, SfxCue.EnemyShot, 0.32f, 160, 0.98f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/EnemyShot.wav");
            Set(entries, SfxCue.EnemyBeam, 0.36f, 144, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Selected/EnemyBeam.wav");
            Set(entries, SfxCue.EnemyHitWood, 0.46f, 112, 0.98f, 1.04f, false, "Assets/_Project/Audio/SFX/Selected/HitWood_A.wav", "Assets/_Project/Audio/SFX/Selected/HitWood_B.wav");
            Set(entries, SfxCue.EnemyHitMetal, 0.42f, 112, 0.98f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/HitMetal_A.wav", "Assets/_Project/Audio/SFX/Selected/HitMetal_B.wav");
            Set(entries, SfxCue.EnemyHitSoft, 0.46f, 112, 0.98f, 1.04f, false, "Assets/_Project/Audio/SFX/Selected/HitSoft_A.wav", "Assets/_Project/Audio/SFX/Selected/HitSoft_B.wav");
            Set(entries, SfxCue.EnemyDeathWood, 0.56f, 40, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Selected/DeathWood.wav");
            Set(entries, SfxCue.EnemyDeathMetal, 0.5f, 40, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Selected/DeathMetal.wav");
            Set(entries, SfxCue.EnemyDeathSoft, 0.54f, 40, 0.98f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/DeathSoft.wav");
            Set(entries, SfxCue.SwitchOn, 0.42f, 112, 0.99f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/SwitchOn.wav");
            Set(entries, SfxCue.SwitchOff, 0.38f, 112, 0.99f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/SwitchOff.wav");
            Set(entries, SfxCue.ImpactStone, 0.46f, 144, 0.98f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/ImpactStone.wav");
            Set(entries, SfxCue.ImpactMetal, 0.42f, 144, 0.98f, 1.03f, false, "Assets/_Project/Audio/SFX/Selected/HitMetal_B.wav", "Assets/_Project/Audio/SFX/Selected/HitMetal_A.wav");
            Set(entries, SfxCue.ImpactWood, 0.44f, 144, 0.98f, 1.04f, false, "Assets/_Project/Audio/SFX/Selected/HitWood_B.wav", "Assets/_Project/Audio/SFX/Selected/HitWood_A.wav");
            Set(entries, SfxCue.BreakStone, 0.58f, 32, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Selected/BreakStone.wav");
            Set(entries, SfxCue.BreakWood, 0.56f, 48, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Selected/BreakWood.wav");
            Set(entries, SfxCue.Explosion, 0.6f, 32, 0.98f, 1.02f, false, "Assets/_Project/Audio/SFX/Selected/Explosion.wav");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bank);

            int saved = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;
                try
                {
                    foreach (EnemyActor actor in root.GetComponentsInChildren<EnemyActor>(true))
                        if (actor.GetComponent<EnemySfxPresenter>() == null)
                        { actor.gameObject.AddComponent<EnemySfxPresenter>(); changed = true; }
                    foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (component != null && MechanismSfxPresenter.Supports(component)
                            && component.GetComponent<MechanismSfxPresenter>() == null)
                        { component.gameObject.AddComponent<MechanismSfxPresenter>(); changed = true; }
                    if (path == PrefabFolder + "/LevelRuntime.prefab" || path == PrefabFolder + "/Enemies/EnemyLabPlayer.prefab")
                    {
                        SfxPlayer audio = root.GetComponent<SfxPlayer>();
                        if (audio == null)
                        {
                            audio = root.AddComponent<SfxPlayer>();
                            var sound = new SerializedObject(audio);
                            sound.FindProperty("_bank").objectReferenceValue = bank;
                            sound.ApplyModifiedPropertiesWithoutUndo(); changed = true;
                        }
                        if (root.GetComponent<WorldSfxBinder>() == null) { root.AddComponent<WorldSfxBinder>(); changed = true; }
                    }
                    if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); saved++; }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/WorldSfx");
            File.WriteAllText("Logs/WorldSfx/Setup.txt", "Updated sound bank and " + saved + " prefabs.\n");
            Debug.Log("[WorldSfxSetup] 新挥剑素材与敌人/机关已接线；修改预制体 " + saved + " 份。");
        }

        private static void Set(SerializedProperty entries, SfxCue cue, float volume, int priority, float pitchMin,
            float pitchMax, bool replace, params string[] clips)
        {
            SerializedProperty item = null;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Cue").intValue == (int)cue)
                { item = entries.GetArrayElementAtIndex(i); break; }
            if (item != null && !replace) return;
            if (item == null) { entries.arraySize++; item = entries.GetArrayElementAtIndex(entries.arraySize - 1); }
            item.FindPropertyRelative("Cue").intValue = (int)cue;
            item.FindPropertyRelative("Volume").floatValue = volume;
            item.FindPropertyRelative("Pitch").vector2Value = new Vector2(pitchMin, pitchMax);
            item.FindPropertyRelative("Priority").intValue = priority;
            item.FindPropertyRelative("MinInterval").floatValue = .12f;
            item.FindPropertyRelative("MaxVoices").intValue = 4;
            var references = item.FindPropertyRelative("Clips");
            references.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clips[i]);
                if (clip == null) throw new InvalidOperationException("缺少音频：" + clips[i]);
                references.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
        }
    }
}
