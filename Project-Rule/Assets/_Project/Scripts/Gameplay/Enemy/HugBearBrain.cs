using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class HugBearBrain : FieldMinionBrain
    {
        private PlayerController _held;
        private float _damageAt;
        private bool _standing;
        private float _threatAt = -1f, _backstepUntil, _evadeReady;
        protected override string FirstAction => "Idle";
        protected override string AfterHurtAction => CanPursue || ContactInReach ? (_standing ? "Chase" : "Stand") : (_standing ? "Return" : "Idle");
        public override bool MovementEnabled => base.MovementEnabled && StateName != "Hug";
        public HugBearBrain(EnemyActor actor) : base(actor) { }
        private bool ContactInReach => TouchesTarget && !Physics2D.Linecast(Body.bounds.center,
            TargetBody != null ? TargetBody.bounds.center : Actor.Target.position, Actor.Motor.SolidMask);
        private bool HandleContact()
        {
            if (!ContactInReach) return false;
            Actor.FaceTarget(); Actor.Motor.Stop();
            if (CooldownAge >= ReadyAt) Go("Hug");
            else CueMovement(false);
            return true;
        }
        private bool CooldownFootwork()
        {
            if (CooldownAge >= ReadyAt || !CanPursue || !SeesTarget) { _threatAt = -1f; return false; }
            if (!TargetThreatening) _threatAt = -1f;
            else if (_threatAt < 0f) _threatAt = CooldownAge;
            if (_threatAt >= 0f && CooldownAge - _threatAt >= .15f && CooldownAge >= _evadeReady)
            { _backstepUntil = CooldownAge + .35f; _evadeReady = CooldownAge + .9f; }
            if (CooldownAge >= _backstepUntil || Actor.Distance > Config.AttackRange * 1.35f) return false;
            Actor.FaceTarget();
            float away = -Actor.Facing;
            if (AtArenaEdge(away) || Actor.Motor.HasHorizontalObstacle(away)) return false;
            Actor.Motor.Intent = Vector2.right * away;
            CueMovement(true); return true;
        }
        private void Hold()
        {
            if (HasTarget) _held = Actor.Target.GetComponentInParent<PlayerController>();
            if (_held != null) _held.SetMovementHeld(this, true);
        }
        public override void OnDisabled()
        {
            if (_held != null) _held.SetMovementHeld(this, false);
            _held = null;
        }
        protected override void EnterBehavior(string name)
        {
            OnDisabled();
            if (name == "Idle") _standing = false;
            if (name == "Stand" || name == "Chase" || name == "Return" || name == "Hug") _standing = true;
            if (name == "Hug") { _damageAt = CooldownAge; Actor.Motor.Stop(); Hold(); }
            if (name == "Chase" || name == "Return") Actor.Cue("Walk", Actor.Duration("Walk"), true);
        }
        protected override void CueMovement(bool moving)
        {
            if (moving) { base.CueMovement(true); return; }
            // Idle 原画是坐姿；交战中站定等待应保持站起后的完整身体。
            if (Actor.AnimationName != "Ready") Actor.Cue("Ready", Actor.Duration("Idle"), true);
        }
        protected override void TickBehavior(string name)
        {
            switch (name)
            {
                case "Idle": if (SeesTarget || ContactInReach) Go("Stand"); break;
                case "Stand": if (Finished) Go("Chase"); break;
                case "Chase":
                    if (CooldownFootwork()) break;
                    // 回巢距离只限制追逐；被玩家贴身堵住时仍应拥抱，不能先被 Return 分支截走。
                    if (HandleContact()) break;
                    if (!CanPursue && Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) break;
                    if (!CanPursue) { Go("Return"); break; }
                    Chase(); CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                    break;
                case "Hug":
                    if (!ContactInReach || CooldownElapsed >= Config.MinionHugDuration)
                    {
                        ReadyAt = CooldownAge + Config.AttackCooldown; Go("Chase"); break;
                    }
                    if (CooldownAge >= _damageAt)
                    {
                        _damageAt = CooldownAge + Config.MinionHugTickInterval;
                        // 拥抱持续伤害不推开被抱住的玩家；外部攻击仍可正常打断。
                        Actor.AreaAttack(Config.AttackRange, 0f);
                    }
                    break;
                case "Return":
                    if (HandleContact()) break;
                    if (CanPursue && SeesTarget) { Go("Chase"); break; }
                    if (Config.MinionNavigationEnabled)
                    {
                        Actor.Navigation.MoveTo(Home, Home, Age);
                        if (Vector2.Distance(Actor.transform.position, Home) <= .25f) Go("Sit");
                        else CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                        break;
                    }
                    float delta = Home.x - Actor.transform.position.x;
                    Actor.Motor.Intent = Mathf.Abs(delta) > .15f ? Vector2.right * Mathf.Sign(delta) : Vector2.zero;
                    Actor.FaceDirection(Actor.Motor.Intent);
                    if (Mathf.Abs(delta) <= .15f) Go("Sit");
                    else
                    {
                        if (Actor.Motor.HasHorizontalObstacle(Mathf.Sign(delta))) Actor.Motor.Stop();
                        CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                    }
                    break;
                case "Sit":
                    if (SeesTarget || ContactInReach) Go("Stand");
                    else if (Finished) Go("Idle");
                    break;
            }
        }
        public override void OnHit(float damage)
        {
            if (StateName == "Hug" && damage > 0f) { ReadyAt = CooldownAge + Config.AttackCooldown; Go("Chase"); }
            base.OnHit(damage);
        }
        private sealed class Data { public float DamageAt, ThreatAt, BackstepUntil, EvadeReady; public bool Standing; }
        protected override object CaptureBehavior() => new Data { DamageAt = _damageAt, Standing = _standing, ThreatAt = _threatAt, BackstepUntil = _backstepUntil, EvadeReady = _evadeReady };
        protected override void RestoreBehavior(object state)
        {
            if (state is Data data) { _damageAt = data.DamageAt; _standing = data.Standing; _threatAt = data.ThreatAt; _backstepUntil = data.BackstepUntil; _evadeReady = data.EvadeReady; }
            if (StateName == "Hug") Hold();
        }
    }
}
