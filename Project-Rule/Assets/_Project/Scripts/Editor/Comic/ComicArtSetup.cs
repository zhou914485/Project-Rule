using System;
using System.Collections.Generic;
using System.IO;
using RuleGame.UI.Comic;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Comic
{
    /// <summary>
    /// 把 <c>本地文档/剧情顺序/1..21.png</c> 导入工程，并按实测像素差把转场分档烤进两份序列资产。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要有这个工具</b>：21 张都是 3500×2600，按原尺寸导入是 36 MB/张（合计 760 MB 显存）。
    /// 手点 21 次 Max Size 既慢又容易漏，所以压缩参数写死在这里，重跑一次即可全部刷新。</para>
    /// <para><b>为什么要比对像素</b>：素材里有两类页 —— 严格叠加的（2→3→4、7→8→9，共享分格逐像素相同）
    /// 和「同一构图只改一处」的（11→12→13 人沉下去、15→16 熊出现、18→19 手伸进来）。
    /// 后者只差 2–8% 像素，过黑场会把「变化」糊成「换了张图」，所以按差异自动选
    /// <see cref="ComicTransition.Dissolve"/>。判定只在编辑器里做一次，运行时不再比图。</para>
    /// <para><b>黑页</b>：1.png 与 6.png 是同一张纯黑图（MD5 相同）。它们标成自动前进，
    /// 玩家不用对着一片黑点一次。</para>
    /// </remarks>
    public static class ComicArtSetup
    {
        private const string SourceFolder = "../本地文档/剧情顺序";
        private const string ArtFolder = "Assets/_Project/Art/Comic";
        private const string SequenceFolder = "Assets/_Project/Resources/Comic";
        private const int PageCount = 21;
        private const int IntroCount = 19;

        /// <summary>低于这个像素差就走原地溶解，不黑场。</summary>
        private const float DissolveThreshold = 0.10f;

        /// <summary>平均亮度低于这个值就当成纯黑页。</summary>
        private const float BlackThreshold = 0.02f;

        /// <summary>场景交界：从这里开始的一页给加长黑场。</summary>
        private static readonly HashSet<int> SceneStarts = new HashSet<int> { 6, 7, 10, 20 };

        [MenuItem("Tools/规则游戏/剧情漫画/① 导入素材并生成序列", false, 221)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");

            string source = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), SourceFolder));
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("找不到素材目录：" + source);

            for (int i = 1; i <= PageCount; i++)
            {
                string file = Path.Combine(source, i + ".png");
                if (!File.Exists(file))
                    throw new FileNotFoundException("缺少素材：" + file);
            }

            EnsureFolder(ArtFolder);
            EnsureFolder(SequenceFolder);

            // ① 复制 + 导入设置
            string[] assetPaths = new string[PageCount];
            for (int i = 1; i <= PageCount; i++)
            {
                string dest = ArtFolder + "/Comic_" + i.ToString("00") + ".png";
                File.Copy(Path.Combine(source, i + ".png"), dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceUpdate);
                ApplyImportSettings(dest);
                assetPaths[i - 1] = dest;
            }

            AssetDatabase.Refresh();

            // ② 实测像素差（读源文件，不依赖导入后的可读性）
            float[] diff = new float[PageCount];
            float[] luma = new float[PageCount];
            Texture2D previous = null;
            for (int i = 1; i <= PageCount; i++)
            {
                Texture2D texture = LoadSource(Path.Combine(source, i + ".png"));
                luma[i - 1] = MeanLuma(texture);
                diff[i - 1] = previous != null ? Difference(previous, texture) : 1f;

                if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
                previous = texture;
            }
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

            // ③ 分档 + 建资产
            ComicPage[] pages = new ComicPage[PageCount];
            for (int i = 1; i <= PageCount; i++)
            {
                Texture2D image = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPaths[i - 1]);

                bool black = luma[i - 1] < BlackThreshold;
                ComicTransition transition =
                    i == 1 ? ComicTransition.Dip
                    : black ? ComicTransition.Dip
                    : diff[i - 1] < DissolveThreshold ? ComicTransition.Dissolve
                    : SceneStarts.Contains(i) ? ComicTransition.SceneDip
                    : ComicTransition.Dip;

                float hold = black ? (i == 1 ? 0.6f : 0.25f) : 0f;
                pages[i - 1] = new ComicPage(image, transition, black, hold);
            }

            WriteSequence("Assets/_Project/Resources/Comic/Comic_Intro.asset", "开场", pages, 1, IntroCount);
            WriteSequence("Assets/_Project/Resources/Comic/Comic_Ending.asset", "结尾", pages, IntroCount + 1, PageCount);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Comic] 21 页已导入 " + ArtFolder + "，两份序列写进 " + SequenceFolder +
                      "（溶解阈值 " + (DissolveThreshold * 100f).ToString("0") + "%）。");
        }

        // ------------------------------------------------------------------
        //  导入
        // ------------------------------------------------------------------

        private static void ApplyImportSettings(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("不是贴图：" + path);

            // 这是整屏 UI 图，不是精灵：不加 mipmap、不切图，Max Size 压到 2048
            //（1080p 下实际只显示约 1454 px 宽）。
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                throw new ArgumentException("目录不合法：" + path);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ------------------------------------------------------------------
        //  像素比对
        // ------------------------------------------------------------------

        private static Texture2D LoadSource(string path)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
                throw new InvalidOperationException("读不了这张图：" + path);
            return texture;
        }

        /// <summary>在 128×96 的网格上采样，返回「明显不同」的采样点占比。</summary>
        private static float Difference(Texture2D a, Texture2D b)
        {
            const int columns = 128;
            const int rows = 96;

            int different = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    float u = (x + 0.5f) / columns;
                    float v = (y + 0.5f) / rows;
                    Color ca = a.GetPixelBilinear(u, v);
                    Color cb = b.GetPixelBilinear(u, v);
                    float delta = Mathf.Max(Mathf.Abs(ca.r - cb.r),
                                    Mathf.Max(Mathf.Abs(ca.g - cb.g), Mathf.Abs(ca.b - cb.b)));
                    if (delta > 0.05f) different++;
                }
            }

            return different / (float)(columns * rows);
        }

        private static float MeanLuma(Texture2D texture)
        {
            const int columns = 64;
            const int rows = 48;

            float sum = 0f;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Color c = texture.GetPixelBilinear((x + 0.5f) / columns, (y + 0.5f) / rows);
                    sum += c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                }
            }

            return sum / (columns * rows);
        }

        // ------------------------------------------------------------------
        //  资产
        // ------------------------------------------------------------------

        private static void WriteSequence(string path, string label, ComicPage[] all, int from, int to)
        {
            ComicPage[] pages = new ComicPage[to - from + 1];
            for (int i = from; i <= to; i++) pages[i - from] = all[i - 1];

            ComicSequence sequence = AssetDatabase.LoadAssetAtPath<ComicSequence>(path);
            if (sequence == null)
            {
                sequence = ScriptableObject.CreateInstance<ComicSequence>();
                AssetDatabase.CreateAsset(sequence, path);
            }

            sequence.SetPages(pages);
            EditorUtility.SetDirty(sequence);

            Debug.Log("[Comic] " + label + "：" + pages.Length + " 页 → " + path);
        }
    }
}
