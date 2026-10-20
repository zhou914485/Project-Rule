using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using RuleGame.Gameplay.Scene;
using RuleGame.Presentation.SceneComponents;

namespace RuleGame.Editor.Level
{
    /// <summary>把空秋千和带人的 7 帧导入，并打包成 SwingCheckpoint 预制体。</summary>
    public static class SwingCheckpointArtSetup
    {
        public const string Folder = "Assets/_Project/Art/Sprites/Checkpoints";
        public const string PrefabFolder = "Assets/_Project/Prefabs/Checkpoints";
        public const string PrefabPath = PrefabFolder + "/SwingCheckpoint.prefab";
        private const string MaterialPath = "Assets/_Project/Data/Enemies/EnemySprite_Mat.mat";
        private const float PixelsPerUnit = 282f;

        [MenuItem("Tools/规则游戏/落点/③ 生成/更新秋千存档点预制体", false, 62)]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");

            AssetDatabase.Refresh();
            Require("Swing_Empty.png");
            Require("Swing_Sit_01.png");

            Rect empty = AlphaRect("Swing_Empty");
            Rect sit = Union(Enumerable.Range(1, 7).Select(i => "Swing_Sit_" + i.ToString("00")).ToArray());
            float centerX = empty.x + empty.width * .5f;

            Import("Swing_Empty", empty, new Vector2(.5f, 0f));
            for (int i = 1; i <= 7; i++)
                Import("Swing_Sit_" + i.ToString("00"), sit, new Vector2((centerX - sit.x) / sit.width, 0f));

            BuildPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SwingCheckpoint] 空秋千 + 7 帧坐姿已导入，SwingCheckpoint.prefab 已更新。");
        }

        private static void BuildPrefab()
        {
            Directory.CreateDirectory(PrefabFolder);
            AssetDatabase.Refresh();

            bool exists = File.Exists(PrefabPath);
            var root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject("SwingCheckpoint");
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                foreach (var component in root.GetComponents<Component>())
                    if (!(component is Transform)) UnityEngine.Object.DestroyImmediate(component);

                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                root.layer = 0;

                var point = root.AddComponent<PlayerSpawnPoint>();
                var serialized = new SerializedObject(point);
                serialized.FindProperty("_id").stringValue = "checkpoint";
                serialized.FindProperty("_facing").intValue = 1;
                serialized.FindProperty("_applyWhenStandalone").boolValue = true;
                serialized.FindProperty("_activateByAttack").boolValue = true;
                serialized.FindProperty("_reactivate").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var trigger = root.AddComponent<BoxCollider2D>();
                trigger.isTrigger = true;
                trigger.size = new Vector2(2f, 2f);
                trigger.offset = new Vector2(0f, .5f);

                var fade = root.AddComponent<SwingCheckpointFade>();
                fade.Alpha = 0f;

                var art = new GameObject("Art");
                art.transform.SetParent(root.transform, false);
                art.transform.localPosition = new Vector3(0f, -.5f, 0f);

                var empty = CreateRenderer(art.transform, "EmptySwing", Load("Swing_Empty"), 1);
                var sit = CreateRenderer(art.transform, "SitArt", Load("Swing_Sit_01"), 2);
                sit.transform.localScale = new Vector3(.8f, .8f, 1f);
                sit.enabled = false;

                var anchor = new GameObject("CutsceneAnchor");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = Vector3.zero;

                var cutscene = root.AddComponent<SwingCheckpointCutscene>();
                cutscene.SpawnPoint = point;
                cutscene.EmptySwing = empty;
                cutscene.SitArt = sit;
                cutscene.SitFrames = Enumerable.Range(1, 7).Select(i => Load("Swing_Sit_" + i.ToString("00"))).ToArray();
                cutscene.CutsceneAnchor = anchor.transform;
                cutscene.Fade = fade;
                cutscene.FramesPerSecond = 8f;
                cutscene.FadeSeconds = .15f;
                cutscene.CameraSettleSeconds = .2f;
                cutscene.RequireGrounded = true;
                cutscene.LoopUntilInput = true;

                if (exists) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                else PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer CreateRenderer(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = order;
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            return renderer;
        }

        private static void Require(string file)
        {
            if (!File.Exists(Folder + "/" + file))
                throw new InvalidOperationException("缺少交付图：" + Folder + "/" + file);
        }

        private static void Import(string name, Rect rect, Vector2 pivot)
        {
            string path = Folder + "/" + name + ".png";
            Require(name + ".png");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("无法读取纹理导入器：" + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("该导入器不支持 Sprite 数据：" + path);
            provider.InitSpriteEditorDataProvider();

            var existing = provider.GetSpriteRects().FirstOrDefault(x => x.name == name);
            var sprite = new SpriteRect
            {
                name = name,
                rect = rect,
                alignment = SpriteAlignment.Custom,
                pivot = pivot,
                spriteID = existing != null ? existing.spriteID : GUID.Generate()
            };
            provider.SetSpriteRects(new[] { sprite });

            var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (ids == null) throw new InvalidOperationException("无法维护 Sprite 稳定引用：" + path);
            ids.SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(name, sprite.spriteID) });
            provider.Apply();

            importer.userData = "RuleGame.SwingCheckpoint.v1:" + Hash(path);
            importer.SaveAndReimport();
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static Sprite Load(string name)
        {
            string path = Folder + "/" + name + ".png";
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(x => x.name == name)
                ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) throw new InvalidOperationException("缺少已导入精灵：" + path);
            return sprite;
        }

        private static Rect Union(params string[] names)
        {
            Rect result = AlphaRect(names[0]);
            for (int i = 1; i < names.Length; i++)
            {
                Rect next = AlphaRect(names[i]);
                result = Rect.MinMaxRect(Mathf.Min(result.xMin, next.xMin), Mathf.Min(result.yMin, next.yMin),
                    Mathf.Max(result.xMax, next.xMax), Mathf.Max(result.yMax, next.yMax));
            }
            return result;
        }

        private static Rect AlphaRect(string name)
        {
            string path = Folder + "/" + name + ".png";
            Require(name + ".png");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("读取 PNG 失败：" + path);
                var pixels = texture.GetPixels32();
                int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                    {
                        if (pixels[y * texture.width + x].a == 0) continue;
                        minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                    }
                if (maxX < minX) throw new InvalidOperationException("图片全透明：" + path);
                return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
