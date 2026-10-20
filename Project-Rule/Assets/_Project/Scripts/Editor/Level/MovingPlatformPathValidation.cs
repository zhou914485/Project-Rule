using System.Text;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="MovingPlatform"/> 路径坐标的自检：**相对偏移**的语义对不对。
    /// </summary>
    /// <remarks>
    /// <para>核心那条：**移动物体，路径整体跟着平移**。这就是「把整张地图挪个位置，
    /// 平台不会跑偏」的可执行证据 —— 光看代码里的加减法看不出这个。</para>
    /// <para>另外顺带守住旧资产：没迁移的平台仍然按世界坐标解释，不会被悄悄改坏。</para>
    /// </remarks>
    public static class MovingPlatformPathValidation
    {
        private const float Tol = 0.001f;

        [MenuItem("规则游戏/验证/平移平台路径（相对坐标）", false, 72)]
        public static void Run()
        {
            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            void Check(string label, bool ok, string detail = "")
            {
                sb.Append(ok ? "✓ " : "✗ ").Append(label);
                if (!ok && !string.IsNullOrEmpty(detail)) sb.Append(" —— ").Append(detail);
                sb.AppendLine();
                if (ok) pass++; else fail++;
            }

            // ---------------- 1. 场景里的实例已经迁移 ----------------
            int sceneCount = 0;
            int notMigrated = 0;
            foreach (UnityEngine.SceneManagement.Scene scene in OpenScenes())
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (MovingPlatform platform in root.GetComponentsInChildren<MovingPlatform>(true))
                    {
                        if (platform == null) continue;
                        sceneCount++;

                        var so = new SerializedObject(platform);
                        SerializedProperty flag = so.FindProperty("_pathIsRelative");
                        bool relative = flag != null && flag.boolValue;
                        if (!relative)
                        {
                            notMigrated++;
                            sb.AppendLine("    ✗ 未迁移：" + platform.gameObject.name +
                                          "（路径仍是世界坐标）");
                        }
                    }
                }
            }

            Check("场景里的平移平台全部是相对坐标（共 " + sceneCount + " 个）", notMigrated == 0,
                notMigrated + " 个还是世界坐标，跑一次菜单「⑱ 平移平台：世界坐标 → 相对偏移」");

            // ---------------- 2. 相对模式下：拖动物体，路径整体平移 ----------------
            // 用**预览场景**装探针：不往你开着的关卡里塞临时物体，也不会把它标脏。
            UnityEngine.SceneManagement.Scene preview =
                UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var probe = new GameObject("__MovingPlatformPathProbe");
            UnityEditor.SceneManagement.EditorSceneManager.MoveGameObjectToScene(probe, preview);
            try
            {
                var platform = probe.AddComponent<MovingPlatform>();
                probe.transform.position = new Vector3(10f, 5f, 0f);

                platform.SetPath(new Vector2(10f, 5f), new Vector2(14f, 5f));   // 世界坐标入参
                Vector2 beforeStart = platform.StartPoint;
                Vector2 beforeEnd = platform.EndPoint;
                float beforeLength = Vector2.Distance(beforeStart, beforeEnd);

                var shift = new Vector3(100f, -37f, 0f);
                probe.transform.position += shift;

                Vector2 afterStart = platform.StartPoint;
                Vector2 afterEnd = platform.EndPoint;

                Check("相对模式：移动物体后起点同量平移",
                    Mathf.Abs((afterStart - beforeStart).x - shift.x) < Tol &&
                    Mathf.Abs((afterStart - beforeStart).y - shift.y) < Tol,
                    "期望 +" + shift + "，实际 +" + (afterStart - beforeStart));

                Check("相对模式：移动物体后终点同量平移",
                    Mathf.Abs((afterEnd - beforeEnd).x - shift.x) < Tol &&
                    Mathf.Abs((afterEnd - beforeEnd).y - shift.y) < Tol,
                    "期望 +" + shift + "，实际 +" + (afterEnd - beforeEnd));

                Check("相对模式：行程长度不变（形状没被拉变形）",
                    Mathf.Abs(Vector2.Distance(afterStart, afterEnd) - beforeLength) < Tol,
                    beforeLength.ToString("F3") + " → " + Vector2.Distance(afterStart, afterEnd).ToString("F3"));

                Check("新组件默认就是相对模式", IsRelative(platform));

                // ---------------- 3. 迁移是幂等的 ----------------
                Check("已经是相对模式时，迁移工具不重复换算", !platform.ConvertPathToRelative());

                // ---------------- 4. 旧资产：世界坐标语义没被改坏 ----------------
                SetRelative(platform, false);
                probe.transform.position = Vector3.zero;
                platform.SetPath(new Vector2(-50f, 20f), new Vector2(-46f, 20f));
                probe.transform.position = new Vector3(500f, 500f, 0f);

                Check("旧资产（世界坐标模式）：移动物体不影响端点",
                    Vector2.Distance(platform.StartPoint, new Vector2(-50f, 20f)) < Tol &&
                    Vector2.Distance(platform.EndPoint, new Vector2(-46f, 20f)) < Tol,
                    "start=" + platform.StartPoint + " end=" + platform.EndPoint);

                // ---------------- 5. 迁移后端点位置**不变** ----------------
                probe.transform.position = new Vector3(-50f, 20f, 0f);
                Check("迁移后的世界端点与原值重合（只换存法，不换位置）",
                    platform.ConvertPathToRelative() &&
                    Vector2.Distance(platform.StartPoint, new Vector2(-50f, 20f)) < Tol &&
                    Vector2.Distance(platform.EndPoint, new Vector2(-46f, 20f)) < Tol,
                    "start=" + platform.StartPoint + " end=" + platform.EndPoint);
            }
            finally
            {
                Object.DestroyImmediate(probe);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
            }

            sb.AppendLine();
            sb.AppendLine(fail == 0
                ? "全部通过：" + pass + " 项。"
                : "通过 " + pass + " 项，失败 " + fail + " 项。");

            if (fail == 0) Debug.Log("[平移平台路径验证]\n" + sb);
            else Debug.LogError("[平移平台路径验证]\n" + sb);
        }

        private static System.Collections.Generic.IEnumerable<UnityEngine.SceneManagement.Scene> OpenScenes()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) yield return scene;
            }
        }

        private static bool IsRelative(MovingPlatform platform)
        {
            var so = new SerializedObject(platform);
            SerializedProperty flag = so.FindProperty("_pathIsRelative");
            return flag != null && flag.boolValue;
        }

        private static void SetRelative(MovingPlatform platform, bool value)
        {
            var so = new SerializedObject(platform);
            SerializedProperty flag = so.FindProperty("_pathIsRelative");
            if (flag == null) return;
            flag.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
