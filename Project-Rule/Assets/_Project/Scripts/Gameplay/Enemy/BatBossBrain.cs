using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>空中换位、预警俯冲和近身撤离；所有路线与冷却均使用游戏时钟并参加快照。</summary>
    public sealed class BatBossBrain : EnemyBrain
    {
        private float _nextSummon = 4f, _nextPoint, _nextRule, _nextDash = 1.2f;
        private float _nextEvade, _nextWaypoint;
        private int _orbitSide = 1;
        private Vector2 _destination, _dashDirection, _lastTargetPosition, _targetVelocity, _lastFlightPosition;
        private Transform _trackedTarget;
        private Vector2 _restLeft, _restRight;
        private int _restDirection = 1;
        private bool _preferMelee = true;
        protected override string FirstAction => "Fly";
        protected override string[] ActionNames => new[]
        {
            "Idle", "Fly", "Approach", "Evade", "Windup", "Dash", "Recover", "Slash", "Shake", "Roar", "WingSpread", "Point", "Hurt"
        };
        public BatBossBrain(EnemyActor actor) : base(actor) { }
        private float Tempo => 1f + (Phase - 1) * 0.15f;
        private Vector2 Position => Actor.transform.position;
        public override float ClampHealth(float health)
            => Mathf.Max(Phase == 1 ? Config.Health * 2f / 3f : Phase == 2 ? Config.Health / 3f : 0f, health);

        protected override void TickGlobal()
        {
            if (HasTarget)
            {
                Vector2 current = Actor.Target.position;
                if (_trackedTarget == Actor.Target && Delta > 0f)
                    _targetVelocity = Vector2.Lerp(_targetVelocity,
                        Vector2.ClampMagnitude((current - _lastTargetPosition) / Delta, 12f), Mathf.Clamp01(Delta * 8f));
                else _targetVelocity = Vector2.zero;
                _lastTargetPosition = current;
                _trackedTarget = Actor.Target;
            }
            else { _trackedTarget = null; _targetVelocity = Vector2.zero; }
            if (Root.ActiveStateName != "Alive") return;
            float threshold = Phase == 1 ? Config.Health * 2f / 3f : Config.Health / 3f;
            if (Phase < 3 && Actor.Status.Health <= threshold + 0.01f)
                Transition("Weak", Phase + 1, Actor.Duration("Weak", 2.5f));
        }

        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            switch (name)
            {
                case "Rest":
                    float height = Mathf.Lerp(Actor.ArenaY.x, Actor.ArenaY.y, Mathf.Clamp01(Config.BatRestHeightRatio));
                    float span = Mathf.Max(0.5f, Config.BatRestPatrolDistance);
                    _restLeft = ClampPoint(new Vector2(Position.x - span, height));
                    _restRight = ClampPoint(new Vector2(Position.x + span, height));
                    _restDirection = _orbitSide;
                    TickRest();
                    break;
                case "Fly":
                case "Idle":
                    Actor.Cue("Fly", Actor.Duration("Fly"), true);
                    ChooseFlightPoint();
                    break;
                case "Approach":
                    Actor.Cue("Fly", Actor.Duration("Fly"), true);
                    ApproachTarget();
                    break;
                case "Evade":
                    _nextEvade = Age + Mathf.Max(0.5f, Config.BatEvadeCooldown / Tempo);
                    Vector2 away = HasTarget ? Position - (Vector2)Actor.Target.position : Vector2.right * _orbitSide;
                    if (away.sqrMagnitude < 0.01f) away = Vector2.right * _orbitSide;
                    _destination = ClampPoint(Position + away.normalized * Config.BatOrbitDistance
                        + Actor.Motor.Up * Config.BatHoverHeight);
                    Actor.Cue("Fly", Config.BatEvadeDuration, true);
                    SteerTo(_destination, 1.7f * Tempo);
                    break;
                case "Windup":
                    _nextDash = Age + Mathf.Max(1f, Config.BatDashCooldown / Tempo);
                    Vector2 aim = HasTarget ? PredictTarget(.18f) : Position;
                    _dashDirection = (aim - Position).normalized;
                    if (_dashDirection.sqrMagnitude < 0.01f) _dashDirection = Vector2.right * Actor.Facing;
                    Actor.FaceDirection(_dashDirection);
                    // 进入蓄力时就锁定路线，之后玩家可以通过移动闪避。
                    Actor.Cue("DashWindup", Mathf.Max(0.25f, Config.BatDashWindup / Tempo));
                    break;
                case "Dash":
                    Actor.FaceDirection(_dashDirection);
                    Actor.Cue("Dash", Config.BatDashDuration, true);
                    break;
                case "Recover":
                    _orbitSide = -_orbitSide;
                    ChooseFlightPoint();
                    Actor.Cue("Fly", Config.BatRecoverDuration, true);
                    break;
                case "Shake":
                    Actor.Cue("Shake", Mathf.Max(.9f, 1.3f / Tempo,
                        Mathf.Clamp(Config.BatVolleyWaves, 1, 8) * Mathf.Max(.1f, Config.BatVolleyInterval) + .2f));
                    break;
            }
        }

        protected override void AfterTransition(string name)
        {
            if (name != "Weak") return;
            _nextDash = Age + 0.8f;
            NextAttack = Age + 0.35f;
            _preferMelee = true;
            _orbitSide = -_orbitSide;
            Go("Approach");
        }

        protected override void TickAction(string name)
        {
            if (!HasTarget && name != "Hurt")
            {
                if (name != "Fly") Go("Fly");
                Actor.Motor.Stop();
                return;
            }
            switch (name)
            {
                case "Idle":
                case "Fly":
                    if (Age >= NextAttack && TargetVisible && TargetInMeleeRange) { Go("Slash"); return; }
                    if (Actor.Distance < Config.AttackRange * 0.8f && Age >= _nextEvade)
                    { Go("Evade"); return; }
                    if (Age >= NextAttack && TargetVisible && Actor.Distance <= RangedRange)
                    {
                        if (TargetVisible && TargetInMeleeRange) { Go("Slash"); return; }
                        if (Phase >= 2 && Actor.CanCastRule && Age >= _nextRule)
                        { _nextRule = Age + 8f; Go("WingSpread"); return; }
                        if (Phase >= 2 && Actor.SummonCount > 0 && Age >= _nextPoint)
                        { _nextPoint = Age + 8f; Go("Point"); return; }
                        if (Phase >= 2 && Age >= _nextSummon && Actor.SummonCount < 3)
                        { _nextSummon = Age + 12f; _nextPoint = Age + 3f; Go("Roar"); return; }
                        if (Age >= _nextDash) { Go("Windup"); return; }
                        // 刚出生先完成一段换位，再开始弹幕。
                        if (Elapsed >= 0.45f) { Go("Shake"); return; }
                    }
                    if (Age >= _nextWaypoint)
                    {
                        if ((Position - _lastFlightPosition).sqrMagnitude < 0.04f) _orbitSide = -_orbitSide;
                        ChooseFlightPoint();
                    }
                    SteerTo(_destination, Tempo);
                    break;
                case "Approach":
                    // 高空休息后主动降至玩家侧面。近身阶段不因玩家靠近就立即撤离。
                    if (Age >= NextAttack)
                    {
                        if (TargetVisible && TargetInMeleeRange) { Go("Slash"); return; }
                        if (TargetVisible && Actor.Distance <= RangedRange && ((!_preferMelee && Elapsed >= .35f)
                            || Elapsed >= Mathf.Max(.5f, Config.BatApproachDuration)))
                        { ChooseRangedAttack(); return; }
                    }
                    ApproachTarget();
                    break;
                case "Evade":
                    Actor.FaceTarget(); SteerTo(_destination, 1.7f * Tempo);
                    if (Finished || (Position - _destination).sqrMagnitude < 0.2f) Go("Approach");
                    break;
                case "Windup":
                    Actor.Motor.Intent = Vector2.zero;
                    if (Finished) Go("Dash");
                    break;
                case "Dash":
                    Actor.Motor.Intent = BoundIntent(_dashDirection);
                    Actor.Motor.SpeedMultiplier = 3f * Tempo;
                    Actor.Attack(Config.AttackRange, sustained: true);
                    bool blocked = Elapsed > 0.16f && Actor.Motor.Velocity.sqrMagnitude < 0.25f;
                    if (Finished || blocked || Actor.Motor.Intent.sqrMagnitude < 0.01f) Go("Recover");
                    break;
                case "Recover":
                    SteerTo(_destination, 1.3f * Tempo);
                    if (Finished) FinishAttack();
                    break;
                case "Slash":
                    if (Pulses < 3 && Elapsed >= Actor.AnimationDuration * (0.3f + Pulses * 0.23f))
                    { Actor.Attack(Config.AttackRange, true); Pulses++; }
                    if (Finished) FinishAttack();
                    break;
                case "Shake":
                    // 发射弹幕后缓慢侧移，不在同一点持续站桩。
                    SteerTo(_destination, 0.65f * Tempo);
                    if (Elapsed >= Mathf.Max(.1f, Config.BatVolleyInterval) * (Pulses + 1)
                        && Pulses < Mathf.Clamp(Config.BatVolleyWaves, 1, 8))
                    {
                        float sweep = (Pulses & 1) == 0 ? -6f : 6f;
                        Vector2 middle = Quaternion.Euler(0f, 0f, sweep) * (PredictTarget(.18f) - Position).normalized;
                        Actor.ShootFan(middle, Config.BatFanCount, Config.BatFanSpread, 11f, radius: .24f);
                        if (Pulses == 0 && Config.BatRingCount > 0)
                            Actor.ShootRing(Config.BatRingCount, 6.5f, Age * 17f, .18f);
                        Pulses++;
                    }
                    if (Finished) FinishAttack();
                    break;
                case "Roar":
                case "WingSpread":
                case "Point":
                    SteerTo(_destination, 0.35f);
                    if (Impact())
                    {
                        if (name == "Roar") Actor.SpawnMinions(true, 3);
                        else if (name == "WingSpread") Actor.CastRule();
                        else Actor.CommandSummons();
                        if (Config.BatRingCount > 0) Actor.ShootRing(Config.BatRingCount, 8f, Age * 23f);
                    }
                    if (Finished) FinishAttack();
                    break;
                case "Hurt": if (Finished) Go("Evade"); break;
            }
        }

        private void FinishAttack()
        {
            _preferMelee = StateName != "Slash";
            _orbitSide = -_orbitSide;
            RestAfterAttack("Approach", Mathf.Max(0.2f, Config.AttackCooldown / Tempo));
        }
        private void ApproachTarget()
        {
            if (!HasTarget) { Actor.Motor.Stop(); return; }
            Vector2 target = PredictTarget(.15f);
            float side = Mathf.Abs(Position.x - target.x) > .1f ? Mathf.Sign(Position.x - target.x) : _orbitSide;
            _destination = ClampPoint(target + Vector2.right * (side * Mathf.Max(.5f, Config.BatApproachDistance))
                + Actor.Motor.Up * Mathf.Max(0f, Config.BatApproachHeight));
            SteerTo(_destination, Mathf.Max(.5f, Config.BatApproachSpeedMultiplier) * Tempo);
        }
        private void ChooseRangedAttack()
        {
            if (Phase >= 2 && Actor.CanCastRule && Age >= _nextRule)
            { _nextRule = Age + 8f; Go("WingSpread"); return; }
            if (Phase >= 2 && Actor.SummonCount > 0 && Age >= _nextPoint)
            { _nextPoint = Age + 8f; Go("Point"); return; }
            if (Phase >= 2 && Age >= _nextSummon && Actor.SummonCount < 3)
            { _nextSummon = Age + 12f; _nextPoint = Age + 3f; Go("Roar"); return; }
            Go(Age >= _nextDash ? "Windup" : "Shake");
        }
        protected override void TickRest()
        {
            // 休息路线以起点和场地为准，不追踪玩家，也不触发攻击或近身撤离。
            float ahead = Position.x + Actor.Motor.Velocity.x * 0.15f;
            if (_restDirection > 0 && ahead >= _restRight.x - 0.2f) _restDirection = -1;
            else if (_restDirection < 0 && ahead <= _restLeft.x + 0.2f) _restDirection = 1;
            Vector2 point = _restDirection > 0 ? _restRight : _restLeft;
            float vertical = Mathf.Clamp((point.y - Position.y) / 1.2f, -1f, 1f);
            float horizontal = _restRight.x - _restLeft.x > 0.4f ? _restDirection : 0f;
            if (Position.y < point.y - 0.6f) horizontal *= 0.35f;
            Actor.Motor.Intent = BoundIntent(Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f));
            Actor.Motor.SpeedMultiplier = Mathf.Clamp(Config.BatRestSpeedMultiplier, 0.1f, 1f);
            Actor.FaceDirection(Actor.Motor.Intent);
        }
        private void ChooseFlightPoint()
        {
            Vector2 target = HasTarget ? (Vector2)Actor.Target.position : Position;
            _destination = ClampPoint(target + Vector2.right * (_orbitSide * Config.BatOrbitDistance)
                + Actor.Motor.Up * (Config.BatHoverHeight + Mathf.Sin(Age * 2.2f) * 0.5f));
            _nextWaypoint = Age + 0.8f;
            _lastFlightPosition = Position;
        }
        private Vector2 ClampPoint(Vector2 point)
        {
            Vector2 margin = Config.BodySize * 0.5f + Vector2.one * 0.2f;
            float x = (Actor.ArenaX.x + Actor.ArenaX.y) * 0.5f;
            float y = (Actor.ArenaY.x + Actor.ArenaY.y) * 0.5f;
            point.x = Actor.ArenaX.y - Actor.ArenaX.x > margin.x * 2f
                ? Mathf.Clamp(point.x, Actor.ArenaX.x + margin.x, Actor.ArenaX.y - margin.x) : x;
            point.y = Actor.ArenaY.y - Actor.ArenaY.x > margin.y * 2f
                ? Mathf.Clamp(point.y, Actor.ArenaY.x + margin.y, Actor.ArenaY.y - margin.y) : y;
            return point;
        }
        private Vector2 BoundIntent(Vector2 intent)
        {
            Vector2 margin = Config.BodySize * 0.5f + Vector2.one * 0.1f;
            Vector2 ahead = Position + Actor.Motor.Velocity * 0.15f;
            if ((intent.x < 0 && ahead.x <= Actor.ArenaX.x + margin.x)
                || (intent.x > 0 && ahead.x >= Actor.ArenaX.y - margin.x)) intent.x = 0;
            if ((intent.y < 0 && ahead.y <= Actor.ArenaY.x + margin.y)
                || (intent.y > 0 && ahead.y >= Actor.ArenaY.y - margin.y)) intent.y = 0;
            return intent;
        }
        private void SteerTo(Vector2 point, float multiplier)
        {
            Actor.FaceTarget();
            Actor.Motor.Intent = BoundIntent(Vector2.ClampMagnitude((point - Position) / 1.2f, 1f));
            Actor.Motor.SpeedMultiplier = multiplier;
        }

        private sealed class FlightData
        {
            public float Summon, Point, Rule, Dash, Evade, Waypoint;
            public int Side;
            public Vector2 Destination, Direction, TargetPosition, TargetVelocity, FlightPosition;
            public Transform Target;
            public Vector2 RestLeft, RestRight;
            public int RestDirection;
            public bool PreferMelee;
        }
        protected override object CaptureExtra() => new FlightData
        {
            Summon = _nextSummon, Point = _nextPoint, Rule = _nextRule, Dash = _nextDash,
            Evade = _nextEvade, Waypoint = _nextWaypoint, Side = _orbitSide, Destination = _destination,
            Direction = _dashDirection, TargetPosition = _lastTargetPosition, TargetVelocity = _targetVelocity,
            FlightPosition = _lastFlightPosition, Target = _trackedTarget,
            RestLeft = _restLeft, RestRight = _restRight, RestDirection = _restDirection, PreferMelee = _preferMelee
        };
        protected override void RestoreExtra(object state)
        {
            if (!(state is FlightData data)) return;
            _nextSummon = data.Summon; _nextPoint = data.Point; _nextRule = data.Rule; _nextDash = data.Dash;
            _nextEvade = data.Evade; _nextWaypoint = data.Waypoint; _orbitSide = data.Side;
            _destination = data.Destination; _dashDirection = data.Direction;
            _lastTargetPosition = data.TargetPosition; _targetVelocity = data.TargetVelocity;
            _lastFlightPosition = data.FlightPosition; _trackedTarget = data.Target;
            _restLeft = data.RestLeft; _restRight = data.RestRight; _restDirection = data.RestDirection;
            _preferMelee = data.PreferMelee;
        }
    }
}
