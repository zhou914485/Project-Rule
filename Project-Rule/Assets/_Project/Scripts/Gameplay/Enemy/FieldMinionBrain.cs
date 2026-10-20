using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>关卡小怪共享巡逻、感知、临时散架和快照；具体决策由各类 Brain 负责。</summary>
    public abstract class FieldMinionBrain : EnemyBrain
    {
        private static readonly string[] States = { "Idle", "Patrol", "Attack", "Ignore", "Hurt", "Down", "Rebirth",
            "Armed", "Explode", "Hop", "Pop", "Walk", "Hide", "Fly", "Stand", "Chase", "Hug", "Return", "Sit", "Wander", "Slide", "Howl", "BellySlap", "Charge", "Bump" };
        protected Vector2 Home;
        protected float Heading = 1f, ReadyAt;
        protected int Pattern;
        private float _staggerDamage, _lastHit, _lastSeenAt = float.NegativeInfinity;
        private Vector2 _lastSeen;
        private Transform _cachedTarget;
        private bool _pursuing, _returning;
        private RuleGame.Gameplay.Player.EntityStatus _targetStatus;
        private readonly RaycastHit2D[] _sightHits = new RaycastHit2D[8];
        protected Collider2D TargetBody;
        protected readonly Collider2D Body;
        protected override string[] ActionNames => States;
        protected FieldMinionBrain(EnemyActor actor) : base(actor)
        {
            Home = actor.transform.position;
            Body = actor.GetComponent<Collider2D>();
        }
        protected bool Dormant => StateName == "Down" || StateName == "Rebirth";
        public override bool BodyEnabled => !Actor.Defeated && !Dormant && !(Config.Kind == EnemyKind.ClockMouse && StateName == "Idle");
        public override bool MovementEnabled => BodyEnabled && StateName != "Armed" && StateName != "Explode";
        public override bool DamageLocked => Dormant || !BodyEnabled || StateName == "Armed" || StateName == "Explode";
        public override bool ContactDamageEnabled => BodyEnabled
            && ((Config.Kind == EnemyKind.ClockMouse && StateName == "Walk") || Config.Kind == EnemyKind.ChargingCube);
        protected bool LiveTarget
        {
            get
            {
                if (_cachedTarget != Actor.Target)
                {
                    _cachedTarget = Actor.Target;
                    TargetBody = HasTarget ? Actor.Target.GetComponent<Collider2D>() ?? Actor.Target.GetComponentInParent<Collider2D>() : null;
                    _targetStatus = HasTarget ? Actor.Target.GetComponentInParent<RuleGame.Gameplay.Player.EntityStatus>() : null;
                    _lastSeenAt = float.NegativeInfinity; _pursuing = false; _returning = true;
                }
                if (!HasTarget) return false;
                return _targetStatus == null || _targetStatus.Health > 0f;
            }
        }
        protected bool SeesTarget
        {
            get
            {
                if (!LiveTarget || Actor.Distance > Config.MinionSenseRange
                    || Vector2.Distance(Actor.Target.position, Home) > Config.MinionLeashRange
                    || Vector2.Distance(Actor.transform.position, Home) > Config.MinionLeashRange) return false;

                var filter = new ContactFilter2D { useTriggers = false };
                filter.SetLayerMask(Actor.Motor.SolidMask);
                int count = Physics2D.Linecast(Actor.transform.position, Actor.Target.position, filter, _sightHits);
                for (int i = 0; i < count; i++)
                {
                    Collider2D hit = _sightHits[i].collider;
                    if (hit == null) continue;
                    // 三棍支架是细木棍，隔着它仍然看得见；地形 / 墙照常挡视线。
                    if (hit.GetComponentInParent<TrestleTrap>() != null) continue;
                    return false;
                }
                return true;
            }
        }
        protected bool TouchesTarget
        {
            get
            {
                if (!LiveTarget || !Body.enabled) return false;
                return TargetBody != null && TargetBody.enabled
                    ? Body.Distance(TargetBody).distance <= Mathf.Max(.08f, Config.ContactPadding)
                    : Actor.Distance <= Config.BodySize.x * .5f + .4f;
            }
        }
        protected override void TickGlobal()
        {
            if (SeesTarget)
            {
                _lastSeenAt = Age; _lastSeen = Actor.Target.position;
                _pursuing = true; _returning = false;
            }
            else if (_pursuing && !CanPursue)
            {
                _pursuing = false; _returning = true; Actor.Navigation?.Repath();
            }
        }
        /// <summary>「摩擦 × 反转」现在生效 —— 本实体的**意图与位移是反的**。</summary>
        protected static bool IsInverted => RuleSystem.Instance?.Attributes.Values.FrictionInverted == true;

        // 只影响地面移动意图；不覆盖具体动作，不接管飞行控制。
        protected bool FleeMode => IsInverted && !IsFlying && Actor.Motor.Grounded && LiveTarget;

        protected bool CanPursue => LiveTarget && Age - _lastSeenAt <= Config.MinionTargetMemory
            && Vector2.Distance(_lastSeen, Home) <= Config.MinionLeashRange
            && Vector2.Distance(Actor.transform.position, Home) <= Config.MinionLeashRange;
        protected Vector2 LastSeenPosition => _lastSeen;
        protected bool ReturnHome()
        {
            if (!_returning) return false;
            if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return true;
            if (Vector2.Distance(Actor.transform.position, Home) <= .25f) { _returning = false; return false; }
            if (Config.MinionNavigationEnabled)
                Actor.Navigation.MoveTo(Home, Home, Age, Config.Kind == EnemyKind.SpringToy, Config.MinionHopHeight);
            else
            {
                float side = Mathf.Sign(Home.x - Actor.transform.position.x);
                Actor.Motor.Intent = Actor.Motor.HasHorizontalObstacle(side) ? Vector2.zero : Vector2.right * side;
                Actor.FaceDirection(Actor.Motor.Intent);
            }
            return true;
        }
        protected virtual void CueMovement(bool moving)
        {
            string action = moving ? (IsFlying ? "Fly" : "Walk") : "Idle";
            if (Actor.AnimationName != action) Actor.Cue(action, Actor.Duration(action), true);
        }
        protected void TrackMeleeWindup()
        {
            if (Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction && SeesTarget) Actor.FaceTarget();
        }
        protected void TickMeleeHit()
        {
            TrackMeleeWindup();
            // 一个短命中窗容许玩家在出手中进入范围；同一挥击仍只结算一次。
            if (Elapsed >= Actor.AnimationDuration * Config.ImpactFraction
                && Elapsed <= Actor.AnimationDuration * Mathf.Min(1f, Config.ImpactFraction + .2f))
                Actor.Attack(Config.AttackRange);
        }
        protected void Patrol(float distance = -1f)
        {
            // 显式传了距离的（例如 ClockMouse）照旧；没传的走「本实例的巡逻半径」——
            // 它可以在 Inspector 里逐只调小，且被夹到可移动范围内（见 EnemyActor.ResolvePatrolRange）。
            float radius = distance > 0f ? distance : Actor.ResolvePatrolRange(Home);
            float x = Actor.transform.position.x;
            // 摩擦反转时实体实际朝意图的**反方向**走，所以边界判断要按世界方向算，
            // 否则它会在意图方向还没到界时就已经走出去了。
            float travel = Actor.Motor.WorldDirection(Heading);
            if ((x >= Mathf.Min(Home.x + radius, ClampArenaX(Home.x + radius)) && travel > 0f)
                || (x <= Mathf.Max(Home.x - radius, ClampArenaX(Home.x - radius)) && travel < 0f)
                || Actor.Motor.HasHorizontalObstacle(Heading)) Heading = -Heading;
            Actor.FaceDirection(Vector2.right * Heading);
            Actor.Motor.Intent = Vector2.right * Heading;
        }
        protected void Chase()
        {
            if (Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
            if (!CanPursue) { Actor.Motor.Intent = Vector2.zero; return; }
            Vector2 pursuit = SeesTarget ? PredictTarget(.18f) : _lastSeen;
            if (Config.MinionNavigationEnabled)
            {
                Actor.Navigation.MoveTo(pursuit, Home, Age);
                // 寻路失败（体型 / 台阶 / 狭窄口）时不能一边播 Walk 一边原地站着：
                // 退化为「朝目标直接给意图」，遇墙才停；允许踏空，先追上去再说。
                if (Actor.Motor.Intent.sqrMagnitude < .0001f)
                {
                    float fallback = pursuit.x - Actor.transform.position.x;
                    if (Mathf.Abs(fallback) > .12f && !Actor.Motor.HasWallAhead(Mathf.Sign(fallback)))
                    {
                        Actor.FaceDirection(Vector2.right * fallback);
                        Actor.Motor.Intent = Vector2.right * Mathf.Sign(fallback);
                        Actor.Motor.AllowLedgeTraversal = true;
                    }
                }
                return;
            }
            float offset = pursuit.x - Actor.transform.position.x;
            Actor.FaceDirection(Vector2.right * offset);
            Actor.Motor.Intent = Mathf.Abs(offset) > .12f ? Vector2.right * Mathf.Sign(offset) : Vector2.zero;
            if (Actor.Motor.HasHorizontalObstacle(Actor.Facing) && !TouchesTarget) Actor.Motor.Intent = Vector2.zero;
        }
        protected override bool TryHandleDeath()
        {
            if (!Config.MinionReassembles) return HandleLethal();
            if (!Dormant) Go("Down");
            return true;
        }
        protected virtual bool HandleLethal() => false;
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "Down") { Actor.Motor.Stop(); Actor.Cue("Dead", Actor.Duration("Dead", .6f)); }
            else if (name == "Rebirth") Actor.Cue("Rebirth", Actor.Duration("Rebirth", .8f));
            else if (name != "Hurt") EnterBehavior(name);
        }
        protected virtual void EnterBehavior(string name) { }
        protected virtual string AfterHurtAction => FirstAction;
        protected override void TickAction(string name)
        {
            if (name == "Down") { if (CooldownElapsed >= Mathf.Max(Actor.AnimationDuration, Config.MinionRespawnDelay)) Go("Rebirth"); return; }
            if (name == "Rebirth")
            {
                if (Finished)
                {
                    Actor.Status.ApplyHealthDelta(Config.Health - Actor.Status.Health);
                    ReadyAt = CooldownAge + .3f;
                    Go(FirstAction);
                }
                return;
            }
            if (name == "Hurt") { if (CooldownElapsed >= Actor.AnimationDuration) Go(AfterHurtAction); return; }

            TickBehavior(name);
            // 回位、巡逻等移动在反转时保持朝目标的意图，使实际位移持续远离。
            // 拥抱、引信、攻击和滑行等状态仍由各自 Brain 完整执行。
            if (FleeMode && Mathf.Abs(Actor.Motor.Intent.x) > .001f
                && (StateName == "Chase" || StateName == "Patrol" || StateName == "Ignore"
                    || StateName == "Wander" || StateName == "Return" || StateName == "Charge"))
            {
                float offset = Actor.Target.position.x - Actor.transform.position.x;
                Actor.Motor.Intent = new Vector2(Mathf.Abs(offset) > .12f ? Mathf.Sign(offset) : 0f, Actor.Motor.Intent.y);
                Actor.FaceTarget(); Actor.Motor.AllowLedgeTraversal = true;
            }
        }
        protected abstract void TickBehavior(string name);
        public override void OnHit(float damage)
        {
            if (damage <= 0f) return;
            if (Actor.Status.Health <= 0f) { OnDisabled(); return; }
            if (Dormant || StateName == "Hurt" || HurtClock < NextHurt) return;
            // 已经出手的攻击不被轻击逐帧取消；玩家仍可靠重击在前摇打断。
            bool committed = StateName == "Attack" || StateName == "Slide" || StateName == "Howl" || StateName == "BellySlap";
            if (committed && Elapsed >= Actor.AnimationDuration * Config.MinionAimLockFraction) return;
            if (CooldownAge - _lastHit > Config.StaggerResetDelay) _staggerDamage = 0f;
            _lastHit = CooldownAge;
            _staggerDamage += damage;
            if (_staggerDamage < Mathf.Max(1f, Config.MinionStaggerThreshold)) return;
            _staggerDamage = 0f;
            OnDisabled();
            Go("Hurt");
        }
        private sealed class Data
        {
            public Vector2 Home;
            public float Heading, Ready;
            public float StaggerDamage, LastHit, LastSeenAt;
            public Vector2 LastSeen;
            public int Pattern;
            public object Behavior;
            public bool Pursuing, Returning;
        }
        protected override object CaptureExtra() => new Data { Home = Home, Heading = Heading, Ready = ReadyAt, Pattern = Pattern,
            StaggerDamage = _staggerDamage, LastHit = _lastHit, LastSeenAt = _lastSeenAt, LastSeen = _lastSeen,
            Pursuing = _pursuing, Returning = _returning, Behavior = CaptureBehavior() };
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
            OnDisabled(); Home = data.Home; Heading = data.Heading; ReadyAt = data.Ready; Pattern = data.Pattern;
            _staggerDamage = data.StaggerDamage; _lastHit = data.LastHit; _lastSeenAt = data.LastSeenAt; _lastSeen = data.LastSeen;
            _pursuing = data.Pursuing; _returning = data.Returning;
            RestoreBehavior(data.Behavior);
        }
        protected virtual object CaptureBehavior() => null;
        protected virtual void RestoreBehavior(object state) { }
    }
}
