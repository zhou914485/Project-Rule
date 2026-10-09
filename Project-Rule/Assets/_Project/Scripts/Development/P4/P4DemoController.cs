using System;
using System.Collections.Generic;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using RuleGame.UI.P4;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuleGame.Development.P4
{
    /// <summary>
    /// 独立白盒场景的临时集成入口：把 HUD 接到 P2 重力系统，其余属性保持 UI 预览。
    /// 暂时承担初始化、能量、暂停和重试；P1/P3 落地后由正式适配器接管。
    /// </summary>
    [DefaultExecutionOrder(1000)] // 在角色提交 MovePosition 后推进本帧物理模拟。
    public sealed class P4DemoController : MonoBehaviour
    {
        [SerializeField] private P4HudPresenter _hud;
        [SerializeField, Min(1f)] private float _healthMax = 100f;
        [SerializeField, Min(1f)] private float _energyMax = 100f;
        [SerializeField, Min(0f)] private float _startingHealth = 80f;
        [SerializeField, Min(0f)] private float _startingEnergy = 100f;
        [SerializeField] private TextAsset _previewCatalog;
        [SerializeField] private RuleCatalog _gravityCatalog;
        private List<DemoRulePreview> _previews = new();

        // 重力槽位与寿命只由 P2 维护；下面的字典仅保存其他属性的演示数据。
        private RuleSystem _realRules;
        private P4DemoRuleReleaseService _gravityRelease;
        private Vector2 _initialGravity;
        private SimulationMode2D _initialSimulationMode;
        private readonly Dictionary<string, DemoRulePreview> _activePreviews = new();
        private readonly Dictionary<string, float> _remaining = new();
        private readonly List<string> _expired = new();
        private float _nextDisplayRefresh;
        private bool _goalReached;
        private float _health;
        private float _energy;

        public bool IsWorldPaused => _hud.IsAssemblyOpen || _goalReached;
        public float Energy => _energy;
        public event Action OnDemoReset;

        public void Configure(P4HudPresenter hud, TextAsset previewCatalog, RuleCatalog gravityCatalog = null)
        {
            _hud = hud;
            _previewCatalog = previewCatalog;
            _gravityCatalog = gravityCatalog;
        }

        private void Awake()
        {
            // 演示独占 RuleSystem 生命周期，不能与正式 RuleSystemDriver 同时放在场景里。
            if (_gravityCatalog == null)
            {
                _gravityCatalog = Resources.Load<RuleCatalog>("P4GravityDemoCatalog");
            }
            if (_gravityCatalog == null || RuleSystem.Instance != null)
            {
                Debug.LogError("P4 demo requires its gravity catalog and owns the rule-system lifetime. Do not add a second RuleSystemDriver to this demo scene.", this);
                return;
            }
            _initialGravity = Physics2D.gravity;
            _initialSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            RuleSystem.Initialize(_gravityCatalog);
            _realRules = RuleSystem.Instance;
            _gravityRelease = new P4DemoRuleReleaseService(_realRules, _gravityCatalog);
        }

        private void OnEnable()
        {
            if (_hud == null)
            {
                Debug.LogError("P4 demo requires its HUD reference.", this);
                enabled = false;
                return;
            }
            if (_previewCatalog == null)
            {
                _previewCatalog = Resources.Load<TextAsset>("P4RulePreviews");
                if (_previewCatalog == null)
                {
                    Debug.LogError("P4 demo requires its rule preview catalog.", this);
                    enabled = false;
                    return;
                }
            }
            _previews = JsonUtility.FromJson<DemoRuleCatalog>(_previewCatalog.text).Previews;
            _hud.OnSelectionChanged += RefreshPreview;
            _hud.OnConfirmRequested += ConfirmPreview;
            _hud.OnResetRequested += ResetDemo;
            if (_realRules != null)
            {
                _realRules.OnRulesChanged += RefreshGravityDisplay;
            }
        }

        private void Start() => ResetDemo();

        private void OnDisable()
        {
            if (_realRules != null)
            {
                _realRules.OnRulesChanged -= RefreshGravityDisplay;
            }
            if (_hud != null)
            {
                _hud.OnSelectionChanged -= RefreshPreview;
                _hud.OnConfirmRequested -= ConfirmPreview;
                _hud.OnResetRequested -= ResetDemo;
            }
        }

        private void OnDestroy()
        {
            // 全局状态属于整个工程，退出演示时恢复进入前的值，避免污染后续场景。
            if (_realRules != null && RuleSystem.Instance == _realRules)
            {
                _realRules.ClearAll();
                RuleSystem.Reset();
                Physics2D.gravity = _initialGravity;
                Physics2D.simulationMode = _initialSimulationMode;
            }
        }

        private void FixedUpdate()
        {
            // 拼装时不推进物理；不改 timeScale，保留未来时间规则的全局倍率。
            if (_realRules != null && !IsWorldPaused)
            {
                Physics2D.Simulate(Time.fixedDeltaTime);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.tabKey.wasPressedThisFrame)
                {
                    _hud.SetAssemblyOpen(!_hud.IsAssemblyOpen);
                }
                else if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    _hud.SetAssemblyOpen(false);
                }
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    ResetDemo();
                }
            }
            if (!IsWorldPaused)
            {
                _realRules?.Tick(Time.deltaTime);
                _gravityRelease?.Tick(Time.deltaTime);
                TickPreviews(Time.deltaTime);
            }
        }

        public void ResetDemo()
        {
            // 先清理 P2 规则、恢复重力，再通过事件将角色送回出生点。
            _goalReached = false;
            _realRules?.ClearAll();
            _gravityRelease?.Reset();
            _health = Mathf.Clamp(_startingHealth, 0f, _healthMax);
            _energy = Mathf.Clamp(_startingEnergy, 0f, _energyMax);
            _activePreviews.Clear();
            _remaining.Clear();
            string[] attributes = { "gravity", "time", "friction", "mass", "relation", "damage", "causality" };
            string[] relations = { "revert", "nullify", "constant", "enhance", "weaken" };
            foreach (string attr in attributes)
            {
                _hud.ClearActiveRule(attr);
                _hud.SetChoiceAvailable(attr, true, _previews.Exists(p => p.AttributeId == attr));
            }
            foreach (string rel in relations)
            {
                _hud.SetChoiceAvailable(rel, false, _previews.Exists(p => p.RelationId == rel));
            }
            _hud.SetAssemblyOpen(false);
            _hud.SetVitals(_health, _healthMax, _energy, _energyMax);
            _hud.SetCombinationCount(_previews.Count);
            _hud.SetObjective("全部词条已开放，按 Tab 验证拼装界面。");
            _hud.SetMessage("重力会改变世界；其他词条仅作 UI 演示。按 R 重置。");
            _hud.SelectAttribute("gravity");
            _hud.SelectRelation("revert");
            OnDemoReset?.Invoke();
        }

        private DemoRulePreview FindPreview(string attribute, string relation)
        {
            return _previews.Find(p => p.AttributeId == attribute && p.RelationId == relation);
        }

        public void RefreshPreview(string attribute, string relation)
        {
            DemoRulePreview preview = FindPreview(attribute, relation);
            if (preview == null)
            {
                _hud.SetPreview("这两个词，还拼不成规则。", "换一个词试试。", 0f, false, "当前组合不可用");
                return;
            }
            if (attribute == "gravity")
            {
                RefreshGravityPreview(preview, relation);
                return;
            }
            bool enoughEnergy = _energy >= preview.Cost;
            string reason = preview.PendingDesign
                ? "策划未定稿，可验证 UI；确认只更新演示槽位。"
                : "确认只更新演示槽位，同属性的新规则会替换旧规则。";
            if (!enoughEnergy)
            {
                reason += $" 还差 {Mathf.CeilToInt(preview.Cost - _energy)} 能量，按 R 重置。";
            }
            _hud.SetPreview(preview.Title, preview.Description, preview.Cost, enoughEnergy, reason);
        }

        private void RefreshGravityPreview(DemoRulePreview preview, string relation)
        {
            // 费用、冷却和有效性来自正式数据；空文案才回退到规则矩阵说明。
            if (_realRules == null || !Enum.TryParse(relation, true, out RelationId relationId))
            {
                _hud.SetPreview(preview.Title, preview.Description, 0f, false, "正式重力系统未连接。");
                return;
            }
            RuleDef definition = _gravityCatalog.Get(AttributeId.Gravity, relationId);
            bool canRelease = _gravityRelease.CanRelease(relationId, _energy, out string reason);
            string title = !string.IsNullOrEmpty(definition?.DisplayName) ? definition.DisplayName : preview.Title;
            string description = !string.IsNullOrEmpty(definition?.Description) ? definition.Description : preview.Description;
            _hud.SetPreview(title, description, _realRules.GetCost(AttributeId.Gravity, relationId).Energy, canRelease, reason);
        }

        private void ConfirmPreview(string attribute, string relation)
        {
            DemoRulePreview preview = FindPreview(attribute, relation);
            if (preview != null && attribute == "gravity")
            {
                if (_gravityRelease == null || !Enum.TryParse(relation, true, out RelationId relationId))
                {
                    RefreshPreview(attribute, relation);
                    return;
                }
                if (!_gravityRelease.TryRelease(relationId, _energy, out float spent, out string reason))
                {
                    RefreshPreview(attribute, relation);
                    _hud.SetMessage(reason);
                    return;
                }
                // P2 接受请求后才扣费；失败不会消耗能量，也不会关闭拼装界面。
                _energy -= spent;
                _hud.SetVitals(_health, _healthMax, _energy, _energyMax);
                RefreshPreview(attribute, relation);
                _hud.SetMessage($"{preview.Title} / 已作用于世界。");
                _hud.SetAssemblyOpen(false);
                return;
            }
            if (preview == null || _energy < preview.Cost)
            {
                RefreshPreview(attribute, relation);
                return;
            }
            _energy -= preview.Cost;
            _activePreviews[attribute] = preview;
            _remaining[attribute] = preview.Duration;
            RefreshRuleDisplay(attribute);
            _hud.SetVitals(_health, _healthMax, _energy, _energyMax);
            RefreshPreview(attribute, relation);
            _hud.SetMessage($"{preview.Title} / 已加入预览。");
            _hud.SetAssemblyOpen(false);
            Debug.Log($"[P4 Demo] Request: {attribute}.{relation}; preview only, no world rule applied.", this);
        }

        private void TickPreviews(float deltaTime)
        {
            _expired.Clear();
            foreach (KeyValuePair<string, DemoRulePreview> item in _activePreviews)
            {
                if (item.Value.Duration <= 0f)
                {
                    continue;
                }
                _remaining[item.Key] -= deltaTime;
                if (_remaining[item.Key] <= 0f)
                {
                    _expired.Add(item.Key);
                }
            }
            foreach (string attribute in _expired)
            {
                _activePreviews.Remove(attribute);
                _remaining.Remove(attribute);
                _hud.ClearActiveRule(attribute);
            }
            _nextDisplayRefresh -= deltaTime;
            if (_nextDisplayRefresh <= 0f)
            {
                _nextDisplayRefresh = 0.2f;
                foreach (string attribute in _activePreviews.Keys)
                {
                    RefreshRuleDisplay(attribute);
                }
                RefreshGravityDisplay();
                if (_hud.SelectedAttributeId == "gravity")
                {
                    RefreshPreview("gravity", _hud.SelectedRelationId);
                }
            }
        }

        private void RefreshRuleDisplay(string attribute)
        {
            DemoRulePreview preview = _activePreviews[attribute];
            _hud.SetActiveRule(attribute, preview.Title,
                preview.Duration <= 0f ? "本关持续" : $"{Mathf.CeilToInt(_remaining[attribute])} 秒", "演示");
        }

        private void RefreshGravityDisplay()
        {
            // 直接读取 P2 的剩余时间和来源，避免两份计时器发生漂移。
            ActiveRule? slot = _realRules?.Table.GetSlot(AttributeId.Gravity);
            if (!slot.HasValue)
            {
                _hud.ClearActiveRule("gravity");
                return;
            }
            ActiveRule rule = slot.Value;
            string relation = rule.Relation.ToString().ToLowerInvariant();
            DemoRulePreview preview = FindPreview("gravity", relation);
            RuleDef definition = _gravityCatalog.Get(AttributeId.Gravity, rule.Relation);
            string title = !string.IsNullOrEmpty(definition?.DisplayName) ? definition.DisplayName : preview?.Title;
            string source = rule.Source == RuleSource.Player ? "玩家" : rule.Source == RuleSource.Enemy ? "敌人" : "关卡";
            _hud.SetActiveRule("gravity", title ?? "重力", rule.Remaining < 0f ? "本关持续" : $"{Mathf.CeilToInt(rule.Remaining)} 秒", source);
        }

        public void NotifyGoalReached()
        {
            if (_goalReached)
            {
                return;
            }
            _goalReached = true;
            _hud.SetObjective("已抵达出口。");
            _hud.SetMessage("按 R，重新走一次。");
        }
    }
}
