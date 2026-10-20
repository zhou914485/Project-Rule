using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Presentation.SceneComponents;

namespace RuleGame.Editor.Level
{
    /// <summary>把「功能组件」交付图接到积木城预制体：只换 Visual / 加表现组件，不改碰撞与机关参数。</summary>
    /// <remarks>
    /// <para>原始 PNG 逐字节复制进 <c>Art/Sprites/Components</c>，只通过 SpriteRect 裁掉透明画布；
    /// 序列（按钮 / 摇杆 / 飞机 / 泡泡）用并集裁切框，避免切帧时基座或气泡位置跳动。</para>
    /// <para>不处理荆棘、石中剑、石头人和三项未定素材。</para>
    /// </remarks>
    public static class CityArtSetup
    {
        public const string Folder = "Assets/_Project/Art/Sprites/Components";
        private const string PrefabFolder = "Assets/_Project/Prefabs/BrickCity";
        private const string MaterialPath = "Assets/_Project/Data/Enemies/EnemySprite_Mat.mat";
        private const string Revision = "RuleGame.CityArt.v1:";

        [MenuItem("Tools/规则游戏/积木城/接入功能组件交付美术", false, 91)]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");

            AssetDatabase.Refresh();
            Require("CityButton_Off.png");
            Require("CityChest.png");

            Rect button = Union("CityButton_Off", "CityButton_On");
            Rect lever = Union("CityLever_01", "CityLever_02", "CityLever_03", "CityLever_04", "CityLever_05");
            Rect plane = Union("CityLift_Plane_01", "CityLift_Plane_02");
            Rect bubble = Union("CityBubble", "CityBubble_Burst");
            Rect marble = Union("CityMarble_Red", "CityMarble_Blue", "CityMarble_Green");
            Rect reward = Union("CityReward_Health", "CityReward_Energy", "CityReward_Power");
            Rect chest = AlphaRect("CityChest");
            Rect train = AlphaRect("CityTrain");
            Rect refill = AlphaRect("CityRefill");
            Rect hammerBase = AlphaRect("CityHammer_Base");
            Rect hammerGlove = AlphaRect("CityHammer_Glove");
            Rect hammerSpring = AlphaRect("CityHammer_Spring");

            Import("CityButton_Off", button, 116f, new Vector2(.5f, 0f));
            Import("CityButton_On", button, 116f, new Vector2(.5f, 0f));
            for (int i = 1; i <= 5; i++)
                Import("CityLever_" + i.ToString("00"), lever, 134f, BasePivot("CityLever_" + i.ToString("00"), lever));
            Import("CityChest", chest, 145f, new Vector2(.5f, 0f));
            Import("CityMarble_Red", marble, 90f, new Vector2(.5f, .5f));
            Import("CityMarble_Blue", marble, 90f, new Vector2(.5f, .5f));
            Import("CityMarble_Green", marble, 90f, new Vector2(.5f, .5f));
            Import("CityLift_Plane_01", plane, 146f, new Vector2(.5f, .5f));
            Import("CityLift_Plane_02", plane, 146f, new Vector2(.5f, .5f));
            Import("CityTrain", train, 211f, new Vector2(.5f, .5f));
            Import("CityReward_Health", reward, 122f, new Vector2(.5f, .5f));
            Import("CityReward_Energy", reward, 122f, new Vector2(.5f, .5f));
            Import("CityReward_Power", reward, 122f, new Vector2(.5f, .5f));
            Import("CityBubble", bubble, 130f, new Vector2(.5f, .5f));
            Import("CityBubble_Burst", bubble, 130f, new Vector2(.5f, .5f));
            Import("CityHammer_Base", hammerBase, 130f, new Vector2(.5f, .5f));
            Import("CityHammer_Glove", hammerGlove, 130f, new Vector2(.5f, .5f));
            Import("CityHammer_Spring", hammerSpring, 130f, new Vector2(.5f, .5f));
            Import("CityRefill", refill, 282f, new Vector2(.5f, 0f));

