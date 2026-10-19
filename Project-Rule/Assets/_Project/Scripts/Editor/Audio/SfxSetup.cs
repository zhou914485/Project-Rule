using System;
using System.IO;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Audio;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    public static class SfxSetup
    {
        public const string BankPath = "Assets/_Project/Data/Audio/PlayerWheel_SfxBank.asset";
        private const string AudioFolder = "Assets/_Project/Audio/SFX/Kenney/";
        private const string RuntimePath = "Assets/_Project/Prefabs/LevelRuntime.prefab";

        [MenuItem("Tools/规则游戏/音频/④ 接入玩家与轮盘音效")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Audio/SFX" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }
            SfxBank bank = AssetDatabase.LoadAssetAtPath<SfxBank>(BankPath);
            if (bank == null)
            {
                bank = ScriptableObject.CreateInstance<SfxBank>();
                AssetDatabase.CreateAsset(bank, BankPath);
                var serialized = new SerializedObject(bank);
                var entries = serialized.FindProperty("_entries");
                entries.arraySize = 14; // Player/wheel cues; menu ⑥ adds the world cues without blank entries.
                SetEntry(entries, SfxCue.Jump, 0.42f, 0.98f, 1.02f, "Assets/_Project/Audio/SFX/Selected/Jump.wav");
                SetEntry(entries, SfxCue.AirJump, 0.4f, 1.09f, 1.13f, "Assets/_Project/Audio/SFX/Selected/Jump.wav");
                SetEntry(entries, SfxCue.WallJump, 0.44f, 1.03f, 1.07f, "Assets/_Project/Audio/SFX/Selected/Jump.wav");
                SetEntry(entries, SfxCue.Land, 0.33f, 0.98f, 1.02f, "Assets/_Project/Audio/SFX/PlayerFeedback/PlayerLand.wav", "Assets/_Project/Audio/SFX/Selected/Land_B.wav");
                SetEntry(entries, SfxCue.Dash, 0.46f, 0.98f, 1.03f, "Assets/_Project/Audio/SFX/Selected/Dash.wav");
                SetEntry(entries, SfxCue.SwordLight, 0.5f, 0.98f, 1.04f, "Assets/_Project/Audio/SFX/Swishes/SwordLight_A.wav", "Assets/_Project/Audio/SFX/Swishes/SwordLight_B.wav");
                SetEntry(entries, SfxCue.SwordHeavy, 0.58f, 0.98f, 1.02f, "Assets/_Project/Audio/SFX/Swishes/SwordHeavy_A.wav", "Assets/_Project/Audio/SFX/Swishes/SwordHeavy_B.wav");
                SetEntry(entries, SfxCue.Hurt, 0.56f, 0.98f, 1.02f, "Assets/_Project/Audio/SFX/PlayerFeedback/PlayerHurt.wav", "Assets/_Project/Audio/SFX/Selected/Hurt_B.wav");
                SetEntry(entries, SfxCue.WheelOpen, 0.36f, 1f, 1f, "Assets/_Project/Audio/SFX/Selected/WheelOpen.wav");
                SetEntry(entries, SfxCue.WheelPreview, 0.2f, 1f, 1f, "Assets/_Project/Audio/SFX/Selected/WheelPreview.wav");
                SetEntry(entries, SfxCue.WheelConfirm, 0.42f, 1f, 1f, "Assets/_Project/Audio/SFX/Selected/WheelConfirm.wav");
                SetEntry(entries, SfxCue.WheelCancel, 0.32f, 1f, 1f, "Assets/_Project/Audio/SFX/Selected/WheelCancel.wav");
                SetEntry(entries, SfxCue.WheelBack, 0.28f, 1f, 1f, "Assets/_Project/Audio/SFX/Selected/WheelBack.wav");
                SetEntry(entries, SfxCue.WheelError, 0.34f, 1f, 1f, "Assets/_Project/Audio/SFX/Selected/WheelError.wav");
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            MainMenuSfxSetup.EnsureEntries(bank);
            BossSfxSetup.EnsureEntries(bank);
            GameObject root = PrefabUtility.LoadPrefabContents(RuntimePath);
            try
            {
                var audio = root.GetComponent<SfxPlayer>();
                if (audio == null) audio = root.AddComponent<SfxPlayer>();
                SetReference(audio, "_bank", bank);
                var player = root.GetComponentInChildren<PlayerController>(true);
                var playerSfx = player.GetComponent<PlayerSfxPresenter>();
                if (playerSfx == null) playerSfx = player.gameObject.AddComponent<PlayerSfxPresenter>();
                SetReference(playerSfx, "_audio", audio);
                var wheel = root.GetComponentInChildren<RuleWheelPresenter>(true);
                var wheelSfx = wheel.GetComponent<RuleWheelSfxPresenter>();
                if (wheelSfx == null) wheelSfx = wheel.gameObject.AddComponent<RuleWheelSfxPresenter>();
                SetReference(wheelSfx, "_audio", audio);
                PrefabUtility.SaveAsPrefabAsset(root, RuntimePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/PlayerWheelSfx");
            File.WriteAllText("Logs/PlayerWheelSfx/Setup.txt", "Player and rule wheel sound effects installed in LevelRuntime.\n");
            Debug.Log("[SfxSetup] 玩家动作、实际受击与轮盘音效已接入 LevelRuntime；重复安装保留音效配置。");
        }

        private static void SetReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEntry(SerializedProperty array, SfxCue cue, float volume, float minPitch, float maxPitch,
            params string[] clips)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex((int)cue);
            entry.FindPropertyRelative("Cue").enumValueIndex = (int)cue;
            entry.FindPropertyRelative("Volume").floatValue = volume;
            entry.FindPropertyRelative("Pitch").vector2Value = new Vector2(minPitch, maxPitch);
            entry.FindPropertyRelative("MinInterval").floatValue = cue == SfxCue.WheelError ? 0.18f : 0.06f;
            entry.FindPropertyRelative("MaxVoices").intValue = 2;
            entry.FindPropertyRelative("Priority").intValue = cue == SfxCue.Hurt ? 16 : 96;
            var references = entry.FindPropertyRelative("Clips");
            references.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clips[i].StartsWith("Assets/") ? clips[i] : AudioFolder + clips[i] + ".ogg");
                if (clip == null) throw new InvalidOperationException("缺少音效：" + clips[i]);
                references.GetArrayElementAtIndex(i).objectReferenceValue = clip;
            }
        }
    }
}
