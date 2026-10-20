using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 多按钮门：**只有 <see cref="_buttons"/> 里的按钮全部按下才开门**；任意一个松开就关门。
    /// 按钮数量 = 数组长度，在 Inspector 里自己加 / 删。
    /// </summary>
    /// <remarks>
    /// <para><b>AND 逻辑</b>：<see cref="AllButtonsPressed"/> 逐个检查非空的按钮；
    /// 数组为空、或任何一个槽位是空 / 没按下，门都保持关闭。现场配错时会打警告。</para>
    ///
    /// <para><b>怎么动</b>：门是 **Kinematic 刚体 + BoxCollider2D**，沿
    /// <see cref="_openOffset"/>（本地偏移）滑到开门位置；速度 <see cref="_speed"/>。
    /// 默认向上滑，门洞留在下方。</para>
    ///
    /// <para><b>不夹玩家</b>：关门时如果玩家在门洞里（按门的**关门位置**包围盒查），
    /// 门会停在当前位置，等玩家出去再关（<see cref="IsWaitingForClear"/>）。只检查玩家；
    /// 敌人 / 箱子由它们自己的物理负责（箱子会被推开，Kinematic 敌人可能被夹，是本原型的已知边界）。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时 <c>TimeManager.IsPaused</c> 早退，门不动；
    /// 积分用 <c>Time.fixedDeltaTime</c>，时间变慢时门也跟着慢。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑲ 多按钮门（所有按钮按下才开）</c>
    /// 生成「2 按钮 + 1 门 + 1 个压按钮的铁箱」演示；也可以给已有门挂本组件，
    /// 把自己的 <see cref="EntityPressureButton"/> 拖进数组。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class MultiButtonDoor : MonoBehaviour
    {
        /// <summary>到达判定：进度离 0 / 1 这么近就算关 / 开。</summary>
        private const float ArriveEpsilon = 0.0005f;

        /// <summary>玩家「站在门顶上」的容差。</summary>
        private const float TopTolerance = 0.05f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("按钮")]
        [Tooltip("把 EntityPressureButton 拖进来。数组长度 = 需要同时按下的按钮数量。\n" +
                 "所有按钮都按下才开门；任意一个松开就关门。")]
        [SerializeField] private EntityPressureButton[] _buttons;

        [Header("门")]
        [Tooltip("开门位置相对关门位置的**本地偏移**。默认向上滑 2.8 个单位。")]
        [SerializeField] private Vector2 _openOffset = new Vector2(0f, 2.8f);

        [Tooltip("门的移动速度（单位 / 秒）。")]
        [SerializeField, Min(0f)] private float _speed = 4f;

        [Tooltip("关门时如果玩家卡在门洞里，保持打开等玩家出来（防夹）。\n" +
                 "只检查玩家；敌人 / 箱子不在此列。")]
        [SerializeField] private bool _waitForPlayerClear = true;

        [Header("白盒")]
        [Tooltip("白盒标签：显示门 · 关 / 开 / 移动 / 等待。接正式美术时清空。")]
        [SerializeField] private TextMesh _label;

        [Header("调试")]
        [Tooltip("开 / 关 / 等待时打一行 Log。")]
        [SerializeField] private bool _logState = false;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private BoxCollider2D _box;

        /// <summary>关门 / 开门位置（父物体的本地坐标）。</summary>
        private Vector3 _closedLocal;
        private Vector3 _openLocal;

        /// <summary>当前世界位置（自己记，MovePosition 要到物理步进才生效）。</summary>
        private Vector3 _position;

        /// <summary>0 = 关，1 = 开。</summary>
        private float _progress;

        /// <summary>查玩家是否在门洞里的缓存。</summary>
        private readonly Collider2D[] _overlapHits = new Collider2D[16];

        /// <summary>Log 边沿去重（只在开了 _logState 时用）。</summary>
        private bool _loggedOpen;
        private bool _loggedClosed;
        private bool _loggedWaiting;

        // ==================================================================
        //  只读状态（调试用）
        // ==================================================================

        public bool IsOpen { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsMoving { get; private set; }

        /// <summary>该关门但玩家还在门洞里，门停在当前位置。</summary>
        public bool IsWaitingForClear { get; private set; }

        /// <summary>0 = 关，1 = 开。</summary>
        public float Progress => _progress;

        /// <summary>数组里配了几个按钮（含空槽）。</summary>
        public int ButtonCount => _buttons != null ? _buttons.Length : 0;

        /// <summary>当前按下的按钮数量。</summary>
        public int PressedCount
        {
            get
            {
                if (_buttons == null) return 0;

                int count = 0;
                for (int i = 0; i < _buttons.Length; i++)
                {
                    if (_buttons[i] != null && _buttons[i].IsPressed) count++;
                }

                return count;
            }
        }

        /// <summary>是不是所有按钮都按下了（空数组 / 空槽都算 false）。</summary>
        public bool AllButtonsPressed
        {
            get
            {
                if (_buttons == null || _buttons.Length == 0) return false;

                for (int i = 0; i < _buttons.Length; i++)
                {
                    EntityPressureButton button = _buttons[i];
                    if (button == null || !button.IsPressed) return false;
                }

                return true;
            }
        }

        /// <summary>Inspector 显示 / 调试用。</summary>
        public EntityPressureButton[] Buttons => _buttons;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _box = GetComponent<BoxCollider2D>();

            ConfigureBody();

            _closedLocal = transform.localPosition;
            _openLocal = _closedLocal + (Vector3)_openOffset;
            _progress = 0f;
            IsOpen = false;
            IsClosed = true;
            IsMoving = false;
            IsWaitingForClear = false;

            _position = ToWorld(_closedLocal);
            _body.position = _position;

            if (_buttons == null || _buttons.Length == 0)
            {
                Debug.LogWarning("[MultiButtonDoor] 没有配按钮 —— 门会永远关着。" +
                                 "把 EntityPressureButton 拖进「按钮」数组。", this);
            }

            RefreshLabel();
        }

        private void OnEnable()
        {
            // 重新启用时保持当前开 / 关位置，不重置进度。
            if (_body != null) _body.position = _position;
            RefreshLabel();
        }

        private void FixedUpdate()
        {
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            bool all = AllButtonsPressed;
            IsWaitingForClear = false;

            // 该关但玩家还在门洞里：停在当前位置，等人出去。
            if (!all && _waitForPlayerClear && IsPlayerInDoorway())
            {
                IsWaitingForClear = true;

                if (_logState && !_loggedWaiting)
                {
                    _loggedWaiting = true;
                    Debug.Log("[MultiButtonDoor] 按钮已松开，但玩家还在门洞里 —— 门停住等待。", this);
                }

                RefreshLabel();
                return;
            }

            _loggedWaiting = false;

            MoveDoor(all ? 1f : 0f, dt);
            RefreshLabel();
        }

        // ==================================================================
        //  运动
        // ==================================================================

        private void MoveDoor(float target, float dt)
        {
            float length = Vector2.Distance(_closedLocal, _openLocal);

            if (length <= 0.0001f)
            {
                _progress = target;
                ApplyDoorPosition();
                return;
            }

            _progress = Mathf.MoveTowards(_progress, target, _speed * dt / length);
            ApplyDoorPosition();
        }

        private void ApplyDoorPosition()
        {
            Vector3 local = Vector3.LerpUnclamped(_closedLocal, _openLocal, _progress);
            _position = ToWorld(local);

            if (_body != null) _body.MovePosition(_position);

            IsOpen = _progress >= 1f - ArriveEpsilon;
            IsClosed = _progress <= ArriveEpsilon;
            IsMoving = !IsOpen && !IsClosed;

            if (!_logState) return;

            if (IsOpen && !_loggedOpen)
            {
                _loggedOpen = true;
                _loggedClosed = false;
                Debug.Log("[MultiButtonDoor] 门已打开（所有按钮都按下）。", this);
            }
            else if (IsClosed && !_loggedClosed)
            {
                _loggedClosed = true;
                _loggedOpen = false;
                Debug.Log("[MultiButtonDoor] 门已关闭。", this);
            }
        }

        private void ConfigureBody()
        {
            // 门的位置完全由本组件决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            _body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        private Vector3 ToWorld(Vector3 local)
        {
            return transform.parent != null
                ? transform.parent.TransformPoint(local)
                : local;
        }

        // ==================================================================
        //  防夹
        // ==================================================================

        /// <summary>玩家是不是还在门洞里（按关门位置的包围盒查）。</summary>
        /// <remarks>
        /// <para>站在门顶 / 在门下方的不算；其余与关门包围盒相交的玩家身体都算。</para>
        /// <para>只处理玩家。敌人 / 箱子没有承载入口，本原型不负责。</para>
        /// </remarks>
        private bool IsPlayerInDoorway()
        {
            if (_box == null) return false;

            Bounds bounds = ClosedBounds();
            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(
                (Vector2)bounds.center, (Vector2)bounds.size, 0f, filter, _overlapHits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _overlapHits[i];
                if (hit == null) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (transform.IsChildOf(hit.transform)) continue;
                if (!PlayerLookup.IsPlayer(hit)) continue;

                Bounds player = hit.bounds;
                if (player.min.y >= bounds.max.y - TopTolerance) continue;   // 站在门顶
                if (player.max.y <= bounds.min.y + TopTolerance) continue;   // 在门下方

                return true;
            }

            return false;
        }

        /// <summary>门在**关门位置**时的世界包围盒。</summary>
        private Bounds ClosedBounds()
        {
            Vector3 scale = transform.lossyScale;
            var size = new Vector2(
                Mathf.Abs(_box.size.x * scale.x),
                Mathf.Abs(_box.size.y * scale.y));

            Vector3 center = ToWorld(_closedLocal + (Vector3)_box.offset);
            return new Bounds(center, size);
        }

        // ==================================================================
        //  表现
        // ==================================================================

        private void RefreshLabel()
        {
            if (_label == null) return;

            string state = IsWaitingForClear
                ? "等待"
                : IsOpen
                    ? "开"
                    : IsClosed
                        ? "关"
                        : "移动";

            string wanted = "门 · " + state;
            if (_label.text != wanted) _label.text = wanted;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接好按钮数组、开门偏移、速度与标签。</summary>
        public void Configure(EntityPressureButton[] buttons, Vector2 openOffset,
                              float speed, TextMesh label)
        {
            _buttons = buttons;
            _openOffset = openOffset;
            _speed = Mathf.Max(0f, speed);
            _label = label;

            _closedLocal = transform.localPosition;
            _openLocal = _closedLocal + (Vector3)_openOffset;
            _progress = 0f;
            IsOpen = false;
            IsClosed = true;
            IsMoving = false;
            IsWaitingForClear = false;

            RefreshLabel();
        }

        private void OnValidate()
        {
            _speed = Mathf.Max(0f, _speed);

            // 编辑模式 Awake 不跑；gizmo 要靠 _box 画关门包围盒。
            if (_box == null) _box = GetComponent<BoxCollider2D>();
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 closedLocal = Application.isPlaying ? _closedLocal : transform.localPosition;
            Vector3 openLocal = closedLocal + (Vector3)_openOffset;
            Vector3 closedWorld = ToWorld(closedLocal);
            Vector3 openWorld = ToWorld(openLocal);

            Gizmos.color = IsWaitingForClear
                ? new Color(1f, 0.6f, 0.2f, 0.95f)
                : IsOpen
                    ? new Color(0.35f, 0.95f, 0.45f, 0.95f)
                    : new Color(0.9f, 0.4f, 0.3f, 0.95f);

            Gizmos.DrawLine(closedWorld, openWorld);
            Gizmos.DrawWireSphere(closedWorld, 0.12f);
            Gizmos.DrawWireSphere(openWorld, 0.12f);

            if (_box == null) return;

            Vector3 scale = transform.lossyScale;
            var size = new Vector2(
                Mathf.Abs(_box.size.x * scale.x),
                Mathf.Abs(_box.size.y * scale.y));
            Vector3 closedCenter = ToWorld(closedLocal + (Vector3)_box.offset);

            Gizmos.DrawWireCube(closedCenter, size);
        }
    }
}
