using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Enemy;

namespace RuleGame.Editor.Enemy
{
    /// <summary>只接线四个 Boss 与独立白盒，不重建敌人参数、主角或正式关卡。</summary>
    public static class EnemyVfxSetup
    {
        private const string Art = "Assets/_Project/Art/Vfx";
        private const string Data = "Assets/_Project/Data/Enemies/Vfx";
        private static readonly string[] Names = { "Blow2", "Claw1", "Dark2", "Gun3", "Light1", "Light4", "Sword3" };
        private static readonly int[] First = { 2, 1, 1, 2, 2, 2, 5 };
        private static readonly int[] Count = { 64, 22, 49, 52, 30, 49, 62 };

        [MenuItem("RuleGame/Enemies/接入 Boss 作者特效（保留 AI 与参数）")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            Directory.CreateDirectory(Data); AssetDatabase.Refresh();
            for (int i = 0; i < Names.Length; i++)
            {
                string path = Art + "/Effekseer/" + Names[i] + ".png";
                ImportTexture(path, false);
                EnemyVfxClip clip = LoadOrCreate<EnemyVfxClip>(Data + "/" + Names[i] + ".asset");
                clip.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                clip.FirstFrame = First[i]; clip.FrameCount = Count[i]; clip.FramesPerSecond = 30f;
                EditorUtility.SetDirty(clip);
            }
            ImportTexture(Art + "/BeamTexture.png", false);
            ImportTexture(Art + "/ProjectileTexture.png", true);
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.ClockBoss, EnemyKind.RabbitBoss, EnemyKind.BatBoss })
            {
                EnemyVfxProfile profile = LoadOrCreate<EnemyVfxProfile>(Data + "/" + kind + "_Vfx.asset");
                profile.Accent = kind == EnemyKind.BatBoss ? new Color(.5f,.35f,.95f) :
                    kind == EnemyKind.RabbitBoss ? new Color(.95f,.25f,.6f) :
                    kind == EnemyKind.ClockBoss ? new Color(1f,.65f,.12f) : new Color(1f,.4f,.14f);
                profile.Secondary = Color.Lerp(profile.Accent, new Color(.16f,.1f,.21f), .25f);
                profile.Material = Material(kind + "_Sprite", profile.Accent, null);
                profile.BeamMaterial = Material(kind + "_Beam", profile.Accent,
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/BeamTexture.png"));
                profile.BeamCoreMaterial = Material(kind + "_Core", profile.Accent, null);
                profile.ProjectileSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "/ProjectileTexture.png");
                profile.Impact = Clip(kind == EnemyKind.BatBoss ? "Dark2" : "Sword3");
                profile.Slash = Clip("Claw1"); profile.Cast = Clip(kind == EnemyKind.BatBoss ? "Dark2" : "Gun3");
                profile.Charge = Clip("Light4"); profile.Death = Clip("Blow2"); profile.Shot = Clip("Light1");
                profile.ImpactSize = kind == EnemyKind.BrickBoss ? 6f : 4f;
                profile.SlashSize = 5.2f; profile.CastSize = kind == EnemyKind.ClockBoss ? 7f : 5.5f;
                profile.CameraImpulse = kind == EnemyKind.BrickBoss ? .12f : .07f;
                EditorUtility.SetDirty(profile);
                if (kind == EnemyKind.RabbitBoss)
                {
                    var animations = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>("Assets/_Project/Data/Enemies/RabbitBoss_Animations.asset");
                    if (animations != null) EnemyLabBuilder.ConfigureRabbitProjectiles(animations);
                }
                if (kind == EnemyKind.BrickBoss || kind == EnemyKind.ClockBoss)
                    EnemyDeliverySetup.Configure(kind, AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>(
                        "Assets/_Project/Data/Enemies/" + kind + "_Animations.asset"));
                string prefab = "Assets/_Project/Prefabs/Enemies/" + kind + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(prefab);
                try { Wire(root.GetComponent<EnemyActor>(), profile); PrefabUtility.SaveAsPrefabAsset(root, prefab); }
                finally { PrefabUtility.UnloadPrefabContents(root); }

                string scenePath = EnemyLabBuilder.SceneFolder + "/Test_Enemy_" + kind + ".unity";
                var scene = SceneManager.GetSceneByPath(scenePath);
                bool openedHere = !scene.IsValid() || !scene.isLoaded;
                if (openedHere) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    foreach (GameObject go in scene.GetRootGameObjects())
                    {
                        foreach (var actor in go.GetComponentsInChildren<EnemyActor>(true))
                            if (actor.Definition != null && actor.Definition.Kind == kind) Wire(actor, profile);
                        foreach (var camera in go.GetComponentsInChildren<Camera>(true))
                            if (camera.CompareTag("MainCamera") && camera.GetComponent<BossCameraImpulse>() == null)
                                camera.gameObject.AddComponent<BossCameraImpulse>();
                    }
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
                finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
            }
            AssetDatabase.SaveAssets();
            CalibrateClipBounds();
            Debug.Log("[BossVfx] 四个 Boss 与四个白盒已接线；无原生插件运行依赖。");
        }
        [MenuItem("RuleGame/Enemies/校准 Boss 特效可见范围（保留战斗参数）")]
        public static void CalibrateClipBounds()
        {
            foreach (string name in Names)
            {
                EnemyVfxClip clip = Clip(name);
                if (clip == null || clip.Atlas == null) continue;
                // 读原 PNG，不改 TextureImporter 的可读性或压缩设置。
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!image.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(clip.Atlas))))
                        throw new InvalidOperationException("无法读取特效图集：" + name);
                    Color32[] pixels = image.GetPixels32();
                    int width = image.width / clip.Grid.x, height = image.height / clip.Grid.y;
                    int minX = width, minY = height, maxX = 0, maxY = 0;
                    for (int frame = clip.FirstFrame; frame < Mathf.Min(clip.FrameCount, clip.Grid.x * clip.Grid.y); frame++)
                    {
                        int left = frame % clip.Grid.x * width;
                        int bottom = (clip.Grid.y - 1 - frame / clip.Grid.x) * height;
                        for (int y = 0; y < height; y++)
                            for (int x = 0; x < width; x++)
                                if (pixels[(bottom + y) * image.width + left + x].a >= 40)
                                {
                                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                                    maxX = Mathf.Max(maxX, x + 1); maxY = Mathf.Max(maxY, y + 1);
                                }
                    }
                    if (maxX <= minX || maxY <= minY) throw new InvalidOperationException("特效没有有效像素：" + name);
                    Rect visible = Rect.MinMaxRect(minX / (float)width, minY / (float)height,
                        maxX / (float)width, maxY / (float)height);
                    if (clip.VisibleRect == visible) continue;
                    clip.VisibleRect = visible;
                    EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip);
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
        }
        private static void Wire(EnemyActor actor, EnemyVfxProfile profile)
        {
            var presenter = actor.GetComponent<EnemyVfxPresenter>();
            if (presenter == null) presenter = actor.gameObject.AddComponent<EnemyVfxPresenter>();
            presenter.Profile = profile;
            var hands = actor.GetComponent<ClockHandView>();
            if (hands != null && hands.Hands.Length > 0) actor.BeamOrigin = hands.Hands[0];
            EditorUtility.SetDirty(actor); EditorUtility.SetDirty(presenter);
            if (PrefabUtility.IsPartOfPrefabInstance(actor))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(presenter);
            }
        }
        private static EnemyVfxClip Clip(string name) => AssetDatabase.LoadAssetAtPath<EnemyVfxClip>(Data + "/" + name + ".asset");
        private static Material Material(string name, Color palette, Texture2D texture)
        {
            string path = Data + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("RuleGame/Enemy/VfxSprite");
                if (shader == null) throw new InvalidOperationException("缺少 EnemyVfxSprite.shader。");
                material = new Material(shader); AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_Accent", palette); material.SetFloat("_Recolor", .88f);
            bool sprite = name.EndsWith("_Sprite", StringComparison.Ordinal);
            material.SetFloat("_Sprite", sprite ? 1f : 0f);
            if (sprite) material.EnableKeyword("_VFX_SPRITE"); else material.DisableKeyword("_VFX_SPRITE");
            if (texture != null) material.mainTexture = texture;
            EditorUtility.SetDirty(material); return material;
        }
        private static void ImportTexture(string path, bool sprite)
        {
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少烘焙纹理：" + path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.spritePixelsPerUnit = 256f; importer.SaveAndReimport();
        }
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }
    }
}
