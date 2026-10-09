using UnityEngine;

namespace RuleGame.Gameplay.Dialogue
{
    /// <summary>复用的 NPC 交互入口，不依赖临时 P4 玩家或尚未实现的 P3 输入系统。</summary>
    public sealed class DialogueNpc : MonoBehaviour
    {
        [SerializeField] private DialogueHost _host;
        [SerializeField] private string _speakerId;
        [SerializeField, Min(0.1f)] private float _interactionRadius = 2f;
        [SerializeField, Min(0f)] private float _repeatDelay = 2f;
        private float _nextSpeakAt;
        public DialogueHost Host => _host;
        public string SpeakerId => _speakerId;

        /// <summary>P3 在交互键按下时传入角色 Transform；未接入时可保持不调用。</summary>
        public bool TryInteract(Transform actor)
        {
            return actor != null && ((Vector2)(actor.position - transform.position)).sqrMagnitude <= _interactionRadius * _interactionRadius
                && TrySpeak();
        }

        /// <summary>场景事件、按钮或教学触发器可直接调用，不强制依赖角色控制器。</summary>
        public void Speak() => TrySpeak();

        public bool TrySpeak()
        {
            if (!isActiveAndEnabled || _host == null || Time.unscaledTime < _nextSpeakAt) return false;
            if (!_host.TrySpeak(_speakerId)) return false;
            _nextSpeakAt = Time.unscaledTime + _repeatDelay;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.8f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, _interactionRadius);
        }
    }
}
