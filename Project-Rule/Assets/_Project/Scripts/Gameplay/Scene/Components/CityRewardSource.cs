using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using UnityEngine;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>
    /// 宝箱 / 龙珠：领一次奖励，并把「已领取」记在玩家身上。
    /// </summary>
    /// <remarks>
    /// <para><b>两种打开方式</b>：<see cref="OpenByAttack"/> 关着时按 E（<c>CityInteractable</c>）；
    /// 勾上时 E 无效，必须被**攻击盒 / 飞刀**打中 —— 走 <see cref="IContactSwitch.ImpactByContact"/>，
    /// 与按钮 / 拉杆同一条路，不进伤害管线。</para>
    /// <para><b>能力 / 规则奖励</b>不逐箱拖引用：<see cref="Receiver"/> 留空时自动找玩家身上的
    /// <c>PlayerProgress</c>。这样关卡里摆一个宝箱只要填 <c>SourceId</c> + <c>Kind</c> + 对应值。</para>
    /// <para>⚠️ <b>已领取记录住在玩家身上</b>（<c>PlayerProgress.IsClaimed</c>）：回溯会把本组件的
    /// <c>_claimed</c> 一起回滚，但玩家的账本不会，于是箱子仍然是「已开」——
    /// 这正是「领奖记录必须与奖励一起提交」的实现。</para>
    /// </remarks>
    public sealed class CityRewardSource : CityInteractable, IContactSwitch
    {
        [Tooltip("每个场景实例必须有唯一 ID，不能直接沿用 prefab 的空 ID。")]
        public string SourceId;

        [Tooltip("奖励类型。全部走玩家账本（PlayerProgress）：Health / Energy 提升上限，其余解锁词条 / 能力。")]
        public CityRewardKind Kind;

        [Tooltip("Kind = Health / Energy 时生效：上限提升多少（当前值同步抬高同一段）。")]
        public int Amount = 1;

        [Header("能力 / 规则奖励")]
        [Tooltip("Kind = Ability 时生效。")]
        public PlayerAbility Ability;

        [Tooltip("Kind = RuleAttribute 时生效：解锁整个属性（该属性下已解锁的关系都能用）。")]
        public AttributeId Attribute;

        [Tooltip("Kind = RuleRelation 时生效：解锁整个关系（该关系下已解锁的属性都能用）。")]
        public RelationId Relation;

        [Tooltip("勾上 = 必须被攻击（攻击盒 / 飞刀）打中才开，E 键无效。能力 / 规则宝箱用这个。")]
        public bool OpenByAttack;

        [Tooltip("需实现 ICityRewardReceiver；留空时自动找玩家身上的 PlayerProgress。")]
        public MonoBehaviour Receiver;

        public bool PickupVisual;

        private bool _claimed;
        private ICityRewardReceiver _sink;

        private ICityRewardReceiver Sink
        {
            get
            {
                // 显式指定的 Receiver 永远优先：它可能在 Awake 之后才被接上，
                // 不能让之前自动找到的 PlayerProgress 把它顶掉。
                if (Receiver is ICityRewardReceiver explicitSink)
                {
                    _sink = explicitSink;
                    return _sink;
                }

                if (_sink != null) return _sink;

                PlayerProgress progress = PlayerProgress.Find();
                if (progress != null) _sink = progress;
                return _sink;
            }
        }

        private bool Claimable
            => !_claimed && !string.IsNullOrWhiteSpace(SourceId) && Sink != null && !Sink.IsClaimed(SourceId);

        public override bool Available
        {
            get
            {
                SyncClaimed();
                return !OpenByAttack && Claimable;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            SyncClaimed();
        }

        private void Start() => SyncClaimed();

        public override bool Interact(PlayerController player)
        {
            if (OpenByAttack || Paused || player == null || !Claimable) return false;
            return Grant(player);
        }

        // 压住型语义对宝箱没有意义：箱子不会因为石头压了一下就打开。
        public void PressByContact() { }
        public void ReleaseByContact() { }

        /// <summary>被攻击盒 / 飞刀打中：勾了 <see cref="OpenByAttack"/> 才开门。</summary>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!OpenByAttack || Paused) return;

            SyncClaimed();
            if (_claimed) return;

            PlayerProgress progress = PlayerProgress.Find();
            if (progress == null) return;

            // 账本不一定和 PlayerController 挂在同一个物体上；Grant 允许 player 为 null，
            // 会退回 Receiver / 已经解析到的 PlayerProgress。
            Grant(progress.GetComponent<PlayerController>());
        }

        private bool Grant(PlayerController player)
        {
            ICityRewardReceiver sink = ResolveSink(player);
            if (sink == null || !Claimable) return false;

            if (!sink.TryGrant(SourceId, BuildReward())) return false;

            _claimed = true;
            RefreshVisual();
            return true;
        }

        private ICityRewardReceiver ResolveSink(PlayerController player)
        {
            if (Receiver is ICityRewardReceiver explicitSink)
            {
                _sink = explicitSink;
                return _sink;
            }

            if (_sink != null) return _sink;
            if (player != null) _sink = player.GetComponent<PlayerProgress>();
            return _sink;
        }

        private CityReward BuildReward()
        {
            switch (Kind)
            {
                case CityRewardKind.Ability:
                    return new CityReward(Kind, ability: Ability);
                case CityRewardKind.RuleAttribute:
                    return new CityReward(Kind, attribute: Attribute);
                case CityRewardKind.RuleRelation:
                    return new CityReward(Kind, relation: Relation);
                default:
                    return new CityReward(Kind, Mathf.Max(1, Amount));
            }
        }

        /// <summary>玩家账本说「这个编号领过了」就把本实例也标成已开，流式加载回来不会重新变回关着。</summary>
        private void SyncClaimed()
        {
            if (_claimed) return;

            if (Sink != null && Sink.IsClaimed(SourceId))
            {
                _claimed = true;
                RefreshVisual();
            }
        }

        private void RefreshVisual()
        {
            if (Visuals == null) return; // 编辑器里直接 AddComponent（没走 Awake）时没有视觉列表
            if (PickupVisual) SetVisible(!_claimed);
            else if (_claimed && Visuals.Length > 0) Visuals[0].color = new Color(.35f, .35f, .35f);
        }

        protected override object CaptureMechanismState() => _claimed;

        protected override void RestoreMechanismState(object state)
        {
            _claimed = (bool)state || (Sink != null && Sink.IsClaimed(SourceId));
            RefreshVisual();
        }
    }
}
