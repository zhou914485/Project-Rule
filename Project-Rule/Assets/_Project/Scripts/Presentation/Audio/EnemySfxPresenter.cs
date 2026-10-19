using RuleGame.Gameplay.Enemy;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyActor))]
    public sealed class EnemySfxPresenter : MonoBehaviour
    {
        private EnemyActor _actor;
        private bool _deathPlayed;
        private string _lastAction;
        private float _lastActionAge = -1f;
        private bool IsBoss => _actor.Definition != null && _actor.Definition.IsBoss;

        private void Awake() => _actor = GetComponent<EnemyActor>();
        private void OnEnable()
        {
            _actor.Feedback += Feedback;
            _actor.OnDefeated += Defeated;
            _actor.Restored += Restored;
            _actor.AnimationChanged += AnimationChanged;
            Restored();
        }
        private void OnDisable()
        {
            _actor.Feedback -= Feedback;
            _actor.OnDefeated -= Defeated;
            _actor.Restored -= Restored;
            _actor.AnimationChanged -= AnimationChanged;
        }

        private void Restored()
        {
            _deathPlayed = _actor.Defeated || (_actor.Status != null && _actor.Status.Health <= 0f);
            _lastAction = null; _lastActionAge = -1f;
        }
        private void AnimationChanged()
        {
            // 可重组小怪恢复生命后，下一次倒地仍允许发声；恢复快照本身不发声。
            if (!_actor.Defeated && _actor.Status != null && _actor.Status.Health > 0f
                && _actor.AnimationName != "Dead") _deathPlayed = false;
            if (!IsBoss || _deathPlayed || _actor.IsRestoringSnapshot) return;
            string action = _actor.AnimationName;
            float age = _actor.Brain?.SimulationAge ?? Time.time;
            // 某些脑在同一帧先设置通用动画，再设置同名专用时长，只发声一次。
            if (_lastAction == action && _lastActionAge == age) return;
            _lastAction = action; _lastActionAge = age;
            SfxCue? cue = null;
            switch (_actor.Definition.Kind)
            {
                case EnemyKind.BrickBoss:
                    if (action == "HammerPlayer" || action == "HammerGround") cue = SfxCue.BrickBossWindup;
                    else if (action == "Split") cue = SfxCue.BrickBossSplit;
                    else if (action == "Angry") cue = SfxCue.BossPhaseChange;
                    break;
                case EnemyKind.ClockBoss:
                    if (action == "Charge") cue = SfxCue.ClockBossCharge;
                    else if (action == "Stun") cue = SfxCue.ClockBossStun;
                    else if (action == "Rewind") cue = SfxCue.ClockBossRewind;
                    else if (action == "Recover") cue = SfxCue.BossPhaseChange;
                    break;
                case EnemyKind.RabbitBoss:
                    if (action == "Hide") cue = SfxCue.RabbitBossBurrow;
                    else if (action == "Pop") cue = SfxCue.RabbitBossEmerge;
                    else if (action == "Flip") cue = SfxCue.BossPhaseChange;
                    break;
                case EnemyKind.BatBoss:
                    if (action == "DashWindup") cue = SfxCue.BatBossWindup;
                    else if (action == "Dash") cue = SfxCue.BatBossDash;
                    else if (action == "WingSpread") cue = SfxCue.BatBossFlap;
                    else if (action == "Roar") cue = SfxCue.BatBossRoar;
                    else if (action == "Weak") cue = SfxCue.BossPhaseChange;
                    break;
            }
            if (cue.HasValue) Play(cue.Value, transform.position);
        }
        private void Feedback(EnemyFeedback feedback)
        {
            if (_actor.Definition == null) return;
            switch (feedback.Kind)
            {
                case EnemyFeedbackKind.Strike:
                case EnemyFeedbackKind.AreaStrike:
                    Play(IsBoss && _actor.Definition.Kind == EnemyKind.BrickBoss ? SfxCue.BrickBossSlam : SfxCue.EnemyMelee, feedback.Position); break;
                case EnemyFeedbackKind.ProjectileSpawn:
                    Play(IsBoss && _actor.Definition.Kind == EnemyKind.BrickBoss ? SfxCue.BrickBossThrow
                        : IsBoss && _actor.Definition.Kind == EnemyKind.RabbitBoss ? SfxCue.RabbitBossShot : SfxCue.EnemyShot, feedback.Position); break;
                case EnemyFeedbackKind.BeamSpawn:
                    Play(IsBoss && _actor.Definition.Kind == EnemyKind.ClockBoss ? SfxCue.ClockBossBeam : SfxCue.EnemyBeam, feedback.Position); break;
                case EnemyFeedbackKind.BarrageWarning:
                    if (IsBoss) Play(SfxCue.ClockBossBarrageWarning, feedback.Position); break;
                case EnemyFeedbackKind.RuleCast:
                    if (IsBoss) Play(SfxCue.BossRuleCast, feedback.Position); break;
                case EnemyFeedbackKind.Summon:
                    if (IsBoss) Play(SfxCue.BossSummon, feedback.Position); break;
                case EnemyFeedbackKind.Hit:
                    if (feedback.Damage <= 0f) return;
                    if (feedback.Lethal) Death();
                    else if (!_deathPlayed) Play(MaterialCue(false), feedback.Position);
                    break;
            }
        }
        private void Defeated(EnemyActor actor) => Death();
        private void Death()
        {
            if (_deathPlayed || _actor.Definition == null) return;
            _deathPlayed = true;
            Play(MaterialCue(true), transform.position);
        }
        private SfxCue MaterialCue(bool death)
        {
            EnemyKind kind = _actor.Definition.Kind;
            if (kind == EnemyKind.ClockBoss || kind == EnemyKind.ClockMouse || kind == EnemyKind.ClockBomb || kind == EnemyKind.SpringToy)
                return death ? SfxCue.EnemyDeathMetal : SfxCue.EnemyHitMetal;
            if (kind == EnemyKind.BrickBoss || kind == EnemyKind.BrickMinion || kind == EnemyKind.StoneGuard
                || kind == EnemyKind.BrickSoldier || kind == EnemyKind.ExplosiveBlock)
                return death ? SfxCue.EnemyDeathWood : SfxCue.EnemyHitWood;
            return death ? SfxCue.EnemyDeathSoft : SfxCue.EnemyHitSoft;
        }
        private void Play(SfxCue cue, Vector3 position)
        {
            if (_actor.Definition == null || _actor.Status?.IsPaused == true) return;
            SfxPlayer.Active?.PlayAt(cue, position, this);
        }
    }
}
