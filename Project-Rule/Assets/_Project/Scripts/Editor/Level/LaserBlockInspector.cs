using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="LaserBlock"/> 的 Inspector：加四个按钮，省得手敲世界坐标。
    /// </summary>
    /// <remarks>
    /// 摆块的常规流程：把物体拖到想要的位置 → 点「起点 = 当前位置」→ 再拖到另一头
    /// → 点「终点 = 当前位置」。末尾报出行程与单程耗时 —— 配速度时直接看这个数。
    /// </remarks>
    [CustomEditor(typeof(LaserBlock))]
    public sealed class LaserBlockInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var block = (LaserBlock)target;
            var t = block.transform;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("轨道工具", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("起点 = 当前位置"))
                {
                    SetPath(block, t.position.x, block.EndX, "设置激光块起点");
                }
                if (GUILayout.Button("终点 = 当前位置"))
                {
                    SetPath(block, block.StartX, t.position.x, "设置激光块终点");
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("块移到起点"))
                {
                    MoveTo(t, block.StartX);
                }
                if (GUILayout.Button("块移到终点"))
                {
                    MoveTo(t, block.EndX);
                }
            }

            EditorGUILayout.Space();
            float length = Mathf.Abs(block.EndX - block.StartX);
            EditorGUILayout.LabelField("行程", $"{length:0.###} 单位");

            if (length <= 0f)
            {
                EditorGUILayout.HelpBox("起点和终点重合，块不会动。", MessageType.Warning);
                return;
            }

            if (block.Speed <= 0f)
            {
                EditorGUILayout.HelpBox("速度为 0，块不会动。", MessageType.Warning);
                return;
            }

            float oneWay = length / block.Speed;
            EditorGUILayout.LabelField("单程耗时", $"{oneWay:0.##} 秒");
            EditorGUILayout.LabelField("往返一趟", $"{oneWay * 2f:0.##} 秒");

            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0 && t.gameObject.layer != ground)
            {
                EditorGUILayout.HelpBox(
                    "块不在 Ground 层：玩家会直接穿过去（激光照常生效）。\n" +
                    "要让它能站、能挡路，把 Layer 改成 Ground。",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "激光只查 Hurtbox 层、不套阵营准入（和刺一样对谁都烧），" +
                "伤害走 CombatSystem.ApplyDamage 这个唯一出口。\n" +
                "碰到 Ground 层的实心地形会被截短。",
                MessageType.None);

            EditorGUILayout.HelpBox(
                "开关驱动：把本组件拖到 CityButton / CityLever 的 FollowMotor 上。\n" +
                "开关只控**激光发射**，块照常巡逻；实际状态 = 开局状态 XOR 开关状态" +
                "（按钮开局是「关」，第一次按下正好翻过去）。",
                MessageType.None);

            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("当前", block.IsOn ? "发射中" : "已关闭");
            }
        }

        private static void SetPath(LaserBlock block, float startX, float endX, string label)
        {
            Undo.RecordObject(block, label);
            block.SetPath(startX, endX);
            EditorUtility.SetDirty(block);
        }

        private static void MoveTo(Transform t, float x)
        {
            Undo.RecordObject(t, "移动激光块");
            t.position = new Vector3(x, t.position.y, t.position.z);
        }
    }
}
