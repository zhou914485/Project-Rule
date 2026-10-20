using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="EntityPressureButton"/> 的 Inspector：显示实体压力状态、纸飞机锁、
    /// 最近撞击点，并提供「立即检测 / 解除纸飞机锁定」按钮。
    /// </summary>
    [CustomEditor(typeof(EntityPressureButton))]
    public sealed class EntityPressureButtonInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var button = (EntityPressureButton)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("按钮状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("状态", button.CurrentStateName);
            EditorGUILayout.LabelField("实体压着", button.IsBodyPressed ? "是" : "否");
            EditorGUILayout.LabelField("纸飞机锁",
                button.IsLatchActive
                    ? (button.IsLatchPermanent ? "永久" : $"{button.LatchRemaining:0.##} 秒")
                    : "无");
            EditorGUILayout.LabelField("压着它的实体",
                button.LastPresser != null ? button.LastPresser.name : "无");

            if (button.HasImpact)
            {
                EditorGUILayout.LabelField("最近撞击点", button.LastImpactPoint.ToString("0.##"));
            }

            Bounds area = button.AreaBounds;
            EditorGUILayout.LabelField("检测区尺寸",
                $"{area.size.x:0.###} × {area.size.y:0.###}");

            if (!button.HasArea)
            {
                EditorGUILayout.HelpBox(
                    "没有检测区：按钮永远不会被按下。\n" +
                    "挂一个 Trigger Collider 并拖进「检测区」，或留空让 Awake 取同物体上的 Collider2D。",
                    MessageType.Error);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("立即检测"))
                {
                    button.RefreshState();
                    EditorUtility.SetDirty(button);
                }
                if (GUILayout.Button("解除纸飞机锁定"))
                {
                    button.ResetLatch();
                    EditorUtility.SetDirty(button);
                }
            }

            if (Application.isPlaying) Repaint();

            EditorGUILayout.HelpBox(
                "压力：只认「带 Rigidbody2D 且不是 Static」的实体；地面 / Tilemap / 静态道具没有刚体，不会误触。\n" +
                "纸飞机 / 攻击盒：走 IContactSwitch 的撞击入口碰一下就锁住（0 秒 = 永久），不需要压着。\n" +
                "⚠️ 纸飞机只对 Trigger 调查找开关，检测区必须是 Trigger。",
                MessageType.Info);
        }
    }
}
