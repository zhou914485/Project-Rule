using System;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Persistence;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 实体压力按钮 + 纸飞机撞击开关：
    /// **实体压在检测区里**算按下；**纸飞机 / 攻击盒碰一下**也会锁住按钮（不需要压着）。
    /// </summary>
    /// <remarks>
    /// <para><b>两条触发路径</b>：</para>
    /// <list type="number">
    /// <item><b>实体压住</b>：检测区里任何带**非静态 Rigidbody2D** 的碰撞体都算 ——
    /// 玩家 / 敌人 / 铁箱 / 石头 / 巨石 / 会动的平台。地面、Tilemap、静态道具没有刚体，
    /// 所以永远不会被地面误触；纯 Trigger（受击箱 / 风场）也不算。</item>
    /// <item><b>撞击锁住</b>：实现 <see cref="IContactSwitch.ImpactByContact"/> ——
    /// 纸飞机（<c>ThrownKnife</c>）的 Trigger 扫掠、玩家的攻击盒都会走这个入口。
    /// 纸飞机本身没有碰撞体，所以**不能靠重叠查询**，只能走这条撞击入口。
    /// 碰一下就锁：<see cref="_latchSeconds"/> = 0 永久，&gt; 0 保持 N 秒（暂停冻结）。</item>
    /// </list>
    ///
    /// <para><b>碰到就算，不需要压着</b>：纸飞机 / 攻击盒只负责把按钮**锁成按下**；
    /// 之后纸飞机飞走也不影响。<see cref="ResetLatch"/> / 禁用按钮可解除锁。</para>
    ///
    /// <para><b>⚠️ 纸飞机只认 Trigger</b>：<c>ThrownKnife</c> 只在扫到 Trigger 时调
    /// <see cref="IContactSwitch.ImpactByContact"/>。要让纸飞机触发，检测区必须是 Trigger
    /// （生成工具默认就是）。实心检测区只能被玩家的攻击盒 / 实体压住。</para>
    ///
    /// <para><b>暂停</b>：<c>TimeManager.IsPaused</c> 时早退；定时锁的剩余时间也冻结。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑳ 多按钮门（所有按钮按下才开）</c>
    /// 生成两个按钮 + 一扇门；也可以把本组件挂到自己的按钮物体上，
    /// 指定检测区（Trigger）与按钮板。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class EntityPressureButton : MonoBehaviour, IContactSwitch, ISnapshotParticipant
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("检测区")]
        [Tooltip("按钮检测区：按它的包围盒查实体。\n" +
                 "⚠️ 要让纸飞机触发，必须是 Trigger —— 飞刀只在 Trigger 上调查找开关。\n" +
                 "留空 = 自动取同物体上的 Collider2D。")]
        [SerializeField] private Collider2D _area;

        [Header("表现")]
        [Tooltip("按下时下沉的按钮板（子物体，不要带碰撞体）。留空 = 只换标签。")]
        [SerializeField] private Transform _plate;

        [Tooltip("按钮板下沉的距离（世界单位）。")]
        [SerializeField, Min(0f)] private float _travel = 0.08f;

        [Tooltip("白盒标签。接正式美术时清空。")]
        [SerializeField] private TextMesh _label;

        [Tooltip("标签前缀，例如「按钮 A」。")]
        [SerializeField] private string _labelPrefix = "按钮";

        [Header("纸飞机 / 攻击")]
        [Tooltip("被纸飞机 / 攻击盒碰一下后，按钮保持「按下」多少秒。\n" +
                 "0 = 永久锁住（用 ResetLatch / 禁用按钮解除）；> 0 = 保持 N 秒，暂停时冻结。\n" +
                 "纸飞机没有碰撞体，靠 ThrownKnife 的 IContactSwitch 撞击入口；检测区必须是 Trigger。")]
        [SerializeField, Min(0f)] private float _latchSeconds = 0f;

        [Header("调试")]
        [Tooltip("按下 / 弹起 / 撞击锁住时打一行 Log。")]
        [SerializeField] private bool _logState = false;

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>按下 / 弹起（边沿）。参数是新的 <see cref="IsPressed"/>。</summary>
        public event Action<bool> PressedChanged;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>查实体的缓存，避免每次分配。按钮上可能堆着箱子 / 敌人，给宽一点。</summary>
        private readonly Collider2D[] _hits = new Collider2D[24];

        /// <summary>按钮板的弹起态局部位置。</summary>
        private Vector3 _plateRest;

        /// <summary>检测区里现在有没有实体。</summary>
        private bool _bodyPressed;

        /// <summary>逻辑状态：实体压着 或 纸飞机锁住。</summary>
        private bool _pressed;

        /// <summary>撞击锁：永久 / 剩余秒数。</summary>
        private bool _latchedPermanent;
        private float _latchRemaining;

        /// <summary>最近一次撞击点（纸飞机 / 攻击盒）。</summary>
        private bool _hasImpact;
        private Vector2 _lastImpactPoint;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>逻辑上是否按下（实体压着 或 纸飞机锁住）。门读这个。</summary>
        public bool IsPressed => _pressed;

        /// <summary>现在有没有实体压在检测区里。</summary>
        public bool IsBodyPressed => _bodyPressed;

        /// <summary>纸飞机 / 攻击盒的锁是否还在生效。</summary>
        public bool IsLatchActive => _latchedPermanent || _latchRemaining > 0f;

        /// <summary>锁是不是永久（<see cref="_latchSeconds"/> = 0）。</summary>
        public bool IsLatchPermanent => _latchedPermanent;

        /// <summary>定时锁还剩多少秒（永久锁时为 0）。</summary>
        public float LatchRemaining => _latchRemaining;

        /// <summary>有没有被纸飞机 / 攻击盒撞过。</summary>
        public bool HasImpact => _hasImpact;

        /// <summary>最近一次撞击点（世界坐标）。</summary>
        public Vector2 LastImpactPoint => _lastImpactPoint;

        /// <summary>最近一次压着它的实体（纸飞机不在这里，见 <see cref="LastImpactPoint"/>）。</summary>
        public GameObject LastPresser { get; private set; }

        /// <summary>检测区的世界包围盒。</summary>
        public Bounds AreaBounds =>
            _area != null ? _area.bounds : new Bounds(transform.position, Vector3.zero);

        /// <summary>有没有接检测区。</summary>
        public bool HasArea => _area != null;

        /// <summary>当前状态的中文名（Inspector 显示用）。</summary>
        public string CurrentStateName
        {
            get
            {
                if (IsLatchActive)
                {
                    return IsLatchPermanent
                        ? "按下（永久锁）"
                        : $"按下（锁 {_latchRemaining:0.#}s）";
                }

                return _bodyPressed ? "按下" : "弹起";
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private struct ButtonSnapshot
        {
            public bool Pressed;
            public bool BodyPressed;
            public bool LatchedPermanent;
            public float LatchRemaining;
        }

        /// <inheritdoc />
        public bool DiscardWhenMissing => false;

        /// <inheritdoc />
        public object CaptureSnapshotState() => new ButtonSnapshot
        {
            Pressed = _pressed,
            BodyPressed = _bodyPressed,
            LatchedPermanent = _latchedPermanent,
            LatchRemaining = _latchRemaining,
        };

        /// <inheritdoc />
        public void RestoreSnapshotState(object snapshot)
        {
            if (!(snapshot is ButtonSnapshot state)) return;

            _pressed = state.Pressed;
            _bodyPressed = state.BodyPressed;
            _latchedPermanent = state.LatchedPermanent;
            _latchRemaining = state.LatchRemaining;
            LastPresser = null;
            ApplyPlatePose();
            RefreshLabel();
        }

        private void Awake()
        {
            if (Application.isPlaying) SnapshotParticipantRegistry.Register(this);
            if (_area == null) _area = GetComponent<Collider2D>();

            if (_area == null)
            {
                Debug.LogError("[EntityPressureButton] 没有检测区 —— 按钮永远不会被按下。" +
                               "挂一个 Trigger Collider 并拖到「检测区」里。", this);
            }

            if (_plate != null) _plateRest = _plate.localPosition;

            ApplyPlatePose();
            RefreshLabel();
        }

        private void OnEnable()
        {
            // 重新启用时清空锁与实体状态；第一物理帧再探测。
            ResetAll();

            if (WorldStateStore.GetBool(SaveStateKey))
            {
                _latchedPermanent = true;
                RefreshLogicalState();
            }
        }

        private void OnDestroy()
        {
            SnapshotParticipantRegistry.Unregister(this);
        }

        private void OnDisable()
        {
            // 按钮被关掉时门不能一直以为它还被按着。
            ResetAll();
        }

        private void FixedUpdate()
        {
            // 暂停：玩家 / 纸飞机 / 实体都不动，保持当前状态。
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            if (_latchRemaining > 0f)
            {
                _latchRemaining -= dt;
                if (_latchRemaining <= 0f)
                {
                    _latchRemaining = 0f;
                    RefreshLogicalState();
                }
            }

            Evaluate();
        }

        // ==================================================================
        //  探测
        // ==================================================================

        /// <summary>立刻重新探测一次实体（Inspector 调试按钮 / 编辑器预览用）。</summary>
        public void RefreshState() => Evaluate();

        /// <summary>解除纸飞机 / 攻击盒的锁（实体压着仍然算按下）。</summary>
        public void ResetLatch()
        {
            _latchedPermanent = false;
            _latchRemaining = 0f;
            RefreshLogicalState();
            WorldStateStore.SetBool(SaveStateKey, false);
        }

        private void Evaluate()
        {
            bool bodyPressed = TryFindPresser(out GameObject presser);
            _bodyPressed = bodyPressed;

            if (bodyPressed) LastPresser = presser;
            else if (!IsLatchActive) LastPresser = null;

            RefreshLogicalState();
        }

        /// <summary>
        /// 检测区里有没有**带非静态刚体**的实体。找到就返回 true 并给出它。
        /// </summary>
        /// <remarks>
        /// <para>没有 Rigidbody2D 的碰撞体（地面 / Tilemap / 静态道具）直接跳过 —— 这就是
        /// 「除了地面」的实现。</para>
        /// <para>按钮自己的子物体、以及挂载按钮的父物体（例如会动的平台）都跳过，
        /// 免得按钮被自己 / 自己的载体一直按着。</para>
        /// </remarks>
        private bool TryFindPresser(out GameObject presser)
        {
            presser = null;
            if (_area == null) return false;

            Bounds bounds = _area.bounds;
            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(
                (Vector2)bounds.center, (Vector2)bounds.size, 0f, filter, _hits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                if (hit.transform == transform) continue;
                if (hit.transform.IsChildOf(transform)) continue;
                if (transform.IsChildOf(hit.transform)) continue;   // 挂载它的平台 / 父物体

                Rigidbody2D body = hit.attachedRigidbody;
                if (body == null) continue;                         // 地面 / 静态碰撞体
                if (body.bodyType == RigidbodyType2D.Static) continue;

                presser = body.gameObject;
                return true;
            }

            return false;
        }

        private void RefreshLogicalState()
        {
            bool pressed = _bodyPressed || IsLatchActive;

            if (_pressed == pressed)
            {
                // 逻辑没变，但「实体按下」→「纸飞机锁住」这类变化要刷新标签。
                RefreshLabel();
                return;
            }

            _pressed = pressed;

            ApplyPlatePose();
            RefreshLabel();

            if (_logState)
            {
                Debug.Log($"[EntityPressureButton] {(pressed ? "按下" : "弹起")}" +
                          (IsLatchActive ? "（纸飞机锁）" : "。"), this);
            }

            PressedChanged?.Invoke(_pressed);
        }

        private void ResetAll()
        {
            _bodyPressed = false;
            _latchedPermanent = false;
            _latchRemaining = 0f;
            LastPresser = null;

            RefreshLogicalState();
        }

        private void ApplyPlatePose()
        {
            if (_plate == null) return;

            _plate.localPosition = _plateRest + Vector3.down * (_pressed ? _travel : 0f);
        }

        private void RefreshLabel()
        {
            if (_label == null) return;

            string state = IsLatchActive ? "按下 · 锁" : _bodyPressed ? "按下" : "弹起";
            string prefix = string.IsNullOrEmpty(_labelPrefix) ? "按钮" : _labelPrefix;
            string wanted = prefix + " · " + state;

            if (_label.text != wanted) _label.text = wanted;
        }

        // ==================================================================
        //  IContactSwitch：纸飞机 / 攻击盒碰一下
        // ==================================================================

        /// <summary>被非玩家的东西压到。**本按钮的压力由检测区自己查**，这里不处理。</summary>
        public void PressByContact()
        {
        }

        /// <summary>压着的东西离开。压力由检测区自己查，这里不处理。</summary>
        public void ReleaseByContact()
        {
        }

        /// <summary>
        /// 被**纸飞机 / 攻击盒**碰了一下：把按钮锁成按下，不需要继续压着。
        /// </summary>
        /// <param name="sourcePoint">撞击点（调试用）。</param>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            if (_latchSeconds > 0f)
            {
                _latchedPermanent = false;
                _latchRemaining = Mathf.Max(_latchRemaining, _latchSeconds);
            }
            else
            {
                _latchedPermanent = true;
                _latchRemaining = 0f;
                WorldStateStore.SetBool(SaveStateKey, true);
            }

            _hasImpact = true;
            _lastImpactPoint = sourcePoint;

            RefreshLogicalState();

            if (_logState)
            {
                string duration = _latchSeconds > 0f ? _latchSeconds.ToString("0.##") + " 秒" : "永久";
                Debug.Log($"[EntityPressureButton] 撞击锁住（{duration}）：{sourcePoint}", this);
            }
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接好检测区、按钮板、标签与前缀。</summary>
        public void Configure(Collider2D area, Transform plate, TextMesh label, string labelPrefix)
        {
            _area = area;
            _plate = plate;
            _label = label;
            _labelPrefix = labelPrefix;

            if (_plate != null) _plateRest = _plate.localPosition;

            ApplyPlatePose();
            RefreshLabel();
        }

        private void OnValidate()
        {
            _travel = Mathf.Max(0f, _travel);
            _latchSeconds = Mathf.Max(0f, _latchSeconds);

            if (_area == null) _area = GetComponent<Collider2D>();
        }

        [ContextMenu("调试/解除纸飞机锁定")]
        private void DebugResetLatch()
        {
            ResetLatch();
        }

        private void OnDrawGizmosSelected()
        {
            if (_area == null) return;

            Bounds bounds = _area.bounds;
            Gizmos.color = IsPressed
                ? new Color(0.35f, 0.95f, 0.45f, 0.9f)
                : new Color(1f, 0.8f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private string SaveStateKey => WorldStateStore.KeyOf(this, "latched");
    }
}
