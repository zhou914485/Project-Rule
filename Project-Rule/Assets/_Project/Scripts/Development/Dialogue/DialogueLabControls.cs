using RuleGame.Core.Rules;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Infrastructure.Rules;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Development.Dialogue
{
    /// <summary>独立试播场景的调试控件，正式关卡不需要此组件，也不依赖 P4 白盒角色。</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DialogueLabControls : MonoBehaviour
    {
        [SerializeField] private DialogueHost _host;
        private VisualElement _root;
        private Vector2 _initialGravity;
        private RuleSystem _labRules;

        private void Awake()
        {
            _initialGravity = Physics2D.gravity;
            _labRules = RuleSystem.Instance;
        }
        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.Q<Button>("observer").clicked += SpeakObserver;
            _root.Q<Button>("caretaker").clicked += SpeakCaretaker;
            _root.Q<Button>("archivist").clicked += SpeakArchivist;
            _root.Q<Button>("reverseGravity").clicked += Reverse;
            _root.Q<Button>("clearRules").clicked += Clear;
        }
        private void OnDisable()
        {
            _root.Q<Button>("observer").clicked -= SpeakObserver;
            _root.Q<Button>("caretaker").clicked -= SpeakCaretaker;
            _root.Q<Button>("archivist").clicked -= SpeakArchivist;
            _root.Q<Button>("reverseGravity").clicked -= Reverse;
            _root.Q<Button>("clearRules").clicked -= Clear;
        }
        private void SpeakObserver() => _host.TrySpeak("observer");
        private void SpeakCaretaker() => _host.TrySpeak("caretaker");
        private void SpeakArchivist() => _host.TrySpeak("archivist");
        private void Reverse()
        {
            // 试播工具以关卡来源改变测试状态，绝不充当玩家的释放/扣费服务。
            _labRules?.Request(AttributeId.Gravity, RelationId.Revert, RuleSource.LevelScript);
        }
        private void Clear() => _labRules?.ClearAll();

        private void OnDestroy()
        {
            // 试播可能改动全局物理参数；退出时只清理本场景捕获的规则实例并恢复重力。
            _labRules?.ClearAll();
            Physics2D.gravity = _initialGravity;
        }
    }
}