            UpdateButton();
            UpdateLever();
            UpdateChest();
            UpdateMarble();
            UpdateLift();
            UpdateTrain();
            UpdateReward();
            UpdateBubble();
            UpdateHammer();
            UpdateRefill();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CityArt] 功能组件交付图已接入 10 个积木城预制体（荆棘 / 石中剑 / 石头人未动）。");
        }

        // ------------------------------------------------------------------
        //  预制体
        // ------------------------------------------------------------------

        private static void UpdateButton()
        {
            EditPrefab("CityButton", root =>
            {
                var visual = Visual(root);
                SetBottom(root, visual, Load("CityButton_Off"));
                var toggle = root.GetComponent<CitySwitch>();
                toggle.KeepArtColor = true;
                var view = root.GetComponent<CitySwitchSpriteView>() ?? root.AddComponent<CitySwitchSpriteView>();
                view.Switch = toggle;
                view.Target = visual;
                view.Frames = new[] { Load("CityButton_Off"), Load("CityButton_On") };
                view.FramesPerSecond = 30f;
            });
        }

        private static void UpdateLever()
        {
            EditPrefab("CityLever", root =>
            {
                var visual = Visual(root);
                SetBottom(root, visual, Load("CityLever_01"));
                var toggle = root.GetComponent<CitySwitch>();
                toggle.KeepArtColor = true;
                var serialized = new SerializedObject(toggle);
                serialized.FindProperty("_handle").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var view = root.GetComponent<CitySwitchSpriteView>() ?? root.AddComponent<CitySwitchSpriteView>();
                view.Switch = toggle;
                view.Target = visual;
                view.Frames = new[] { Load("CityLever_01"), Load("CityLever_02"), Load("CityLever_03"), Load("CityLever_04"), Load("CityLever_05") };
                view.FramesPerSecond = 22f;
            });
        }

        private static void UpdateChest()
        {
            EditPrefab("CityChest", root => SetBottom(root, Visual(root), Load("CityChest")));
        }

        private static void UpdateMarble()
        {
            EditPrefab("CityMarble", root => SetCenter(root, Visual(root), Load("CityMarble_Red")));
        }

        private static void UpdateLift()
        {
            EditPrefab("CityLift", root =>
            {
                var visual = Visual(root);
                SetTop(root, visual, Load("CityLift_Plane_01"));
                var flip = root.GetComponent<SpriteFlipbook>() ?? root.AddComponent<SpriteFlipbook>();
                flip.Target = visual;
                flip.Frames = new[] { Load("CityLift_Plane_01"), Load("CityLift_Plane_02") };
                flip.FramesPerSecond = 14f;
            });
        }

        private static void UpdateTrain()
        {
            EditPrefab("CityTrain", root => SetTop(root, Visual(root), Load("CityTrain")));
        }

        private static void UpdateReward()
        {
            EditPrefab("CityReward", root =>
            {
                var visual = Visual(root);
                // 战斗力那条奖励已删（2026-10-20），预制体默认 Kind = 生命，默认贴图跟着换。
                SetCenter(root, visual, Load("CityReward_Health"));
                var source = root.GetComponent<CityRewardSource>();
                var view = root.GetComponent<CityRewardSpriteView>() ?? root.AddComponent<CityRewardSpriteView>();
                view.Source = source;
                view.Target = visual;
                view.Health = Load("CityReward_Health");
                view.Energy = Load("CityReward_Energy");
            });
        }

        private static void UpdateBubble()
        {
            EditPrefab("CityBubble", root =>
            {
                var visual = Visual(root);
                SetTop(root, visual, Load("CityBubble"));
                var bubble = root.GetComponent<CityBubble>();
                var view = root.GetComponent<CityBubbleSpriteView>() ?? root.AddComponent<CityBubbleSpriteView>();
                view.Bubble = bubble;
                view.Target = visual;
                view.Intact = Load("CityBubble");
                view.Burst = Load("CityBubble_Burst");
            });
        }

        private static void UpdateHammer()
        {
            EditPrefab("CityHammer", root =>
            {
                var oldVisual = root.transform.Find("Visual");
                Material material = null;
                if (oldVisual != null)
                {
                    var oldRenderer = oldVisual.GetComponent<SpriteRenderer>();
                    if (oldRenderer != null) material = oldRenderer.sharedMaterial;
                }
                if (material == null) material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    string childName = root.transform.GetChild(i).name;
                    if (childName == "Visual" || childName == "Spring" || childName == "Glove" || childName == "Base")
                        UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                }

                var container = new GameObject("Visual");
                container.transform.SetParent(root.transform, false);

                var hammer = root.GetComponent<CityHammer>();
                hammer.KeepArtColor = true;

                CreatePart(container.transform, "Base", Load("CityHammer_Base"), new Vector2(1.2f, .4f), new Vector3(0f, .38f, 0f), 1, material);
                CreatePart(container.transform, "Spring", Load("CityHammer_Spring"), new Vector2(.3f, .6f), new Vector3(0f, 0f, 0f), 0, material);
                CreatePart(container.transform, "Glove", Load("CityHammer_Glove"), new Vector2(.55f, .55f), new Vector3(0f, -.28f, 0f), 2, material);
            });
        }

        private static void UpdateRefill()
        {
            EditPrefab("CityRefill", root => SetBottom(root, Visual(root), Load("CityRefill")));
        }

        // ------------------------------------------------------------------
        //  预制体辅助
        // ------------------------------------------------------------------

        private static SpriteRenderer Visual(GameObject root)
        {
            var child = root.transform.Find("Visual");
            if (child == null) throw new InvalidOperationException(root.name + " 没有 Visual 子物体");
            var renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null) throw new InvalidOperationException(root.name + "/Visual 没有 SpriteRenderer");
            return renderer;
        }

        private static void Prepare(SpriteRenderer renderer, Sprite sprite)
        {
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.drawMode = SpriteDrawMode.Simple;
            renderer.transform.localScale = Vector3.one;
        }

        private static void SetBottom(GameObject root, SpriteRenderer renderer, Sprite sprite)
        {
            Prepare(renderer, sprite);
            var box = root.GetComponent<BoxCollider2D>();
            renderer.transform.localPosition = new Vector3(box.offset.x, box.offset.y - box.size.y * .5f, 0f);
        }

        private static void SetCenter(GameObject root, SpriteRenderer renderer, Sprite sprite)
        {
            Prepare(renderer, sprite);
            renderer.transform.localPosition = Vector3.zero;
        }

        private static void SetTop(GameObject root, SpriteRenderer renderer, Sprite sprite)
        {
            Prepare(renderer, sprite);
            var box = root.GetComponent<BoxCollider2D>();
            float top = box.offset.y + box.size.y * .5f;
            renderer.transform.localPosition = new Vector3(box.offset.x, top - sprite.bounds.extents.y, 0f);
        }

        private static void ConfigurePart(SpriteRenderer renderer, Sprite sprite, Vector2 size, Vector3 at, int order)
        {
            Prepare(renderer, sprite);
            renderer.sortingOrder = order;
            renderer.transform.localPosition = at;
            renderer.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1f);
        }

        private static void CreatePart(Transform parent, string name, Sprite sprite, Vector2 size, Vector3 at, int order, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = material;
            ConfigurePart(renderer, sprite, size, at, order);
        }

        private static void EditPrefab(string name, Action<GameObject> edit)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            if (!File.Exists(path)) throw new InvalidOperationException("缺少预制体：" + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        //  导入
        // ------------------------------------------------------------------

        private static void Require(string file)
        {
            if (!File.Exists(Folder + "/" + file))
                throw new InvalidOperationException("缺少交付图：" + Folder + "/" + file);
        }

        private static void Import(string name, Rect rect, float ppu, Vector2 pivot)
        {
            string path = Folder + "/" + name + ".png";
            Require(name + ".png");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("无法读取纹理导入器：" + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
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

            importer.userData = Revision + Hash(path);
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

        /// <summary>摇杆的轴心取底座中心（底部 20% 的 alpha 横向中点），整段帧序列共用。</summary>
        private static Vector2 BasePivot(string name, Rect rect)
        {
            string path = Folder + "/" + name + ".png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("读取 PNG 失败：" + path);
                var pixels = texture.GetPixels32();
                int band = Mathf.Max(1, Mathf.RoundToInt(rect.height * .2f));
                int minX = texture.width, maxX = -1;
                for (int y = (int)rect.yMin; y < Mathf.Min(texture.height, (int)rect.yMin + band); y++)
                    for (int x = (int)rect.xMin; x < (int)rect.xMax; x++)
                    {
                        if (pixels[y * texture.width + x].a == 0) continue;
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    }
                if (maxX < minX) return new Vector2(.5f, 0f);
                float center = (minX + maxX + 1) * .5f;
                return new Vector2((center - rect.x) / rect.width, 0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
