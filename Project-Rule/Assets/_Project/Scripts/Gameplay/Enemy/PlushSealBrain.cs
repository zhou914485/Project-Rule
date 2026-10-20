using UnityEngine;
using RuleGame.Core.Rules;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PlushSealBrain : FieldMinionBrain
    {
        private Vector2 _iceAim;
        private uint _random = 7139;
        private float _wanderAt, _slideWindup, _nextSlide, _nextHowl, _nextIce;
        private bool _sliding;
        protected override string FirstAction => "Wander";
        public PlushSealBrain(EnemyActor actor) : base(actor) { ReadyAt = .25f; }
        private float Recovery => Mathf.Clamp(Config.MinionSkillInterval * .1f, .25f, .6f);
        protected override void EnterBehavior(string name)
        {
            Actor.Motor.SpeedMultiplier = 1f;
            if (name == "Wander")
            {
                ReadyAt = Mathf.Max(ReadyAt, CooldownAge + Recovery);
                CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                return;
            }
            Actor.Motor.Stop();
            if (name == "Slide")
            {
                _nextSlide = CooldownAge + Config.MinionSkillInterval;
                Heading = Actor.Facing; _slideWindup = Actor.Duration("Slide") * .35f; _sliding = false;
                Actor.Cue("Idle", _slideWindup);
            }
            if (name == "Howl") _nextHowl = CooldownAge + Config.MinionSkillInterval;
            if (name == "BellySlap")
            {
                _nextIce = CooldownAge + Config.MinionSkillInterval;
                _iceAim = PredictTarget(.25f);
            }
        }
        private bool UsefulHowl
        {
            get
            {
                if (!Actor.CanCastRule) return false;
                var active = RuleSystem.Instance?.Table.GetSlot(Config.RuleAttribute);
                return !active.HasValue || active.Value.Relation != Config.RuleRelation;
            }
        }
        private bool CanSlide()
        {
            if (!HasTarget || Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) > Config.BodySize.y) return false;
            float reach = Config.MoveSpeed * Config.MinionDashMultiplier * Config.MinionSlideDuration * Actor.Motor.GroundSpeedScale;
            if (Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x) > reach + Config.BodySize.x * .5f) return false;
            // 身体接触就是滑行的命中条件，不能把玩家身体当成地形而跳过近身攻击。
            if (Actor.Motor.HasTerrainWallAhead(Actor.Facing)) return false;
            return !Actor.Motor.Grounded || Actor.Motor.GroundAcceleration > .01f || Mathf.Abs(Actor.Motor.Velocity.x) > .2f;
        }
        private bool SelectSkill()
        {
            if (!CanPursue || !SeesTarget || CooldownAge < ReadyAt) return false;
            Actor.FaceTarget();
            float gap = Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x);
            bool slide = CooldownAge >= _nextSlide && CanSlide();
            bool ice = CooldownAge >= _nextIce;
            bool distant = gap > Mathf.Max(Config.AttackRange * 1.5f, Config.MoveSpeed * Config.MinionDashMultiplier * Config.MinionSlideDuration * .7f)
                || Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) > Config.BodySize.y;
            if (slide && !distant) Go("Slide");
            else if (ice && (distant || !slide)) Go("BellySlap");
            else if (CooldownAge >= _nextHowl && gap > Config.AttackRange * 1.25f && UsefulHowl) Go("Howl");
            else if (slide) Go("Slide");
            else if (ice) Go("BellySlap");
            else return false;
            return true;
        }
        private void CombatSpacing()
        {
            Actor.FaceTarget();
            float gap = Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x);
            float preferred = Config.AttackRange * 1.35f;
            if (gap > preferred + .35f) Chase();
            else if (gap < preferred - .6f)
            {
                float away = -Actor.Facing;
                Actor.Motor.Intent = AtArenaEdge(away) || Actor.Motor.HasHorizontalObstacle(away) ? Vector2.zero : Vector2.right * away;
                Actor.Motor.SpeedMultiplier = 1f;
            }
            else Actor.Motor.Stop();
            CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Wander")
            {
                if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
                if (SelectSkill()) return;
                if (!CanPursue && ReturnHome()) { CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f); return; }
                if (CanPursue && SeesTarget) { CombatSpacing(); return; }
                if (Age >= _wanderAt)
                {
                    _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
                    Heading = (_random & 1) == 0 ? -1f : 1f;
                    _wanderAt = Age + 1.3f + (_random % 100) * .01f;
                }
                Patrol(); CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                return;
            }
            if (name == "Slide")
            {
                if (Elapsed < _slideWindup) { Actor.Motor.Intent = Vector2.zero; return; }
                if (!_sliding)
                {
                    _sliding = true;
                    Actor.Cue("Slide", Config.MinionSlideDuration, true);
                }
                Actor.Motor.Intent = Vector2.right * Heading;
                Actor.Motor.SpeedMultiplier = Config.MinionDashMultiplier;
                if (TouchesTarget) { Actor.AreaAttack(Config.AttackRange); Go("Wander"); }
                else if (Elapsed >= _slideWindup + Config.MinionSlideDuration || Actor.Motor.HasTerrainWallAhead(Heading)) Go("Wander");
                return;
            }
            TrackMeleeWindup();
            if (name == "BellySlap" && Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction && SeesTarget)
                _iceAim = PredictTarget(.25f);
            if (Impact())
            {
                if (name == "Howl" && !Actor.CastRule()) _nextHowl = CooldownAge + Recovery;
                if (name == "BellySlap" && HasTarget)
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        Vector2 origin = _iceAim + Vector2.right * i + Actor.Motor.Up * 4f;
                        origin.x = Mathf.Clamp(origin.x, Actor.ArenaX.x + .3f, Actor.ArenaX.y - .3f);
                        origin.y = Mathf.Clamp(origin.y, Actor.ArenaY.x + .3f, Actor.ArenaY.y - .3f);
                        Actor.Shoot(-Actor.Motor.Up, 3f, 1f, .3f, origin: origin);
                    }
                }
            }
            if (Finished) Go("Wander");
        }
        protected override object CaptureBehavior() => new object[] { _iceAim, _random, _wanderAt, _slideWindup, _sliding, _nextSlide, _nextHowl, _nextIce };
        protected override void RestoreBehavior(object state)
        {
            if (state is object[] data)
            {
                _iceAim = (Vector2)data[0]; _random = (uint)data[1]; _wanderAt = (float)data[2];
                _slideWindup = (float)data[3]; _sliding = (bool)data[4];
                _nextSlide = (float)data[5]; _nextHowl = (float)data[6]; _nextIce = (float)data[7];
            }
        }
    }
}
