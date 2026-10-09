using UnityEngine;

namespace RuleGame.Gameplay.Dialogue
{
    /// <summary>由物件自己的状态事件更新，例如门打开或房屋损坏；不从历史猜当前状态。</summary>
    public sealed class DialogueSceneFlag : MonoBehaviour
    {
        [SerializeField, Tooltip("台词中的 scene_flag 参数，场景内唯一。")]
        private string _key;
        [SerializeField] private bool _value;
        public string Key => _key;
        public bool Value => _value;
        public void SetValue(bool value) => _value = value;
    }
}
