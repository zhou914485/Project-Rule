using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class SpringMinionBrain : FieldMinionBrain
    {
        private float _hopAt;
        private bool _wasAirborne;
        protected override string FirstAction => "Patrol";
        public SpringMinionBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterBehavior(string name)
        {
            if (name == "Attack") { ReadyAt = CooldownAge + Config.AttackCooldown; Actor.Motor.Stop(); }
            else Actor.Cue("Hop", Actor.Duration("Hop", .5f), true);
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Attack")
            {
                TickMeleeHit();
                if (Finished) Go("Ignore");
                return;
            }
            if (name == "Ignore" && CooldownElapsed >= Config.MinionIgnoreDuration) { Go("Patrol"); return; }
            if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age))
            {
                _wasAirborne = true;
                return;
            }
            // 空中保持本次起跳方向，不在玩家换边时突然折返。
            Actor.Motor.Intent = Vector2.right * Heading;
            if (!Actor.Motor.Grounded || Vector2.Dot(Actor.Motor.Velocity, Actor.Motor.Up) > .1f)
            {
                _wasAirborne = true;
                return;
            }
            if (_wasAirborne)
            {
                _wasAirborne = false;
                _hopAt = CooldownAge + Config.MinionHopInterval;
            }
            if (!CanPursue)
            {
                if (CooldownAge < _hopAt || !ReturnHome()) Actor.Motor.Intent = Vector2.zero;
                return;
            }
            if (name == "Patrol" && SeesTarget && TargetInMeleeRange && CooldownAge >= ReadyAt) { Go("Attack"); return; }
            Actor.Motor.Intent = Vector2.zero;
            if (CooldownAge >= _hopAt)
            {
                if (Config.MinionNavigationEnabled && Physics2D.gravity.sqrMagnitude > .001f)
                {
                    Actor.Navigation.MoveTo(LastSeenPosition, Home, Age, true,
                        DesiredHopHeight());
                    if (Actor.Navigation.JumpStarted) { Pattern++; Heading = Actor.Facing; }
                    return;
                }
                Actor.FaceTarget(); Heading = Actor.Facing;
                if (Actor.Motor.HasHorizontalObstacle(Heading) && !TouchesTarget) return;
                Actor.Motor.Jump(DesiredHopHeight()); Pattern++;
                Actor.Motor.Intent = Vector2.right * Heading;
            }
        }
        private float DesiredHopHeight()
        {
            bool high = CanPursue && Vector2.Dot(LastSeenPosition - (Vector2)Actor.transform.position, Actor.Motor.Up) > Config.MinionHopHeight * .5f;
            return high || (Pattern & 1) != 0 ? Config.MinionHighHopHeight : Config.MinionHopHeight;
        }
        protected override object CaptureBehavior() => new object[] { _hopAt, _wasAirborne };
        protected override void RestoreBehavior(object state)
        {
            if (state is object[] data) { _hopAt = (float)data[0]; _wasAirborne = (bool)data[1]; }
        }
    }
}
