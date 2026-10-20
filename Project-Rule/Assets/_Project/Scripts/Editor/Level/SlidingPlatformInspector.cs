using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="SlidingPlatform"/> 的 Inspector：加路径按钮，并提示开关接法。
    /// </summary>
    [CustomEditor(typeof(SlidingPlatform))]
    public sealed class SlidingPlatformInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var platform = (SlidingPlatform)target;
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

            EditorGUILayout.LabelField("单程耗时", $"{length / platform.Speed:0.##} 秒");
            EditorGUILayout.HelpBox(
                "把本组件拖到 CityButton / CityLever 的 FollowMotor 上：\n" +
                "开关开 → 滑到终点，开关关 → 滑回起点；半路反向会就地掉头。\n" +
                "按钮的 Toggle 要勾着，才能「按一下去、再按一下回」。",
                MessageType.None);

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("当前目标", platform.TargetEnd ? "终点" : "起点");
            }
        }

        private static void SetPath(SlidingPlatform platform, Vector2 start, Vector2 end, string label)
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
