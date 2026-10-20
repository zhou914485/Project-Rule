using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="AttackExtendedPlatform"/> 的 Inspector：把「攻击前 → 攻击后」的长度、
    /// 方向、延长速度直接算给策划看，外加编辑期预览与 Play 期调试按钮。
    /// </summary>
    /// <remarks>
    /// 摆这种平台的常规流程：把物体拖到位置 → 在「长度」两栏里填攻击前 / 攻击后 →
    /// 点「预览：攻击后」看它会伸到哪儿 → 再点回「预览：攻击前」。
    /// Play 中可以在最下面看到当前长度、状态、有没有被挡住。
    /// </remarks>
    [CustomEditor(typeof(AttackExtendedPlatform))]
    public sealed class AttackExtendedPlatformInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var platform = (AttackExtendedPlatform)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("攻击 → 延长", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("长度（攻击前 → 攻击后）",
                $"{platform.LengthBefore:0.###} → {platform.LengthAfter:0.###} 单位");
            EditorGUILayout.LabelField("延长量",
                $"{(platform.LengthAfter - platform.LengthBefore):0.###} 单位");
            EditorGUILayout.LabelField("方向", platform.DirectionName);
            EditorGUILayout.LabelField("触发方式", platform.TriggerName);

            string autoRetract = platform.WillAutoRetract
                ? $"{platform.AutoRetractSeconds:0.##} 秒"
                : "一直保持（0）";
            if (Application.isPlaying && platform.IsExtended && platform.WillAutoRetract)
            {
                autoRetract += $"（剩余 {platform.AutoRetractRemaining:0.##} 秒）";
            }
            EditorGUILayout.LabelField("长到最长后自动缩回", autoRetract);

            // 「玩家攻击」这一路是全局的 —— 不提醒的话，关卡里放两个就会觉得"怎么两个都动了"。
            if (platform.Trigger != AttackExtendedPlatform.TriggerMode.HitByAttack)
            {
                EditorGUILayout.HelpBox(
                    "「玩家攻击」这一路是全局的：玩家每一刀真正出手，场景里所有带这个模式的平台都会一起长。\n" +
                    "只想让被砍中的那个长，把上面的「触发方式」改成「打中平台」。",
                    MessageType.Warning);
            }

            if (platform.ExtensionSpeed > 0f)
                EditorGUILayout.LabelField("延长速度", $"{platform.ExtensionSpeed:0.###} 单位/秒");
            else
                EditorGUILayout.HelpBox("延长时长为 0：攻击后瞬间到位。", MessageType.Info);

            if (platform.LengthAfter <= platform.LengthBefore)
                EditorGUILayout.HelpBox("攻击后长度必须大于攻击前长度，否则平台不会变长。",
                                        MessageType.Warning);

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("当前长度", $"{platform.CurrentLength:0.###} 单位");
                EditorGUILayout.LabelField("状态", platform.StateName);
                EditorGUILayout.LabelField("被挡住", platform.IsBlocked
                    ? (platform.BlockedBy != null ? platform.BlockedBy.name : "是")
                    : "否");

                if (platform.Trigger != AttackExtendedPlatform.TriggerMode.HitByAttack)
                {
                    EditorGUILayout.LabelField("监听玩家", platform.IsListeningToPlayer ? "是" : "还没找到玩家");
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("调试：延长")) platform.Extend();
                    if (GUILayout.Button("调试：缩回")) platform.Retract();
                    if (GUILayout.Button("复位到攻击前")) platform.ResetToBefore();
                }

                // 让这些数字在 Play 中实时刷新（OnInspectorGUI 本身不会每帧自动重绘）。
                Repaint();
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("编辑期预览", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("预览：攻击前")) SetPreview(platform, false);
                    if (GUILayout.Button("预览：攻击后")) SetPreview(platform, true);
                }

                string hint;
                switch (platform.Trigger)
                {
                    case AttackExtendedPlatform.TriggerMode.HitByAttack:
                        hint = "进 Play 后用轻击 / 重击打它才会长（攻击盒在胸口高度；贴地的薄平台从上面砍不到，先把它抬高）。\n" +
                               "纸飞机打在实心平台上会停住、不触发；要投射物触发，给平台加一个 Trigger 子碰撞体。";
                        break;

                    case AttackExtendedPlatform.TriggerMode.Both:
                        hint = "进 Play 后：玩家出手会触发（所有同模式平台一起），攻击盒打中它也会触发。\n" +
                               "两条都不进伤害管线。";
                        break;

                    default:
                        hint = "进 Play 后按攻击键（轻击 / 重击）就会长 —— 不用打到平台；投掷（纸飞机）不算攻击。\n" +
                               "要让某个平台只被「打中」才长，把触发方式改成「打中平台」。";
                        break;
                }

                EditorGUILayout.HelpBox(hint, MessageType.Info);
            }
        }

        private static void SetPreview(AttackExtendedPlatform platform, bool extended)
        {
            // 预览会同时改组件字段、碰撞体尺寸和白盒方块的 transform —— 整棵子树一起记，
            // 否则撤销之后碰撞体还停在预览长度上。
            Undo.RegisterFullObjectHierarchyUndo(platform.gameObject,
                extended ? "预览攻击后长度" : "预览攻击前长度");
            platform.SetPreviewExtended(extended);
            EditorUtility.SetDirty(platform);
        }
    }
}
