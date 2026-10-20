using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="PlatformSoliditySwitch"/> 的 Inspector：显示全局 1/0、开局值，
    /// Play 时提供「切换 / 设为 A / 设为 B」按钮。
    /// </summary>
    [CustomEditor(typeof(PlatformSoliditySwitch))]
    public sealed class PlatformSoliditySwitchInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var toggle = (PlatformSoliditySwitch)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("全局参数", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("AIsSolid",
                toggle.AIsSolid ? "1（A 实体 / B 虚化）" : "0（A 虚化 / B 实体）");
            EditorGUILayout.LabelField("开局值", toggle.StartAIsSolid ? "1" : "0");

            if (Application.isPlaying)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("切换 A/B")) toggle.Toggle();
                    if (GUILayout.Button("设为 A 实体（1）")) toggle.Set(true);
                    if (GUILayout.Button("设为 B 实体（0）")) toggle.Set(false);
                }

                Repaint();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "触发区 = 本物体上的 Trigger 碰撞体；玩家攻击盒 / 纸飞机打中即切换 1/0。\n" +
                    "进 Play 后每次攻击切换；这里的开局值在 Awake 写一次。\n" +
                    "场景里建议只放一个开关 —— 多个会互相覆盖开局值。",
                    MessageType.Info);
            }
        }
    }
}
