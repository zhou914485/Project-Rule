using System.Collections.Generic;
using System;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Gameplay.Combat;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>
    /// 巡逻炸弹：挂着 <see cref="TrackMotor"/> 沿固定轨道走，**玩家一碰到就立刻炸**，没有引信。
    /// </summary>
    /// <remarks>
    /// <para><b>和 <c>CityBomb</c> 的区别</b>：那个是「按钮点火 → 引信倒计时 → 定点炸」，
    /// 站桩不动；这个是「沿轨道巡逻 → 接触即爆」。两者的伤害出口、爆炸半径语义完全一致。</para>
    ///
    /// <para><b>为什么用显式重叠查询而不是 OnTriggerEnter2D</b>：主角是 Kinematic 刚体，
    /// 位移由 <c>Slide</c> 自己解算，触发器回调在「两个 Kinematic 之间」不可靠。
    /// 显式查一遍自己的形状（与 <c>CityDamageZone</c> 同一条路）才稳。</para>
    ///
    /// <para><b>碰到 = 身体压到炸弹的碰撞体</b>：只看有阵营且阵营是玩家的实体
    /// （<c>Faction.Player</c>），不看受击箱 —— 受击箱是「能被打中」的判定，
    /// 跟「踩到地雷」不是一件事。</para>
    ///
    /// <para><b>伤害一律交给 CombatSystem</b>：这里只提交基础伤害，缩放 / 反转 / 因果
    /// 全部由规则系统在 <c>DamageProcessor</c> 里结算（与所有攻击同一条出口）。</para>
    ///
    /// <para><b>炸完不消失、只是哑掉</b>：碰撞体与图关闭、轨道停住，实体留着，
    /// 于是快照回溯能把炸弹原地复活（<c>CityBomb</c> 也是这个约定）。</para>
    /// </remarks>
    [RequireComponent(typeof(TrackMotor))]
    public sealed class CityTrackBomb : CityMechanism
    {
        [Header("爆炸")]
        [Tooltip("爆炸半径（世界单位）。")]
        [Min(0.1f)] public float Radius = 3f;

        [Tooltip("基础伤害。规则缩放由 CombatSystem 结算。")]
        public float Damage = 30f;

        [Tooltip("击退强度。")]
        public float Knockback = 10f;

        [Tooltip("爆炸波及的层。默认全部。")]
        public LayerMask Targets = ~0;

        [Tooltip("伤害出口。留空会在运行时自动找场景里的 CombatSystem。")]
        public CombatSystem Combat;
        public event Action OnDetonated;

        [Header("引爆")]
        [Tooltip("玩家碰到就爆。关掉 = 只能被开关联动引爆。")]
        public bool DetonateOnContact = true;

        private TrackMotor _track;
        private bool _spent;
        private readonly Collider2D[] _hits = new Collider2D[24];
        private readonly HashSet<IDamageable> _targets = new HashSet<IDamageable>();

        protected override void Awake()
        {
            base.Awake();
            _track = GetComponent<TrackMotor>();
        }

        private void Start()
        {
            if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>();
        }

        /// <summary>开关联动入口：按钮 / 摇杆一拉就立刻引爆（不看引信）。</summary>
        public void SetOn(bool on)
        {
            if (on) Detonate();
        }

        private void FixedUpdate()
        {
            if (Paused || _spent || !DetonateOnContact || Shape == null) return;

            // 只看身体碰撞体：受击箱是触发器，useTriggers = false 天然排除它。
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Targets);
            int count = Shape.Overlap(filter, _hits);
            for (int i = 0; i < count; i++)
            {
                var faction = _hits[i].GetComponentInParent<IFactionMember>();
                if (faction == null || faction.Faction != Faction.Player) continue;
                Detonate();
                return;
            }
        }

        /// <summary>引爆：先结算范围内的伤害，再把自己哑掉。伤害出口没接上时不引爆、不消耗。</summary>
        private void Detonate()
        {
            if (_spent || Paused) return;
            // 未配置伤害出口时明确等待，不表现成已经成功爆炸。
            if (Combat == null || Combat.Processor == null) return;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Targets);
            int count = Physics2D.OverlapCircle(Body.position, Mathf.Max(0.1f, Radius), filter, _hits);
            _targets.Clear();
            for (int i = 0; i < count; i++)
            {
                var target = _hits[i].GetComponentInParent<IDamageable>();
                if (target != null && _targets.Add(target)) Combat.ApplyDamage(this, target, Damage, Knockback);
            }

            _spent = true;
            if (_track != null) _track.SetOn(false);
            Shape.enabled = false;
            SetVisible(false);
            OnDetonated?.Invoke();
        }

        private struct State
        {
            public bool Spent;
            public object Track;
        }

        protected override object CaptureMechanismState() => new State
        {
            Spent = _spent,
            Track = _track != null ? _track.CaptureTrackState() : null,
        };

        protected override void RestoreMechanismState(object state)
        {
            if (!(state is State saved)) return;
            _spent = saved.Spent;
            if (_track != null && saved.Track != null) _track.RestoreTrackState(saved.Track);
        }
    }
}
