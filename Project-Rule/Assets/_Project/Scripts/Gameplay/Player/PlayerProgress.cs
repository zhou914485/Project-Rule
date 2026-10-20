using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Rules;
using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 玩家的**解锁账本**：能力、规则属性、规则关系、已领取的宝箱编号。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么在玩家身上</b>：与 <c>TrainPass</c> 同一条原则 —— 宝箱是场景物件，
    /// 换一局游戏就该是另一套进度；解锁属于玩家，不属于地图，也不会跟着分区卸载丢掉。</para>
    ///
    /// <para><b>规则锁是「属性 × 关系」的交叉积</b>：开局只给
    /// <c>Gravity</c> 属性 + <c>Revert</c> 关系，所以只有「重力 × 反转」可用；
    /// 之后每个宝箱解锁**一个属性**或**一个关系**，可用格位自然长出来。</para>
    ///
    /// <para><b>不写盘</b>：只暴露 <see cref="UnlockedAbilities"/> /
    /// <see cref="UnlockedAttributes"/> / <see cref="UnlockedRelations"/> /
    /// <see cref="ClaimedSourceIds"/> 这几个小集合（约束 C5：不序列化物理状态）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerProgress : MonoBehaviour, IRuleUnlocks, ICityRewardReceiver,
        RuleGame.Infrastructure.Persistence.IRuleUnlockStore
    {
        [SerializeField] private PlayerController _player;

        [Tooltip("开局就有的规则属性。默认只有「重力」—— 配合下面的「反转」，开局只开重力×反转。")]
        [SerializeField] private AttributeId[] _startingAttributes = { AttributeId.Gravity };

        [Tooltip("开局就有的规则关系。默认只有「反转」。")]
        [SerializeField] private RelationId[] _startingRelations = { RelationId.Revert };

        [Tooltip("开发期开关：勾上 = 所有规则格位都可用。\n" +
                 "正式开局由 GameRoot.StartStreaming 调 StartNewGame() 把它关掉；\n" +
                 "直接进关卡场景调试时保持勾上，规则轮盘才能试全部词条。")]
        [SerializeField] private bool _unlockAllRulesForDevelopment = true;

        private readonly HashSet<AttributeId> _attributes = new HashSet<AttributeId>();
        private readonly HashSet<RelationId> _relations = new HashSet<RelationId>();
        private readonly HashSet<string> _claimed = new HashSet<string>();

        /// <summary>刚解锁一项能力（参数是新增的那几位）。</summary>
        public event Action<PlayerAbility> AbilityUnlocked;

        /// <summary>刚解锁一个规则属性（该属性下所有已解锁关系都变得可用）。</summary>
        public event Action<AttributeId> AttributeUnlocked;

        /// <summary>刚解锁一个规则关系（该关系下所有已解锁属性都变得可用）。</summary>
        public event Action<RelationId> RelationUnlocked;

        /// <summary>当前已解锁的能力位（存档只需要这一份）。</summary>
        public PlayerAbility UnlockedAbilities => _player != null ? _player.UnlockedAbilities : PlayerAbility.None;

        /// <summary>已解锁的规则属性（存档只需要这一串）。</summary>
        public IReadOnlyCollection<AttributeId> UnlockedAttributes => _attributes;

        /// <summary>已解锁的规则关系（存档只需要这一串）。</summary>
        public IReadOnlyCollection<RelationId> UnlockedRelations => _relations;

        /// <summary>已领取的宝箱 / 奖励编号（存档只需要这一串）。</summary>
        public IReadOnlyCollection<string> ClaimedSourceIds => _claimed;

        private void Awake()
        {
            if (_player == null) _player = GetComponent<PlayerController>();
            ResetRuleSets();
        }

        /// <summary>
        /// 正式开局：清空进度、只留 <c>_startingAttributes × _startingRelations</c>，
        /// 并把玩家的能力全部锁上。由 <c>GameRoot.StartStreaming</c> 调用。
        /// </summary>
        /// <remarks>
        /// 调试场景通常不经过 GameRoot，所以不会走到这里 —— 那边保持
        /// <see cref="_unlockAllRulesForDevelopment"/> 的默认值，轮盘照常可试全部词条。
        /// </remarks>
        public void StartNewGame()
        {
            _unlockAllRulesForDevelopment = false;
            _claimed.Clear();
            ResetRuleSets();
            _player?.SetUnlockedAbilities(PlayerAbility.None);
        }

        /// <summary>属性与关系都解锁了，这一格才可用。</summary>
        public bool IsUnlocked(AttributeId attribute, RelationId relation)
            => _unlockAllRulesForDevelopment
            || (_attributes.Contains(attribute) && _relations.Contains(relation));

        /// <summary>这个奖励编号领过了吗。回溯 / 重新流式加载后靠它挡住重复领取。</summary>
        public bool IsClaimed(string sourceId)
            => !string.IsNullOrEmpty(sourceId) && _claimed.Contains(sourceId);

        /// <summary>解锁一项能力。已经解锁时返回 false，不会重复广播。</summary>
        public bool UnlockAbility(PlayerAbility ability)
        {
            if (_player == null || !_player.UnlockAbility(ability)) return false;
            AbilityUnlocked?.Invoke(ability);
            return true;
        }

        /// <summary>解锁一个规则属性。已经解锁时返回 false。</summary>
        public bool UnlockAttribute(AttributeId attribute)
        {
            if (!_attributes.Add(attribute)) return false;
            AttributeUnlocked?.Invoke(attribute);
            return true;
        }

        /// <summary>解锁一个规则关系。已经解锁时返回 false。</summary>
        public bool UnlockRelation(RelationId relation)
        {
            if (!_relations.Add(relation)) return false;
            RelationUnlocked?.Invoke(relation);
            return true;
        }

        /// <summary>
        /// 宝箱 / 龙珠的领取入口。**先记账再兑现**：同一个编号第二次直接拒绝。
        /// </summary>
        public bool TryGrant(string sourceId, CityReward reward)
        {
            if (string.IsNullOrWhiteSpace(sourceId) || _claimed.Contains(sourceId)) return false;

            switch (reward.Kind)
            {
                case CityRewardKind.Ability:
                    if (_player == null || reward.Ability == PlayerAbility.None) return false;
                    _claimed.Add(sourceId);
                    UnlockAbility(reward.Ability);
                    return true;

                case CityRewardKind.RuleAttribute:
                    _claimed.Add(sourceId);
                    UnlockAttribute(reward.Attribute);
                    return true;

                case CityRewardKind.RuleRelation:
                    _claimed.Add(sourceId);
                    UnlockRelation(reward.Relation);
                    return true;

                default:
                    // Power / Health / Energy 目前没有玩家侧结算，交给别家接收方。
                    return false;
            }
        }

        /// <summary>场景里找玩家身上的账本（没有返回 null）。</summary>
        // ==================================================================
        //  IRuleUnlockStore：存档桥接（显式实现，避免与游戏内用的枚举集合重名冲突）
        // ==================================================================

        IReadOnlyList<string> RuleGame.Infrastructure.Persistence.IRuleUnlockStore.UnlockedAttributes
            => ToNameList(_attributes);

        IReadOnlyList<string> RuleGame.Infrastructure.Persistence.IRuleUnlockStore.UnlockedRelations
            => ToNameList(_relations);

        /// <summary>把存档里的词条名灌回来。名字对不上（旧档 / 配置改名）就跳过并留痕。</summary>
        void RuleGame.Infrastructure.Persistence.IRuleUnlockStore.RestoreUnlocks(
            IEnumerable<string> attributes, IEnumerable<string> relations)
        {
            if (attributes != null)
            {
                foreach (string name in attributes)
                {
                    if (Enum.TryParse(name, true, out AttributeId attr)) UnlockAttribute(attr);
                    else Debug.LogWarning($"[PlayerProgress] 存档里的属性词条无法识别，已跳过：\"{name}\"。", this);
                }
            }

            if (relations != null)
            {
                foreach (string name in relations)
                {
                    if (Enum.TryParse(name, true, out RelationId rel)) UnlockRelation(rel);
                    else Debug.LogWarning($"[PlayerProgress] 存档里的关系词条无法识别，已跳过：\"{name}\"。", this);
                }
            }
        }

        private static IReadOnlyList<string> ToNameList<T>(IEnumerable<T> values)
        {
            var list = new List<string>();
            foreach (T v in values) list.Add(v.ToString());
            return list;
        }

        public static PlayerProgress Find()
            => UnityEngine.Object.FindAnyObjectByType<PlayerProgress>();

        private void ResetRuleSets()
        {
            _attributes.Clear();
            _relations.Clear();

            if (_startingAttributes != null && _startingAttributes.Length > 0)
                foreach (AttributeId attribute in _startingAttributes) _attributes.Add(attribute);
            else
                _attributes.Add(AttributeId.Gravity);

            if (_startingRelations != null && _startingRelations.Length > 0)
                foreach (RelationId relation in _startingRelations) _relations.Add(relation);
            else
                _relations.Add(RelationId.Revert);
        }

        [ContextMenu("调试：解锁全部能力")]
        private void DebugUnlockAllAbilities() => UnlockAbility(PlayerAbility.All);

        [ContextMenu("调试：解锁全部规则")]
        private void DebugUnlockAllRules()
        {
            foreach (AttributeId attribute in Enum.GetValues(typeof(AttributeId))) _attributes.Add(attribute);
            foreach (RelationId relation in Enum.GetValues(typeof(RelationId))) _relations.Add(relation);
        }

        [ContextMenu("调试：按开局配置重置")]
        private void DebugStartNewGame() => StartNewGame();
    }
}
