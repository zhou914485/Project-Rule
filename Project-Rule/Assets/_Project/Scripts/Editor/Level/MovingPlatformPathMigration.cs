using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 把 <see cref="MovingPlatform"/> 的路径从**世界坐标**一次性迁移成**相对本物体的偏移**。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么迁移</b>：旧版把起点 / 终点存成裸的世界坐标，于是「把整张地图挪个位置」时
    /// 平台搬走了、终点还留在原地。迁移后 <c>_start</c> / <c>_end</c> 是相对本物体的偏移，
    /// 拖动物体整条路径跟着走。</para>
    ///
    /// <para><b>范围</b>：<c>Assets/_Project</c> 下所有场景（已打开的在原地改并标脏、
    /// 没打开的临时附加加载 → 存盘 → 关掉）+ 所有预制体。
    /// **已打开的场景不会被自动存盘** —— 让你自己看一遍 diff 再保存。</para>
    ///
    /// <para><b>幂等</b>：已经是相对模式的平台会被跳过（<see cref="MovingPlatform.ConvertPathToRelative"/>
    /// 自己判重），跑第二遍应该是 0 个。</para>
    /// </remarks>
    public static class MovingPlatformPathMigration
    {
        private const string SearchRoot = "Assets/_Project";
        private const string UndoLabel = "平移平台路径改为相对坐标";

        [MenuItem("Tools/规则游戏/地图组件/⑱ 平移平台：世界坐标 → 相对偏移（一次性迁移）", false, 70)]
        public static void Run()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[路径迁移] 先退出 Play 模式再跑 —— 播放中 transform 是移动后的位置，换算会错。");
                return;
            }

            int converted = 0;
            var savedAssets = new List<string>();
            var dirtyOpenScenes = new List<string>();

            // ---------- 1. 已打开的场景（原地改，不替你存盘）----------
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;

                int n = ConvertScene(scene, useUndo: true);
                if (n <= 0) continue;

                dirtyOpenScenes.Add(scene.name + "（" + n + " 个）");
                converted += n;
            }

            // ---------- 2. 没打开的场景（附加加载 → 改 → 存盘 → 关掉）----------
            var openPaths = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(open.path)) openPaths.Add(open.path);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SearchRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || openPaths.Contains(path)) continue;
                if (!AnyInSceneAsset(path)) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                int n = ConvertScene(scene, useUndo: false);
                if (n > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    savedAssets.Add(path + "（" + n + " 个）");
                    converted += n;
                }
                EditorSceneManager.CloseScene(scene, true);
            }

            // ---------- 3. 预制体 ----------
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { SearchRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !HasPlatformAsset(path)) continue;

                int n = ConvertPrefab(path);
                if (n <= 0) continue;

                savedAssets.Add(path + "（" + n + " 个）");
                converted += n;
            }

            AssetDatabase.SaveAssets();

            // ---------- 汇报 ----------
            if (converted == 0)
            {
                Debug.Log("[路径迁移] 没有需要迁移的平台 —— 全部都已经是相对坐标了。");
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine("[路径迁移] 共迁移 " + converted + " 个 MovingPlatform。");
            foreach (string line in dirtyOpenScenes) report.AppendLine("  已打开 · 待你保存：" + line);
            foreach (string line in savedAssets) report.AppendLine("  已存盘：" + line);
            report.AppendLine("  端点数值现在是「相对本物体摆放位置的偏移」；" +
                              "拖动物体时 Gizmo 上的路径应当整体跟着走。");
            Debug.Log(report.ToString());
        }

        private static int ConvertScene(Scene scene, bool useUndo)
        {
            int n = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MovingPlatform platform in root.GetComponentsInChildren<MovingPlatform>(true))
                {
                    if (platform == null || !NeedsConversion(platform)) continue;

                    if (useUndo) Undo.RecordObject(platform, UndoLabel);
                    if (!platform.ConvertPathToRelative()) continue;

                    EditorUtility.SetDirty(platform);
                    n++;
                }
            }

            if (useUndo && n > 0) EditorSceneManager.MarkSceneDirty(scene);
            return n;
        }

        private static int ConvertPrefab(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int n = 0;
                foreach (MovingPlatform platform in contents.GetComponentsInChildren<MovingPlatform>(true))
                {
                    if (platform == null || !NeedsConversion(platform)) continue;
                    if (!platform.ConvertPathToRelative()) continue;
                    n++;
                }

                if (n > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
                return n;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>场景里有没有这个组件 —— 没有就别白开一次场景。</summary>
        private static bool AnyInSceneAsset(string scenePath)
        {
            foreach (string dependency in AssetDatabase.GetDependencies(scenePath, true))
            {
                if (dependency.EndsWith("MovingPlatform.cs")) return true;
            }
            return false;
        }

        /// <summary>预制体里有没有这个组件 —— 只读加载，不做 LoadPrefabContents。</summary>
        private static bool HasPlatformAsset(string prefabPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return asset != null && asset.GetComponentInChildren<MovingPlatform>(true) != null;
        }

        /// <summary>
        /// 走 SerializedObject 判重，避免在没改动的物体上记 Undo / 标脏。
        /// </summary>
        private static bool NeedsConversion(MovingPlatform platform)
        {
            var so = new SerializedObject(platform);
            SerializedProperty flag = so.FindProperty("_pathIsRelative");
            return flag == null || !flag.boolValue;
        }
    }
}
