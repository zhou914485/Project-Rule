using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using RuleGame.Development.P4;
using RuleGame.UI.P4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RuleGame.Editor.P4
{
    /// <summary>
    /// 首次创建 P4 白盒与 UI 资源的编辑器工具。已有场景不覆盖，手工调整保留在场景资产中。
    /// </summary>
    public static class P4DemoSceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Level_01.unity";
        private const string UiPath = "Assets/_Project/UI/P4/";
        private const string FontPath = "Assets/_Project/UI/Fonts/";
        private const string SquarePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string MaterialPath = "Assets/_Project/Art/Materials/P4Whitebox.mat";

        [MenuItem("RuleGame/P4/Open Level 01")]
        public static void OpenScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(ScenePath);
            }
        }

        [MenuItem("RuleGame/P4/Create Demo Assets")]
        public static void Build()
        {
            RuleCatalog gravityCatalog = EnsureGravityDemoCatalog();
            if (File.Exists(ScenePath))
            {
                Debug.Log("P4 Level_01 already exists. Open and edit the authored scene; creation does not overwrite it.");
                return;
            }

            CreateFont();
            // 动态字体创建后重新导入 UI，确保样式表中的字体引用已能解析。
            AssetDatabase.ImportAsset(UiPath + "P4Hud.uss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(UiPath + "P4Theme.tss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(UiPath + "P4Hud.uxml", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Sprite square = CreateSquare();
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null)
                {
                    throw new InvalidOperationException("The project's URP 2D unlit sprite shader is required.");
                }
                AssetDatabase.CreateAsset(new Material(shader), MaterialPath);
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(UiPath + "P4PanelSettings.asset");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1280, 720);
                panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panel.match = 0.5f;
                panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(UiPath + "P4Theme.tss");
                AssetDatabase.CreateAsset(panel, UiPath + "P4PanelSettings.asset");
            }

            Scene previous = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previous.path))
            {
                throw new InvalidOperationException("Open a saved scene before creating the P4 demo, so the current scene can be preserved.");
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                cameraObject.transform.position = new Vector3(10f, 2f, -10f);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 7.5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Hex("F2F1EB");

                var world = new GameObject("Whitebox - edit platforms in Scene view");
                Block(world.transform, square, "Ground_Left", new Vector2(2.5f, -1f), new Vector2(9f, 1f), Hex("21211F"), true);
                Block(world.transform, square, "Ground_Right", new Vector2(15.5f, -1f), new Vector2(12f, 1f), Hex("21211F"), true);
                Block(world.transform, square, "Step_01", new Vector2(5.5f, 0f), new Vector2(2f, 1f), Hex("21211F"), true);
                Block(world.transform, square, "Bridge_Platform", new Vector2(9f, 1f), new Vector2(3f, 0.45f), Hex("21211F"), true);
                Block(world.transform, square, "Upper_Platform", new Vector2(13f, 2f), new Vector2(3f, 0.45f), Hex("21211F"), true);
                Block(world.transform, square, "Ceiling_ForFutureGravityRules", new Vector2(10f, 5.5f), new Vector2(24f, 0.1f), Hex("C1C1B8"), true);
                Block(world.transform, square, "Left_Boundary", new Vector2(-2.5f, 2f), new Vector2(0.1f, 7f), Hex("C1C1B8"), true);
                Block(world.transform, square, "Right_Boundary", new Vector2(22f, 2f), new Vector2(0.1f, 7f), Hex("C1C1B8"), true);
                var markers = new GameObject("Markers");
                var spawn = new GameObject("PlayerSpawn - replace demo pawn with P3 player");
                spawn.transform.SetParent(markers.transform);
                spawn.transform.position = new Vector3(0f, 0.4f, 0f);
                Block(markers.transform, square, "SpawnMarker", new Vector2(0f, -0.46f), new Vector2(1.6f, 0.06f), Hex("21211F"), false);
                Block(markers.transform, square, "GapMarker", new Vector2(8f, -1.5f), new Vector2(2f, 0.04f), Hex("C1C1B8"), false);
                GameObject goal = Block(markers.transform, square, "GoalArea - connect to ILevelFlow later", new Vector2(20f, 0.9f), new Vector2(1.3f, 2.8f), Hex("21211F"), true);
                goal.GetComponent<BoxCollider2D>().isTrigger = true;
                Block(markers.transform, square, "GoalLintel", new Vector2(20f, 2.4f), new Vector2(1.8f, 0.12f), Hex("21211F"), false);

                var ui = new GameObject("P4 HUD");
                UIDocument document = ui.AddComponent<UIDocument>();
                document.panelSettings = panel;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiPath + "P4Hud.uxml");
                P4HudPresenter presenter = ui.AddComponent<P4HudPresenter>();

                var demoObject = new GameObject("P4 Demo - remove when integrating real systems");
                P4DemoController demo = demoObject.AddComponent<P4DemoController>();
                demo.Configure(presenter, AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>("Assets/_Project/UI/P4/Resources/P4RulePreviews.json"), gravityCatalog);
                goal.AddComponent<P4DemoGoal>().Configure(demo);
                GameObject pawn = Block(demoObject.transform, square, "DemoPawn", new Vector2(0f, 0.4f), new Vector2(0.8f, 1.5f), Hex("21211F"), true);
                AddEye(pawn.transform, square, -0.2f);
                AddEye(pawn.transform, square, 0.2f);
                Rigidbody2D body = pawn.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f;
                body.useFullKinematicContacts = true;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                pawn.AddComponent<P4DemoPawn>().Configure(demo, spawn.transform);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("P4 demo assets created: " + ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                {
                    SceneManager.SetActiveScene(previous);
                }
            }
        }

        public const string GravityCatalogPath = "Assets/_Project/UI/P4/Resources/P4GravityDemoCatalog.asset";

        /// <summary>单独引用已有五条重力定义，不修改 P2 的正式规则目录。</summary>
        public static RuleCatalog EnsureGravityDemoCatalog()
        {
            RuleCatalog catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(GravityCatalogPath);
            if (catalog != null)
            {
                return catalog;
            }
            var definitions = new RuleDef[5];
            string[] names = { "Revert", "Nullify", "Constant", "Enhance", "Weaken" };
            for (int index = 0; index < names.Length; index++)
            {
                definitions[index] = AssetDatabase.LoadAssetAtPath<RuleDef>("Assets/_Project/Data/Rules/Gravity" + names[index] + ".asset");
                if (definitions[index] == null || definitions[index].Attribute != AttributeId.Gravity
                    || (int)definitions[index].Relation != index)
                {
                    throw new InvalidOperationException("Missing or mismatched P2 gravity definition: " + names[index]);
                }
            }
            catalog = ScriptableObject.CreateInstance<RuleCatalog>();
            var serialized = new SerializedObject(catalog);
            SerializedProperty rules = serialized.FindProperty("_rules");
            rules.arraySize = definitions.Length;
            for (int index = 0; index < definitions.Length; index++)
            {
                rules.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(catalog, GravityCatalogPath);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static void CreateFont()
        {
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(FontPath + "P4ChineseFont.asset") != null)
            {
                return;
            }
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath + "NotoSansCJKsc-Regular.otf");
            if (font == null)
            {
                throw new InvalidOperationException("The licensed Noto CJK source font must be imported first.");
            }
            FontAsset asset = FontAsset.CreateFontAsset(font, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = "P4ChineseFont";
            AssetDatabase.CreateAsset(asset, FontPath + "P4ChineseFont.asset");
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (Texture2D atlas in asset.atlasTextures)
            {
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static Sprite CreateSquare()
        {
            if (!File.Exists(SquarePath))
            {
                var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var pixels = new Color[256];
                for (int index = 0; index < pixels.Length; index++)
                {
                    pixels[index] = Color.white;
                }
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(SquarePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(SquarePath, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(SquarePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 16f;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
        }

        private static GameObject Block(Transform parent, Sprite sprite, string name, Vector2 position, Vector2 size, Color color, bool solid)
        {
            var block = new GameObject(name, typeof(SpriteRenderer));
            block.transform.SetParent(parent);
            block.transform.position = position;
            block.transform.localScale = new Vector3(size.x, size.y, 1f);
            SpriteRenderer renderer = block.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            renderer.color = color;
            if (solid)
            {
                block.AddComponent<BoxCollider2D>().size = Vector2.one;
            }
            return block;
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            return color;
        }

        private static void AddEye(Transform pawn, Sprite square, float x)
        {
            var eye = new GameObject("Eye", typeof(SpriteRenderer));
            eye.transform.SetParent(pawn, false);
            eye.transform.localPosition = new Vector3(x, 0.23f, 0f);
            eye.transform.localScale = new Vector3(0.15f, 0.13f, 1f);
            SpriteRenderer renderer = eye.GetComponent<SpriteRenderer>();
            renderer.sprite = square;
            renderer.color = Hex("F2F1EB");
            renderer.sortingOrder = 2;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        }

    }
}
