using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class ClockMouseBrain : FieldMinionBrain
    {
        private float _previousOffset, _blockedFor;
        private int _passes;
        protected override string FirstAction => "Idle";
        public ClockMouseBrain(EnemyActor actor) : base(actor) { }
        protected override void EnterBehavior(string name)
        {
            if (name == "Idle") { ReadyAt = CooldownAge + Config.MinionMouseCooldown; Actor.Motor.Stop(); }
            if (name == "Pop")
            {
                Heading = HasTarget && Actor.Target.position.x < Actor.transform.position.x ? -1f : 1f;
                _passes = 0; _blockedFor = 0f; _previousOffset = Actor.transform.position.x - Home.x;
            }
            if (name == "Walk") Actor.Cue("Walk", Actor.Duration("Walk"), true);
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Idle") { if (CooldownAge >= ReadyAt && SeesTarget) Go("Pop"); return; }
            if (name == "Pop") { if (Finished) Go("Walk"); return; }
            if (name == "Hide") { if (Finished) Go("Idle"); return; }
            // 狭窄地形或玩家身体可能阻止经过出生点，不能因此永远卡在地面来回转向。
            if (CooldownElapsed >= Config.MinionMouseRange * 4f / Mathf.Max(.1f, Config.MoveSpeed) + 2f) { Go("Hide"); return; }
            _blockedFor = Actor.Motor.HasWallAhead(Heading) ? _blockedFor + Delta : 0f;
            if (_blockedFor >= .4f || (!CanPursue && CooldownElapsed > .6f)) { Go("Hide"); return; }
            float offset = Actor.transform.position.x - Home.x;
            if ((_previousOffset > .01f && offset <= 0f) || (_previousOffset < -.01f && offset >= 0f)) _passes++;
            _previousOffset = offset;
            if (_passes >= 2) { Go("Hide"); return; }
            Patrol(Config.MinionMouseRange);
        }
        protected override object CaptureBehavior() => new object[] { _previousOffset, _passes, _blockedFor };
        protected override void RestoreBehavior(object state)
        {
            if (state is object[] data) { _previousOffset = (float)data[0]; _passes = (int)data[1]; _blockedFor = (float)data[2]; }
        }
    }
}
