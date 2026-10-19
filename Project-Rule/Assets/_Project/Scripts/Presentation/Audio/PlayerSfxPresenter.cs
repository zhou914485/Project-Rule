using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    [DefaultExecutionOrder(50), DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    public sealed class PlayerSfxPresenter : MonoBehaviour
    {
        [SerializeField] private SfxPlayer _audio;
        private PlayerController _player;
        private EntityStatus _status;
        private EntityDamageable _damageable;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _status = GetComponent<EntityStatus>();
            _damageable = GetComponent<EntityDamageable>();
            if (_audio == null) _audio = GetComponentInParent<SfxPlayer>();
        }

        private void OnEnable()
        {
            _player.OnJumped += Jump;
            _player.OnAirJumped += AirJump;
            _player.OnWallJumped += WallJump;
            _player.OnLanded += Land;
            _player.OnDashed += Dash;
            _player.OnAttackStruck += Sword;
            if (_damageable != null) _damageable.OnDamageTaken += Hurt;
        }

        private void OnDisable()
        {
            _player.OnJumped -= Jump;
            _player.OnAirJumped -= AirJump;
            _player.OnWallJumped -= WallJump;
            _player.OnLanded -= Land;
            _player.OnDashed -= Dash;
            _player.OnAttackStruck -= Sword;
            if (_damageable != null) _damageable.OnDamageTaken -= Hurt;
        }

        private void Play(SfxCue cue, bool lethal = false)
        {
            if (_audio == null || !_player.isActiveAndEnabled || (_status != null
                && (_status.IsPaused || (!lethal && _status.Health <= 0f)))) return;
            _audio.Play(cue);
        }

        private void Jump() => Play(SfxCue.Jump);
        private void AirJump() => Play(SfxCue.AirJump);
        private void WallJump() => Play(SfxCue.WallJump);
        private void Land() => Play(SfxCue.Land);
        private void Dash() => Play(SfxCue.Dash);
        private void Sword(PlayerAttackCue cue) => Play(cue.Heavy ? SfxCue.SwordHeavy : SfxCue.SwordLight);
        private void Hurt(DamageInfo info) => Play(SfxCue.Hurt, true);
    }
}
