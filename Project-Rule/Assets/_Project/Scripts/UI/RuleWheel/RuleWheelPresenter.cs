using System;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Rules;
using RuleGame.Infrastructure.Interaction;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.UI.RuleWheel
{
    /// <summary>
    /// 常驻一级属性扇、悬停展开二级关系扇。攻击键只结算当前关系。
    /// 输入与镜头以令牌接管；慢时间交给 P1 的 <c>TimeManager</c>（以 <c>TimeSource.UI</c> 来源合成），
    /// 关闭 / 禁用 / 失焦统一释放。
    /// </summary>
    [DefaultExecutionOrder(-80), RequireComponent(typeof(UIDocument))]
    public sealed class RuleWheelPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerController _player;
        [SerializeField] private RuleCatalog _catalog;
        [SerializeField] private MonoBehaviour _unlocks;
        [SerializeField] private MonoBehaviour _cameraFocus;
        [SerializeField] private Camera _worldCamera;
        public const float DefaultSelectionScale = 0.001f;
        [SerializeField, Range(0.001f, 1f)] private float _selectionScale = DefaultSelectionScale;
        [SerializeField] private Vector2 _playerOffset = new Vector2(70f, 0f);
        private static readonly AttributeId[] Attributes = (AttributeId[])Enum.GetValues(typeof(AttributeId));
        private static readonly RelationId[] Relations = (RelationId[])Enum.GetValues(typeof(RelationId));
        private static readonly string[] AttributeWords = { "重力", "时间", "摩擦", "质量", "伤害/生命", "因果" };
        private static readonly string[] RelationWords = { "反转", "失效", "恒定", "增强", "削弱" };
        private VisualElement _document, _root, _relationGroup;
        private RuleFan _fan, _relationFan;
        private Label _feedback;
        private Transform _visualAnchor;
        private RuleSelectionSession _session;
        private ITimeManager _time;
        private bool _timeScaleApplied;
        private IDisposable _inputLease, _focusLease;
        private bool _waitForRelease;
        private float _nextAvailability;
        private readonly bool[] _attributeAvailable = new bool[Attributes.Length];
        private readonly bool[] _relationAvailable = new bool[Relations.Length];
        private readonly bool[] _attributeActive = new bool[Attributes.Length];
        private readonly bool[] _relationActive = new bool[Relations.Length];
        private bool _selectionActive, _relationStick, _waitForStickNeutral;
        private bool _facingLeft;
        private bool _closingVisual;
        private float _closeElapsed;
        public event Action SelectionOpened;
        /// <summary>运行时轮盘接线完成。供独立表现层附加视图，UI 不依赖具体特效程序集。</summary>
        public static event Action<RuleWheelPresenter> Ready;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetReady() => Ready = null;
        public event Action<bool> SelectionClosed;
        public event Action<AttributeId> AttributePreviewed;
        public event Action ChoicePreviewed;
        public event Action SelectionReturned;
        public event Action<RuleReleaseStatus> SelectionRejected;
        public bool IsOpen => Stage != RuleSelectionStage.Closed;
        public RuleSelectionStage Stage => _session?.Stage ?? RuleSelectionStage.Closed;
        public RuleFan Fan => _fan;
        public RuleFan RelationFan => _relationFan;
        public PlayerController Player => _player;
        public Camera WorldCamera => _worldCamera;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>().rootVisualElement;
            _root = _document.Q("wheelRoot");
            _fan = _document.Q<RuleFan>("ruleFan");
            _relationFan = _document.Q<RuleFan>("relationFan");
            _relationGroup = _document.Q("relationGroup");
            _feedback = _document.Q<Label>("feedback");
            _root?.AddToClassList("hidden");
        }

        private void Start()
        {
            EntityStatus status = _player != null ? _player.GetComponent<EntityStatus>() : null;
            _time = TimeManager.Instance;
            if (_root == null || _fan == null || _relationFan == null || _relationGroup == null
                || _feedback == null || status == null
                || _catalog == null || RuleSystem.Instance == null || !(_unlocks is IRuleUnlocks)
                || _time == null || !(_cameraFocus is IRuleSelectionFocus) || _worldCamera == null)
            {
                Debug.LogError("[RuleWheel] 请配置布局、P3 玩家、目录、解锁查询与镜头接口，"
                    + "并确保 P1 的时间管理器（RuleSystemDriver.Awake 里初始化）已就绪。", this);
                enabled = false;
                return;
            }
            _session = new RuleSelectionSession(RuleSystem.Instance, _catalog, status, (IRuleUnlocks)_unlocks);
            Ready?.Invoke(this);
        }

        public bool TryOpen()
        {
            if (!isActiveAndEnabled || _session == null || IsOpen || _player == null || !_player.isActiveAndEnabled
                || _player.IsInputBlocked                       // 过场 / 传送 / 对话正拿着门禁：不能叠在别人的过场上面开
                || _player.GetComponent<EntityStatus>().IsPaused) return false;
            try
            {
                // 慢时间走 P1 的 TimeManager：选择模式只是一路独立来源（TimeSource.UI），
                // 与时间规则乘法合成；关闭时只移除这一路，轮盘不再自己保存 / 恢复全局倍率。
                _time.PushScale(TimeSource.UI, _selectionScale);
                _timeScaleApplied = true;
                _inputLease = ((IRuleSelectionInputGate)_player).BlockGameplayInput();
                _focusLease = ((IRuleSelectionFocus)_cameraFocus).AcquireSelectionFocus();
                if (_focusLease == null) { ReleaseEffects(); return false; }
                _session.Open();
                _selectionActive = true;
                _relationStick = false; _waitForStickNeutral = false;
                _closingVisual = false; _root.style.opacity = 1f;
                _root.RemoveFromClassList("hidden");
                RefreshChoices();
                _fan.PlayReveal();
                PositionWheel();
                SelectionOpened?.Invoke();
                return true;
            }
            catch (Exception error) { Close(); Debug.LogException(error, this); return false; }
        }

        private void Update()
        {
            TickVisuals();
            if (_player == null || !_player.isActiveAndEnabled || _player.GetComponent<EntityStatus>().IsPaused)
            { if (IsOpen) Close(); return; }
            bool held = Keyboard.current?.leftShiftKey.isPressed == true || Keyboard.current?.rightShiftKey.isPressed == true
                || Gamepad.current?.rightTrigger.isPressed == true;
            if (!held) { if (IsOpen) Close(); _waitForRelease = false; return; }
            if (!IsOpen) { if (!_waitForRelease) TryOpen(); return; }
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Close(); return; }
            if (Mouse.current?.rightButton.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true)
            { ReturnToAttributes(); return; }
            PositionWheel();
            if (Time.unscaledTime >= _nextAvailability)
            {
                _nextAvailability = Time.unscaledTime + 0.1f;
                RefreshAvailability();
            }
            Vector2 stick = Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;
            if (stick.sqrMagnitude > 0.15f)
            {
                if (!_waitForStickNeutral)
                {
                    RuleFan active = _relationStick ? _relationFan : _fan;
                    Vector2 center = active.Center;
                    int index = active.IndexAt(center + new Vector2(stick.x, -stick.y).normalized
                        * ((active.InnerRadius + active.OuterRadius) * .5f));
                    if (_relationStick) SelectRelationIndex(index); else SelectAttributeIndex(index);
                }
            }
            else
            {
                _waitForStickNeutral = false;
                if (Mouse.current != null && !_relationStick)
                    PreviewPointer(Mouse.current.position.ReadValue());
            }
            if (Mouse.current?.leftButton.wasPressedThisFrame == true) ConfirmSelection();
            if (Gamepad.current?.buttonWest.wasPressedThisFrame == true)
            {
                if (!_relationStick && Stage == RuleSelectionStage.Relation)
                { _relationStick = true; _waitForStickNeutral = true; _relationFan.Select(-1); }
                else ConfirmSelection();
            }
        }

        /// <summary>二级扇区优先命中。跨过两扇之间的间隙时保留属性，避免子菜单突然消失。</summary>
        public void PreviewPointer(Vector2 screen)
        {
            if (!IsOpen || _document.panel == null) return;
            Vector2 panel = RuntimePanelUtils.ScreenToPanel(_document.panel, new Vector2(screen.x, _worldCamera.pixelHeight - screen.y));
            int relation = Stage == RuleSelectionStage.Relation ? _relationFan.IndexAt(_relationFan.WorldToLocal(panel)) : -1;
            if (relation >= 0) { SelectRelationIndex(relation); return; }
            _relationFan.Select(-1);
            int attribute = _fan.IndexAt(_fan.WorldToLocal(panel));
            if (attribute >= 0) SelectAttributeIndex(attribute);
        }

        public void SelectIndex(int index)
        { if (Stage == RuleSelectionStage.Attribute) SelectAttributeIndex(index); else SelectRelationIndex(index); }

        public void SelectAttributeIndex(int index)
        {
            if (!IsOpen || index < 0 || index >= Attributes.Length || index == _fan.SelectedIndex) return;
            _fan.Select(index); _relationFan.Select(-1); _feedback.text = "";
            ChoicePreviewed?.Invoke();
            if (!_session.SelectAttribute(Attributes[index]))
            {
                _session.Back(); SetBranchVisible(false); _feedback.text = "尚不可用"; return;
            }
            SetBranchVisible(true);
            _feedback.text = _session.TryGetActiveRelation(Attributes[index], out _)
                ? "生效中 · 悬停关系可取消" : "悬停关系 · 左键释放";
            PositionBranch(); RefreshAvailability();
            AttributePreviewed?.Invoke(Attributes[index]);
        }

        public void SelectRelationIndex(int index)
        {
            if (Stage != RuleSelectionStage.Relation) return;
            bool changed = index != _relationFan.SelectedIndex;
            _relationFan.Select(index);
            if (changed && index >= 0 && index < Relations.Length) ChoicePreviewed?.Invoke();
            if (index < 0 || index >= Relations.Length) { _feedback.text = "悬停关系 · 左键释放"; return; }
            // 生效中的关系再按一次是取消，取消免费 —— 提示里要写清不扣能量。
            if (_session.IsActive(_session.Attribute, Relations[index]))
            { _feedback.text = "左键取消 · 不消耗能量"; return; }
            RuleReleaseStatus reason = _session.GetBlockReason(Relations[index]);
            _feedback.text = reason == RuleReleaseStatus.Success ? "左键释放 · 松开 Shift 取消" : BlockMessage(reason);
        }

        public bool ConfirmSelection()
        {
            if (Stage != RuleSelectionStage.Relation) return false;
            int index = _relationFan.SelectedIndex;
            if (index < 0 || index >= Relations.Length) return false;
            RuleReleaseResult result = _session.Confirm(Relations[index]);
            if (result.Succeeded) { Close(true); return true; }
            SelectionRejected?.Invoke(result.Status);
            _feedback.text = BlockMessage(result.Status);
            RefreshAvailability();
            return false;
        }

        private static string BlockMessage(RuleReleaseStatus reason) => reason == RuleReleaseStatus.NotEnoughEnergy ? "能量不足"
            : reason == RuleReleaseStatus.OnCooldown ? "还在冷却" : "尚不可用";

        private void RefreshAvailability()
        {
            // 规则可能在选择期间到期或被覆盖，所以可用性与高亮一起按同一节拍刷新。
            for (int i = 0; i < Attributes.Length; i++)
            {
                _attributeAvailable[i] = _session.IsAttributeAvailable(Attributes[i]);
                _attributeActive[i] = _session.IsAttributeActive(Attributes[i]);
            }
            _fan.SetAvailable(_attributeAvailable);
            _fan.SetActiveMarks(_attributeActive);
            bool relationStage = Stage == RuleSelectionStage.Relation;
            for (int i = 0; i < Relations.Length; i++)
            {
                _relationActive[i] = relationStage && _session.IsActive(_session.Attribute, Relations[i]);
                // 生效中的关系会返回 Success：二次释放是免费取消，不受冷却 / 能量限制。
                _relationAvailable[i] = relationStage
                    && _session.GetBlockReason(Relations[i]) == RuleReleaseStatus.Success;
            }
            _relationFan.SetAvailable(_relationAvailable);
            _relationFan.SetActiveMarks(_relationActive);
        }

        private void RefreshChoices()
        {
            _feedback.text = "";
            _fan.SetOptions(AttributeWords, _attributeAvailable);
            _relationFan.SetOptions(RelationWords, _relationAvailable);
            SetBranchVisible(false); RefreshAvailability();
        }

        private void ReturnToAttributes()
        {
            bool returning = Stage == RuleSelectionStage.Relation;
            _session.Back(); _relationStick = false; _waitForStickNeutral = true;
            _fan.Select(-1); _relationFan.Select(-1); _feedback.text = "";
            SetBranchVisible(false);
            if (returning) SelectionReturned?.Invoke();
        }

        private void SetBranchVisible(bool visible)
        {
            bool wasHidden = _relationGroup.ClassListContains("hidden");
            _relationGroup.EnableInClassList("hidden", !visible);
            if (visible && wasHidden) _relationFan.PlayReveal();
        }

        private void PositionBranch()
        {
            if (_fan.SelectedIndex < 0) return;
            float angle = Mathf.Lerp(_fan.StartAngle, _fan.EndAngle, (_fan.SelectedIndex + .5f) / Attributes.Length);
            // 关系弧的中点始终位于所选属性的径向延长线上；上、下边缘也不截断角度。
            // 五个关系的中间项与属性正对，鼠标从属性向外移动的距离和角度保持一致。
            _relationFan.SetArc(angle - 60f, angle + 60f);
        }

        public void SetVisualAnchor(Transform anchor) => _visualAnchor = anchor;

        private void PositionWheel()
        {
            _facingLeft = _player.Facing < 0f;
            _fan.SetMirrored(_facingLeft);
            _relationFan.SetMirrored(_facingLeft);
            _root.EnableInClassList("facing-left", _facingLeft);
            Vector3 screen = _worldCamera.WorldToScreenPoint(_visualAnchor != null ? _visualAnchor.position : _player.transform.position);
            Vector2 actor = RuntimePanelUtils.ScreenToPanel(_document.panel, new Vector2(screen.x, _worldCamera.pixelHeight - screen.y));
            float width = float.IsNaN(_root.resolvedStyle.width) ? 650f : _root.resolvedStyle.width;
            float height = float.IsNaN(_root.resolvedStyle.height) ? 650f : _root.resolvedStyle.height;
            float x = Mathf.Clamp(actor.x - width * .5f + (_visualAnchor != null ? 0f : _playerOffset.x * (_facingLeft ? -1f : 1f)),
                20f, Mathf.Max(20f, _document.contentRect.width - width - 20f));
            float y = Mathf.Clamp(actor.y - height * 0.5f + _playerOffset.y, 20f, Mathf.Max(20f, _document.contentRect.height - height - 20f));
            _root.style.translate = new Translate(x, y);
            PositionBranch();
        }

        public void Close() => Close(false);
        private void Close(bool released)
        {
            bool notify = _selectionActive;
            _selectionActive = false;
            _session?.Close();
            if (notify && isActiveAndEnabled)
            {
                _closingVisual = true; _closeElapsed = 0f;
                _fan.PlayExit(released); _relationFan.PlayExit(released);
            }
            else if (!_closingVisual) _root?.AddToClassList("hidden");
            ReleaseEffects();
            _waitForRelease = true;
            if (notify) SelectionClosed?.Invoke(released);
        }

        private void TickVisuals()
        {
            if (_root == null || (!IsOpen && !_closingVisual)) return;
            _fan.TickAnimation(Time.unscaledDeltaTime);
            _relationFan.TickAnimation(Time.unscaledDeltaTime);
            if (!_closingVisual) return;
            _closeElapsed += Time.unscaledDeltaTime;
            _root.style.opacity = 1f - Mathf.Clamp01(_closeElapsed / RuleFan.ExitDuration);
            if (_closeElapsed >= RuleFan.ExitDuration)
            {
                _closingVisual = false;
                _root.AddToClassList("hidden");
            }
        }

        private void ReleaseEffects()
        {
            _focusLease?.Dispose(); _focusLease = null;
            _inputLease?.Dispose(); _inputLease = null;
            if (_timeScaleApplied) { _time?.RemoveScale(TimeSource.UI); _timeScaleApplied = false; }
        }

        private void OnDisable()
        {
            Close(); _closingVisual = false; _root?.AddToClassList("hidden");
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) { Close(); _closingVisual = false; _root?.AddToClassList("hidden"); }
        }
    }
}
