using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.UI.P4
{
    /// <summary>
    /// P4 的纯显示层：绑定 UXML 固定元素，更新 HUD，并向外发出交互事件。
    /// 能量结算、规则释放和暂停由订阅者负责，方便正式系统替换演示适配器。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class P4HudPresenter : MonoBehaviour
    {
        private readonly Dictionary<Button, Action> _clickHandlers = new();
        private readonly Dictionary<string, Button> _attributes = new();
        private readonly Dictionary<string, Button> _relations = new();
        private readonly Dictionary<string, VisualElement> _ruleRows = new();
        private VisualElement _root;
        private VisualElement _assembly;
        private ProgressBar _health;
        private ProgressBar _energy;
        private Button _confirm;
        private Button _open;
        private bool _canConfirm;
        private bool _isAssemblyOpen;

        public bool IsAssemblyOpen => _isAssemblyOpen;
        public string SelectedAttributeId { get; private set; }
        public string SelectedRelationId { get; private set; }

        // ID 对应 UXML 按钮的 name；由适配器转换为团队共享枚举。
        public event Action<string, string> OnSelectionChanged;
        public event Action<string, string> OnConfirmRequested;
        public event Action<bool> OnAssemblyVisibilityChanged;
        public event Action OnResetRequested;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _assembly = _root.Q<VisualElement>("assemblyOverlay");
            _health = _root.Q<ProgressBar>("healthBar");
            _energy = _root.Q<ProgressBar>("energyBar");
            _confirm = _root.Q<Button>("confirmButton");
            _open = _root.Q<Button>("assembleButton");
            if (_assembly == null || _health == null || _energy == null || _confirm == null || _open == null)
            {
                Debug.LogError("P4 HUD requires P4Hud.uxml on its UIDocument.", this);
                enabled = false;
                return;
            }

            BindChoices("attribute-choice", _attributes, true);
            BindChoices("relation-choice", _relations, false);
            _root.Query<VisualElement>(className: "rule-row").ForEach(row => _ruleRows.Add(row.name, row));
            Bind(_open, () => SetAssemblyOpen(true));
            Bind(_root.Q<Button>("closeButton"), () => SetAssemblyOpen(false));
            Bind(_confirm, ConfirmSelection);
            Bind(_root.Q<Button>("resetButton"), () => OnResetRequested?.Invoke());
            _confirm.SetEnabled(false);
            _assembly.EnableInClassList("hidden", !_isAssemblyOpen);
        }

        private void OnDisable()
        {
            // 保留每个委托实例并逐一解绑，避免反复启用 HUD 后一次点击触发多次扣费。
            if (_isAssemblyOpen)
            {
                SetAssemblyOpen(false);
            }
            foreach (KeyValuePair<Button, Action> binding in _clickHandlers)
            {
                binding.Key.clicked -= binding.Value;
            }
            _clickHandlers.Clear();
            _attributes.Clear();
            _relations.Clear();
            _ruleRows.Clear();
            _canConfirm = false;
            SelectedAttributeId = null;
            SelectedRelationId = null;
        }

        private void Bind(Button button, Action handler)
        {
            button.clicked += handler;
            _clickHandlers.Add(button, handler);
        }

        private void BindChoices(string className, Dictionary<string, Button> choices, bool isAttribute)
        {
            _root.Query<Button>(className: className).ForEach(button =>
            {
                choices.Add(button.name, button);
                Bind(button, () => SelectChoice(button.name, isAttribute));
            });
        }

        private void SelectChoice(string id, bool isAttribute)
        {
            Dictionary<string, Button> choices = isAttribute ? _attributes : _relations;
            if (!choices.TryGetValue(id, out Button button) || !button.enabledSelf)
            {
                return;
            }
            if (isAttribute)
            {
                SelectedAttributeId = id;
            }
            else
            {
                SelectedRelationId = id;
            }
            foreach (KeyValuePair<string, Button> choice in choices)
            {
                choice.Value.EnableInClassList("selected", choice.Key == id);
            }
            // 切换词条后先撤销确认资格，等待适配器检查新组合的费用、冷却和可用性。
            _canConfirm = false;
            _confirm.SetEnabled(false);
            Label selectedWord = _root.Q<Label>(isAttribute ? "selectedAttribute" : "selectedRelation");
            selectedWord.text = button.text;
            selectedWord.EnableInClassList("long-word", button.text.Length > 3);
            OnSelectionChanged?.Invoke(SelectedAttributeId, SelectedRelationId);
        }

        public void SelectAttribute(string id) => SelectChoice(id, true);
        public void SelectRelation(string id) => SelectChoice(id, false);

        public void ConfirmSelection()
        {
            // 同时校验界面状态和两个词条，程序调用也遵循按钮的确认条件。
            if (_isAssemblyOpen && _canConfirm && SelectedAttributeId != null && SelectedRelationId != null
                && _attributes.TryGetValue(SelectedAttributeId, out Button attribute) && attribute.enabledSelf
                && _relations.TryGetValue(SelectedRelationId, out Button relation) && relation.enabledSelf)
            {
                OnConfirmRequested?.Invoke(SelectedAttributeId, SelectedRelationId);
            }
        }

        public void SetAssemblyOpen(bool open)
        {
            if (_assembly == null || _isAssemblyOpen == open)
            {
                return;
            }
            _isAssemblyOpen = open;
            _assembly.EnableInClassList("hidden", !open);
            // 显示层只通知开关变化；正式接入时由 P1 的适配器申请或释放暂停。
            OnAssemblyVisibilityChanged?.Invoke(open);
            if (open)
            {
                _root.Q<Button>("closeButton").Focus();
            }
            else
            {
                _open.Focus();
            }
        }

        public void SetVitals(float health, float healthMax, float energy, float energyMax)
        {
            _health.highValue = Mathf.Max(1f, healthMax);
            _health.value = Mathf.Clamp(health, 0f, _health.highValue);
            _energy.highValue = Mathf.Max(1f, energyMax);
            _energy.value = Mathf.Clamp(energy, 0f, _energy.highValue);
            _root.Q<Label>("healthValue").text = $"{health:0} / {healthMax:0}";
            _root.Q<Label>("energyValue").text = $"{energy:0} / {energyMax:0}";
            _root.Q<Label>("assemblyEnergy").text = $"手中能量  {energy:0} / {energyMax:0}";
        }

        public void SetChoiceAvailable(string id, bool isAttribute, bool available)
        {
            Dictionary<string, Button> choices = isAttribute ? _attributes : _relations;
            if (choices.TryGetValue(id, out Button button))
            {
                button.SetEnabled(available);
            }
        }

        public void SetPreview(string title, string description, float cost, bool canConfirm, string reason)
        {
            _root.Q<Label>("previewTitle").text = title;
            _root.Q<Label>("previewDescription").text = description;
            _root.Q<Label>("previewCost").text = $"需要 {cost:0} 能量";
            _root.Q<Label>("previewReason").text = reason;
            _canConfirm = canConfirm;
            _confirm.SetEnabled(canConfirm);
        }

        public void SetCombinationCount(int count)
        {
            _root.Q<Label>("combinationCount").text = $"可以拼出 {count} 条规则";
        }

        public void SetActiveRule(string attributeId, string title, string remaining, string source)
        {
            if (_ruleRows.TryGetValue(attributeId + "Rule", out VisualElement row))
            {
                row.RemoveFromClassList("hidden");
                row.Q<Label>(className: "rule-title").text = title;
                row.Q<Label>(className: "rule-time").text = remaining;
                row.Q<Label>(className: "rule-source").text = source;
                _root.Q<Label>("noRules").AddToClassList("hidden");
            }
        }

        public void ClearActiveRule(string attributeId)
        {
            if (_ruleRows.TryGetValue(attributeId + "Rule", out VisualElement row))
            {
                row.AddToClassList("hidden");
            }
            bool anyVisible = false;
            foreach (VisualElement ruleRow in _ruleRows.Values)
            {
                anyVisible |= !ruleRow.ClassListContains("hidden");
            }
            _root.Q<Label>("noRules").EnableInClassList("hidden", anyVisible);
        }

        public void SetObjective(string text) => _root.Q<Label>("objective").text = text;
        public void SetMessage(string text) => _root.Q<Label>("statusMessage").text = text;
    }
}
