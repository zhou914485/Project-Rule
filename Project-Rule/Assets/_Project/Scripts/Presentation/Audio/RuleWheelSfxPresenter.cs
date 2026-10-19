using RuleGame.Gameplay.Player;
using RuleGame.UI.RuleWheel;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    [DisallowMultipleComponent, RequireComponent(typeof(RuleWheelPresenter))]
    public sealed class RuleWheelSfxPresenter : MonoBehaviour
    {
        [SerializeField] private SfxPlayer _audio;
        private RuleWheelPresenter _wheel;

        private void Awake()
        {
            _wheel = GetComponent<RuleWheelPresenter>();
            if (_audio == null) _audio = GetComponentInParent<SfxPlayer>();
        }

        private void OnEnable()
        {
            _wheel.SelectionOpened += Open;
            _wheel.ChoicePreviewed += Preview;
            _wheel.SelectionClosed += Close;
            _wheel.SelectionReturned += Back;
            _wheel.SelectionRejected += Reject;
        }

        private void OnDisable()
        {
            _wheel.SelectionOpened -= Open;
            _wheel.ChoicePreviewed -= Preview;
            _wheel.SelectionClosed -= Close;
            _wheel.SelectionReturned -= Back;
            _wheel.SelectionRejected -= Reject;
        }

        private void Play(SfxCue cue)
        {
            if (_audio != null && _wheel.isActiveAndEnabled) _audio.Play(cue);
        }

        private void Open() => Play(SfxCue.WheelOpen);
        private void Preview() => Play(SfxCue.WheelPreview);
        private void Back() => Play(SfxCue.WheelBack);
        private void Reject(RuleReleaseStatus reason) => Play(SfxCue.WheelError);
        private void Close(bool released) => Play(released ? SfxCue.WheelConfirm : SfxCue.WheelCancel);
    }
}
