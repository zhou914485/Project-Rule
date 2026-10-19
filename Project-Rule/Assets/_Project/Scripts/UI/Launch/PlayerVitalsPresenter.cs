using RuleGame.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.UI.Launch
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerVitalsPresenter : MonoBehaviour
    {
        [SerializeField] private EntityStatus _status;
        private VisualElement _healthClip, _energyClip;
        private Label _value;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _healthClip = root.Q("healthClip");
            _energyClip = root.Q("energyClip");
            _value = root.Q<Label>("vitalsValue");
            if (_status == null || _healthClip == null || _energyClip == null || _value == null)
            {
                Debug.LogError("[PlayerVitals] 请配置玩家 EntityStatus 和 PlayerVitals.uxml。", this);
                enabled = false;
                return;
            }
            _status.OnHealthChanged += HealthChanged;
            _status.OnEnergyChanged += EnergyChanged;
            HealthChanged(_status.Health, _status.HealthMax);
            EnergyChanged(_status.Energy, _status.EnergyMax);
        }

        private void OnDisable()
        {
            if (_status == null) return;
            _status.OnHealthChanged -= HealthChanged;
            _status.OnEnergyChanged -= EnergyChanged;
        }

        private void HealthChanged(float current, float max)
        {
            // 只改变裁切窗口，彩色原画保持固定尺寸，避免花纹和边框被压扁。
            _healthClip.style.width = Length.Percent(Ratio(current, max) * 100f);
            RefreshValue();
        }

        private void EnergyChanged(float current, float max)
        {
            _energyClip.style.width = Length.Percent(Ratio(current, max) * 100f);
            RefreshValue();
        }

        private void RefreshValue()
        {
            _value.text = $"生命 {_status.Health:0} / {_status.HealthMax:0}    能量 {_status.Energy:0} / {_status.EnergyMax:0}";
        }

        private static float Ratio(float value, float max) => max > 0f ? Mathf.Clamp01(value / max) : 0f;
    }
}
