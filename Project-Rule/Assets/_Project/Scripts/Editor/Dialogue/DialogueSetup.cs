using System;
using RuleGame.Core.Rules;
using RuleGame.Development.Dialogue;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Infrastructure.Rules;
using RuleGame.UI.Dialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using FontAsset = UnityEngine.TextCore.Text.FontAsset;
using AtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;
using UnityEngine.UIElements;
using Yarn.Unity;

namespace RuleGame.Editor.Dialogue
{
    /// <summary>生成可复用的对话 Prefab 与独立试播场景；已有资源、用户关卡不会被覆盖。</summary>
    public static class DialogueSetup
    {
        public const string ContentPath = "Assets/_Project/Data/Dialogue/DialogueContent.asset";
        public const string ProjectPath = "Assets/_Project/Data/Dialogue/NpcBarks.yarnproject";
        public const string PrefabPath = "Assets/_Project/UI/Dialogue/DialogueSystem.prefab";
        public const string LabScenePath = "Assets/_Project/Scenes/DialogueLab.unity";
        private const string PanelPath = "Assets/_Project/UI/Dialogue/DialoguePanelSettings.asset";

        [MenuItem("RuleGame/Dialogue/Create Assets and Lab")]
        public static void Create()
        {
            EnsureFont();
            YarnProject project = AssetDatabase.LoadAssetAtPath<YarnProject>(ProjectPath);
            if (project == null || project.compiledYarnProgram == null || project.compiledYarnProgram.Length == 0)
                throw new InvalidOperationException("先等待 Yarn 导入成功，再创建对话资源。");
            DialogueContent content = AssetDatabase.LoadAssetAtPath<DialogueContent>(ContentPath);
            if (content == null)
            {
                content = ScriptableObject.CreateInstance<DialogueContent>();
                var fields = new SerializedObject(content);
                fields.FindProperty("_project").objectReferenceValue = project;
                SerializedProperty scripts = fields.FindProperty("_scripts");
                scripts.arraySize = 1;
                scripts.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Data/Dialogue/NpcBarks.yarn");
                SerializedProperty speakers = fields.FindProperty("_speakers");
                speakers.arraySize = 3;
                string[] ids = { "observer", "caretaker", "archivist" };
                string[] names = { "路边的人", "看门的人", "记事的人" };
                string[] entries = { "Observer", "Caretaker", "Archivist" };
                for (int index = 0; index < ids.Length; index++)
                {
                    SerializedProperty speaker = speakers.GetArrayElementAtIndex(index);
                    speaker.FindPropertyRelative("Id").stringValue = ids[index];
                    speaker.FindPropertyRelative("DisplayName").stringValue = names[index];
                    speaker.FindPropertyRelative("EntryNode").stringValue = entries[index];
                    speaker.FindPropertyRelative("FallbackNode").stringValue = entries[index] + "Fallback";
                }
                fields.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(content, ContentPath);
            }
            var errors = DialogueContentValidator.Validate(content);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1280, 720);
                panel.match = 0.5f;
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/_Project/UI/Dialogue/DialogueTheme.tss");
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) CreatePrefab(content, panel);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LabScenePath) == null) CreateLab(panel);
            Debug.Log("[Dialogue] 资源已准备。DialogueSystem.prefab 可拖入正式场景；DialogueLab 可独立试播。");
        }

        private static void EnsureFont()
        {
            const string fontPath = "Assets/_Project/UI/Dialogue/DialogueChineseFont.asset";
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(fontPath) != null) return;
            Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf");
            if (source == null) throw new InvalidOperationException("缺少随台词模块分发的 Noto CJK 字体，请保留原字体和 OFL 许可。");
            // 独立资产避免引用尚未合并的白盒字体，也不会修改正在编辑的 P4 字形缓存。
            FontAsset asset = FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = "DialogueChineseFont";
            AssetDatabase.CreateAsset(asset, fontPath);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (Texture2D atlas in asset.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static void CreatePrefab(DialogueContent content, PanelSettings panel)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = new GameObject("Dialogue System");
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                root.AddComponent<DialogueWorldContext>();
                DialogueRunner runner = root.AddComponent<DialogueRunner>();
                runner.autoStart = false;
                runner.SetProject(content.Project);
                DialogueHost host = root.AddComponent<DialogueHost>();
                SetReference(host, "_content", content);
                UIDocument document = root.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Dialogue/Dialogue.uxml");
                document.sortingOrder = 100;
                DialogueToolkitPresenter presenter = root.AddComponent<DialogueToolkitPresenter>();
                SetReference(presenter, "_runner", runner);
                runner.DialoguePresenters = new[] { presenter };
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static void CreateLab(PanelSettings panel)
        {
            RuleCatalog catalog = CreateLabCatalog();
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera));
                Camera camera = cameraObject.GetComponent<Camera>();
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.86f, 0.86f, 0.82f);
                RuleSystemDriver driver = new GameObject("P2 Rules - lab only").AddComponent<RuleSystemDriver>();
                SetReference(driver, "_catalog", catalog);
                GameObject system = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                var controls = new GameObject("Dialogue Lab Controls", typeof(UIDocument));
                UIDocument document = controls.GetComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Dialogue/DialogueLab.uxml");
                DialogueLabControls buttons = controls.AddComponent<DialogueLabControls>();
                SetReference(buttons, "_host", system.GetComponent<DialogueHost>());
                EditorSceneManager.SaveScene(scene, LabScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static RuleCatalog CreateLabCatalog()
        {
            const string path = "Assets/_Project/Data/Dialogue/DialogueLabRules.asset";
            RuleCatalog catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(path);
            if (catalog != null) return catalog;
            catalog = ScriptableObject.CreateInstance<RuleCatalog>();
            var fields = new SerializedObject(catalog);
            SerializedProperty rules = fields.FindProperty("_rules");
            rules.arraySize = 5;
            string[] names = { "Revert", "Nullify", "Constant", "Enhance", "Weaken" };
            for (int index = 0; index < names.Length; index++)
            {
                RuleDef definition = AssetDatabase.LoadAssetAtPath<RuleDef>("Assets/_Project/Data/Rules/Gravity" + names[index] + ".asset");
                if (definition == null) throw new InvalidOperationException("缺少 P2 重力定义：" + names[index]);
                rules.GetArrayElementAtIndex(index).objectReferenceValue = definition;
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(catalog, path);
            return catalog;
        }

        private static void SetReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var fields = new SerializedObject(target);
            fields.FindProperty(property).objectReferenceValue = value;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("RuleGame/Dialogue/Validate Selected Content")]
        public static void ValidateSelected()
        {
            DialogueContent content = Selection.activeObject as DialogueContent;
            var errors = DialogueContentValidator.Validate(content);
            foreach (string error in errors) Debug.LogError("[Dialogue] " + error);
            if (errors.Count == 0) Debug.Log("[Dialogue] 内容校验通过。场景物件绑定会在 Host 启动时另外校验。");
        }
    }
}
