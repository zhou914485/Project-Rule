using System;
using System.IO;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    /// <summary>通过 Editor API 导入与接线，不手改场景、预制体或配置的 YAML。</summary>
    public static class MusicSetup
    {
        public const string DataFolder = "Assets/_Project/Data/Audio";
        public const string PrefabFolder = "Assets/_Project/Prefabs/Audio";
        public const string LabPath = "Assets/_Project/Scenes/Test_AreaMusic.unity";
        private const string RuntimePath = "Assets/_Project/Prefabs/LevelRuntime.prefab";

        [MenuItem("Tools/规则游戏/音频/① 接入三地区域音乐")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            EnsureFolder(DataFolder);
            EnsureFolder(PrefabFolder);
            MusicProfile bricks = MakeProfile("Bricks", "bgm_area_bricks");
            MusicProfile clockwork = MakeProfile("Clockwork", "bgm_area_clockwork");
            MusicProfile fluffy = MakeProfile("Fluffy", "bgm_area_fluffy");
            MakeRegionPrefab("Bricks", bricks);
            MakeRegionPrefab("Clockwork", clockwork);
            MakeRegionPrefab("Fluffy", fluffy);

            GameObject root = PrefabUtility.LoadPrefabContents(RuntimePath);
            try
            {
                MusicDirector director = root.GetComponent<MusicDirector>();
                if (director == null) director = root.AddComponent<MusicDirector>();
                var serialized = new SerializedObject(director);
                serialized.FindProperty("_defaultProfile").objectReferenceValue = bricks;
                serialized.FindProperty("_focus").objectReferenceValue = root.GetComponentInChildren<PlayerController>(true)?.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, RuntimePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[AreaMusic] 三份音乐配置、区域预制体和 LevelRuntime 已接线；序章/中心默认积木音乐。");
            Directory.CreateDirectory("Logs/AreaMusic");
            File.WriteAllText("Logs/AreaMusic/Setup.txt", "Installed three profiles, three region prefabs and LevelRuntime music.\n");
        }

        private static MusicProfile MakeProfile(string area, string basename)
        {
            string audioFolder = "Assets/_Project/Audio/Music/" + area + "/";
            string introPath = audioFolder + basename + "_intro.wav";
            string loopPath = audioFolder + basename + (area == "Fluffy" ? ".wav" : "_loop.wav");
            ConfigureImport(loopPath, true);
            if (File.Exists(introPath)) ConfigureImport(introPath, false);
            string path = DataFolder + "/" + area + "_MusicProfile.asset";
            MusicProfile profile = AssetDatabase.LoadAssetAtPath<MusicProfile>(path);
            bool created = profile == null;
            if (created)
            {
                profile = ScriptableObject.CreateInstance<MusicProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            var serialized = new SerializedObject(profile);
            if (created)
            {
                serialized.FindProperty("_intro").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(introPath);
                serialized.FindProperty("_loop").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(loopPath);
            }
            // 重跑接线保留人工选择的素材、音量和淡化时长。
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!profile.IsPlayable) throw new InvalidOperationException("缺少循环音乐：" + loopPath);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void ConfigureImport(string path, bool streaming)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("无法导入音频：" + path);
            AudioImporterSampleSettings sample = importer.defaultSampleSettings;
            sample.loadType = streaming ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat = AudioCompressionFormat.Vorbis;
            sample.quality = 1f;
            sample.preloadAudioData = true;
            sample.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = sample;
            importer.forceToMono = false;
            importer.loadInBackground = streaming;
            importer.SaveAndReimport();
        }

        private static void MakeRegionPrefab(string area, MusicProfile profile)
        {
            string path = PrefabFolder + "/" + area + "_MusicRegion.prefab";
            bool exists = File.Exists(path);
            GameObject go = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(area + "_MusicRegion");
            try
            {
                MusicRegion region = go.GetComponent<MusicRegion>();
                if (region == null) region = go.AddComponent<MusicRegion>();
                var serialized = new SerializedObject(region);
                serialized.FindProperty("_profile").objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [MenuItem("Tools/规则游戏/音频/② 创建三地区域音乐试听场景")]
        public static void CreateLab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(LabPath))
            {
                Debug.Log("[AreaMusic] 试听场景已存在，保留已有编辑：" + LabPath);
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var runtime = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePath), scene);
            PlayerController player = runtime.GetComponentInChildren<PlayerController>(true);
            player.transform.position = new Vector3(-20f, 2f, 0f);
            var floor = new GameObject("MusicLab_Ground", typeof(BoxCollider2D), typeof(SpriteRenderer));
            floor.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/Stone/StoneSquare.png");
            floor.GetComponent<SpriteRenderer>().drawMode = SpriteDrawMode.Tiled;
            floor.GetComponent<SpriteRenderer>().size = new Vector2(80f, 2f);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(floor, scene);
            floor.layer = LayerMask.NameToLayer("Ground");
            floor.transform.position = new Vector3(0f, -1f, 0f);
            floor.GetComponent<BoxCollider2D>().size = new Vector2(80f, 2f);
            // 三个连续矩形，用真实 PlayerController 左右行走；轮盘暂停走原来的输入。
            string[] areas = { "Bricks", "Clockwork", "Fluffy" };
            for (int i = 0; i < areas.Length; i++)
            {
                var zone = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + areas[i] + "_MusicRegion.prefab"), scene);
                zone.transform.position = new Vector3((i - 1) * 20f, 0f, 0f);
                var serialized = new SerializedObject(zone.GetComponent<MusicRegion>());
                serialized.FindProperty("_size").vector2Value = new Vector2(20f, 40f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var label = new GameObject(areas[i] + "_Label", typeof(TextMesh));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(label, scene);
                label.transform.position = new Vector3((i - 1) * 20f, 4f, 0f);
                TextMesh text = label.GetComponent<TextMesh>();
                text.text = areas[i] + "\nWalk left/right; open rule wheel to duck music";
                text.characterSize = 0.12f;
                text.anchor = TextAnchor.MiddleCenter;
            }
            EditorSceneManager.SaveScene(scene, LabPath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            Debug.Log("[AreaMusic] 试听场景已生成：" + LabPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
