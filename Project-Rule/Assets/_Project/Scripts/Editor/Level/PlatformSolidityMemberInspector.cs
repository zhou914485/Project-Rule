using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="PlatformSolidityMember"/> 的 Inspector：显示角色、全局值、目标 / 实际状态、
    /// 是否在等玩家让开，以及一个「按全局状态刷新」按钮。
    /// </summary>
    [CustomEditor(typeof(PlatformSolidityMember))]
    public sealed class PlatformSolidityMemberInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var member = (PlatformSolidityMember)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("虚实状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("角色",
                member.PlatformRole == PlatformSolidityMember.Role.A ? "A" : "B");
            EditorGUILayout.LabelField("全局 AIsSolid",
                PlatformSolidityState.AIsSolid ? "1（A 实体 / B 虚化）" : "0（A 虚化 / B 实体）");
            EditorGUILayout.LabelField("目标", member.WantsSolid ? "实体" : "虚化");
            EditorGUILayout.LabelField("实际", member.IsSolid ? "实体" : "虚化");

            if (member.IsWaitingForClear)
            {
                EditorGUILayout.HelpBox(
                    "玩家还卡在平台范围里：先保持虚化，等玩家离开再变实体（防卡住）。",
                    MessageType.Warning);
            }

            EditorGUILayout.LabelField("碰撞体 / 精灵",
                $"{member.SolidColliderCount} / {member.VisualCount}");

            if (member.SolidColliderCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有实心碰撞体：平台永远不会挡住人。\n" +
                    "把平台的 BoxCollider2D 拖进「引用」，或把数组清空让 Awake 自动收集非 Trigger 碰撞体。",
                    MessageType.Error);
            }

            if (member.VisualCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有 SpriteRenderer：不会变半透明。\n" +
                    "把美术子物体拖进「引用」，或把数组清空让 Awake 自动收集。",
                    MessageType.Warning);
            }

            if (GUILayout.Button("按全局状态刷新"))
            {
                member.RefreshState();
                EditorUtility.SetDirty(member);
            }

            if (Application.isPlaying) Repaint();
        }
    }
}
