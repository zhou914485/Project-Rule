using System;
using UnityEngine;
using System.Collections.Generic;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>按钮引爆的独立机关。保留实体并记录引信/消耗状态，伤害仍发给统一处理模块。</summary>
    public sealed class CityBomb : CityMechanism
    {
        public float Fuse = .8f, Radius = 3f, Damage = 30f, Knockback = 10f;
        public LayerMask Targets = ~0;
        public CombatSystem Combat;
        public event Action OnDetonated;
        private bool _armed, _spent;
        private float _remaining;
        private readonly Collider2D[] _hits = new Collider2D[24];
        private readonly HashSet<IDamageable> _targets = new HashSet<IDamageable>();
        private void Start() { if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>(); }
        public void SetOn(bool on)
        {
            if (!on || _armed || _spent || Paused) return;
            _armed = true; _remaining = Mathf.Max(.1f, Fuse);
            if (Visuals.Length > 0) Visuals[0].color = new Color(.95f, .25f, .1f);
        }
        private void FixedUpdate()
        {
            if (Paused || !_armed || _spent) return;
            _remaining -= Time.fixedDeltaTime;
            if (_remaining > 0f) return;
            // 未配置伤害出口时明确等待，不表现成已经成功爆炸。
            if (Combat == null || Combat.Processor == null) return;
            var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(Targets);
            int count = Physics2D.OverlapCircle(Body.position, Mathf.Max(.1f, Radius), filter, _hits);
            _targets.Clear();
            for (int i = 0; i < count; i++)
            {
                var target = _hits[i].GetComponentInParent<IDamageable>();
                if (target != null && _targets.Add(target)) Combat.ApplyDamage(this, target, Damage, Knockback);
            }
            _spent = true; _armed = false; Shape.enabled = false; SetVisible(false);
            OnDetonated?.Invoke();
        }
        private struct State { public bool Armed, Spent; public float Remaining; }
        protected override object CaptureMechanismState() => new State { Armed = _armed, Spent = _spent, Remaining = _remaining };
        protected override void RestoreMechanismState(object state) { var s = (State)state; _armed = s.Armed; _spent = s.Spent; _remaining = s.Remaining; }
    }
}
