using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 伤害处理模块：**全游戏唯一的伤害出口**。
    /// </summary>
    /// <remarks>
    /// <para><b>它读 <see cref="IAttributeManager"/>，不读 <c>IRuleSystem</c>。</b>
    /// 属性管理器就是「规则生效后的事实来源」；去解释「现在有哪些规则生效」
    /// 等于把算法搬回战斗侧，违反 D10。</para>
    ///
    /// <para><b>它不认识「玩家 / 敌人」这种类型</b>，但<b>认识阵营</b> ——
    /// 阵营是从 <see cref="IFactionMember"/> 读出来的**数据**，不是类型判断。</para>
    ///
    /// <para><b>边界</b>：抗性 / 无敌帧不进本模块（约束 C4，那些写在承受方的
    /// <c>ApplyDamage</c> 里）。模块负责「算多少」，承受方负责「扣不扣」。</para>
    ///
    /// <para><b>纯 C# 类</b>，构造注入依赖，不取单例 —— 这样它可被单元测试。
    /// 由 <c>CombatSystem</c> 持有并驱动。</para>
    /// </remarks>
    public sealed class DamageProcessor
    {
        /// <summary>「扩散到周围」的半径。⚠️ 待策划确认。</summary>
        public const float SpreadRadius = 3f;

        private readonly IAttributeManager _attributes;
        private readonly IWorldRegistry     _registry;

        /// <summary>挂账账本：逐实体**净额**（伤害记正、治疗记负）。</summary>
        private readonly Dictionary<IDamageable, float> _deferred = new Dictionary<IDamageable, float>();

        /// <summary>上一帧 <c>DamageDeferred</c> 的值，用来捕捉 true → false 的翻转。</summary>
        private bool _wasDeferring;

        // 复用的缓冲，避免每次攻击都分配
        private readonly List<IDamageable> _receivers  = new List<IDamageable>();
        private readonly List<IDamageable> _candidates = new List<IDamageable>();

        /// <summary>一次伤害被真正施加后触发（含挂账结算、含扩散产生的额外目标）。</summary>
        public event Action<DamageInfo> OnDamageApplied;

        /// <summary>挂账账本里当前还有几个实体在等结算。仅用于调试与验证。</summary>
        public int PendingCount => _deferred.Count;

        public DamageProcessor(IAttributeManager attributes, IWorldRegistry registry)
        {
            _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
            _registry   = registry;
        }

        // ==================================================================
        //  入口
        // ==================================================================

        /// <summary>
        /// 结算一次攻击并把结果交给承受方。
        /// </summary>
        /// <returns>实际被施加（或记账）的目标数；<b>0 表示这一击无效</b>。</returns>
        public int Process(in AttackInfo attack)
        {
            AttributeValues v = _attributes.Values;

            // ① 伤害失效：攻击与回血都无效，连命中都不算
            if (v.DamageDisabled) return 0;

            // ② 伤害缩放 + ③ 伤害反转（攻击 ↔ 回血）
            float amount = attack.BaseAmount * v.DamageScale;
            if (v.DamageInverted) amount = -amount;

            // ④ 因果削弱
            amount *= v.CausalityScale;

            // ⑤ 因果失效之一：倍率随机
            if (v.CausalityRandom) amount *= RollMultiplier();

            if (Mathf.Approximately(amount, 0f)) return 0;

            bool    healing         = amount < 0f;
            Faction attackerFaction = ResolveFaction(attack.Attacker);

            _receivers.Clear();

            if (v.CausalityInverted)
            {
                // ⑥ 因果反转：**作用对象转换** —— 伤害落到发起方自己身上。
                //    注意这是一次替换、不是反弹：不递归，不需要深度上限。
                //    发起方自己不可被伤害（没有 IDamageable）时本击作废。
                var self = ResolveDamageable(attack.Attacker);
                if (self == null) return 0;
                _receivers.Add(self);
            }
            else
            {
                // 目标集合 = 命中判定给出的精确结果
                if (attack.Targets != null)
                {
                    for (int i = 0; i < attack.Targets.Count; i++)
                        if (attack.Targets[i] != null) _receivers.Add(attack.Targets[i]);
                }

                // ⑦ 因果增强：扩散
                //    攻击 → 以 Origin 为心、半径内、**非**发起方阵营（不打自己人）
                //    治疗 → 同阵营、**不限距离**（「全队」；玩家是独阵营，所以对玩家等于只有自己）
                if (v.CausalitySpread)
                {
                    CollectCandidates(attack.Origin, healing ? 0f : SpreadRadius,
                                      attackerFaction, healing, _candidates);

                    for (int i = 0; i < _candidates.Count; i++)
                        if (!_receivers.Contains(_candidates[i])) _receivers.Add(_candidates[i]);
                }

                // ⑧ 因果失效之二：作用对象随机
                //    在候选集里换一个对象；候选集为空就退回精确命中的结果（不让攻击凭空消失）。
                if (v.CausalityRandom)
                {
                    CollectCandidates(attack.Origin, SpreadRadius, attackerFaction, healing, _candidates);

                    if (_candidates.Count > 0)
                    {
                        for (int i = 0; i < _receivers.Count; i++)
                            _receivers[i] = _candidates[UnityEngine.Random.Range(0, _candidates.Count)];
                        Deduplicate(_receivers);
                    }
                }
            }

            if (_receivers.Count == 0) return 0;

            // ⑨ 挂账 or 立即施加
            // 随机倍率若把攻击变成治疗，恢复共享仍需覆盖所有存活实体，而非仅随机到的一个。
            if (healing && v.CausalityRecoveryShared)
                CollectCandidates(attack.Origin, 0f, attackerFaction, true, _receivers, true);

            bool deferring = v.DamageDeferred;
            for (int i = 0; i < _receivers.Count; i++)
            {
                if (!deferring)
                {
                    Emit(_receivers[i], attack.Attacker, amount, attack.Knockback);
                    continue;
                }

                // 挂账**只挂伤害，击退照常施加** —— 被打飞的手感和掉血的账本是两回事，
                // 把击退也推迟会让这一下看起来完全没打中。
                // 走同一个出口，但伤害填 0：承受方会因为 Amount 为 0 而只推不扣。
                Defer(_receivers[i], amount);

                if (!Mathf.Approximately(attack.Knockback, 0f))
                    Emit(_receivers[i], attack.Attacker, 0f, attack.Knockback);
            }
            return _receivers.Count;
        }

        /// <summary>
        /// 每帧调一次，用来捕捉 <c>DamageDeferred</c> 从 true 翻回 false 的时刻并**结清账本**。
        /// </summary>
        /// <remarks>
        /// 这样模块仍然只读 <see cref="IAttributeManager"/>，不必去订阅 <c>IRuleSystem</c> 的事件
        /// —— 提案里那条「别搞反」的依赖约束。代价是最多延迟一帧结算。
        /// </remarks>
        public void Tick()
        {
            bool deferring = _attributes.Values.DamageDeferred;
            if (deferring == _wasDeferring) return;
            _wasDeferring = deferring;

            if (deferring)
            {
                // 新开一个挂账窗口，旧账作废（正常情况下旧账早该结清了）
                _deferred.Clear();
                return;
            }

            SettleAll();
        }

        // ==================================================================
        //  挂账
        // ==================================================================

        /// <summary>
        /// 记账：伤害记正、治疗记负，按**净额**累积。
        /// </summary>
        /// <remarks>
        /// <b>只有伤害进账本</b> —— 击退在 <see cref="Process"/> 里当场就推了，
        /// 所以账本里永远是纯标量，结算时用 0 击退。
        /// </remarks>
        private void Defer(IDamageable receiver, float amount)
        {
            _deferred.TryGetValue(receiver, out float pending);
            _deferred[receiver] = pending + amount;
        }

        /// <summary>
        /// 规则结束：一次性施加每个实体的净额。
        /// </summary>
        /// <remarks>击退不在这里补 —— 它在挂账当时就已经施加过了。</remarks>
        private void SettleAll()
        {
            foreach (KeyValuePair<IDamageable, float> pair in _deferred)
            {
                // ⚠️ IDamageable 是接口，pair.Key != null 走的是普通引用比较，
                //    已经 Destroy 的 MonoBehaviour 引用并不为 null —— 会被漏掉然后抛
                //    MissingReferenceException。必须用 Unity 的重载比较。
                if (pair.Key is UnityEngine.Object unityObject && unityObject == null) continue;
                if (Mathf.Approximately(pair.Value, 0f)) continue;

                Emit(pair.Key, null, pair.Value, 0f);
            }
            _deferred.Clear();
        }

        // ==================================================================
        //  施加
        // ==================================================================

        private void Emit(IDamageable receiver, object attacker, float amount, float knockback)
        {
            var info = new DamageInfo
            {
                Attacker  = attacker,
                Target    = receiver,
                Amount    = amount,
                Knockback = knockback,
            };

            receiver.ApplyDamage(info);
            OnDamageApplied?.Invoke(info);
        }

        // ==================================================================
        //  候选集
        // ==================================================================

        /// <summary>
        /// 查对象注册表收集候选目标。
        /// </summary>
        /// <param name="radius">0 或负数表示<b>不限距离</b>。</param>
        /// <param name="sameFaction">true = 只取发起方**同阵营**；false = 只取**敌对阵营**。</param>
        private void CollectCandidates(Vector2 origin, float radius, Faction attackerFaction,
                                       bool sameFaction, List<IDamageable> results, bool anyFaction = false)
        {
            results.Clear();
            if (_registry == null) return;

            IReadOnlyList<IRuleAffected> all = _registry.All;
            float r2 = radius * radius;
            bool  limited = radius > 0f;

            for (int i = 0; i < all.Count; i++)
            {
                Rigidbody2D body = all[i].Body;
                if (body == null || !body.gameObject.activeInHierarchy) continue;

                if (limited && (body.position - origin).sqrMagnitude > r2) continue;

                var target = body.GetComponentInParent<IDamageable>();
                if (target == null) continue;                    // 不可命中

                var member = body.GetComponentInParent<IFactionMember>();
                if (member == null) continue;                    // 没阵营信息：不进候选集

                // 不打自己人：攻击只找敌对阵营，治疗只找同阵营
                if (!anyFaction && (member.Faction == attackerFaction) != sameFaction) continue;

                var status = body.GetComponentInParent<RuleGame.Gameplay.Player.EntityStatus>();
                if (status != null && status.Health <= 0f) continue;
                if (!results.Contains(target)) results.Add(target);
            }
        }

        private static void Deduplicate(List<IDamageable> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
                if (list.IndexOf(list[i]) != i) list.RemoveAt(i);
        }

        // ==================================================================
        //  阵营
        // ==================================================================

        /// <summary>从任意形式的实体引用上找它自己的 <see cref="IDamageable"/>（因果反转要用）。</summary>
        private static IDamageable ResolveDamageable(object entity)
        {
            switch (entity)
            {
                case IDamageable damageable:
                    return damageable;

                case Component component:
                    return component.GetComponentInParent<IDamageable>();

                case GameObject go:
                    return go.GetComponentInParent<IDamageable>();

                default:
                    return null;
            }
        }

        /// <summary>
        /// 从任意形式的实体引用上找阵营。
        /// </summary>
        /// <remarks>发起方传进来的类型不固定（<c>PlayerController</c> 传的是 <c>this</c>，
        /// 调试工具传的是 <c>transform</c>），所以这里照 <c>EntityDamageable</c>
        /// 解析击退方向的同一套写法处理。找不到时按 <see cref="Faction.Enemy"/>。</remarks>
        private static Faction ResolveFaction(object entity)
        {
            switch (entity)
            {
                case IFactionMember member:
                    return member.Faction;

                case Component component:
                {
                    var found = component.GetComponentInParent<IFactionMember>();
                    return found != null ? found.Faction : Faction.Enemy;
                }

                case GameObject go:
                {
                    var found = go.GetComponentInParent<IFactionMember>();
                    return found != null ? found.Faction : Faction.Enemy;
                }

                default:
                    return Faction.Enemy;
            }
        }

        /// <summary>「正常 / 2 倍 / -1 倍」里等概率取一个（-1 倍 = 效果反向）。</summary>
        private static float RollMultiplier()
        {
            switch (UnityEngine.Random.Range(0, 3))
            {
                case 0:  return 1f;
                case 1:  return 2f;
                default: return -1f;
            }
        }
    }
}
