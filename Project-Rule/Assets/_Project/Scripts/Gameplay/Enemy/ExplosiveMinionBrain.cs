using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class ExplosiveMinionBrain : FieldMinionBrain
    {
        protected override string FirstAction => "Idle";
        public ExplosiveMinionBrain(EnemyActor actor) : base(actor) { }
        private void Arm() { if (StateName != "Armed" && StateName != "Explode") Go("Armed"); }
        protected override bool HandleLethal() { Arm(); return true; }
        public override void OnHit(float damage) { if (damage > 0f) Arm(); }
        protected override void EnterBehavior(string name)
        {
            if (name == "Armed") { Actor.Motor.Stop(); Actor.Cue("Armed", Config.MinionFuseDuration, true); }
            if (name == "Explode") Actor.Cue("Explode", Actor.Duration("Explode", .4f));
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Armed") { if (CooldownElapsed >= Config.MinionFuseDuration) Go("Explode"); return; }
            if (name == "Explode")
            {
                if (Pulses == 0) { Pulses = 1; Actor.AreaAttack(Config.MinionExplosionRadius); }
                if (Finished) { Actor.MarkDefeated(); Actor.gameObject.SetActive(false); }
                return;
            }
            if (TouchesTarget) { Arm(); return; }
            if (name == "Idle")
            {
                if (SeesTarget) Heading = Mathf.Sign(Actor.Target.position.x - Actor.transform.position.x);
                Patrol();
                if (Config.Kind == EnemyKind.ClockBomb && SeesTarget) Go("Chase");
            }
            else
            {
                Actor.Motor.SpeedMultiplier = Config.MinionDashMultiplier; Chase();
                if (!HasTarget || CooldownElapsed >= 3f || (!Config.MinionNavigationEnabled && Actor.Motor.HasHorizontalObstacle(Actor.Facing))) Arm();
            }
        }
    }
}
