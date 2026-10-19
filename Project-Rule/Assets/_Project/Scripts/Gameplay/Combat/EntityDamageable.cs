using System;
using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// <see cref="IDamageable"/> 的实现：抗性 → 无敌帧 → 生命 → 击退 → 死亡通知。
    /// </summary>
    /// <remarks>
    /// <para><b>抗性只写在这里</b> —— 规则系统与战斗系统都不认识任何实体的抗性（约束 C4）。</para>
    /// <para>伤害数值已由<c>伤害处理模块</c>算好（含缩放 / 反转 / 失效 / 因果，
    /// 见 <c>DamageProcessor</c>），本类只做「承受方自己的事」。<c>Amount</c> 为负表示回血。</para>
    /// <para>死亡只负责广播，不负责重开或结算 —— 那是关卡流程（P1）的事。</para>
    /// </remarks>
    [RequireComponent(typeof(EntityStatus))]
    public class EntityDamageable : MonoBehaviour, IDamageable, IFactionMember
    {
        // ==================================================================
        //  序列化字段
        // ==================================================================

        [Header("阵营")]
        [Tooltip("玩家挂 Player；其余一切可攻击实体都是 Enemy。默认 Enemy —— 现有场景不用改。")]
        [SerializeField] private Faction _faction = Faction.Enemy;

        [Header("引用")]
        [Tooltip("生命来源。留空则自动取同一物体上的 EntityStatus。")]
        [SerializeField] private EntityStatus _status;

        [Tooltip("死亡通知的出口。")]
        [SerializeField] private CombatSystem _combat;

        [Header("抗性")]
        [Tooltip("伤害减免比例：0 = 全额承受，0.5 = 只吃一半，1 = 完全免疫。")]
        [SerializeField, Range(0f, 1f)] private float _resistance;

        [Tooltip("受伤后的无敌秒数。0 = 没有无敌帧，同一帧内每次命中都结算。")]
        [SerializeField] private float _invincibleSeconds = 0.5f;

        [Tooltip("击退减免比例：0 = 全额击退，1 = 完全免疫击退。")]
        [SerializeField, Range(0f, 1f)] private float _knockbackResistance;

        [Tooltip("击退方向的抬升量：1 = 纯向上，0 = 纯水平。")]
        [SerializeField, Range(0f, 1f)] private float _knockbackLift = 0.4f;

        // ==================================================================
        //  运行时状态
        // ==================================================================

        private IKnockbackReceiver _knockbackReceiver;
        private float _invincibleUntil;
        private bool  _died;

        /// <summary>
        /// 阵营。伤害处理模块算因果扩散时用它过滤 —— **不打自己人**。
        /// </summary>
        /// <remarks>玩家独一个阵营、没有友方，所以这里只有两个值。</remarks>
        public Faction Faction => _faction;

        /// <summary>无敌帧内（或已死亡）：再来伤害也不会结算。</summary>
        public bool IsInvincible => !_died && (Invulnerable || Time.time < _invincibleUntil);

        public bool IsDead => _died;

        /// <summary>实际扣血后通知表现层；不包含无敌、治疗、挂账纯击退或快照恢复。</summary>
        public event Action<DamageInfo> OnDamageTaken;
        /// <summary>外部显式控制的免伤开关；独立于受击无敌帧和抗性，不修改共享伤害计算。</summary>
        public bool Invulnerable { get; private set; }
        public void SetInvulnerable(bool invulnerable) => Invulnerable = invulnerable;

        /// <summary>
        /// 复活：清死亡标记、回满血、给一小段无敌帧。
        /// </summary>
        /// <remarks>
        /// <para>死亡只广播、不负责收拾（见类注释），所以「活过来」也必须由外层显式调用 ——
        /// 现在的调用方是 <c>PlayerController.Respawn</c>（玩家被搬回落点时）。</para>
        /// <para>无敌帧是为了防「复活点旁边正好有伤害源」的连死循环；它至少 1 秒，
        /// 不受 <see cref="_invincibleSeconds"/> 配成 0 的影响。</para>
        /// </remarks>
        public void Revive()
        {
            _died = false;

            if (_status != null)
            {
                _status.ApplyHealthDelta(_status.HealthMax - _status.Health);
            }

            _invincibleUntil = Time.time + Mathf.Max(_invincibleSeconds, 1f);
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_status == null) _status = GetComponent<EntityStatus>();

            if (_status == null)
                Debug.LogError("[EntityDamageable] 缺少 EntityStatus，收到伤害也无处可记。", this);
            if (_combat == null)
                Debug.LogWarning("[EntityDamageable] 未指定 CombatSystem，死亡不会广播给关卡流程。", this);

            _knockbackReceiver = GetComponent<IKnockbackReceiver>();
        }

        // ==================================================================
        //  承受伤害
        // ==================================================================

        public void ApplyDamage(in DamageInfo damage)
        {
            if (_died || _status == null) return;

            // 负数是回血（伤害反转规则）：不受无敌帧与抗性限制。
            // 注意符号约定相反：DamageInfo 里「负数 = 回血」，而 ApplyHealthDelta 里「负数 = 受伤」，
            // 所以这里要取反，否则反转规则会把攻击变成掉血。
            if (damage.Amount < 0f)
            {
                _status.ApplyHealthDelta(-damage.Amount);
                return;
            }

            // 包括挂账时的零伤害击退；回血仍按上面的负伤害路径处理。
            if (Invulnerable) return;

            // 零伤害 = **纯击退**（伤害挂账期间那一下：只推不扣）。
            // 击退照常，但不进无敌帧、不动生命 —— 它本来就不是一次伤害结算。
            if (Mathf.Approximately(damage.Amount, 0f))
            {
                ApplyKnockback(damage);
                return;
            }

            if (IsInvincible) return;

            float amount = damage.Amount * (1f - _resistance);
            if (amount <= 0f) return;               // 完全免疫：不扣血、不进无敌、也不被击退

            float before = _status.Health;
            _status.ApplyHealthDelta(-amount);
            float applied = before - _status.Health;
            if (_invincibleSeconds > 0f) _invincibleUntil = Time.time + _invincibleSeconds;

            ApplyKnockback(damage);

            if (applied > 0f)
            {
                DamageInfo feedback = damage;
                feedback.Target = this;
                feedback.Amount = applied;
                OnDamageTaken?.Invoke(feedback);
            }

            if (_status.Health > 0f) return;

            _died = true;
            if (_combat != null) _combat.NotifyDied(this);
        }

        // ==================================================================
        //  击退
        // ==================================================================

        private void ApplyKnockback(in DamageInfo damage)
        {
            float strength = damage.Knockback * (1f - _knockbackResistance);
            if (strength <= 0f || _knockbackReceiver == null) return;

            _knockbackReceiver.AddImpulse(
                ResolveKnockbackDirection(transform.position, damage.Attacker, _knockbackLift) * strength);
        }

        /// <summary>
        /// 击退方向：水平背离施加方，再沿「上」抬升一点。
        /// </summary>
        /// <remarks>
        /// <para>静态公开：玩家 / 敌人走 <see cref="EntityDamageable"/>，场景机关（<c>SpringBackBody</c>）
        /// 不吃血量、但要求**同一套手感**，所以共用这一个实现，而不是各自抄一份。</para>
        /// <para><paramref name="lift"/> 就是各实体自己的 <c>_knockbackLift</c>：
        /// 抬多少是**实体自己的物理属性**，公式是共用的。</para>
        /// </remarks>
        public static Vector2 ResolveKnockbackDirection(Vector2 targetPosition, object attacker, float lift)
        {
            // 施加方拿不到位置时默认往右推（伤害数值本身仍然成立）。
            Vector2 away = Vector2.right;
            if (TryGetPosition(attacker, out Vector2 attackerPosition) &&
                !Mathf.Approximately(targetPosition.x, attackerPosition.x))
            {
                away = targetPosition.x > attackerPosition.x ? Vector2.right : Vector2.left;
            }

            // 抬升方向跟随重力，「重力反转」后击退也朝新的「上」。
            Vector2 up = Physics2D.gravity.sqrMagnitude > 0.0001f
                ? -Physics2D.gravity.normalized
                : Vector2.up;

            return (away + up * lift).normalized;
        }

        /// <summary>施加方在 <see cref="DamageInfo.Attacker"/> 里可能是任意 <c>object</c>。</summary>
        private static bool TryGetPosition(object source, out Vector2 position)
        {
            switch (source)
            {
                case Component component:
                    position = component.transform.position;
                    return true;
                case GameObject go:
                    position = go.transform.position;
                    return true;
                default:
                    position = default;
                    return false;
            }
        }
    }
}
