using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="PlayerTouchDisappear"/> 的 Inspector：显示目标、检测区、触发状态，
    /// 并在 Play 中提供「立即触发」按钮。
    /// </summary>
    [CustomEditor(typeof(PlayerTouchDisappear))]
    public sealed class PlayerTouchDisappearInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var disappear = (PlayerTouchDisappear)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("触碰消失", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("状态", disappear.StateName);
            EditorGUILayout.LabelField("目标",
                disappear.HasTarget ? disappear.Target.name : "未指定（只消失自己）");
            EditorGUILayout.LabelField("消失方式",
                disappear.Mode == PlayerTouchDisappear.DisappearMode.Destroy
                    ? "Destroy（永久销毁）"
                    : "SetActive(false)（可恢复）");

            Bounds area = disappear.DetectionBounds;
            EditorGUILayout.LabelField("检测区尺寸",
                $"{area.size.x:0.###} × {area.size.y:0.###}");

            if (!disappear.HasTarget)
            {
                EditorGUILayout.HelpBox(
                    "「目标」是空的：玩家碰到时只会消失自己。\n" +
                    "把场景里要让其消失的物件（门 / 平台 / 道具）拖进上面的「目标」字段。",
                    MessageType.Warning);
            }
            else if (disappear.Target == disappear.gameObject)
            {
                EditorGUILayout.HelpBox(
                    "「目标」就是自己：只会消失一次，不会另外再让东西消失。",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("立即触发（Play）"))
                {
                    disappear.Trigger();
                    EditorUtility.SetDirty(disappear);
                }
            }

            if (Application.isPlaying) Repaint();

            EditorGUILayout.HelpBox(
                "玩家身体碰到检测区才触发；敌人 / 铁箱 / 纸飞机不算。\n" +
                "自己 + 目标一起消失；目标留空 = 只消失自己。\n" +
                "默认 SetActive(false) 可恢复；把「消失方式」改成 Destroy 则永久销毁。\n" +
                "暂停时冻结；只触发一次。",
                MessageType.Info);
        }
    }
}
