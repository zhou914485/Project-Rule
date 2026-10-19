using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    /// <summary>机关只读表现：状态事件负责开关与破碎，真实接触法向速度负责碰撞。</summary>
    [DisallowMultipleComponent]
    public sealed class MechanismSfxPresenter : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _minimumImpactSpeed = 1.5f;
        private CitySwitch _switch;
        private ImpactButton _button;
        private GravityBoulder _boulder;
        private SpringBackBody _spring;
        private BreakableGate _gate;
        private CityBomb _bomb;
        private CityTrackBomb _trackBomb;
        private RustyGear _gear;
        private TrestleTrap _trestle;
        private Rigidbody2D _body;
        private Vector2 _velocity;
        private SfxCue _impact;

        public static bool Supports(MonoBehaviour component) => component is CitySwitch || component is ImpactButton
            || component is GravityBoulder || component is SpringBackBody || component is BreakableGate
            || component is CityBomb || component is CityTrackBomb || component is RustyGear
            || component is TrestleTrap || component is StoneBody || component is PushableBody || component is CityMarble;

        private void Awake()
        {
            _switch = GetComponent<CitySwitch>(); _button = GetComponent<ImpactButton>();
            _boulder = GetComponent<GravityBoulder>(); _spring = GetComponent<SpringBackBody>();
            _gate = GetComponent<BreakableGate>(); _bomb = GetComponent<CityBomb>();
            _trackBomb = GetComponent<CityTrackBomb>(); _gear = GetComponent<RustyGear>();
            _trestle = GetComponent<TrestleTrap>(); _body = GetComponent<Rigidbody2D>();
            _impact = _gate != null || _trestle != null ? SfxCue.ImpactWood
                : (_spring != null || _gear != null || _bomb != null || _trackBomb != null || GetComponent<PushableBody>() != null)
                    ? SfxCue.ImpactMetal : SfxCue.ImpactStone;
        }
        private void OnEnable()
        {
            if (_switch != null) _switch.OnChanged.AddListener(Switch);
            if (_button != null) { _button.OnPressed += On; _button.OnReleased += Off; }
            if (_boulder != null) { _boulder.OnArmed += StoneCrack; _boulder.OnShattered += StoneBreak; _boulder.OnUnsealed += On; }
            if (_spring != null) _spring.OnKnockedBack += Knocked;
            if (_gate != null) _gate.OnBroken += WoodBreak;
            if (_bomb != null) _bomb.OnDetonated += Explode;
            if (_trackBomb != null) _trackBomb.OnDetonated += Explode;
            if (_gear != null) { _gear.OnActivated += On; _gear.OnDeactivated += Off; }
            if (_trestle != null) { _trestle.OnToppled += Toppled; _trestle.OnSealed += Off; }
        }
        private void OnDisable()
        {
            if (_switch != null) _switch.OnChanged.RemoveListener(Switch);
            if (_button != null) { _button.OnPressed -= On; _button.OnReleased -= Off; }
            if (_boulder != null) { _boulder.OnArmed -= StoneCrack; _boulder.OnShattered -= StoneBreak; _boulder.OnUnsealed -= On; }
            if (_spring != null) _spring.OnKnockedBack -= Knocked;
            if (_gate != null) _gate.OnBroken -= WoodBreak;
            if (_bomb != null) _bomb.OnDetonated -= Explode;
            if (_trackBomb != null) _trackBomb.OnDetonated -= Explode;
            if (_gear != null) { _gear.OnActivated -= On; _gear.OnDeactivated -= Off; }
            if (_trestle != null) { _trestle.OnToppled -= Toppled; _trestle.OnSealed -= Off; }
        }
        private void FixedUpdate() { if (_body != null) _velocity = _body.linearVelocity; }
        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision == null) return;
            var other = collision.collider.GetComponentInParent<MechanismSfxPresenter>();
            if (other != null && other.isActiveAndEnabled && other.GetEntityId().CompareTo(GetEntityId()) < 0) return;
            float speed = 0f;
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector2 normal = collision.GetContact(i).normal;
                speed = Mathf.Max(speed, Mathf.Max(Mathf.Abs(Vector2.Dot(collision.relativeVelocity, normal)),
                    Mathf.Abs(Vector2.Dot(_velocity, normal))));
            }
            if (speed >= _minimumImpactSpeed) Play(_impact);
        }
        private void Play(SfxCue cue) => SfxPlayer.Active?.PlayAt(cue, transform.position, this);
        private void Switch(bool on) { if (on) On(); else Off(); }
        private void On() => Play(SfxCue.SwitchOn);
        private void Off() => Play(SfxCue.SwitchOff);
        private void StoneCrack() => Play(SfxCue.ImpactStone);
        private void StoneBreak() => Play(SfxCue.BreakStone);
        private void WoodBreak() => Play(SfxCue.BreakWood);
        private void Explode() => Play(SfxCue.Explosion);
        private void Toppled(GameObject pusher) => Play(SfxCue.BreakWood);
        private void Knocked(Vector2 impulse) { if (impulse.sqrMagnitude > 0.001f) Play(SfxCue.ImpactMetal); }
    }
}
