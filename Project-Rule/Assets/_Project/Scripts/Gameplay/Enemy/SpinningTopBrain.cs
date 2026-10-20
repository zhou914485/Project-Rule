using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>旋转平移，按真实接触法线及双方质量交换等大反向冲量。</summary>
    public sealed class SpinningTopBrain : EnemyBrain
    {
        private float _heading;
        private readonly Rigidbody2D _body;
        private readonly Dictionary<Collider2D, float> _contacts = new Dictionary<Collider2D, float>();
        public override float ContactKnockback => 0f;
        protected override string FirstAction => "Spin";
        protected override string[] ActionNames => new[] { "Spin", "Hurt" };
        public SpinningTopBrain(EnemyActor actor) : base(actor)
        {
            _body = actor.GetComponent<Rigidbody2D>();
            _heading = Config.TopInitialDirection < 0f ? -1f : 1f;
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Spin") Actor.Cue("Spin", Actor.Duration("Spin"), true);
        }
        protected override void TickAction(string name)
        {
            if (name == "Hurt") { if (Finished) Go("Spin"); return; }
            if (AtArenaEdge(Actor.Motor.WorldDirection(_heading))) _heading = -_heading;
            Actor.Motor.Intent = Vector2.right * _heading;
            Actor.FaceDirection(Actor.Motor.Intent);
        }
        public override bool OnMotorCollision(Collider2D collider, Vector2 normal, Vector2 velocity)
        {
            if (Actor.Defeated || collider == null || collider.isTrigger) return false;
            Rigidbody2D other = collider.attachedRigidbody;
            if (other == _body || (_contacts.TryGetValue(collider, out float until) && Age < until)) return false;
            // 支撑面的微小重力接触不是一次撞击；只在向表面真正靠近时反弹。
            Vector2 otherVelocity = other != null ? other.linearVelocity : Vector2.zero;
            var otherMotor = other != null ? other.GetComponent<EnemyMotor>() : null;
            var momentum = other != null ? other.GetComponent<IMomentumSource>() : null;
            var surface = collider.GetComponentInParent<IMovingSurface>();
            if (otherMotor != null) otherVelocity = otherMotor.ContactVelocity;
            else if (momentum != null) otherVelocity = momentum.Momentum / Mathf.Max(.1f, other.mass);
            else if (surface != null) otherVelocity = surface.Velocity;
            float closing = -Vector2.Dot(velocity - otherVelocity, normal);
            if (closing < .35f) return false;
            var receiver = other != null ? other.GetComponent<IKnockbackReceiver>() : null;
            bool movable = other != null && (other.bodyType == RigidbodyType2D.Dynamic || receiver != null);
            if (other != null && other.GetComponent<EnemyActor>()?.Brain?.Anchored == true) movable = false;
            float inverseMass = 1f / Mathf.Max(.1f, _body.mass);
            float otherInverse = movable ? 1f / Mathf.Max(.1f, other.mass) : 0f;
            Vector2 impulse = normal * ((1f + Mathf.Clamp01(Config.TopRestitution)) * closing / (inverseMass + otherInverse));
            Actor.Motor.AddImpulse(impulse);
            if (movable)
            {
                if (otherMotor != null) otherMotor.AddImpulse(-impulse);
                else if (receiver != null) receiver.AddImpulse(-impulse * otherInverse);
                else other.AddForce(-impulse, ForceMode2D.Impulse);
            }
            if (Mathf.Abs(normal.x) > .5f) _heading = Actor.Motor.WorldDirection(Mathf.Sign(normal.x));
            // 限定缓存大小，长时间关卡中不停碰到新对象也不会无限增长。
            if (_contacts.Count >= 64) _contacts.Clear();
            _contacts[collider] = Age + Mathf.Max(.05f, Config.TopImpactCooldown);
            return true;
        }
        private sealed class Data { public float Heading; public KeyValuePair<Collider2D, float>[] Contacts; }
        protected override object CaptureExtra()
        {
            var contacts = new KeyValuePair<Collider2D, float>[_contacts.Count];
            int i = 0; foreach (var pair in _contacts) contacts[i++] = pair;
            return new Data { Heading = _heading, Contacts = contacts };
        }
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
            _heading = data.Heading; _contacts.Clear();
            foreach (var pair in data.Contacts) if (pair.Key != null) _contacts[pair.Key] = pair.Value;
        }
    }
}
