using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 战斗系统：**命中判定** + 事件宿主。
    /// </summary>
    /// <remarks>
    /// <para><b>职责已经收窄</b>：伤害算多少归 <see cref="DamageProcessor"/>（唯一伤害出口），
    /// 这里只做「这一击打到了谁」的物理判定，以及把模块的结果广播出去。</para>
    /// <para><b>谁算被打到，只认受击箱</b>：查询一律打 <see cref="Hurtbox"/> 层（物理碰撞体不参与），
    /// 准入规则（自己不打自己 / 不打同阵营）集中在 <see cref="CanHit"/>。</para>
    /// <para><b>只读规则，不写规则</b>（架构约束）。</para>
    /// </remarks>
    public class CombatSystem : MonoBehaviour, ICombatSystem
    {
        /// <summary>单次命中判定的上限，避免每帧分配。</summary>
        private const int MaxHits = 16;

        private readonly Collider2D[]      _hits       = new Collider2D[MaxHits];
        private readonly List<IDamageable> _hitTargets = new List<IDamageable>();

        private DamageProcessor _processor;
        private readonly HashSet<EntityStatus> _energyTargets = new HashSet<EntityStatus>();
        private readonly HashSet<EntityStatus> _sharedEnergySources = new HashSet<EntityStatus>();
        private float _sharedEnergyPulse;

        public event Action<DamageInfo> OnDamageApplied;
        public event Action<object>     OnEntityDied;

        /// <summary>
        /// 伤害处理模块。**懒建** —— 规则系统没起来之前建不出来，
        /// 而第一次真的打出伤害时它一定已经起来了。
        /// </summary>
        public DamageProcessor Processor
        {
            get
            {
                if (_processor != null) return _processor;

                IAttributeManager attributes = RuleSystem.Instance?.Attributes;
                if (attributes == null) return null;        // 规则系统未就绪，本帧打不出伤害

                _processor = new DamageProcessor(attributes, WorldRegistry.Instance);
                _processor.OnDamageApplied += info => OnDamageApplied?.Invoke(info);
                return _processor;
            }
        }

        /// <summary>因果失效期间，命中回能与敌人自然回能共享给所有存活实体。</summary>
        /// <remarks>同一帧内的多个来源先累加，由 <see cref="Update"/> 统一发放一次；
        /// 避免每个敌人每帧自然回能都遍历一次世界注册表。</remarks>
        public void RestoreEnergy(EntityStatus source, float amount, bool defer = false)
        {
            if (source == null || amount <= 0f) return;
            if (RuleSystem.Instance?.Attributes.Values.CausalityRecoveryShared != true)
            { source.RestoreEnergy(amount); return; }

            _sharedEnergyPulse += amount;
            _sharedEnergySources.Add(source);
            if (!defer) FlushSharedEnergy();
        }

        private void FlushSharedEnergy()
        {
            if (_sharedEnergyPulse <= 0f) return;
            float amount = _sharedEnergyPulse;
            _sharedEnergyPulse = 0f;

            _energyTargets.Clear();
            foreach (var source in _sharedEnergySources)
                if (source != null) _energyTargets.Add(source);
            _sharedEnergySources.Clear();

            var all = WorldRegistry.Instance?.All;
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                {
                    var body = all[i].Body;
                    if (body == null || !body.gameObject.activeInHierarchy) continue;
                    var status = body.GetComponentInParent<EntityStatus>();
                    if (status != null && status.Health > 0f) _energyTargets.Add(status);
                }
            foreach (var status in _energyTargets) status.RestoreEnergy(amount);
        }
        private int RewardEnemyHits(object attacker, int hits)
        {
            if (hits > 0 && attacker is EnemyActor enemy && !enemy.Definition.IsBoss
                && RuleSystem.Instance?.Attributes.Values.CausalityRecoveryShared == true)
                RestoreEnergy(enemy.Status, enemy.Definition.EnergyPerHit * hits);
            return hits;
        }

        private void Update()
        {
            _processor?.Tick();
            FlushSharedEnergy();
        }

        // ==================================================================
        //  命中判定
        // ==================================================================

        /// <summary>
        /// 在指定矩形范围内做一次攻击判定，把命中的目标交给伤害处理模块。
        /// </summary>
        /// <remarks>
        /// <para><b>判定范围只认受击箱</b>（<see cref="Hurtbox"/> 层）—— 物理碰撞体不参与命中判定，
        /// 「能不能被打到」由场景里的受击箱决定，而不是身体碰撞体顺带决定。掩码固定取
        /// <see cref="Hurtbox.Mask"/>，调用方**不再自带 mask**（那是「谁强谁弱」散落在各处配错的根源）。</para>
        /// <para>命中判定归这里（发起方一侧），**伤害算多少归模块** ——
        /// 发起方拿不到「其余可命中实体」，因果扩散的候选集由模块自己查注册表。</para>
        /// </remarks>
        /// <returns>模块实际作用到的目标数；<b>0 表示这一击无效或打空</b>。</returns>
        public int PerformAttack(object attacker, Vector2 center, Vector2 size,
                                 float baseDamage, float knockback)
            => PerformAttack(attacker, center, size, baseDamage, knockback, null);

        /// <summary>
        /// 连续攻击窗口：发起方在每次挥刀前清空集合，窗口内按实体去重。
        /// 原有单次判定接口保持不变；伤害、因果传播和挂账仍由 DamageProcessor 处理。
        /// </summary>
        public int PerformAttack(object attacker, Vector2 center, Vector2 size,
                                 float baseDamage, float knockback, ISet<IDamageable> swingTargets)
        {
            DamageProcessor processor = Processor;
            if (processor == null) return 0;

            // 受击箱是 Trigger，所以 useTriggers 必须开。
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);

            int count = Physics2D.OverlapBox(center, size, 0f, filter, _hits);

            _hitTargets.Clear();
            for (int i = 0; i < count; i++)
            {
                if (!TryResolveTarget(_hits[i], attacker, out IDamageable target)) continue;
                if (_hitTargets.Contains(target)) continue;                            // 同一实体多个箱子只算一次
                if (swingTargets != null && swingTargets.Contains(target)) continue;    // 同一次挥砍窗口内去重

                _hitTargets.Add(target);
                swingTargets?.Add(target);
            }

            if (_hitTargets.Count == 0) return 0;           // 打空：连模块都不进

            var attack = new AttackInfo
            {
                Attacker   = attacker,
                Origin     = center,
                BaseAmount = baseDamage,
                Knockback  = knockback,
                Targets    = _hitTargets,
            };

            return RewardEnemyHits(attacker, processor.Process(attack));
        }

        // ==================================================================
        //  命中准入
        // ==================================================================

        /// <summary>
        /// 碰撞体 → 可命中目标。**命中准入的唯一实现** —— 挥砍 / 接触伤害 / 投射物 / 光束都走它，
        /// 不要在各自那边另写一套过滤。
        /// </summary>
        /// <remarks>
        /// <para>不是受击箱的碰撞体（层上有裸碰撞体 = 配置错误）在这里被丢弃。</para>
        /// <para>⚠️ 危险瓦片（刺格）**故意不走这里**：它对谁都扎，只解析受击箱、不做阵营过滤。</para>
        /// </remarks>
        public static bool TryResolveTarget(Collider2D collider, object attacker, out IDamageable target)
        {
            target = Hurtbox.ResolveOwner(collider);
            return target != null && CanHit(attacker, target);
        }

        /// <summary>
        /// 这一击允不允许落到这个承受方身上。
        /// </summary>
        /// <remarks>
        /// <para>受击箱是**所有实体共用**的一层，所以「打谁不打谁」必须有一条显式规则 ——
        /// 以前是靠「每个调用点各自配一个只含敌方层的 mask」隐式成立的，换成共用层之后就没了：</para>
        /// <list type="number">
        /// <item>自己不打自己 —— 玩家长 1.2 的攻击盒会盖到自己的受击箱，不挡就是自伤；</item>
        /// <item>双方都有阵营时只打**敌对**阵营 —— 敌人的判定同样会盖到同伴的受击箱；</item>
        /// <item>有一方没有阵营（机关 / 场景道具 / 可破坏物）就放行，免得中立物体莫名免疫。</item>
        /// </list>
        /// <para>因果反转（伤害落到发起方自己头上）走的是 <c>DamageProcessor</c> 内部的目标替换，
        /// 不经过这里，所以不会被第 1 条挡住。</para>
        /// </remarks>
        public static bool CanHit(object attacker, IDamageable owner)
        {
            if (owner == null) return false;

            if (ReferenceEquals(owner, attacker)) return false;
            if (attacker is IDamageable self && ReferenceEquals(owner, self)) return false;

            Faction? attackerFaction = FactionOf(attacker);
            Faction? ownerFaction    = FactionOf(owner);
            if (attackerFaction.HasValue && ownerFaction.HasValue)
                return attackerFaction.Value != ownerFaction.Value;

            return true;
        }

        /// <summary>
        /// 从任意形式的实体引用上找阵营；**没有阵营返回 null**（与 <c>DamageProcessor</c> 里
        /// 「找不到按 Enemy」的写法不同 —— 那是数值计算要个默认值，这里要区分「中立」）。
        /// </summary>
        private static Faction? FactionOf(object entity)
        {
            switch (entity)
            {
                case IFactionMember member:
                    return member.Faction;

                case Component component:
                {
                    var found = component.GetComponentInParent<IFactionMember>();
                    return found != null ? found.Faction : (Faction?)null;
                }

                case GameObject go:
                {
                    var found = go.GetComponentInParent<IFactionMember>();
                    return found != null ? found.Faction : (Faction?)null;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// 对单个**已知**目标施加一次攻击（也供调试工具使用）。
        /// </summary>
        /// <remarks>
        /// ⚠️ <paramref name="baseDamage"/> 是<b>基础值</b>，不是最终值 ——
        /// 它同样要过一遍伤害处理模块，所以伤害 / 因果规则对这条路径一样生效。
        /// </remarks>
        /// <returns>模块实际作用到的目标数。</returns>
        public int ApplyDamage(object attacker, IDamageable target, float baseDamage, float knockback)
        {
            if (target == null) return 0;

            DamageProcessor processor = Processor;
            if (processor == null) return 0;

            _hitTargets.Clear();
            _hitTargets.Add(target);

            var attack = new AttackInfo
            {
                Attacker   = attacker,
                Origin     = PositionOf(target),
                BaseAmount = baseDamage,
                Knockback  = knockback,
                Targets    = _hitTargets,
            };

            return RewardEnemyHits(attacker, processor.Process(attack));
        }

        /// <summary>
        /// 由实体自身在血量归零时调用（其 <see cref="IDamageable"/> 实现内部判断死亡）。
        /// </summary>
        public void NotifyDied(object entity) => OnEntityDied?.Invoke(entity);

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>取一个承受方的世界坐标，用作因果扩散的搜索圆心。</summary>
        private static Vector2 PositionOf(IDamageable target)
        {
            // IDamageable 的静态类型是接口，GameObject 不可能是它的实现
            //（GameObject 是 UnityEngine 的密封类型），所以只判 Component 分支。
            return target is Component component ? component.transform.position : Vector2.zero;
        }
    }
}
