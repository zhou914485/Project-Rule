using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="MultiButtonDoor"/> 的 Inspector：显示按钮数量、按下数量、门的状态，
    /// 并逐个列出按钮的按下情况。
    /// </summary>
    [CustomEditor(typeof(MultiButtonDoor))]
    public sealed class MultiButtonDoorInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var door = (MultiButtonDoor)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("按钮（AND）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("数量 / 按下",
                $"{door.ButtonCount} / {door.PressedCount}");
            EditorGUILayout.LabelField("全部按下", door.AllButtonsPressed ? "是" : "否");

            EntityPressureButton[] buttons = door.Buttons;
            if (buttons != null)
            {
                bool missing = false;
                for (int i = 0; i < buttons.Length; i++)
                {
                    EntityPressureButton button = buttons[i];
                    if (button == null) missing = true;

                    EditorGUILayout.LabelField(
                        "按钮 " + (i + 1),
                        button == null ? "未指定" : button.CurrentStateName);
                }

                if (missing)
                {
                    EditorGUILayout.HelpBox(
                        "「按钮」数组里有空槽：门不会开。把按钮拖满，或把数组缩小。",
                        MessageType.Error);
                }
            }

            if (door.ButtonCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有按钮：门会永远关着。把 EntityPressureButton 拖进「按钮」数组。",
                    MessageType.Error);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("门", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("状态",
                door.IsWaitingForClear ? "等待玩家离开门洞"
                : door.IsOpen ? "开"
                : door.IsClosed ? "关"
                : "移动");
            EditorGUILayout.LabelField("进度", $"{door.Progress:0.###}（0 关 / 1 开）");

            if (door.IsWaitingForClear)
            {
                EditorGUILayout.HelpBox(
                    "按钮已经松开，但玩家还卡在门洞里：门停在当前位置，等玩家出去再关（防夹）。",
                    MessageType.Warning);
            }

            if (Application.isPlaying) Repaint();

            EditorGUILayout.HelpBox(
                "「按钮」数组长度 = 按钮数量；所有按钮都按下才开门，任意一个松开就关门。\n" +
                "按钮可以被实体压住，也可以被纸飞机 / 攻击碰一下锁住（锁住也算按下）。\n" +
                "想改成 1 个 / 3 个按钮：直接改数组长度，把场景里的按钮拖进来。\n" +
                "门沿「开门偏移」滑动；关门时玩家在门洞里会等玩家出来（只检查玩家）。",
                MessageType.Info);
        }
    }
}
