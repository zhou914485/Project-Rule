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
        RuleGame.Infrastructure.Persistence.IRuleUnlockStore,
        RuleGame.Infrastructure.Persistence.IPlayerProgressStore
    {
        [SerializeField] private PlayerController _player;

        [Tooltip("玩家的生命 / 能量组件。留空时自动取同一物体上的 EntityStatus。")]
        [SerializeField] private EntityStatus _status;

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

        // 读档回灌期间不发解锁事件：那不是「刚获得」，是「本来就有」。
        // 解锁演出（AbilityUnlockOverlay）是暂停 + 黑幕，读档被一整串黑幕堵住就卡死了。
        private bool _restoringUnlocks;

        // 宝箱给的上限增量。**账本记绝对值**，EntityStatus 那份只是影子 ——
        // 读档时用这里的数回灌，避免「场景上 0 + 档案里 40」两处各记一半。
        private float _maxHealthBonus;
        private float _maxEnergyBonus;

        /// <summary>刚解锁一项能力（参数是新增的那几位）。</summary>
        public event Action<PlayerAbility> AbilityUnlocked;

        /// <summary>刚解锁一个规则属性（该属性下所有已解锁关系都变得可用）。</summary>
        public event Action<AttributeId> AttributeUnlocked;

        /// <summary>刚解锁一个规则关系（该关系下所有已解锁属性都变得可用）。</summary>
        public event Action<RelationId> RelationUnlocked;

        /// <summary>当前已解锁的能力位（存档只需要这一份）。</summary>
        public PlayerAbility UnlockedAbilities => _player != null ? _player.UnlockedAbilities : PlayerAbility.None;

        /// <summary>四项能力全部解锁。</summary>
        public bool HasAllAbilities
            => _player != null && (_player.UnlockedAbilities & PlayerAbility.All) == PlayerAbility.All;

        /// <summary>
        /// 规则是不是已经全解锁：六个属性与五个关系都在账本里。
        /// 不看 <see cref="_unlockAllRulesForDevelopment"/> —— 那是调试开关，不是玩家进度。
        /// </summary>
        public bool HasAllRuleUnlocks
        {
            get
            {
                foreach (AttributeId attribute in Enum.GetValues(typeof(AttributeId)))
                    if (!_attributes.Contains(attribute)) return false;
                foreach (RelationId relation in Enum.GetValues(typeof(RelationId)))
                    if (!_relations.Contains(relation)) return false;
                return true;
            }
        }

        /// <summary>镇长资格判据：规则全解锁 + 能力全解锁。</summary>
        public bool HasAllUnlocks => HasAllRuleUnlocks && HasAllAbilities;

        /// <summary>已解锁的规则属性（存档只需要这一串）。</summary>
        public IReadOnlyCollection<AttributeId> UnlockedAttributes => _attributes;

        /// <summary>已解锁的规则关系（存档只需要这一串）。</summary>
        public IReadOnlyCollection<RelationId> UnlockedRelations => _relations;

        /// <summary>已领取的宝箱 / 奖励编号（存档只需要这一串）。</summary>
        public IReadOnlyCollection<string> ClaimedSourceIds => _claimed;

        /// <summary>生命上限的累计奖励增量（宝箱）。</summary>
        public float MaxHealthBonus => _maxHealthBonus;

        /// <summary>能量上限的累计奖励增量（宝箱）。</summary>
        public float MaxEnergyBonus => _maxEnergyBonus;

        /// <summary>同一物体上的生命 / 能量组件（读档早于 Awake 时也能拿到）。</summary>
        private EntityStatus Status
        {
            get
            {
                if (_status == null) _status = GetComponent<EntityStatus>();
                return _status;
            }
        }

        private void Awake()
        {
            if (_player == null) _player = GetComponent<PlayerController>();
            if (_status == null) _status = GetComponent<EntityStatus>();
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

            // 上限增量也清零：新的一局从设计初值开始（存档读回来的那条路不走这里）。
            _maxHealthBonus = 0f;
            _maxEnergyBonus = 0f;
            Status?.SetMaxVitalBonuses(0f, 0f);

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

        /// <summary>解锁一个规则属性。已经解锁时返回 false；读档回灌期间不广播事件。</summary>
        public bool UnlockAttribute(AttributeId attribute)
        {
            if (!_attributes.Add(attribute)) return false;
            if (!_restoringUnlocks) AttributeUnlocked?.Invoke(attribute);
            return true;
        }

        /// <summary>解锁一个规则关系。已经解锁时返回 false；读档回灌期间不广播事件。</summary>
        public bool UnlockRelation(RelationId relation)
        {
            if (!_relations.Add(relation)) return false;
            if (!_restoringUnlocks) RelationUnlocked?.Invoke(relation);
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

                case CityRewardKind.Health:
                    return GrantVitalMax(sourceId, reward.Amount, energy: false);

                case CityRewardKind.Energy:
                    return GrantVitalMax(sourceId, reward.Amount, energy: true);

                default:
                    // 未知奖励类型（枚举里未定义的编号）：不认账，交给别家接收方或直接失败。
                    return false;
            }
        }

        /// <summary>
        /// 生命 / 能量宝箱：把奖励量直接加在**上限**上，并把当前值同步抬高同一段。
        /// </summary>
        /// <remarks>
        /// <para>先记账再兑现（与其它奖励同一条纪律）：<c>_claimed.Add</c> 之后即使
        /// <c>EntityStatus</c> 抛错也不会出现「领了但没记」的重复领取。</para>
        /// <para><b>为什么不走 <c>RestoreEnergy</c></b>：那只是回血回蓝，上限涨不上去，
        /// 玩家吃满一条 100 的能量条就再也装不下。</para>
        /// </remarks>
        private bool GrantVitalMax(string sourceId, int amount, bool energy)
        {
            EntityStatus status = Status;
            if (status == null || amount <= 0) return false;

            _claimed.Add(sourceId);

            if (energy)
            {
                _maxEnergyBonus += amount;
                status.RaiseMaxEnergy(amount);
            }
            else
            {
                _maxHealthBonus += amount;
                status.RaiseMaxHealth(amount);
            }

            return true;
        }

        /// <summary>
        /// 读档：整体替换宝箱记录与上限增量，并把增量回灌给 <see cref="EntityStatus"/>。
        /// </summary>
        /// <remarks>
        /// <para>存的是**绝对值**，不是叠加量：读档时直接设成档里的数，不会把上一次会话的增量再加一遍。</para>
        /// <para>必须在 <c>ApplyData</c> 之前调用 —— 那个方法按当前上限 clamp 生命 / 能量。</para>
        /// </remarks>
        public void RestoreProgress(IReadOnlyCollection<string> claimedSourceIds, float healthBonus, float energyBonus)
        {
            _claimed.Clear();
            if (claimedSourceIds != null)
            {
                foreach (string id in claimedSourceIds)
                    if (!string.IsNullOrWhiteSpace(id)) _claimed.Add(id);
            }

            _maxHealthBonus = Mathf.Max(0f, healthBonus);
            _maxEnergyBonus = Mathf.Max(0f, energyBonus);

            Status?.SetMaxVitalBonuses(_maxHealthBonus, _maxEnergyBonus);
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
        /// <remarks>整段不发 <see cref="AttributeUnlocked"/> / <see cref="RelationUnlocked"/>：
        /// 读档不是「刚解锁」，不该弹解锁黑幕（那是要停游戏的）。轮盘每次打开都现读账本，不受影响。</remarks>
        void RuleGame.Infrastructure.Persistence.IRuleUnlockStore.RestoreUnlocks(
            IEnumerable<string> attributes, IEnumerable<string> relations)
        {
            _restoringUnlocks = true;
            try
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
            finally
            {
                _restoringUnlocks = false;
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
