using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Rules;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 宝箱 Inspector：只显示当前 <see cref="CityRewardKind"/> 真正用得到的那一个字段。
    /// </summary>
    /// <remarks>
    /// 三个载荷字段（Ability / Attribute / Relation）同时摆出来会让人以为都要填。
    /// 这里按 Kind 切换，并在底部写一句「当前奖励是什么」。
    /// 生命 / 能量只画 Amount（上限提升多少）。
    /// </remarks>
    [CustomEditor(typeof(CityRewardSource))]
    public sealed class CityRewardSourceEditor : UnityEditor.Editor
    {
        private static readonly string[] AttributeNames = { "重力", "时间", "摩擦", "质量", "伤害/生命", "因果" };
        private static readonly string[] RelationNames = { "反转", "失效", "恒定", "增强", "削弱" };

        public override void OnInspectorGUI()
        {
            var source = (CityRewardSource)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("SourceId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OpenByAttack"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Prompt"));

            SerializedProperty kindProperty = serializedObject.FindProperty("Kind");
            EditorGUILayout.PropertyField(kindProperty);
            var kind = (CityRewardKind)kindProperty.intValue;

            // 只画这一类奖励真正用得到的字段 —— 其余字段不删，切回对应 Kind 就能继续用。
            switch (kind)
            {
                case CityRewardKind.Ability:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Ability"));
                    break;
                case CityRewardKind.RuleAttribute:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Attribute"));
                    break;
                case CityRewardKind.RuleRelation:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Relation"));
                    break;
                default:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Amount"));
                    break;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("Receiver"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PickupVisual"));

            EditorGUILayout.Space();

            if (string.IsNullOrWhiteSpace(source.SourceId))
                EditorGUILayout.HelpBox("SourceId 为空：宝箱不会变成可领取状态。每个场景实例要填唯一 ID。", MessageType.Warning);

            EditorGUILayout.HelpBox(Describe(source, kind), MessageType.Info);

            if (source.Receiver == null && IsPlayerProgressReward(kind))
                EditorGUILayout.HelpBox("Receiver 留空：奖励会自动发给玩家身上的 PlayerProgress。", MessageType.None);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool IsPlayerProgressReward(CityRewardKind kind)
            => kind == CityRewardKind.Ability
            || kind == CityRewardKind.RuleAttribute
            || kind == CityRewardKind.RuleRelation
            || kind == CityRewardKind.Health
            || kind == CityRewardKind.Energy;

        private static string Describe(CityRewardSource source, CityRewardKind kind)
        {
            switch (kind)
            {
                case CityRewardKind.Ability:
                    return source.Ability == PlayerAbility.None
                        ? "当前奖励：未选择能力（打开后不会发奖）。"
                        : "当前奖励：解锁能力「" + AbilityName(source.Ability) + "」。";

                case CityRewardKind.RuleAttribute:
                    return "当前奖励：解锁规则属性「" + AttributeName(source.Attribute)
                        + "」（该属性下已解锁的关系都会变得可用）。";

                case CityRewardKind.RuleRelation:
                    return "当前奖励：解锁规则关系「" + RelationName(source.Relation)
                        + "」（该关系下已解锁的属性都会变得可用）。";

                case CityRewardKind.Health:
                    return "当前奖励：生命上限 +" + Mathf.Max(1, source.Amount)
                        + "（当前生命同步抬高同一段，随存档保存）。";

                case CityRewardKind.Energy:
                    return "当前奖励：能量上限 +" + Mathf.Max(1, source.Amount)
                        + "（当前能量同步抬高同一段，随存档保存）。";

                default:
                    return "当前奖励：未识别的类型 " + (int)kind + "（不会发放）。";
            }
        }

        private static string AbilityName(PlayerAbility ability)
        {
            switch (ability)
            {
                case PlayerAbility.WallClimb: return "爬墙跳";
                case PlayerAbility.AirJump: return "二段跳";
                case PlayerAbility.Attack: return "攻击";
                case PlayerAbility.Throw: return "飞刀";
                default: return ability.ToString();
            }
        }

        private static string AttributeName(AttributeId attribute)
        {
            int index = (int)attribute;
            return index >= 0 && index < AttributeNames.Length ? AttributeNames[index] : attribute.ToString();
        }

        private static string RelationName(RelationId relation)
        {
            int index = (int)relation;
            return index >= 0 && index < RelationNames.Length ? RelationNames[index] : relation.ToString();
        }

    }
}
