using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="MovingPlatform"/> 的 Inspector：加四个按钮，省得手敲世界坐标。
    /// </summary>
    /// <remarks>
    /// 摆平台的常规流程：把物体拖到想要的位置 → 点「起点 = 当前位置」→ 再拖到另一头
    /// → 点「终点 = 当前位置」。两个「平台移到…」按钮用来跳过去对位置。
    /// 末尾还会报出行程与单程耗时 —— 配平台速度时直接看这个数，不用自己算。
    /// </remarks>
    [CustomEditor(typeof(MovingPlatform))]
    public sealed class MovingPlatformInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var platform = (MovingPlatform)target;
            var t = platform.transform;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("路径工具", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("起点 = 当前位置"))
                {
                    SetPath(platform, t.position, platform.EndPoint, "设置平台起点");
                }
                if (GUILayout.Button("终点 = 当前位置"))
                {
                    SetPath(platform, platform.StartPoint, t.position, "设置平台终点");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("平台移到起点"))
                {
                    MoveTo(t, platform.StartPoint);
                }
                if (GUILayout.Button("平台移到终点"))
                {
                    MoveTo(t, platform.EndPoint);
                }
            }

            EditorGUILayout.Space();
            float length = Vector2.Distance(platform.StartPoint, platform.EndPoint);
            EditorGUILayout.LabelField("行程", $"{length:0.###} 单位");

            if (length <= 0f)
            {
                EditorGUILayout.HelpBox("起点和终点重合，平台不会动。", MessageType.Warning);
                return;
            }

            if (platform.Speed <= 0f)
            {
                EditorGUILayout.HelpBox("速度为 0，平台不会动。", MessageType.Warning);
                return;
            }

            float oneWay = length / platform.Speed;
            EditorGUILayout.LabelField("单程耗时", $"{oneWay:0.##} 秒");

            if (platform.Mode == MovingPlatform.PathMode.Ride)
            {
                EditorGUILayout.LabelField("一趟往返", $"{oneWay * 2f:0.##} 秒");
                EditorGUILayout.HelpBox(
                    "待客模式：没人站上来就不动。踩上平台 → 到终点 → 回到起点停住；\n" +
                    "回到起点时车上还有人就不再出发，等清空后重新待客。",
                    MessageType.None);

                if (Application.isPlaying)
                {
                    EditorGUILayout.LabelField("当前", platform.OnDuty ? "载客中" : "待客中");
                }
            }
        }

        private static void SetPath(MovingPlatform platform, Vector2 start, Vector2 end, string label)
        {
            Undo.RecordObject(platform, label);
            platform.SetPath(start, end);
            EditorUtility.SetDirty(platform);
        }

        private static void MoveTo(Transform t, Vector2 target)
        {
            Undo.RecordObject(t, "移动平台");
            t.position = new Vector3(target.x, target.y, t.position.z);
        }
    }
}
