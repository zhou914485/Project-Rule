using System;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.UI.Launch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RuleGame.Editor.UI
{
    public static class LaunchUISetup
    {
        public const string LayoutPath = "Assets/_Project/UI/Launch/";
        public const string BootPath = "Assets/_Project/Scenes/Boot.unity";
        private const string RuntimePath = "Assets/_Project/Prefabs/LevelRuntime.prefab";
        private static readonly string[] BossLabs = { "BrickBoss", "ClockBoss", "RabbitBoss", "BatBoss" };

        [MenuItem("Tools/规则游戏/UI/接入 Boss 测试场景生命能量条")]
        public static void InstallBossLabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            // 在写入任何场景前检查全部目标，避免覆盖编辑器中尚未保存的调整。
            foreach (string boss in BossLabs)
            {
                var scene = SceneManager.GetSceneByPath(BossLabPath(boss));
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    throw new InvalidOperationException(boss + " 场景有未保存修改，请先保存。");
            }
            var previous = SceneManager.GetActiveScene();
            try
            {
                foreach (string boss in BossLabs)
                {
                    string path = BossLabPath(boss);
                    var scene = SceneManager.GetSceneByPath(path);
                    bool openedHere = !scene.IsValid() || !scene.isLoaded;
                    if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        PlayerController player = Find<PlayerController>(scene);
                        if (player == null) throw new InvalidOperationException(boss + " 场景缺少玩家。");
                        EnsurePlayerVitals(scene, player.GetComponent<EntityStatus>());
                        EditorSceneManager.SaveScene(scene);
                        Debug.Log("[BossVitals] 已接入并验证玩家引用：" + path);
                    }
                    finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
                }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        /// <summary>供白盒生成、接线修复和玩家替换复用，始终绑定当前场景的玩家。</summary>
        public static void EnsurePlayerVitals(UnityEngine.SceneManagement.Scene scene, EntityStatus status)
        {
            if (status == null || status.gameObject.scene != scene)
                throw new InvalidOperationException("生命/能量条必须绑定同场景的玩家状态。");
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(LayoutPath + "VitalsPanelSettings.asset");
            VisualTreeAsset layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath + "PlayerVitals.uxml");
            if (panel == null || layout == null) throw new InvalidOperationException("请先接入生命/能量条公共资源。");
            PlayerVitalsPresenter presenter = Find<PlayerVitalsPresenter>(scene);
            if (presenter == null)
            {
                var host = new GameObject("Player Vitals");
                SceneManager.MoveGameObjectToScene(host, scene);
                Document(host, panel, "PlayerVitals.uxml");
                presenter = host.AddComponent<PlayerVitalsPresenter>();
            }
            else Document(presenter.gameObject, panel, "PlayerVitals.uxml");
            Set(presenter, "_status", status);
            var reference = new SerializedObject(presenter);
            if (reference.FindProperty("_status").objectReferenceValue != status)
                throw new InvalidOperationException("生命/能量条玩家状态引用写入失败：" + scene.path);
        }

        private static string BossLabPath(string boss) => "Assets/_Project/Scenes/EnemyLabs/Test_Enemy_" + boss + ".unity";

        [MenuItem("Tools/规则游戏/UI/接入开始菜单与生命能量条")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Project/UI/Art/Launch" }))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = importer.assetPath.EndsWith("TitleScreen.png") ? 8192 : 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }

            PanelSettings hudPanel = Panel("VitalsPanelSettings", 10);
            PanelSettings menuPanel = Panel("MenuPanelSettings", 1000);
            GameObject runtime = PrefabUtility.LoadPrefabContents(RuntimePath);
            try
            {
                EntityStatus status = runtime.GetComponentInChildren<PlayerController>(true).GetComponent<EntityStatus>();
                Transform existing = runtime.transform.Find("Player Vitals");
                GameObject host = existing != null ? existing.gameObject : new GameObject("Player Vitals");
                host.transform.SetParent(runtime.transform, false);
                Document(host, hudPanel, "PlayerVitals.uxml");
                PlayerVitalsPresenter presenter = host.GetComponent<PlayerVitalsPresenter>() ?? host.AddComponent<PlayerVitalsPresenter>();
                Set(presenter, "_status", status);
                PrefabUtility.SaveAsPrefabAsset(runtime, RuntimePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(runtime); }

            UnityEngine.SceneManagement.Scene previous = SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.Scene boot = SceneManager.GetSceneByPath(BootPath);
            bool openedHere = !boot.IsValid() || !boot.isLoaded;
            if (!openedHere && boot.isDirty) throw new InvalidOperationException("Boot 场景有未保存修改，请先保存。");
            if (openedHere) boot = EditorSceneManager.OpenScene(BootPath, OpenSceneMode.Additive);
            try
            {
                GameRoot gameRoot = Find<GameRoot>(boot);
                BootFlowStarter starter = Find<BootFlowStarter>(boot);
                if (gameRoot == null || starter == null) throw new InvalidOperationException("Boot 缺少 GameRoot 或 BootFlowStarter。");
                MainMenuPresenter menu = Find<MainMenuPresenter>(boot);
                GameObject host;
                if (menu == null)
                {
                    host = new GameObject("Main Menu");
                    SceneManager.MoveGameObjectToScene(host, boot);
                    Document(host, menuPanel, "MainMenu.uxml");
                    menu = host.AddComponent<MainMenuPresenter>();
                }
                else Document(menu.gameObject, menuPanel, "MainMenu.uxml");
                Set(menu, "_gameRoot", gameRoot);
                Set(menu, "_player", gameRoot.GetComponentInChildren<PlayerController>(true));
                var data = new SerializedObject(starter);
                data.FindProperty("_startAutomatically").boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(boot);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(boot, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LaunchUI] 已接入 Boot 开始菜单和 LevelRuntime 生命/能量条。");
        }

        private static PanelSettings Panel(string name, int order)
        {
            string path = LayoutPath + name + ".asset";
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, path);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/_Project/UI/P4/P4Theme.tss");
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = .5f;
            panel.sortingOrder = order;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        private static void Document(GameObject host, PanelSettings panel, string layout)
        {
            UIDocument document = host.GetComponent<UIDocument>() ?? host.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath + layout);
        }

        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Find<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }
    }
}
