using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public sealed class PlushBirdBrain : FieldMinionBrain
    {
        private Vector2 _aim;
        private int _spacing;
        private float _attackHeight;
        protected override string FirstAction => "Fly";
        public PlushBirdBrain(EnemyActor actor) : base(actor) { }
        public override bool IsFlying => !Actor.Defeated;
        protected override void EnterBehavior(string name)
        {
            if (name == "Attack") { _aim = PredictTarget(.2f); _attackHeight = Actor.transform.position.y; ReadyAt = CooldownAge + Config.AttackCooldown; }
            else { _spacing = 0; Actor.Cue("Fly", Actor.Duration("Fly", .5f), true); }
        }
        protected override void TickBehavior(string name)
        {
            if (name == "Attack")
            {
                // 前摇允许修正瞄准；锁定后保持出手朝向，不能转身向背后吐弹。
                if (Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction)
                {
                    if (SeesTarget) { _aim = PredictTarget(.2f); Actor.FaceTarget(); }
                    FlyWithSpacing(false);
                    _attackHeight = Actor.transform.position.y;
                }
                else Actor.Motor.Intent = Vector2.up * HeightIntent(_attackHeight);
                if (Impact() && HasTarget)
                {
                    Actor.Shoot(_aim - (Vector2)Actor.transform.position, Config.MinionProjectileSpeed);
                }
                if (Finished) Go("Fly");
                return;
            }
            FlyWithSpacing(true);
            if (CanPursue && CooldownAge >= ReadyAt && SeesTarget && Actor.Distance <= Config.AttackRange) Go("Attack");
        }
        private void FlyWithSpacing(bool faceTarget)
        {
            if (CanPursue && SeesTarget)
            {
                if (faceTarget) Actor.FaceTarget();
                float gap = Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x);
                float preferred = Mathf.Min(Config.MinionPreferredRange, Config.AttackRange * .8f);
                // 启动和停止使用不同阈值，目标小幅走动不会让鸟来回抽动。
                if ((_spacing > 0 && gap <= preferred + .15f) || (_spacing < 0 && gap >= preferred - .15f)) _spacing = 0;
                if (_spacing == 0)
                {
                    if (gap > preferred + .55f) _spacing = 1;
                    else if (gap < preferred - .55f) _spacing = -1;
                }
                float side = _spacing * Actor.Facing;
                if (side != 0f && (AtArenaEdge(side) || Actor.Motor.HasHorizontalObstacle(side))) side = 0f;
                Actor.Motor.Intent = Vector2.right * side;
            }
            else { _spacing = 0; Patrol(); }
            float hover = Mathf.Sin(Age * 1.5f) * .4f;
            float height = CanPursue ? (SeesTarget ? PredictTarget(.15f).y : LastSeenPosition.y) + Actor.Motor.Up.y * 1.5f : Home.y;
            Actor.Motor.Intent = new Vector2(Actor.Motor.Intent.x, HeightIntent(height + hover));
        }
        private float HeightIntent(float height)
        {
            float margin = Config.BodySize.y * .5f + .15f;
            float bottom = Actor.ArenaY.x + margin, top = Actor.ArenaY.y - margin;
            height = bottom <= top ? Mathf.Clamp(height, bottom, top) : (Actor.ArenaY.x + Actor.ArenaY.y) * .5f;
            float delta = height - Actor.transform.position.y;
            return Mathf.Abs(delta) < .05f ? 0f : Mathf.Clamp(delta, -1f, 1f);
        }
        private sealed class Data { public Vector2 Aim; public int Spacing; public float AttackHeight; }
        protected override object CaptureBehavior() => new Data { Aim = _aim, Spacing = _spacing, AttackHeight = _attackHeight };
        protected override void RestoreBehavior(object state)
        {
            if (state is Data data) { _aim = data.Aim; _spacing = data.Spacing; _attackHeight = data.AttackHeight; }
        }
    }
}
