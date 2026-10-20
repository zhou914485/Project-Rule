using UnityEngine;
using UnityHFSM;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>
    /// Alive 内的动作 FSM + 可强制打断的 Transition/Dead，形成实际 HFSM。
    /// 所有判定计时由 EnemyActor 的游戏物理时钟推进，不使用包的墙钟 Timer。
    /// </summary>
    public abstract class EnemyBrain
    {
        protected readonly EnemyActor Actor;
        protected EnemyDefinition Config => Actor.Definition;
        protected readonly StateMachine Root = new StateMachine();
        protected readonly StateMachine Actions = new StateMachine();
        protected float CooldownAge, CooldownElapsed;
        protected float HurtClock => IsBoss ? Age : CooldownAge;
        public float MechanicsAge => CooldownAge;
        protected float Age, Elapsed, NextAttack, NextHurt, TransitionDuration;
        protected string TransitionName;
        protected int NextPhase, Pulses;
        private float _staggerDamage, _lastStaggerHit;
        private float _restUntil;
        private string _restReturn = "Idle";
        public int Phase { get; protected set; } = 1;
        public float Delta { get; private set; }
        public float ActionElapsed => Elapsed;
        public float SimulationAge => Age;
        public float RestRemaining => IsBoss ? Mathf.Max(0f, _restUntil - Age) : 0f;
        protected bool IsResting => RestRemaining > 0f;
        public string StateName => Root.ActiveStateName == "Alive" ? Actions.ActiveStateName : TransitionName;
        public virtual bool DamageLocked => false;
        public virtual bool IsFlying => Config.Flying;
        protected bool HasTarget => Actor.HasTarget;
        protected bool IsBoss => Config.IsBoss;
        public virtual bool BodyEnabled => true;
        public virtual bool MovementEnabled => true;
        public virtual bool Anchored => false;
        public virtual bool OnMotorCollision(Collider2D collider, Vector2 normal, Vector2 velocity) => false;
        public virtual bool ContactDamageEnabled => true;
        public virtual float ContactKnockback => 5f;
        public virtual void OnDisabled() { }
        protected float CombatDistance => !HasTarget ? float.PositiveInfinity : Actor.IsFlying
            ? Actor.Distance : Mathf.Abs(Actor.Target.position.x - Actor.transform.position.x);
        protected bool TargetInMeleeRange => HasTarget && CombatDistance <= Config.AttackRange
            && Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) <= Config.BodySize.y * 0.4f;
        protected float RangedRange => Mathf.Max(Config.AttackRange + Config.SpacingTolerance, Config.RangedRange);
        protected abstract string[] ActionNames { get; }
        protected virtual string FirstAction => "Idle";
        protected EnemyBrain(EnemyActor actor) { Actor = actor; }

        public static EnemyBrain Create(EnemyActor actor)
        {
            switch (actor.Definition.Kind)
            {
                case EnemyKind.BrickBoss: return new BrickBossBrain(actor);
                case EnemyKind.ClockBoss: return new ClockBossBrain(actor);
                case EnemyKind.RabbitBoss: return new RabbitBossBrain(actor);
                case EnemyKind.BatBoss: return new BatBossBrain(actor);
                case EnemyKind.StoneGuard:
                case EnemyKind.BrickSoldier: return new PatrolGuardBrain(actor);
                case EnemyKind.ExplosiveBlock:
                case EnemyKind.ClockBomb: return new ExplosiveMinionBrain(actor);
                case EnemyKind.SpringToy: return new SpringMinionBrain(actor);
                case EnemyKind.ClockMouse: return new ClockMouseBrain(actor);
                case EnemyKind.PlushBird: return new PlushBirdBrain(actor);
                case EnemyKind.HugBear: return new HugBearBrain(actor);
                case EnemyKind.PlushSeal: return new PlushSealBrain(actor);
                case EnemyKind.DiscoBall: return new DiscoBallBrain(actor);
                case EnemyKind.SpinningTop: return new SpinningTopBrain(actor);
                case EnemyKind.ChargingCube: return new ChargingCubeBrain(actor);
                default: return new MinionBrain(actor);
            }
        }
        public void Initialize()
        {
            foreach (string action in ActionNames)
            {
                string name = action;
                Actions.AddState(name, new State(onEnter: _ => EnterAction(name), onLogic: _ => TickAction(name)));
            }
            if (IsBoss)
                Actions.AddState("Rest", new State(onEnter: _ => EnterAction("Rest"), onLogic: _ =>
                {
                    if (IsResting) TickRest();
                    else Go(_restReturn);
                }));
            Actions.SetStartState(FirstAction);
            Root.AddState("Alive", Actions);
            Root.AddState("Transition", new State(onEnter: _ =>
            {
                Elapsed = 0f; CooldownElapsed = 0f; Pulses = 0;
                Actor.Cue(TransitionName, TransitionDuration);
            }, onLogic: _ =>
            {
                TickTransition();
                if (Root.ActiveStateName == "Transition" && Elapsed >= TransitionDuration)
                {
                    Phase = NextPhase;
                    string completed = TransitionName;
                    Root.RequestStateChange("Alive", true);
                    AfterTransition(completed);
                }
            }));
            Root.AddState("Dead", new State(onEnter: _ =>
            {
                Elapsed = 0f; TransitionName = "Dead";
                Actor.Cue("Dead", Actor.Duration("Dead", 1f));
                Actor.Motor.Stop();
            }, onLogic: _ => { if (Elapsed >= Actor.AnimationDuration) Actor.gameObject.SetActive(false); }));
            Root.SetStartState("Alive");
            Root.Init();
        }
        public void Tick(float dt)
        {
            if (dt <= 0f || Actor.Status.IsPaused) return;
            Delta = dt; Actor.AdvanceAnimation(dt); Age += dt; Elapsed += dt;
            CooldownAge += dt; CooldownElapsed += dt;
            // 兔子的退场与半血翻转都按最大生命比例判断，调总血量不会让阶段阈值失真。
            bool end = Actor.Status.Health <= 0f || (Config.Kind == EnemyKind.RabbitBoss && Actor.Status.Health < Config.Health * .2f);
            if (end && Root.ActiveStateName != "Dead" && !TryHandleDeath())
            {
                Actor.MarkDefeated();
                Root.RequestStateChange("Dead", true);
            }
            if (!Actor.Defeated) TickGlobal();
            Root.OnLogic();
        }
        protected virtual void EnterAction(string name)
        {
            Elapsed = 0f; CooldownElapsed = 0f; Pulses = 0;
            Actor.FaceTarget();
            if (name == "Rest")
            {
                string idle = Config.Flying ? "Fly" : "Idle";
                Actor.Cue(idle, Actor.Duration(idle), true);
                Actor.Motor.Stop();
                return;
            }
            float duration = name == "Hurt"
                ? Mathf.Min(Actor.Duration(name), Mathf.Max(0.05f, IsBoss ? Config.BossHurtDuration : Config.MinionHurtDuration))
                : Actor.Duration(name);
            Actor.Cue(name, duration, name == "Idle" || name == "Walk" || name == "Fly");
            // 从受击恢复后开始完整冷却，长受击动画不能吃掉保护窗口。
            if (name == "Hurt") NextHurt = HurtClock + duration + Mathf.Max(0f, Config.HurtCooldown);
        }
        protected void Go(string action)
        {
            // 硬直恢复也要回到尚未结束的休息，不能通过受击提前进入下一轮进攻。
            if (IsResting && action != "Hurt") action = "Rest";
            Actions.RequestStateChange(action, true);
        }
        protected void Transition(string name, int phase, float duration = -1f)
        {
            _staggerDamage = 0f;
            _restUntil = Age;
            TransitionName = name; NextPhase = phase;
            TransitionDuration = duration > 0f ? duration : Actor.Duration(name);
            Root.RequestStateChange("Transition", true);
        }
        protected void WalkToTarget()
        {
            if (!HasTarget) { Actor.Motor.Intent = Vector2.zero; return; }
            Actor.FaceTarget();
            Actor.Motor.Intent = Vector2.right * Actor.Facing;
        }
        /// <summary>移动中使用更窄的停止区间，避免距离边缘反复启停；后撤仍面朝目标。</summary>
        protected void KeepDistance(string movement, float preferredDistance)
        {
            if (!HasTarget)
            {
                if (StateName != "Idle") Go("Idle");
                Actor.Motor.Intent = Vector2.zero;
                return;
            }
            float tolerance = Mathf.Max(0.1f, Config.SpacingTolerance);
            float desired = Mathf.Max(tolerance, preferredDistance);
            float stopBand = StateName == movement ? tolerance * 0.35f : tolerance;
            float difference = CombatDistance - desired;
            Actor.FaceTarget();
            Vector2 intent = Mathf.Abs(difference) <= stopBand ? Vector2.zero
                : Config.Flying ? Actor.TowardTarget * Mathf.Sign(difference)
                : Vector2.right * Actor.Facing * Mathf.Sign(difference);
            if (difference < -stopBand && intent.sqrMagnitude < 0.001f) intent = Vector2.left * Actor.Facing;
            // 不主动退到场地边界外；贴边时留在原位选招。
            float margin = Config.BodySize.x * 0.5f + 0.15f;
            if ((intent.x < 0f && Actor.transform.position.x <= Actor.ArenaX.x + margin)
                || (intent.x > 0f && Actor.transform.position.x >= Actor.ArenaX.y - margin)) intent.x = 0f;
            string action = intent.sqrMagnitude > 0.001f ? movement : "Idle";
            if (StateName != action) Go(action);
            Actor.FaceTarget();
            Actor.Motor.Intent = intent;
            Actor.Motor.SpeedMultiplier = difference < 0f ? Config.RetreatSpeedMultiplier : 1f;
        }
        protected bool Impact(float fraction = -1f)
        {
            if (Pulses != 0 || Elapsed < Actor.AnimationDuration * (fraction < 0f ? Config.ImpactFraction : fraction)) return false;
            Pulses = 1;
            return true;
        }
        protected bool Finished => Elapsed >= Actor.AnimationDuration;
        private Transform _motionTarget;
        private RuleGame.Gameplay.Player.PlayerController _targetPlayer;
        private Rigidbody2D _targetMotion;
        protected Vector2 TargetVelocity
        {
            get
            {
                if (!HasTarget) return Vector2.zero;
                if (_motionTarget != Actor.Target)
                {
                    _motionTarget = Actor.Target;
                    _targetPlayer = Actor.Target.GetComponentInParent<RuleGame.Gameplay.Player.PlayerController>();
                    _targetMotion = Actor.Target.GetComponentInParent<Rigidbody2D>();
                }
                // Slide 驱动的 Kinematic 玩家不把自主速度写进 linearVelocity。
                return Vector2.ClampMagnitude(_targetPlayer != null ? _targetPlayer.Velocity
                    : _targetMotion != null ? _targetMotion.linearVelocity : Vector2.zero, 12f);
            }
        }
        protected bool TargetThreatening
        {
            get
            {
                if (!HasTarget) return false;
                _ = TargetVelocity;
                return _targetPlayer != null && (_targetPlayer.AttackComboIndex >= 0 || _targetPlayer.IsHeavyBusy);
            }
        }
        protected bool TargetVisible => HasTarget && !Physics2D.Linecast(Actor.transform.position,
            Actor.Target.position, Actor.Motor.SolidMask);
        protected Vector2 PredictTarget(float lead)
        {
            if (!HasTarget) return Actor.transform.position;
            Vector2 origin = Actor.Target.position;
            Vector2 offset = TargetVelocity * Mathf.Clamp(lead, 0f, .75f);
            if (offset.sqrMagnitude < .0001f) return origin;
            RaycastHit2D hit = Physics2D.Raycast(origin, offset.normalized, offset.magnitude, Actor.Motor.SolidMask);
            return origin + offset.normalized * (hit.collider != null ? Mathf.Max(0f, hit.distance - .05f) : offset.magnitude);
        }
        protected void TrackAttackWindup()
        {
            if (Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction && TargetVisible) Actor.FaceTarget();
        }
        protected float ClampArenaX(float x)
        {
            float margin = Config.BodySize.x * .5f + .15f;
            return Actor.ArenaX.y - Actor.ArenaX.x > margin * 2f
                ? Mathf.Clamp(x, Actor.ArenaX.x + margin, Actor.ArenaX.y - margin)
                : (Actor.ArenaX.x + Actor.ArenaX.y) * .5f;
        }
        protected bool AtArenaEdge(float direction)
        {
            float ahead = Actor.transform.position.x + Actor.Motor.Velocity.x * .12f;
            return direction < 0f ? ahead <= ClampArenaX(Actor.ArenaX.x) + .05f
                : ahead >= ClampArenaX(Actor.ArenaX.y) - .05f;
        }
        protected void RestAfterAttack(string resume = "Idle", float cooldown = -1f)
        {
            float rest = Mathf.Max(0.1f, Config.BossRestDuration);
            NextAttack = Age + Mathf.Max(rest, cooldown < 0f ? Config.AttackCooldown : cooldown);
            _restUntil = Age + rest;
            _restReturn = resume;
            Go("Rest");
        }
        protected virtual void TickGlobal() { }
        protected virtual bool TryHandleDeath() => false;
        protected virtual void TickRest() => Actor.Motor.Intent = Vector2.zero;
        protected virtual void TickTransition() { }
        protected virtual void AfterTransition(string name) { }
        protected abstract void TickAction(string name);
        public virtual float ClampHealth(float health) => Mathf.Max(0f, health);
        public virtual void CommandAttack() { }
        public virtual void OnHit(float damage)
        {
            if (damage <= 0f || Actor.Status.Health <= 0f || Root.ActiveStateName != "Alive"
                || StateName == "Hurt" || HurtClock < NextHurt) return;
            if (HurtClock - _lastStaggerHit > Mathf.Max(0.1f, Config.StaggerResetDelay)) _staggerDamage = 0f;
            _lastStaggerHit = HurtClock;
            _staggerDamage += damage;
            float threshold = IsBoss ? Config.StaggerDamageThreshold : Config.MinionStaggerThreshold;
            if (_staggerDamage < Mathf.Max(1f, threshold)) return;
            _staggerDamage = 0f;
            Go("Hurt");
        }

        private sealed class Data
        {
            public string Root, Action, Transition;
            public float CooldownAge, CooldownElapsed;
            public float Age, Elapsed, Attack, Hurt, Duration;
            public float StaggerDamage, LastStaggerHit;
            public float RestUntil;
            public string RestReturn;
            public int Phase, NextPhase, Pulses;
            public object Extra;
        }
        public object Capture() => new Data
        {
            // Transition / Dead 会退出 Alive，HFSM 此时主动清空子状态；只记录仍在运行的动作。
            Root = Root.ActiveStateName,
            Action = Root.ActiveStateName == "Alive" ? Actions.ActiveStateName : null,
            Transition = TransitionName,
            CooldownAge = CooldownAge, CooldownElapsed = CooldownElapsed,
            Age = Age, Elapsed = Elapsed, Attack = NextAttack, Hurt = NextHurt, Duration = TransitionDuration,
            Phase = Phase, NextPhase = NextPhase, Pulses = Pulses, Extra = CaptureExtra(),
            StaggerDamage = _staggerDamage, LastStaggerHit = _lastStaggerHit,
            RestUntil = _restUntil, RestReturn = _restReturn
        };
        public void Restore(object state)
        {
            if (!(state is Data data)) return;
            TransitionName = data.Transition; TransitionDuration = data.Duration;
            Phase = data.Phase;
            // EnterAction/Go 可能读取休息状态，先恢复时钟和休息期限。
            Age = data.Age; CooldownAge = data.CooldownAge; _restUntil = data.RestUntil; _restReturn = data.RestReturn;
            Root.RequestStateChange(data.Root, true);
            if (data.Root == "Alive") Actions.RequestStateChange(data.Action, true);
            CooldownAge = data.CooldownAge; CooldownElapsed = data.CooldownElapsed;
            Age = data.Age; Elapsed = data.Elapsed; NextAttack = data.Attack; NextHurt = data.Hurt;
            Phase = data.Phase; NextPhase = data.NextPhase; Pulses = data.Pulses;
            _staggerDamage = data.StaggerDamage; _lastStaggerHit = data.LastStaggerHit;
            RestoreExtra(data.Extra);
        }
        protected virtual object CaptureExtra() => null;
        protected virtual void RestoreExtra(object state) { }
    }

    public sealed class MinionBrain : EnemyBrain
    {
        private bool _commanded;
        private Vector2 _home;
        private float _lastSeenAt = float.NegativeInfinity;
        private Vector2 _lastSeen;
        private Transform _observedTarget;
        private RuleGame.Gameplay.Player.EntityStatus _targetHealth;
        protected override string[] ActionNames => new[] { "Walk", "Idle", "Hurt" };
        protected override string FirstAction => "Walk";
        public MinionBrain(EnemyActor actor) : base(actor) { _home = actor.transform.position; }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            // 飞行召唤物停位时仍悬停拍翼，不能在半空切成倒挂姿势。
            if (Config.Flying && name == "Idle") Actor.Cue("Fly", Actor.Duration("Fly"), true);
        }
        public override void CommandAttack() => _commanded = true;
        protected override object CaptureExtra() => new object[] { _commanded, _home, _lastSeenAt, _lastSeen };
        protected override void RestoreExtra(object state)
        {
            if (!(state is object[] data)) return;
            _commanded = (bool)data[0]; _home = (Vector2)data[1];
            _lastSeenAt = (float)data[2]; _lastSeen = (Vector2)data[3];
        }
        protected override void TickGlobal()
        {
            if (_observedTarget != Actor.Target)
            {
                _observedTarget = Actor.Target;
                _targetHealth = HasTarget ? Actor.Target.GetComponentInParent<RuleGame.Gameplay.Player.EntityStatus>() : null;
                _lastSeenAt = float.NegativeInfinity;
            }
            if (!HasTarget || (_targetHealth != null && _targetHealth.Health <= 0f)
                || Actor.Distance > Config.MinionSenseRange
                || Vector2.Distance(Actor.Target.position, _home) > Config.MinionLeashRange
                || Vector2.Distance(Actor.transform.position, _home) > Config.MinionLeashRange
                || Physics2D.Linecast(Actor.transform.position, Actor.Target.position, Actor.Motor.SolidMask))
            {
                if (!float.IsNegativeInfinity(_lastSeenAt) && (!HasTarget || (_targetHealth != null && _targetHealth.Health <= 0f)
                    || Age - _lastSeenAt > Config.MinionTargetMemory))
                {
                    _lastSeenAt = float.NegativeInfinity; Actor.Navigation?.Repath();
                }
                return;
            }
            if (Age - _lastSeenAt > Config.MinionTargetMemory) Actor.Navigation?.Repath();
            _lastSeenAt = Age; _lastSeen = Actor.Target.position;
        }
        protected override void TickAction(string name)
        {
            if (name == "Hurt") { if (CooldownElapsed >= Actor.AnimationDuration) Go("Walk"); return; }
            if (!HasTarget && !Config.Flying && Config.MinionNavigationEnabled && Actor.Navigation.ContinueTraversal(Age)) return;
            bool sees = HasTarget && TargetVisible && Actor.Distance <= Config.MinionSenseRange;
            if (Config.Flying && !_commanded && Actor.Summoner != null && !sees)
            {
                Vector2 orbit = _home + new Vector2(Mathf.Sin(Age) * 0.8f, Mathf.Cos(Age) * 0.3f);
                SetMovement(Vector2.ClampMagnitude(orbit - (Vector2)Actor.transform.position, 1f));
                return;
            }
            bool pursue = HasTarget && (_targetHealth == null || _targetHealth.Health > 0f)
                && Age - _lastSeenAt <= Config.MinionTargetMemory
                && Vector2.Distance(Actor.transform.position, _home) <= Config.MinionLeashRange;
            Vector2 destination = pursue ? (sees ? PredictTarget(.18f) : _lastSeen) : _home;
            if (Config.Flying) SetMovement(Vector2.ClampMagnitude(destination - (Vector2)Actor.transform.position, 1f));
            else if (Config.MinionNavigationEnabled)
            {
                Actor.Navigation.MoveTo(destination, _home, Age);
                if (Actor.Motor.Intent.sqrMagnitude < .001f && Mathf.Abs(destination.x - Actor.transform.position.x) > .12f
                    && !Actor.Motor.HasHorizontalObstacle(Mathf.Sign(destination.x - Actor.transform.position.x)))
                    Actor.Motor.Intent = Vector2.right * Mathf.Sign(destination.x - Actor.transform.position.x);
                SetMovement(Actor.Motor.Intent);
            }
            else
            {
                Vector2 offset = destination - (Vector2)Actor.transform.position;
                Actor.Motor.Intent = Mathf.Abs(offset.x) > .12f ? Vector2.right * Mathf.Sign(offset.x) : Vector2.zero;
            }
        }
        private void SetMovement(Vector2 intent)
        {
            if (intent.sqrMagnitude < .01f) intent = Vector2.zero;
            string state = intent == Vector2.zero ? "Idle" : "Walk";
            if (StateName != state) Go(state);
            Actor.Motor.Intent = intent;
            Actor.FaceDirection(intent);
        }
    }
}
