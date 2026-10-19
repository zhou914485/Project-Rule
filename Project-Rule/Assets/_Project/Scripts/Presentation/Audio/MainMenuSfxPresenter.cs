using RuleGame.UI.Launch;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Presentation.Audio
{
    [DisallowMultipleComponent, RequireComponent(typeof(MainMenuPresenter))]
    public sealed class MainMenuSfxPresenter : MonoBehaviour
    {
        private MainMenuPresenter _menu;
        private void Awake() => _menu = GetComponent<MainMenuPresenter>();
        private void OnEnable() => _menu.ButtonActivated += Activated;
        private void OnDisable() => _menu.ButtonActivated -= Activated;

        private void Activated(Button button)
        {
            SfxCue cue = button.name == "modalCloseButton" || button.name == "modalBackButton" ? SfxCue.MenuBack
                : button.name == "quitConfirmButton" ? SfxCue.MenuConfirm : SfxCue.MenuClick;
            SfxPlayer.Active?.Play(cue);
        }
    }
}
