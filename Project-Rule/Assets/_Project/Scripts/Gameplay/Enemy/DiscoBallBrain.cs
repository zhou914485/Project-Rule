using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>固定悬挂的全周辐射源；只认受击箱，地形实时遮挡，不生成弹体。</summary>
    public sealed class DiscoBallBrain : EnemyBrain
    {
        private float _nextDamage;
        private readonly Collider2D[] _hits = new Collider2D[64];
        private readonly HashSet<IDamageable> _receivers = new HashSet<IDamageable>();
        public bool Radiating => StateName == "Radiate" && !Actor.Defeated && Actor.gameObject.activeInHierarchy;
        public override bool Anchored => true;
        public override bool ContactDamageEnabled => false;
        protected override string FirstAction => "Warning";
        protected override string[] ActionNames => new[] { "Warning", "Radiate", "Rest", "Hurt" };
        public DiscoBallBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            Actor.Motor.Stop();
            if (name == "Radiate") _nextDamage = CooldownAge;
            if (name != "Hurt") Actor.Cue(name == "Radiate" ? "Radiate" : "Idle", Actor.Duration("Idle"), true);
        }
        protected override void TickAction(string name)
        {
            if (name == "Hurt") { if (Finished) Go("Warning"); return; }
            if (name == "Warning" && CooldownElapsed >= Config.RadiationWarning) { Go("Radiate"); return; }
            if (name == "Rest" && CooldownElapsed >= Config.RadiationRest) { Go("Warning"); return; }
            if (name != "Radiate") return;
            if (CooldownElapsed >= Config.RadiationDuration) { Go("Rest"); return; }
            if (CooldownAge < _nextDamage || Actor.Combat == null) return;
            _nextDamage = CooldownAge + Mathf.Max(.05f, Config.RadiationHitInterval);
            var filter = new ContactFilter2D { useTriggers = true }; filter.SetLayerMask(Hurtbox.Mask);
            int count = Physics2D.OverlapCircle(Actor.transform.position, Config.RadiationRadius, filter, _hits);
            _receivers.Clear();
            for (int i = 0; i < count; i++)
                if (CombatSystem.TryResolveTarget(_hits[i], Actor, out IDamageable target)
                    && Exposes(_hits[i]) && _receivers.Add(target))
                    Actor.Combat.ApplyDamage(Actor, target, Config.Damage, 0f);
        }
        public float Reach(Vector2 direction)
        {
            var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(Actor.Motor.SolidMask);
            var hit = Physics2D.Raycast(Actor.transform.position, direction, filter, _ray, Config.RadiationRadius);
            return hit > 0 ? Mathf.Max(0f, _ray[0].distance) : Config.RadiationRadius;
        }
        private readonly RaycastHit2D[] _ray = new RaycastHit2D[1];
        public bool Exposes(Collider2D area)
        {
            if (area == null || !area.enabled || !area.gameObject.activeInHierarchy) return false;
            Vector2 origin = Actor.transform.position;
            Bounds bounds = area.bounds;
            // 检查实体的可见部分，不能只靠中心射线让大半身体露在掩体外仍完全免伤。
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    Vector2 point = area.ClosestPoint((Vector2)bounds.center + new Vector2(bounds.extents.x * x, bounds.extents.y * y));
                    Vector2 delta = point - origin;
                    if (delta.magnitude <= Config.RadiationRadius && (delta.sqrMagnitude < .0001f || Reach(delta.normalized) + .001f >= delta.magnitude)) return true;
                }
            return false;
        }
        protected override object CaptureExtra() => _nextDamage;
        protected override void RestoreExtra(object state) { if (state is float next) _nextDamage = next; _receivers.Clear(); }
    }
}
