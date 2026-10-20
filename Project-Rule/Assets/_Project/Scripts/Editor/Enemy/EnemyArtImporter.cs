using System;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    [Serializable] public sealed class EnemyArtFrame
    {
        public string path, source;
        public int width, height;
        public float pivotX, pivotY, ppu;
        public float clockFaceX, clockFaceY;
    }
    [Serializable] public sealed class EnemyArtSequence
    {
        public string actor, action;
        public bool facesRight;
        public bool nativeFrameRate;
        public string[] frames;
    }
    [Serializable] public sealed class EnemyArtManifest
    {
        public EnemyArtFrame[] frames;
        public EnemyArtSequence[] sequences;
        public int frameRate;
    }

    /// <summary>仅通过编辑器 API 导入 Sprite 和修改锚点，不写 .meta，不改变源图。</summary>
    public static class EnemyArtImporter
    {
        public const string ManifestPath = "Assets/_Project/Art/Enemies/EnemyArtManifest.json";
        public static EnemyArtManifest Load()
        {
            TextAsset text = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (text == null) throw new InvalidOperationException("请先运行 Tools/EnemyAssets/import_resources.py。");
            return JsonUtility.FromJson<EnemyArtManifest>(text.text);
        }
        public static void Import() => ImportActor(null);
        public static void ImportActor(string actor)
        {
            EnemyArtManifest manifest = Load();
            EnemyArtFrame[] frames = Array.FindAll(manifest.frames,
                frame => actor == null || frame.path.Contains("/" + actor + "/"));
            ImportFrames(frames);
        }
        public static void ImportSequences(params string[] actions)
        {
            EnemyArtManifest manifest = Load();
            var paths = new System.Collections.Generic.HashSet<string>();
            foreach (EnemyArtSequence sequence in manifest.sequences)
                if (Array.IndexOf(actions, sequence.action) >= 0)
                    foreach (string path in sequence.frames) paths.Add(path);
            ImportFrames(Array.FindAll(manifest.frames, frame => paths.Contains(frame.path)));
        }
        internal static void ImportFrames(EnemyArtFrame[] frames)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (EnemyArtFrame frame in frames)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(frame.path);
                    if (importer.textureType == TextureImporterType.Sprite && importer.maxTextureSize == 512
                        && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.maxTextureSize = 512;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.isReadable = false;
                    AssetDatabase.WriteImportSettingsIfDirty(frame.path);
                    AssetDatabase.ImportAsset(frame.path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (EnemyArtFrame frame in frames)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(frame.path);
                    var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                    if (provider == null) throw new InvalidOperationException("Sprite 数据提供者不可用：" + frame.path);
                    provider.InitSpriteEditorDataProvider();
                    var capability = provider.GetDataProvider<ISpriteFrameEditCapability>();
                    if (capability == null || !capability.GetEditCapability().HasCapability(EEditCapability.EditPivot))
                        throw new InvalidOperationException("导入器不支持安全修改 Pivot：" + frame.path);
                    SpriteRect[] rects = provider.GetSpriteRects();
                    if (rects.Length != 1) throw new InvalidOperationException("每张 PNG 应只包含一个 Sprite：" + frame.path);
                    // TextureImporter 已按缩图比例换算 Sprite 的实际 PPU，不能再补偿一次。
                    float ppu = frame.ppu;
                    if (rects[0].alignment == SpriteAlignment.Custom && Vector2.Distance(rects[0].pivot, new Vector2(frame.pivotX, frame.pivotY)) < 0.00001f
                        && Mathf.Abs(importer.spritePixelsPerUnit - ppu) < 0.001f) continue;
                    rects[0].alignment = SpriteAlignment.Custom;
                    rects[0].pivot = new Vector2(frame.pivotX, frame.pivotY);
                    provider.SetSpriteRects(rects);
                    provider.Apply();
                    // 始终传源图像素尺度；Unity 自己保证导入缩图前后的世界尺寸一致。
                    importer.spritePixelsPerUnit = ppu;
                    AssetDatabase.WriteImportSettingsIfDirty(frame.path);
                    AssetDatabase.ImportAsset(frame.path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            Debug.Log($"[EnemyArt] 已导入 {frames.Length} 张帧，原图与源文件保持完整。");
        }
    }
}
