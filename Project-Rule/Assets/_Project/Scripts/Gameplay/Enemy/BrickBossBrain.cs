using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>快步压近、锁定重击与有限连招；分裂阶段仍只触发一次。</summary>
    public sealed class BrickBossBrain : EnemyBrain
    {
        private readonly bool[] _thresholds = new bool[3];
        private float _nextAdvance, _advanceFacing;
        private int _combo;
        private string _lastAttack;
        private Vector2 _throwDirection;
        protected override string[] ActionNames => new[] { "Idle", "Walk", "Advance", "HammerPlayer", "HammerGround", "Throw", "Hurt" };
        public BrickBossBrain(EnemyActor actor) : base(actor) { }

        private sealed class Data
        {
            public bool[] Thresholds;
            public float Advance, Facing;
            public int Combo;
            public string LastAttack;
            public Vector2 Throw;
        }
        protected override object CaptureExtra() => new Data
        {
            Thresholds = (bool[])_thresholds.Clone(), Advance = _nextAdvance, Facing = _advanceFacing,
            Combo = _combo, LastAttack = _lastAttack, Throw = _throwDirection
        };
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
            System.Array.Copy(data.Thresholds, _thresholds, 3);
            _nextAdvance = data.Advance; _advanceFacing = data.Facing;
            _combo = data.Combo; _lastAttack = data.LastAttack; _throwDirection = data.Throw;
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive") return;
            float ratio = Actor.Status.Health / Config.Health;
            for (int i = 0; i < 3; i++)
            {
                if (_thresholds[i] || ratio > 0.75f - i * .25f) continue;
                _thresholds[i] = true;
                Transition(i == 1 ? "Angry" : "Split", i + 2);
                return;
            }
        }
        protected override void TickTransition()
        {
            if (TransitionName == "Split" && Impact(.6f)) Actor.SpawnMinions(false, 3);
        }
        protected override void AfterTransition(string name)
        {
            if (name == "Angry") { Actor.CastRule(); Transition("Split", Phase); }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Advance")
            {
                _advanceFacing = Actor.Facing;
                _nextAdvance = Age + Mathf.Max(.5f, Config.BrickAdvanceCooldown);
                Actor.Cue("Walk", Actor.Duration("Walk"), true);
            }
            if (name == "HammerPlayer" || name == "HammerGround" || name == "Throw") Actor.Motor.Stop();
            if (name == "Throw")
            {
                Vector2 delta = PredictTarget(.25f) - (Vector2)Actor.transform.position;
                float travel = Mathf.Clamp(Mathf.Abs(delta.x) / Mathf.Max(1f, Config.BrickThrowSpeed), .25f, 1.2f);
                _throwDirection = (delta - Physics2D.gravity * (.45f * travel * travel * .5f)).normalized;
            }
        }
        protected override void TickAction(string name)
        {
            if (!HasTarget && name != "Hurt")
            {
                if (name != "Idle") Go("Idle");
                Actor.Motor.Intent = Vector2.zero;
                return;
            }
            switch (name)
            {
                case "Idle":
                case "Walk":
                    if (TargetVisible && Age >= NextAttack && CombatDistance <= RangedRange)
                    {
                        _combo = 0;
                        bool level = Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) <= Config.BodySize.y * .5f;
                        if (TargetInMeleeRange) Go("HammerPlayer");
                        else if (level && Age >= _nextAdvance && (CombatDistance <= RangedRange * .75f || _lastAttack == "Throw")) Go("Advance");
                        else Go(level && CombatDistance <= RangedRange * .7f && _lastAttack != "HammerGround" ? "HammerGround" : "Throw");
                        return;
                    }
                    KeepDistance("Walk", Config.AttackRange * .85f);
                    break;
                case "Advance":
                    Actor.FaceDirection(Vector2.right * _advanceFacing);
                    Actor.Motor.Intent = Vector2.right * _advanceFacing;
                    Actor.Motor.SpeedMultiplier = Mathf.Max(1f, Config.BrickAdvanceSpeedMultiplier);
                    bool crossed = (Actor.Target.position.x - Actor.transform.position.x) * _advanceFacing < 0f;
                    if (Actor.Motor.HasTerrainWallAhead(_advanceFacing)) { Go(TargetInMeleeRange ? "HammerPlayer" : "Throw"); break; }
                    if (TargetInMeleeRange || crossed || AtArenaEdge(_advanceFacing)
                        || Elapsed >= Mathf.Max(.2f, Config.BrickAdvanceDuration))
                        Go(TargetInMeleeRange ? "HammerPlayer" : "HammerGround");
                    break;
                case "HammerPlayer":
                    TrackAttackWindup();
                    if (Impact()) Actor.Attack(Config.AttackRange, true, sustained: true);
                    if (Pulses > 0 && Elapsed < Actor.AnimationDuration * .8f) Actor.Attack(Config.AttackRange, sustained: true);
                    if (Finished) FinishAttack(name);
                    break;
                case "HammerGround":
                    TrackAttackWindup();
                    if (Impact())
                    {
                        GroundWave();
                    }
                    else if (Pulses > 0 && Pulses < Mathf.Clamp(Config.BrickGroundWaves, 1, 5)
                        && Elapsed >= Actor.AnimationDuration * Config.ImpactFraction
                            + Pulses * Mathf.Max(.12f, Config.BrickWaveInterval))
                    { GroundWave(); Pulses++; }
                    if (Finished) FinishAttack(name);
                    break;
                case "Throw":
                    if (Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction && TargetVisible)
                    {
                        Actor.FaceTarget();
                        Vector2 delta = PredictTarget(.25f) - (Vector2)Actor.transform.position;
                        float travel = Mathf.Clamp(Mathf.Abs(delta.x) / Mathf.Max(1f, Config.BrickThrowSpeed), .25f, 1.2f);
                        _throwDirection = (delta - Physics2D.gravity * (.45f * travel * travel * .5f)).normalized;
                    }
                    if (Impact()) ThrowFan(_throwDirection);
                    if (Phase >= 3 && Pulses == 1 && Elapsed >= Actor.AnimationDuration * .8f)
                    {
                        ThrowFan(_throwDirection + Actor.Motor.Up * .25f);
                        Pulses++;
                    }
                    if (Finished) FinishAttack(name);
                    break;
                case "Hurt": if (Finished) { _combo = 0; Go("Idle"); } break;
            }
        }
        private void ThrowFan(Vector2 direction)
            => Actor.ShootFan(direction, Mathf.Clamp(Config.BrickThrowCount, 1, 15), Config.BrickThrowSpread,
                Config.BrickThrowSpeed, .45f, .4f);

        private void GroundWave()
        {
            Actor.Shoot(Vector2.left, 11f, 0f, .5f);
            Actor.Shoot(Vector2.right, 11f, 0f, .5f);
            if (Config.BrickSlamRange <= 0f) return;
            Vector2 feet = (Vector2)Actor.transform.position - Actor.Motor.Up * Config.BodySize.y * .5f;
            Bounds area = Actor.GroundSlamBounds;
            Actor.AttackArea(area.center, area.size);
            Actor.ShootFan(Actor.Motor.Up, Config.BrickThrowCount, 150f, 9f, .8f, .25f, origin: feet + Actor.Motor.Up * .5f);
        }
        private void FinishAttack(string name)
        {
            _lastAttack = name; _combo++;
            int limit = Mathf.Clamp(Config.BrickComboLimit - (Phase == 1 ? 1 : 0), 1, 3);
            if (TargetVisible && _combo < limit && CombatDistance <= RangedRange)
            {
                if (name == "HammerPlayer") { Go("HammerGround"); return; }
                if (name == "HammerGround") { Go(TargetInMeleeRange ? "HammerPlayer" : "Throw"); return; }
            }
            RestAfterAttack();
        }
    }
}
