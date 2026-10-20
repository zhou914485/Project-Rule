using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>冲撞与发条弱点 HFSM，叠加独立的周期变速和有预警的齿轮弹幕。</summary>
    public sealed class ClockBossBrain : EnemyBrain
    {
        private float _nextCharge = 2.4f;
        private float _chargeFacing, _nextChargeTurn;
        private float _nextTimeRule = .8f, _nextBurst = 1.1f, _burstRelease, _burstAngle;
        private bool _enhanceNext = true, _burstPreparing, _waitingForTimeRule;
        public int TimeRuleCastCount { get; private set; }
        public RelationId LastTimeRule { get; private set; } = RelationId.Constant;
        public bool BarragePreparing => _burstPreparing;
        public bool ExtraBarrageEnabled { get; private set; }
        protected override string[] ActionNames => new[] { "Idle", "Walk", "JumpShake", "Charge", "Hurt" };
        public override bool DamageLocked => Root.ActiveStateName == "Transition";
        public ClockBossBrain(EnemyActor actor) : base(actor)
            => ExtraBarrageEnabled = actor.Definition.ClockExtraBarrageEnabled;
        /// <summary>白盒只切换本实例，不能通过调试按钮修改团队共享的配置资产。</summary>
        public void SetExtraBarrageEnabled(bool enabled)
        {
            ExtraBarrageEnabled = enabled;
            _burstPreparing = false;
            _nextBurst = Age + .35f;
        }
        // 保留旧表现/工具的查询入口，计时数据与推进均由独立发射器负责。
        public float HandInterval(int hand) => Actor.ClockBeams.HandInterval(hand);
        public float HandProgress(int hand) => Actor.ClockBeams.HandProgress(hand);
        private sealed class Data
        {
            public float Charge, Facing, ChargeTurn, TimeRule, Burst, Release, Angle;
            public bool EnhanceNext, Preparing, WaitingForTimeRule, ExtraBarrage;
            public int CastCount;
            public RelationId LastRule;
        }
        protected override object CaptureExtra() => new Data
        {
            Charge = _nextCharge, Facing = _chargeFacing, ChargeTurn = _nextChargeTurn, TimeRule = _nextTimeRule,
            Burst = _nextBurst, Release = _burstRelease, Angle = _burstAngle,
            EnhanceNext = _enhanceNext, Preparing = _burstPreparing,
            WaitingForTimeRule = _waitingForTimeRule,
            ExtraBarrage = ExtraBarrageEnabled,
            CastCount = TimeRuleCastCount, LastRule = LastTimeRule
        };
        protected override void RestoreExtra(object state)
        {
            if (!(state is Data data)) return;
            _nextCharge = data.Charge; _chargeFacing = data.Facing; _nextTimeRule = data.TimeRule;
            _nextChargeTurn = data.ChargeTurn;
            _nextBurst = data.Burst; _burstRelease = data.Release; _burstAngle = data.Angle;
            _enhanceNext = data.EnhanceNext; _burstPreparing = data.Preparing;
            _waitingForTimeRule = data.WaitingForTimeRule;
            ExtraBarrageEnabled = data.ExtraBarrage;
            TimeRuleCastCount = data.CastCount; LastTimeRule = data.LastRule;
        }
        public override float ClampHealth(float health)
        {
            float floor = Phase == 1 ? Config.Health * 2f / 3f : Phase == 2 ? Config.Health / 3f : 0f;
            return Mathf.Max(floor, health);
        }
        protected override void TickGlobal()
        {
            if (Root.ActiveStateName != "Alive")
            {
                _burstPreparing = false;
                _nextBurst = Mathf.Max(_nextBurst, Age + .35f);
                return;
            }
            TickPressure();
            if (Phase == 1 && Actor.Status.Health <= Config.Health * 2f / 3f + 0.01f)
            {
                Phase = 2;
            }
            if (Phase == 2 && Actor.Status.Health <= Config.Health / 3f + 0.01f)
            {
                Actor.WeakPoint?.Open(Config.ClockCoreHealth);
                Transition("Stun", 2, Config.ClockCoreWindow);
                return;
            }
        }
        private void TickPressure()
        {
            if (!HasTarget) return;
            TickTimeRule();
            if (!ExtraBarrageEnabled || Config.ClockBurstCount <= 0) return;
            if (TimeManager.Instance?.TimeMechanicsEnabled == false)
            {
                _burstPreparing = false;
                _nextBurst = Age + Config.ClockBurstInterval;
                return;
            }
            if (!_burstPreparing && Age >= _nextBurst)
            {
                _burstPreparing = true;
                _burstRelease = Age + Mathf.Max(.2f, Config.ClockBurstWarning);
                Actor.EmitFeedback(EnemyFeedbackKind.BarrageWarning,
                    Actor.BeamOrigin != null ? Actor.BeamOrigin.position : Actor.transform.position, Actor.Motor.Up);
            }
            if (_burstPreparing && Age >= _burstRelease)
            {
                _burstPreparing = false;
                _burstAngle = (_burstAngle + 11f) % 360f;
                Actor.ShootRing(Config.ClockBurstCount + (Phase - 1) * 8,
                    Config.ClockBurstSpeed + Phase - 1, _burstAngle,
                    origin: Actor.BeamOrigin != null ? (Vector2?)Actor.BeamOrigin.position : null);
                _nextBurst = Age + Mathf.Max(.4f, Config.ClockBurstInterval);
            }
        }
        private void TickTimeRule()
        {
            if (Config.ClockTimeRuleInterval <= 0f) return;
            // 由规则表判断真正的结束时刻，不能用固定施法 CD 提前覆盖仍在生效的规则。
            // 玩家途中替换时间规则时同样等它结束，永久规则则持续等待，不检查规则来源优先级。
            if (RuleSystem.Instance?.Table.GetSlot(AttributeId.Time) != null)
            {
                _waitingForTimeRule = true;
                return;
            }
            if (_waitingForTimeRule)
            {
                _waitingForTimeRule = false;
                _nextTimeRule = Age + Mathf.Max(.5f, Config.ClockTimeRuleInterval);
            }
            if (Age < _nextTimeRule) return;
            RelationId relation = _enhanceNext ? RelationId.Enhance : RelationId.Weaken;
            if (Actor.CastRule(AttributeId.Time, relation))
            {
                _enhanceNext = !_enhanceNext;
                _waitingForTimeRule = true;
                LastTimeRule = relation;
                TimeRuleCastCount++;
            }
            else _nextTimeRule = Age + .5f; // 资源不足正常重试，不白送能量也不逐帧请求。
        }
        protected override void TickTransition()
        {
            if (TransitionName != "Stun") return;
            if (Actor.WeakPoint != null && Actor.WeakPoint.Broken)
            {
                Actor.WeakPoint.Close();
                Transition("Recover", 3, 0.6f);
            }
        }
        protected override void AfterTransition(string name)
        {
            if (name == "Stun")
            {
                Actor.WeakPoint?.Close();
                Transition("Rewind", 2, 0.9f);
            }
            else if (name == "Rewind")
            {
                Actor.Status.ApplyHealthDelta(Config.Health * 0.6f - Actor.Status.Health);
                _nextCharge = Age + 1f;
            }
        }
        protected override void EnterAction(string name)
        {
            base.EnterAction(name);
            if (name == "JumpShake")
            {
                float direction = PredictTarget(.25f).x - Actor.transform.position.x;
                _chargeFacing = Mathf.Abs(direction) > .1f ? Mathf.Sign(direction) : Actor.Facing;
                _nextCharge = Age + Mathf.Max(1f, Config.ClockChargeCooldown);
                Actor.FaceDirection(Vector2.right * _chargeFacing);
            }
            if (name == "Charge")
            {
                _nextChargeTurn = Age;
                Actor.FaceDirection(Vector2.right * _chargeFacing);
                Actor.Cue("Charge", Actor.Duration("Charge", 3f), true);
            }
        }
        protected override void TickAction(string name)
        {
            switch (name)
            {
                case "Idle":
                case "Walk":
                    if (TargetVisible && CombatDistance <= RangedRange && Age >= NextAttack && Age >= _nextCharge
                        && Mathf.Abs(Actor.Target.position.y - Actor.transform.position.y) <= Config.BodySize.y * .6f)
                    {
                        Go("JumpShake");
                        return;
                    }
                    KeepDistance("Walk", Mathf.Min(Config.PreferredDistance, RangedRange));
                    break;
                case "JumpShake":
                    if (Elapsed <= Actor.AnimationDuration * Config.MinionAimLockFraction && TargetVisible)
                    {
                        float offset = PredictTarget(.25f).x - Actor.transform.position.x;
                        if (Mathf.Abs(offset) > .1f) _chargeFacing = Mathf.Sign(offset);
                        Actor.FaceDirection(Vector2.right * _chargeFacing);
                    }
                    // 乱跑是从当前位置起步的移动招式，不在这里隐式释放会回溯自身位置的全局规则。
                    if (Finished) Go("Charge");
                    break;
                case "Charge":
                    if (Elapsed >= Mathf.Max(.2f, Config.ClockChargeDuration))
                    {
                        RestAfterAttack();
                        break;
                    }
                    // 抱头乱跑持续完整时长；碰到玩家/墙或到场地边缘时折返，不结束动作。
                    // 短暂抑制再次掉头，让上一帧的接触离开后再判断，避免贴身左右抖动。
                    if (Age >= _nextChargeTurn && (AtArenaEdge(_chargeFacing) || Actor.Motor.HasHorizontalObstacle(_chargeFacing)))
                    {
                        _chargeFacing = -_chargeFacing;
                        _nextChargeTurn = Age + .15f;
                    }
                    Actor.FaceDirection(Vector2.right * _chargeFacing);
                    Actor.Motor.SpeedMultiplier = Mathf.Max(1f, Config.ClockChargeSpeedMultiplier);
                    Actor.Motor.Intent = Vector2.right * _chargeFacing;
                    // 乱跑只用 EnemyActor 的身体接触伤害，不叠加普通近战的远距离矩形。
                    // 身体被玩家阻挡时会折返，伤害范围仍与可见身体一致。
                    break;
                case "Hurt": if (Finished) Go("Idle"); break;
            }
        }
    }
}
