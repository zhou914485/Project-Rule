using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Enemy
{
    public enum EnemyFeedbackKind
    {
        Strike, ProjectileSpawn, ProjectileImpact, BeamSpawn, Hit, RuleCast, Summon,
        BarrageWarning, AreaStrike
    }

    /// <summary>
    /// 战斗已发生的表现通知。携带判定位置和尺寸，不携带特效资源，也不反过来驱动伤害。
    /// 监听者可缺省；正式关卡只要使用敌人 Prefab 就会带上同一套表现。
    /// </summary>
    public readonly struct EnemyFeedback
    {
        public readonly EnemyFeedbackKind Kind;
        public readonly Vector3 Position;
        public readonly Vector2 Direction;
        public readonly Component Subject;
        public readonly float Damage;
        public readonly bool Staggered;
        public readonly bool Lethal;
        public readonly AttributeId? RuleAttribute;
        public readonly RelationId? RuleRelation;
        public readonly Vector2 Size;
        public readonly bool Sustained;

        public EnemyFeedback(EnemyFeedbackKind kind, Vector3 position, Vector2 direction, Component subject = null,
            float damage = 0f, bool staggered = false, bool lethal = false,
            AttributeId? ruleAttribute = null, RelationId? ruleRelation = null,
            Vector2 size = default, bool sustained = false)
        {
            Kind = kind; Position = position; Direction = direction; Subject = subject;
            Damage = damage; Staggered = staggered; Lethal = lethal;
            RuleAttribute = ruleAttribute; RuleRelation = ruleRelation;
            Size = size; Sustained = sustained;
        }
    }
}
