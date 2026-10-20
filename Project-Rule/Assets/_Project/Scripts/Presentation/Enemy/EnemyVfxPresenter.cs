using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>
    /// 只订阅战斗通知。12 个本地播放槽限制峰值和对象数量；持续攻击不会逐物理帧重播。
    /// 世界慢时间自然减慢序列帧，实体暂停则单独冻结；读档清掉旧时间线的瞬时效果。
    /// </summary>
    [RequireComponent(typeof(EnemyActor))]
    public sealed class EnemyVfxPresenter : MonoBehaviour
    {
        public EnemyVfxProfile Profile;
        private EnemyActor _actor;
        private readonly Slot[] _slots = new Slot[12];
        private float _lastShot = -10f;
        private bool _restoring;

        private sealed class Slot
        {
            public SpriteRenderer Renderer;
            public EnemyVfxClip Clip;
            public float Age, Speed;
            public Transform Follow;
            public Vector3 Offset;
            public Vector3 Position;
            public bool BarrageWarning;
        }
        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
        }
        private void OnEnable()
        {
            _actor.AnimationChanged += OnAction;
            _actor.Feedback += OnFeedback;
            _actor.Restored += OnRestore;
        }
        private void OnDisable()
        {
            _actor.AnimationChanged -= OnAction;
            _actor.Feedback -= OnFeedback;
            _actor.Restored -= OnRestore;
            Clear();
        }
        private Vector3 Feet => transform.position - (Vector3)_actor.Motor.Up * (_actor.Definition.BodySize.y * .5f);
        private void OnRestore() { Clear(); _restoring = true; _lastShot = -10f; }
        private void OnAction()
        {
            if (Profile == null || _restoring) return;
            // 受击或阶段切换打断时，取消还没有释放的蓄力，不能在下一招里继续闪。
            CancelFollowing();
            switch (_actor.AnimationName)
            {
                case "HammerPlayer": case "HammerGround": case "Throw": case "Slash":
                case "JumpShake": case "WingSpread": case "DashWindup": case "Roar": case "Magic": case "FlipThrow": case "Shake":
                    Play(Profile.Charge, transform.position,
                        Profile.CastSize * (_actor.Definition.Kind == EnemyKind.RabbitBoss ? .25f : .55f), Profile.Accent,
                        0f, transform, Mathf.Max(.15f, _actor.AnimationDuration * _actor.Definition.ImpactFraction));
                    if (_actor.AnimationName == "HammerGround" && _actor.Definition.BrickSlamRange > 0f)
                        Play(Profile.Cast, Feet, _actor.Definition.BrickSlamRange * 2f,
                            new Color(Profile.Secondary.r, Profile.Secondary.g, Profile.Secondary.b, .45f),
                            follow: transform, duration: _actor.AnimationDuration * _actor.Definition.ImpactFraction, aspect: .18f);
                    break;
                case "Angry": case "Split": case "Flip": case "Weak": case "Recover": case "Rewind":
                    if (_actor.Definition.Kind == EnemyKind.RabbitBoss) break;
                    Play(Profile.Cast, transform.position, Profile.CastSize, Color.white, duration: .9f);
                    break;
                case "Pop": case "Hide":
                    // 兔子的冒出/缩回已有完整交付帧，不再每次换位都叠加一团原地爆发。
                    if (_actor.Definition.Kind == EnemyKind.RabbitBoss) break;
                    Play(Profile.Cast, Feet, Profile.CastSize * .6f, Color.white, duration: .6f);
                    break;
                case "Dead":
                    Play(Profile.Death, transform.position, Profile.CastSize * 1.2f, Color.white, duration: 1f);
                    BossCameraImpulse.Pulse(Profile.CameraImpulse, .22f);
                    break;
            }
        }
        private void OnFeedback(EnemyFeedback cue)
        {
            if (Profile == null || _restoring) return;
            float angle = Mathf.Atan2(cue.Direction.y, cue.Direction.x) * Mathf.Rad2Deg;
            switch (cue.Kind)
            {
                case EnemyFeedbackKind.Strike:
                    CancelFollowing();
                    bool slash = _actor.Definition.Kind == EnemyKind.BatBoss;
                    Play(slash ? Profile.Slash : Profile.Impact, cue.Position,
                        slash ? Profile.SlashSize : Profile.ImpactSize, Color.white, duration: slash ? .32f : .6f,
                        mirror: slash && _actor.Facing < 0f);
                    if (slash) Play(Profile.Shot, cue.Position, 1.4f, Color.white, duration: .12f);
                    BossCameraImpulse.Pulse(Profile.CameraImpulse, .12f);
                    break;
                case EnemyFeedbackKind.ProjectileSpawn:
                    if (cue.Subject is EnemyProjectile shot)
                    {
                        var view = shot.GetComponent<EnemyProjectileView>();
                        if (view == null) view = shot.gameObject.AddComponent<EnemyProjectileView>();
                        view.Configure(Profile);
                    }
                    // 玩具/石块自身就是出手表现，不能让通用爆发闪光盖住已交付的实体弹体。
                    if (Profile.PhysicalProjectiles || Profile.ProjectileAnimations.Length > 0) break;
                    // 一次散射只播一个发射爆发，毛团弹和分裂不会淹没角色。
                    if (Time.time - _lastShot < .1f) break;
                    _lastShot = Time.time;
                    CancelFollowing();
                    bool ground = _actor.AnimationName == "HammerGround";
                    Play(ground ? Profile.Impact : Profile.Shot, ground ? Feet : cue.Position,
                        ground ? Profile.ImpactSize : 1.4f, Color.white, duration: ground ? .65f : .24f);
                    if (ground)
                    {
                        // 同一作者的能量环压成地面透视，与向上的冲击粒子分层播放。
                        Play(Profile.Cast, Feet + (Vector3)_actor.Motor.Up * .08f,
                            Profile.ImpactSize * 1.1f, Color.white, duration: .5f, aspect: .22f);
                        BossCameraImpulse.Pulse(Profile.CameraImpulse, .18f);
                    }
                    break;
                case EnemyFeedbackKind.ProjectileImpact:
                    if (Profile.PhysicalProjectiles) break;
                    Play(Profile.Impact, cue.Position, Profile.ProjectileAnimations.Length > 0 ? .45f : Profile.ImpactSize * .4f,
                        Color.white, angle, duration: .2f);
                    break;
                case EnemyFeedbackKind.BeamSpawn:
                    if (cue.Subject is EnemyBeam beam)
                    {
                        var view = beam.GetComponent<EnemyBeamView>();
                        if (view == null) view = beam.gameObject.AddComponent<EnemyBeamView>();
                        view.Configure(Profile);
                    }
                    break;
                case EnemyFeedbackKind.BarrageWarning:
                    Play(Profile.Charge, cue.Position, Profile.CastSize * .65f, Profile.Accent,
                        follow: transform, duration: _actor.Definition.ClockBurstWarning, barrageWarning: true);
                    break;
                case EnemyFeedbackKind.AreaStrike:
                    Play(Profile.Cast, Feet, _actor.Definition.BrickSlamRange * 2f, Profile.Accent,
                        duration: .45f, aspect: .18f);
                    Play(Profile.Impact, cue.Position, Profile.ImpactSize * 1.3f, Color.white, duration: .5f);
                    BossCameraImpulse.Pulse(Profile.CameraImpulse, .16f);
                    break;
                case EnemyFeedbackKind.RuleCast:
                    CancelFollowing();
                    // 已携带真实规则身份的施法由星环负责，避免旧通用爆发盖住能量核。
                    if (cue.RuleAttribute.HasValue) break;
                    Color tint = Color.white;
                    if (_actor.Definition.Kind == EnemyKind.ClockBoss)
                    {
                        var time = RuleSystem.Instance?.Table.GetSlot(AttributeId.Time);
                        tint = time?.Relation == RelationId.Enhance ? new Color(1f, .4f, .15f) : new Color(.25f, .7f, 1f);
                    }
                    bool rabbit = _actor.Definition.Kind == EnemyKind.RabbitBoss;
                    Play(rabbit ? Profile.Charge : Profile.Cast, cue.Position,
                        rabbit ? Profile.CastSize * .4f : Profile.CastSize, tint, duration: .7f);
                    break;
                case EnemyFeedbackKind.Summon:
                    CancelFollowing();
                    Play(Profile.Cast, cue.Position, Profile.CastSize, Color.white, duration: .9f);
                    break;
            }
        }
        public void Play(EnemyVfxClip clip, Vector3 position, float size, Color tint,
            float angle = 0f, Transform follow = null, float duration = -1f, bool mirror = false, float aspect = 1f,
            bool barrageWarning = false)
        {
            if (clip == null || Profile == null) return;
            Slot slot = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] == null)
                {
                    var go = new GameObject("EnemyVfx");
                    go.transform.SetParent(transform, false);
                    var renderer = go.AddComponent<SpriteRenderer>();
                    renderer.sharedMaterial = Profile.Material; renderer.sortingOrder = 18;
                    _slots[i] = new Slot { Renderer = renderer };
                }
                if (_slots[i].Clip == null) { slot = _slots[i]; break; }
            }
            if (slot == null && barrageWarning)
            {
                // 密集命中特效占满槽位时，优先让出一个瞬时效果，保证下一圈弹幕的预警可见。
                foreach (Slot active in _slots)
                    if (!active.BarrageWarning && (slot == null || active.Age > slot.Age)) slot = active;
            }
            if (slot == null) return;
            slot.Clip = clip; slot.Age = 0f; slot.Follow = follow;
            slot.BarrageWarning = barrageWarning;
            slot.Position = position;
            slot.Speed = duration > 0f ? clip.Duration / duration : 1f;
            slot.Offset = follow == null ? Vector3.zero : position - follow.position;
            slot.Renderer.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, angle));
            slot.Renderer.transform.localScale = new Vector3(size, size * aspect, 1f);
            slot.Renderer.flipX = mirror;
            slot.Renderer.color = tint; slot.Renderer.sprite = clip.Frame(0f); slot.Renderer.enabled = true;
        }
        private void LateUpdate()
        {
            _restoring = false;
            if (TimeManager.Instance?.IsPaused == true || _actor.Status?.IsPaused == true) return;
            foreach (Slot slot in _slots)
            {
                if (slot == null || slot.Clip == null) continue;
                if (slot.BarrageWarning && (_actor.Defeated || !(_actor.Brain is ClockBossBrain clock) || !clock.BarragePreparing))
                { slot.Clip = null; slot.Renderer.enabled = false; continue; }
                slot.Age += Time.deltaTime * slot.Speed;
                if (slot.Age >= slot.Clip.Duration) { slot.Clip = null; slot.Renderer.enabled = false; continue; }
                slot.Renderer.transform.position = slot.Follow != null ? slot.Follow.position + slot.Offset : slot.Position;
                slot.Renderer.sprite = slot.Clip.Frame(slot.Age);
            }
        }
        private void CancelFollowing()
        {
            foreach (Slot slot in _slots)
                if (slot != null && slot.Clip != null && slot.Follow != null && !slot.BarrageWarning)
                { slot.Clip = null; slot.Renderer.enabled = false; }
        }
        private void Clear()
        {
            foreach (Slot slot in _slots)
                if (slot != null) { slot.Clip = null; slot.Renderer.enabled = false; }
        }
    }
}
