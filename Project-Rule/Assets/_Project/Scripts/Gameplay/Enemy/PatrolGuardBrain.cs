using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PatrolGuardBrain : FieldMinionBrain
    {
        protected override string FirstAction => "Patrol";
        public PatrolGuardBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterBehavior(string name)
        {
            if (name == "Patrol" || name == "Chase") Actor.Cue("Walk", Actor.Duration("Walk"), true);
            else if (name == "Ignore") Actor.Cue("Idle", Actor.Duration("Idle"), true);
            if (name == "Attack") { ReadyAt = CooldownAge + Config.AttackCooldown; Actor.Motor.Stop(); }

        }
        protected override void TickBehavior(string name)
        {
            if (name == "Attack")
            {
                TickMeleeHit();
                if (Finished) Go("Ignore");
                return;
            }
            if (name == "Ignore")
            {
                // 冷却限制下一次攻击，不冻结追踪和站位反应。
                if (CanPursue)
                {
                    if (SeesTarget) Actor.FaceTarget();
                    if (SeesTarget && CombatDistance <= Mathf.Max(Config.BodySize.x * .5f + .45f, Config.AttackRange * .65f)) Actor.Motor.Stop();
                    else Chase();
                }
                else if (!ReturnHome()) Patrol();
                CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
                if (CooldownAge >= ReadyAt && CooldownElapsed >= .25f
                    && CanPursue && SeesTarget && TargetInMeleeRange) Go("Attack");
                else if (CooldownElapsed >= Config.MinionIgnoreDuration) Go("Patrol");
                return;
            }
            if (!CanPursue && Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
            if (CooldownAge >= ReadyAt && CanPursue && SeesTarget && TargetInMeleeRange) { Go("Attack"); return; }
            if (CanPursue)
            {
                if (name != "Chase") Go("Chase");
                Chase();
            }
            else
            {
                if (name != "Patrol") Go("Patrol");
                if (!ReturnHome()) Patrol();
            }
            CueMovement(Actor.Motor.Intent.sqrMagnitude > .001f);
        }
        protected override bool HandleLethal()
        {
            if (Config.Kind == EnemyKind.StoneGuard) Actor.AreaAttack(Config.MinionExplosionRadius);
            return false;
        }
    }
}
