using System;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 攻击延长平台：玩家**攻击后**，沿指定方向从「攻击前长度」长到「攻击后长度」。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么</b>：关卡里需要「打一下，桥/踏板就伸出去」的机关。
    /// 长度与方向都摆在 Inspector 上（<see cref="LengthBefore"/> / <see cref="LengthAfter"/> /
    /// <see cref="Direction"/>），策划不用进代码就能调。</para>
    ///
    /// <para><b>两种触发方式</b>（<see cref="TriggerMode"/>）：
    /// <list type="bullet">
    /// <item><b>玩家攻击</b>（默认）：玩家每一刀**真正出手**时（<c>PlayerController.OnAttackStruck</c>，
    /// 轻击 / 重击都算）就延长，**不管有没有打中平台**。适合「攻击给平台充能」的机关。</item>
    /// <item><b>打中平台</b>：只有攻击盒打中平台才延长。走 <see cref="IContactSwitch.ImpactByContact"/>，
    /// 与拉杆 / 按钮同一个入口。适合「敲一下墙上的踏板」的机关。</item>
    /// <item><b>两者</b>：都行。</item>
    /// </list>
    /// 两条路都**不进伤害管线**，所以「伤害 × 失效」不会把机关一起关掉，平台也不会掉血。</para>
    ///
    /// <para>⚠️ <b>玩家的攻击盒在胸口高度</b>：贴地的薄平台（例如铺在地上的桥）从上面挥刀打不到它 ——
    /// 想「敲平台」，要么把它抬到胸口高度，要么用默认的「玩家攻击」模式。</para>
    ///
    /// <para><b>长度与锚点</b>：根物体的位置 = **攻击前平台的中心**。延长时根不动，
    /// 平台朝 <see cref="Direction"/> 的那一头长出去，**另一头（固定端）留在原地**。</para>
    ///
    /// <para><b>方向只支持上下左右</b>：平台是轴对齐的（与 <c>MovingPlatform</c> /
    /// <c>ImpactButton</c> 一致）。根物体带旋转时，长度按世界方向算、碰撞体按局部方向写，
    /// 两者会错开 —— 不要旋转根物体。</para>
    ///
    /// <para><b>被挡住的玩家 / 动态物体会让平台停住</b>：延长前先扫一遍「这一帧要长出去的那一条」
    /// （<see cref="IsGrowthBlocked"/>）。里面有**非静态刚体**（玩家、敌人、铁箱、其它平台）就
    /// 这一帧不长，等它让开再继续 —— 与 <c>SpringBackBody</c>「推不动就停住」同一条底线，
    /// 绝不把玩家包进碰撞体里。静态地形（墙 / 地面）**不挡**：地图组件可以长进墙里，
    /// 与 <c>MovingPlatform</c> 穿过一切的做法一致。</para>
    ///
    /// <para><b>站在平台顶上的玩家不会被带走</b>：水平延长时顶面不动，人只是站在原处；
    /// 竖直延长（<see cref="ExtendDirection.Up"/>）时，人站在要长出去的那一段里，会被判成
    /// 「挡住」而让平台停下 —— 这是为了避免把玩家顶进碰撞体。要「载人上升」请用电梯
    /// （<c>CityPathMotor</c> / <c>CityMultiStopLift</c>），不是本组件。</para>
    ///
    /// <para><b>自动缩回</b>：长到最长后保持 <see cref="_retractAfter"/> 秒（默认 2），
    /// 然后自己缩回攻击前长度；<c>0</c> = 一直保持。暂停时倒计时冻结。
    /// 运行中把 <c>0</c> 改成 &gt;0 会立刻开始倒计时；改回 <c>0</c> 会取消，停在当前长度。</para>
    ///
    /// <para><b>暂停</b>：<c>TimeManager.IsPaused</c> 时早退，平台停在当前长度，缩回计时也冻结。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑯ 攻击延长平台</c> 生成白盒，
    /// 或用它自带的 Inspector 按钮预览两种形态。</para>
    /// </remarks>
    [ExecuteAlways]
    [DefaultExecutionOrder(-45)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class AttackExtendedPlatform : MonoBehaviour, IContactSwitch
    {
        /// <summary>平台朝哪一头长出去。</summary>
        public enum ExtendDirection
        {
            /// <summary>向右长：左端固定。</summary>
            Right = 0,

            /// <summary>向左长：右端固定。</summary>
            Left = 1,

            /// <summary>向上长：下端固定。</summary>
            Up = 2,

            /// <summary>向下长：上端固定。</summary>
            Down = 3,
        }

        /// <summary>什么算「玩家攻击了」。</summary>
        public enum TriggerMode
        {
            /// <summary>玩家每一刀真正出手时都延长（不管打没打中平台）。</summary>
            PlayerAttack = 0,

            /// <summary>只有攻击盒打中平台才延长。</summary>
            HitByAttack = 1,

            /// <summary>两条路都算。</summary>
            Both = 2,
        }

        /// <summary>状态机。</summary>
        private enum State
        {
            /// <summary>攻击前长度。</summary>
            Retracted,

            /// <summary>正在变长。</summary>
            Extending,

            /// <summary>已经到攻击后长度。</summary>
            Extended,

            /// <summary>正在缩回去。</summary>
            Retracting,
        }

        /// <summary>长度下限。0 会让碰撞体退化成一条线。</summary>
        private const float MinLength = 0.02f;

        /// <summary>离目标多近算「到了」。</summary>
        private const float ArriveEpsilon = 0.0005f;

        /// <summary>挡路探测盒沿延长方向的两端余量，免得浮点误差漏掉贴脸的东西。</summary>
        private const float ProbeSkin = 0.01f;

        /// <summary>挡路探测盒在横截面上的内缩量：贴着顶面站的人不算挡住（Box2D 重叠含边界）。</summary>
        private const float ProbeCrossInset = 0.02f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("长度（世界单位）")]
        [Tooltip("攻击前的长度（沿「延长方向」量）。根物体的位置就是这个平台的中心。\n" +
                 "运行中也可以改：改完平台立刻按新长度重摆。")]
        [SerializeField, Min(MinLength)] private float _lengthBefore = 2.5f;

        [Tooltip("攻击后的长度（沿「延长方向」量）。必须 ≥ 攻击前长度。\n" +
                 "延长时朝「延长方向」的那一头长出去，另一头不动。")]
        [SerializeField, Min(MinLength)] private float _lengthAfter = 5.5f;

        [Tooltip("垂直于延长方向的厚度（也是平台的高 / 宽，取决于方向）。\n" +
                 "水平方向 = 平台高度；竖直方向 = 平台宽度。")]
        [SerializeField, Min(MinLength)] private float _thickness = 0.4f;

        [Header("延长方向")]
        [Tooltip("朝哪一头长出去。固定端在相反的那一头。")]
        [SerializeField] private ExtendDirection _extendDirection = ExtendDirection.Right;

        [Header("触发方式")]
        [Tooltip("· 玩家攻击（默认）= 玩家每一刀真正出手时就长，**不管有没有打中平台**\n" +
                 "· 打中平台 = 只有攻击盒打中它才长（贴地的薄平台从上面砍不到，要用这个模式请把它抬到胸口高度）\n" +
                 "· 两者 = 都行\n" +
                 "⚠️「玩家攻击」会让场景里**所有**这个模式的平台一起长；只想让打中的那个长，选「打中平台」。")]
        [SerializeField] private TriggerMode _triggerMode = TriggerMode.PlayerAttack;

        [Header("节奏")]
        [Tooltip("从攻击前长度长到攻击后长度要多久（秒）。0 = 攻击瞬间到位。")]
        [SerializeField, Min(0f)] private float _extendDuration = 0.35f;

        [Tooltip("长到最长后保持多久再自己缩回去（秒）。默认 2 秒；0 = 一直保持，等下一次攻击 / 调试按钮。")]
        [SerializeField, Min(0f)] private float _retractAfter = 2f;

        [Header("表现")]
        [Tooltip("会跟着变长的白盒子物体（1×1 单位的精灵，生成工具会接上 Art）。\n" +
                 "留空 = 自动找子物体上的第一个 SpriteRenderer。")]
        [SerializeField] private Transform _visual;

        [Tooltip("白盒标签。接正式美术时清空它 —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        [Header("调试")]
        [Tooltip("勾上：进 Play 后一开局就是「攻击后」的形态，方便直接看落点 / 走位。\n" +
                 "取消：开局是「攻击前」形态，等攻击才长出去。")]
        [SerializeField] private bool _startExtended = false;

        [Tooltip("勾上：**编辑模式**直接显示「攻击后」的形态，方便摆关卡时看平台会伸到哪儿。\n" +
                 "只在编辑器里生效，不影响 Play。")]
        [SerializeField] private bool _previewExtended = false;

        [Tooltip("勾上：延长 / 缩回 / 被谁挡住时在 Console 打一行。排查「砍了没反应」时打开。")]
        [SerializeField] private bool _logState = false;

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>长到攻击后长度的那一瞬。</summary>
        public event Action OnExtended;

        /// <summary>缩回攻击前长度的那一瞬。</summary>
        public event Action OnRetracted;

        // ==================================================================
        //  运行时
        // ==================================================================

        private BoxCollider2D _box;
        private Rigidbody2D _body;

        /// <summary>当前长度（世界单位）。</summary>
        private float _current;

        private State _state = State.Retracted;

        /// <summary>自动缩回的剩余秒数。−1 = 不缩回（保持 / 未延长）。</summary>
        private float _retractTimer = -1f;

        /// <summary>挡路探测的结果缓存。</summary>
        private readonly Collider2D[] _blockers = new Collider2D[8];

        /// <summary>根缩放不是 1 的警告只打一次。</summary>
        private bool _warnedScale;

        /// <summary>「精灵挂在根上」的警告只打一次。</summary>
        private bool _warnedRootVisual;

        /// <summary>「被挡住」的 Log 每次阻挡只打一次（否则每个物理帧刷屏）。</summary>
        private bool _loggedBlock;

        /// <summary>编辑器里 <see cref="OnValidate"/> 可能早于 Awake 跑，用它判断几何是否已可用。</summary>
        private bool _geometryReady;

        /// <summary>当前监听的玩家。「玩家攻击」模式才用。</summary>
        private PlayerController _player;

        /// <summary>找不到玩家时的重试倒计时（秒）。</summary>
        private float _playerSearchTimer;

        // ==================================================================
        //  只读状态（调试用）
        // ==================================================================

        /// <summary>攻击前长度（世界单位）。</summary>
        public float LengthBefore => _lengthBefore;

        /// <summary>攻击后长度（世界单位）。</summary>
        public float LengthAfter => _lengthAfter;

        /// <summary>厚度（垂直于延长方向，世界单位）。</summary>
        public float Thickness => _thickness;

        /// <summary>延长方向（世界单位向量）。</summary>
        public Vector2 Direction
        {
            get
            {
                switch (_extendDirection)
                {
                    case ExtendDirection.Left:  return Vector2.left;
                    case ExtendDirection.Up:    return Vector2.up;
                    case ExtendDirection.Down:  return Vector2.down;
                    default:                    return Vector2.right;
                }
            }
        }

        /// <summary>方向的中文名（Inspector 显示用）。</summary>
        public string DirectionName
        {
            get
            {
                switch (_extendDirection)
                {
                    case ExtendDirection.Left:  return "向左";
                    case ExtendDirection.Up:    return "向上";
                    case ExtendDirection.Down:  return "向下";
                    default:                    return "向右";
                }
            }
        }

        /// <summary>当前触发方式。</summary>
        public TriggerMode Trigger => _triggerMode;

        /// <summary>触发方式的中文名（Inspector 显示用）。</summary>
        public string TriggerName
        {
            get
            {
                switch (_triggerMode)
                {
                    case TriggerMode.HitByAttack: return "打中平台";
                    case TriggerMode.Both:        return "玩家攻击 / 打中平台";
                    default:                      return "玩家攻击";
                }
            }
        }

        /// <summary>现在有没有在监听玩家的攻击（「玩家攻击」模式且已找到玩家）。</summary>
        public bool IsListeningToPlayer => _player != null;

        /// <summary>当前长度（世界单位）。编辑模式读的是预览形态。</summary>
        public float CurrentLength =>
            Application.isPlaying && _geometryReady ? _current : PreviewLength;

        /// <summary>延长速度（单位 / 秒）。时长为 0 时返回 0，表示瞬间到位。</summary>
        public float ExtensionSpeed
        {
            get
            {
                if (_extendDuration <= 0f) return 0f;
                return Mathf.Abs(_lengthAfter - _lengthBefore) / _extendDuration;
            }
        }

        /// <summary>已经长到最长。</summary>
        public bool IsExtended => _state == State.Extended;

        /// <summary>正在变长。</summary>
        public bool IsExtending => _state == State.Extending;

        /// <summary>正在缩回去。</summary>
        public bool IsRetracting => _state == State.Retracting;

        /// <summary>长到最长后保持多久再自动缩回（秒）。0 = 一直保持。</summary>
        public float AutoRetractSeconds => _retractAfter;

        /// <summary>自动缩回还剩多少秒。未延长 / 不自动 / 已到时返回 0。</summary>
        public float AutoRetractRemaining =>
            _state == State.Extended && _retractAfter > 0f && _retractTimer > 0f
                ? _retractTimer
                : 0f;

        /// <summary>当前配置是否会自动缩回。</summary>
        public bool WillAutoRetract => _retractAfter > 0f;

        /// <summary>本物理帧是否因为挡路而没能继续长。</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>本物理帧挡住平台的东西（可能是 <c>null</c>）。</summary>
        public GameObject BlockedBy { get; private set; }

        /// <summary>状态的中文名（Inspector 显示用）。</summary>
        public string StateName
        {
            get
            {
                if (!Application.isPlaying) return _previewExtended ? "预览：攻击后" : "预览：攻击前";

                switch (_state)
                {
                    case State.Extending:  return "延长中";
                    case State.Extended:   return _retractAfter > 0f ? "已延长（等待缩回）" : "已延长";
                    case State.Retracting: return "缩回中";
                    default:               return "攻击前";
                }
            }
        }

        /// <summary>编辑模式下当前要显示的预览长度。</summary>
        private float PreviewLength => _previewExtended ? _lengthAfter : _lengthBefore;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _box = GetComponent<BoxCollider2D>();
            _body = GetComponent<Rigidbody2D>();

            if (_box != null && _box.isTrigger)
            {
                Debug.LogWarning("[AttackExtendedPlatform] 碰撞体勾了 Is Trigger —— 它挡不住人，玩家踩不到。" +
                                 "本组件会把它改回实心。", this);
                _box.isTrigger = false;
            }

            ResolveVisual();

            if (!Application.isPlaying)
            {
                _geometryReady = true;
                ApplyShape(PreviewLength);
                return;
            }

            ConfigureBody();
            _geometryReady = true;
            ResetToInitial();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                if (_box == null) _box = GetComponent<BoxCollider2D>();
                if (_visual == null) ResolveVisual();
                _geometryReady = true;
                ApplyShape(PreviewLength);
                return;
            }

            if (_box == null)
            {
                _box = GetComponent<BoxCollider2D>();
                _body = GetComponent<Rigidbody2D>();
                ConfigureBody();
            }

            _geometryReady = true;
            ResetToInitial();

            // 「玩家攻击」模式：找到玩家并监听他的出手。没找到就每秒重试一次
            // （玩家可能晚于平台加载，例如分区流式加载）。
            _playerSearchTimer = 0f;
            SyncPlayerSubscription(0f);
        }

        private void Start()
        {
            if (!Application.isPlaying) return;

            // 根缩放不是 1 时，box.size 是局部单位、_lengthBefore 是世界单位，两边对不上。
            Vector3 scale = transform.lossyScale;
            if (!_warnedScale && (Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f))
            {
                _warnedScale = true;
                Debug.LogWarning("[AttackExtendedPlatform] 根物体缩放不是 1：长度按世界单位填，" +
                                 "但碰撞体 size 是局部单位，实际长度会被缩放再乘一遍。请把根缩放保持 1。", this);
            }
        }

        private void OnDisable()
        {
            UnsubscribePlayer();
            IsBlocked = false;
            BlockedBy = null;
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            SyncPlayerSubscription(dt);

            switch (_state)
            {
                case State.Extending:
                    StepExtend(dt);
                    break;

                case State.Retracting:
                    StepRetract(dt);
                    break;

                case State.Extended:
                    CountdownRetract(dt);
                    break;
            }
        }

        private void ConfigureBody()
        {
            // 位移 / 形变完全由本组件决定，重力与摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            _body.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }

        /// <summary>回到开局形态（攻击前 / 调试用的攻击后）。</summary>
        private void ResetToInitial()
        {
            _state = _startExtended ? State.Extended : State.Retracted;
            _retractTimer = -1f;
            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            ApplyShape(_startExtended ? _lengthAfter : _lengthBefore);
        }

        // ==================================================================
        //  玩家攻击监听
        // ==================================================================

        /// <summary>当前触发方式需不需要监听玩家。</summary>
        private bool NeedsPlayerAttack =>
            _triggerMode == TriggerMode.PlayerAttack || _triggerMode == TriggerMode.Both;

        /// <summary>
        /// 保证「该监听玩家的时候已经监听上」。切到「打中平台」时顺手退订。
        /// </summary>
        /// <remarks>
        /// <para>玩家可能比平台晚加载（分区流式加载），所以找不到时按
        /// <see cref="_playerSearchTimer"/> 每秒重试一次，不是每帧 <c>Find</c>。</para>
        /// <para>玩家被销毁后 <c>_player</c> 会变成 Unity 的「假 null」，这里会重新找新的。</para>
        /// </remarks>
        private void SyncPlayerSubscription(float dt)
        {
            if (!NeedsPlayerAttack)
            {
                UnsubscribePlayer();
                return;
            }

            if (_player != null) return;

            _playerSearchTimer -= dt;
            if (_playerSearchTimer > 0f) return;

            _playerSearchTimer = 1f;

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;

            _player = player;
            _player.OnAttackStruck += HandlePlayerAttackStruck;
        }

        private void UnsubscribePlayer()
        {
            if (_player == null) return;

            _player.OnAttackStruck -= HandlePlayerAttackStruck;
            _player = null;
        }

        /// <summary>
        /// 玩家**真正出手**（轻击 / 重击的生效段开窗）时调到这里。
        /// </summary>
        /// <remarks>用 <c>OnAttackStruck</c> 而不是 <c>OnAttacked</c>：后者是起手瞬间，
        /// 前者才是「刀挥出去了」的那一下；两者都不看有没有命中，正合「攻击给平台充能」。</remarks>
        private void HandlePlayerAttackStruck(PlayerAttackCue cue)
        {
            if (!NeedsPlayerAttack) return;
            Extend();
        }

        // ==================================================================
        //  状态推进
        // ==================================================================

        private void StepExtend(float dt)
        {
            float previous = _current;

            if (previous >= _lengthAfter - ArriveEpsilon)
            {
                FinishExtend();
                return;
            }

            float step = ExtensionSpeed * dt;

            // 瞬间到位（延长时长 = 0）：也要先过挡路检查，不能借这一下把人包进碰撞体。
            if (step <= 0f)
            {
                if (IsGrowthBlocked(previous, _lengthAfter, out Collider2D instantBlocker))
                {
                    MarkBlocked(instantBlocker);
                    return;
                }

                ApplyShape(_lengthAfter);
                FinishExtend();
                return;
            }

            float next = Mathf.MoveTowards(previous, _lengthAfter, step);

            if (IsGrowthBlocked(previous, next, out Collider2D blocker))
            {
                MarkBlocked(blocker);
                return;
            }

            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            ApplyShape(next);

            if (_current >= _lengthAfter - ArriveEpsilon) FinishExtend();
        }

        /// <summary>记下「这一帧被谁挡住了」，Log 每次阻挡只打一行。</summary>
        private void MarkBlocked(Collider2D blocker)
        {
            IsBlocked = true;
            BlockedBy = blocker != null ? blocker.gameObject : null;

            if (!_logState || _loggedBlock) return;

            _loggedBlock = true;
            Debug.Log("[AttackExtendedPlatform] 延长被挡住：" +
                      (BlockedBy != null ? BlockedBy.name : "未知对象"), this);
        }

        private void StepRetract(float dt)
        {
            // 缩回途中把「延长时长」改成 0：直接到位，不要卡在 Retracting 里。
            if (ExtensionSpeed <= 0f)
            {
                FinishRetract();
                return;
            }

            float next = Mathf.MoveTowards(_current, _lengthBefore, ExtensionSpeed * dt);
            ApplyShape(next);

            if (_current <= _lengthBefore + ArriveEpsilon) FinishRetract();
        }

        private void FinishRetract()
        {
            ApplyShape(_lengthBefore);
            _state = State.Retracted;
            _retractTimer = -1f;
            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            if (_logState) Debug.Log("[AttackExtendedPlatform] 已缩回攻击前长度。", this);

            OnRetracted?.Invoke();
        }

        private void CountdownRetract(float dt)
        {
            // 0 = 不自动缩回：把残留计时清掉，停在最长。
            if (_retractAfter <= 0f)
            {
                _retractTimer = -1f;
                return;
            }

            // 运行中从 0 改成 >0、或开局就是「攻击后」形态时，计时器可能还是 −1；
            // 这里补起一次，保证「到时间自动缩回」在任何入口下都成立。
            if (_retractTimer < 0f) _retractTimer = _retractAfter;

            _retractTimer -= dt;
            if (_retractTimer <= 0f) Retract();
        }

        private void FinishExtend()
        {
            ApplyShape(_lengthAfter);
            _state = State.Extended;
            _retractTimer = _retractAfter > 0f ? _retractAfter : -1f;
            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            if (_logState) Debug.Log("[AttackExtendedPlatform] 已延长到攻击后长度。", this);

            OnExtended?.Invoke();
        }

        // ==================================================================
        //  对外入口
        // ==================================================================

        /// <summary>开始延长。已经是最长时只刷新自动缩回计时。</summary>
        public void Extend()
        {
            if (!isActiveAndEnabled) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            if (_state == State.Extended)
            {
                if (_retractAfter > 0f) _retractTimer = _retractAfter;
                return;
            }

            if (_state == State.Extending) return;

            _state = State.Extending;
            _retractTimer = -1f;
            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            if (_logState) Debug.Log("[AttackExtendedPlatform] 开始延长。", this);

            // 两端一样长：没有可长的距离，直接算到位。
            if (_lengthAfter <= _lengthBefore + ArriveEpsilon)
            {
                ApplyShape(_lengthAfter);
                FinishExtend();
                return;
            }

            // 时长为 0：走同一个 StepExtend（里面有挡路检查），不用等下一帧。
            if (_extendDuration <= 0f) StepExtend(0f);
        }

        /// <summary>缩回攻击前长度。正在缩回时什么都不做。</summary>
        public void Retract()
        {
            if (!isActiveAndEnabled) return;
            if (_state == State.Retracted || _state == State.Retracting) return;

            _state = State.Retracting;
            _retractTimer = -1f;
            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            if (_logState) Debug.Log("[AttackExtendedPlatform] 开始缩回。", this);

            if (_extendDuration <= 0f) FinishRetract();
        }

        /// <summary>立刻回到攻击前长度（调试 / 重开一局用，不走动画）。</summary>
        public void ResetToBefore()
        {
            _state = State.Retracted;
            _retractTimer = -1f;
            IsBlocked = false;
            BlockedBy = null;
            _loggedBlock = false;

            ApplyShape(_lengthBefore);

            if (_logState) Debug.Log("[AttackExtendedPlatform] 复位到攻击前长度。", this);
        }

        /// <summary>
        /// 运行时改两个长度（例如关卡脚本按进度改）。
        /// </summary>
        /// <remarks>立刻按新长度重摆；如果正在延长 / 已延长，当前长度会被夹回新区间。</remarks>
        public void SetLengths(float before, float after)
        {
            _lengthBefore = Mathf.Max(MinLength, before);
            _lengthAfter = Mathf.Max(_lengthBefore, after);

            if (Application.isPlaying && _geometryReady)
            {
                _current = Mathf.Clamp(_current, _lengthBefore, _lengthAfter);

                // 已延长状态下把「攻击后长度」改长了：重新进入延长，别停在旧长度上。
                if (_state == State.Extended && _current < _lengthAfter - ArriveEpsilon)
                    _state = State.Extending;

                ApplyShape(_current);
            }
            else
            {
                ApplyShape(PreviewLength);
            }
        }

        /// <summary>运行时换延长方向。</summary>
        public void SetDirection(ExtendDirection direction)
        {
            _extendDirection = direction;

            if (Application.isPlaying && _geometryReady) ApplyShape(_current);
            else ApplyShape(PreviewLength);
        }

        /// <summary>编辑器预览开关（Inspector 按钮用）。</summary>
        public void SetPreviewExtended(bool extended)
        {
            _previewExtended = extended;
            if (!Application.isPlaying) ApplyShape(PreviewLength);
        }

        /// <summary>
        /// 运行时 / 生成工具改「自动缩回」的保持时间。
        /// </summary>
        /// <remarks>0 = 一直保持。已经处于「已延长」时，按新值重新起算倒计时。</remarks>
        public void SetAutoRetract(float seconds)
        {
            _retractAfter = Mathf.Max(0f, seconds);

            if (!Application.isPlaying || !_geometryReady) return;
            if (_state != State.Extended) return;

            _retractTimer = _retractAfter > 0f ? _retractAfter : -1f;
        }

        // ==================================================================
        //  IContactSwitch：撞击入口
        // ==================================================================

        /// <summary>
        /// 被非玩家的东西压到。**本组件不是压板**：石头 / 铁箱压在平台上不该让平台变长，
        /// 所以这里什么都不做 —— 只有攻击（<see cref="ImpactByContact"/>）才算数。
        /// </summary>
        public void PressByContact()
        {
        }

        /// <summary>压着的东西离开。理由同 <see cref="PressByContact"/>。</summary>
        public void ReleaseByContact()
        {
        }

        /// <summary>
        /// 被**攻击盒**（轻击 / 重击）打中：开始延长。
        /// </summary>
        /// <param name="sourcePoint">从哪儿打来的。本组件不用它 —— 打哪一边都算。</param>
        /// <remarks>
        /// <para>只在「打中平台」/「两者」模式生效。「玩家攻击」模式由
        /// <see cref="HandlePlayerAttackStruck"/> 统一触发，这里直接忽略 —— 同一刀不重复处理。</para>
        /// <para>走的是 <c>PlayerController.PressSwitches</c> 那条路，和拉杆 / 按钮同一个入口，
        /// **不进伤害管线**。纸飞机打**实心**平台时会停在表面、不会调到这条入口
        /// （它只在 Trigger 上查开关），要投射物触发请给平台加一个 Trigger 子碰撞体。</para>
        /// </remarks>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!Application.isPlaying) return;
            if (_triggerMode == TriggerMode.PlayerAttack) return;

            Extend();
        }

        // ==================================================================
        //  几何
        // ==================================================================

        /// <summary>某个长度下平台的世界中心（根 = 攻击前中心，固定端不动）。</summary>
        private Vector2 CenterFor(float length)
        {
            return (Vector2)transform.position + Direction * ((length - _lengthBefore) * 0.5f);
        }

        /// <summary>某个长度下碰撞体的世界尺寸（轴对齐）。</summary>
        private Vector2 SizeFor(float length)
        {
            return Mathf.Abs(Direction.x) > 0.5f
                ? new Vector2(length, _thickness)
                : new Vector2(_thickness, length);
        }

        /// <summary>某个长度下**会动的那一头**（世界坐标）。</summary>
        private Vector2 LeadingEdge(float length)
        {
            return CenterFor(length) + Direction * (length * 0.5f);
        }

        /// <summary>
        /// 从 <paramref name="fromLength"/> 长到 <paramref name="toLength"/> 的这一段会不会撞上
        /// **非静态刚体**（玩家 / 敌人 / 铁箱 / 其它平台）。撞上就返回 true 并给出挡路的东西。
        /// </summary>
        /// <remarks>
        /// <para>只挡非静态刚体：静态地形（墙 / 地面）不挡 —— 地图组件允许长进墙里，
        /// 与 <c>MovingPlatform</c> 一致；否则半埋在地里的平台永远长不出去。</para>
        /// <para>横截面比平台本身**内缩**一点（<see cref="ProbeCrossInset"/>）：
        /// Box2D 的重叠判定含边界，贴着顶面站的人会被算进来 —— 那不是「挡路」。</para>
        /// </remarks>
        private bool IsGrowthBlocked(float fromLength, float toLength, out Collider2D blocker)
        {
            blocker = null;

            if (_box == null) return false;

            Vector2 axis = Direction;
            var perp = new Vector2(-axis.y, axis.x);

            Vector2 fromEdge = LeadingEdge(fromLength);
            Vector2 toEdge = LeadingEdge(toLength);

            float span = Vector2.Distance(fromEdge, toEdge) + ProbeSkin * 2f;
            float cross = Mathf.Max(MinLength, _thickness - ProbeCrossInset);
            var center = (fromEdge + toEdge) * 0.5f;

            var size = new Vector2(
                Mathf.Abs(axis.x) * span + Mathf.Abs(perp.x) * cross,
                Mathf.Abs(axis.y) * span + Mathf.Abs(perp.y) * cross);

            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(center, size, 0f, filter, _blockers);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _blockers[i];
                if (hit == null) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

                Rigidbody2D body = hit.attachedRigidbody;
                if (body == null || body.bodyType == RigidbodyType2D.Static) continue;

                blocker = hit;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 把碰撞体 / 白盒摆到 <paramref name="length"/> 对应的形态。
        /// </summary>
        /// <remarks>根物体**不动**：固定端留在原地，只有另一头伸出去。</remarks>
        private void ApplyShape(float length)
        {
            _current = Mathf.Max(0f, length);

            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_box == null) return;

            Vector2 axis = Direction;
            Vector2 offset = axis * ((_current - _lengthBefore) * 0.5f);
            Vector2 size = SizeFor(_current);

            if (_box.isTrigger) _box.isTrigger = false;
            if (_box.size != size) _box.size = size;
            if (_box.offset != offset) _box.offset = offset;

            UpdateVisual(offset, size);
            RefreshLabel();
        }

        /// <summary>白盒方块跟着碰撞体走：中心对齐、长宽对齐。</summary>
        /// <remarks>
        /// <para>⚠️ 只适用于 **1×1 单位的精灵**（白盒方块就是）。换成别的图时，缩放会按
        /// 图自己的世界尺寸再乘一遍 —— 正式美术请给平台换一个「能被拉伸」的图，
        /// 或把美术挂在子物体下单独处理。</para>
        /// </remarks>
        private void UpdateVisual(Vector2 offset, Vector2 size)
        {
            if (_visual == null) return;

            // ⚠️ 绝对不能拿根物体自己的 SpriteRenderer 当白盒：缩放根会把碰撞体一起放大，
            //    每帧再按新尺寸算一次 → 平台越缩越大。美术必须挂在子物体上。
            if (_visual == transform)
            {
                WarnRootVisual();
                return;
            }

            var wantedPosition = new Vector3(offset.x, offset.y, _visual.localPosition.z);
            var wantedScale = new Vector3(size.x, size.y, _visual.localScale.z);

            if (_visual.localPosition != wantedPosition) _visual.localPosition = wantedPosition;
            if (_visual.localScale != wantedScale) _visual.localScale = wantedScale;
        }

        /// <summary>自动找白盒方块：**子物体**上的第一个 SpriteRenderer（跳过根自己）。</summary>
        private void ResolveVisual()
        {
            if (_visual != null) return;

            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].transform == transform) continue;
                _visual = renderers[i].transform;
                return;
            }

            // 只有根上有精灵：不拿它当白盒，报一次让作者把美术挪到子物体。
            if (GetComponent<SpriteRenderer>() != null) WarnRootVisual();
        }

        private void WarnRootVisual()
        {
            if (_warnedRootVisual) return;

            _warnedRootVisual = true;
            Debug.LogWarning("[AttackExtendedPlatform] 白盒方块不能挂在根物体上 —— 缩放根会把碰撞体一起放大。" +
                             "请把美术放到子物体（例如 Art），再把它拖到「表现 / 白盒方块」里。", this);
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float lengthBefore, float lengthAfter)
        {
            // 用 ASCII 箭头：白盒标签走 TextMesh + 中文字体，不保证含 U+2192。
            return "延长 " + lengthBefore.ToString("0.##") + " -> " + lengthAfter.ToString("0.##");
        }

        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_lengthBefore, _lengthAfter);
            if (_label.text != wanted) _label.text = wanted;
        }

        // ==================================================================
        //  调试入口（右键组件菜单）
        // ==================================================================

        [ContextMenu("调试/延长（Play）")]
        public void DebugExtend()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[AttackExtendedPlatform] 编辑模式不会改运行状态；用 Inspector 上的「预览：攻击后」看形态。", this);
                return;
            }

            Extend();
        }

        [ContextMenu("调试/缩回（Play）")]
        public void DebugRetract()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[AttackExtendedPlatform] 编辑模式不会改运行状态；用 Inspector 上的「预览：攻击前」看形态。", this);
                return;
            }

            Retract();
        }

        [ContextMenu("调试/复位到攻击前（Play）")]
        public void DebugReset()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[AttackExtendedPlatform] 编辑模式不会改运行状态；用 Inspector 上的「预览：攻击前」看形态。", this);
                return;
            }

            ResetToBefore();
        }

        // ==================================================================
        //  只给编辑器工具用
        // ==================================================================

        /// <summary>只给编辑器工具用：改长度 / 方向并立刻重摆。</summary>
        public void Configure(float lengthBefore, float lengthAfter, float thickness, ExtendDirection direction)
        {
            _lengthBefore = Mathf.Max(MinLength, lengthBefore);
            _lengthAfter = Mathf.Max(_lengthBefore, lengthAfter);
            _thickness = Mathf.Max(MinLength, thickness);
            _extendDirection = direction;

            if (_box == null) _box = GetComponent<BoxCollider2D>();
            ApplyShape(PreviewLength);
        }

        /// <summary>只给编辑器工具用：接上白盒方块与标签。</summary>
        public void SetWhitebox(Transform visual, TextMesh label)
        {
            _visual = visual;
            _label = label;

            ApplyShape(Application.isPlaying && _geometryReady ? _current : PreviewLength);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        private void OnValidate()
        {
            _lengthBefore = Mathf.Max(MinLength, _lengthBefore);
            _thickness = Mathf.Max(MinLength, _thickness);
            _lengthAfter = Mathf.Max(_lengthBefore, _lengthAfter);
            _extendDuration = Mathf.Max(0f, _extendDuration);
            _retractAfter = Mathf.Max(0f, _retractAfter);

            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_visual == null) ResolveVisual();

            if (!Application.isPlaying)
            {
                _geometryReady = true;
                ApplyShape(PreviewLength);
                return;
            }

            // Play 中在 Inspector 里拖长度：立刻按新值重摆，不用退出重进。
            if (!_geometryReady) return;

            _current = Mathf.Clamp(_current, _lengthBefore, _lengthAfter);
            ApplyShape(_current);

            // Play 中改了触发方式：立刻退订 / 补订玩家（组件没启用时不要挂订阅）。
            if (isActiveAndEnabled) SyncPlayerSubscription(0f);
        }

        private void OnDrawGizmos()
        {
            Vector2 axis = Direction;
            var perp = new Vector2(-axis.y, axis.x);

            // 攻击前：灰蓝。
            Gizmos.color = new Color(0.45f, 0.62f, 0.78f, 0.85f);
            Gizmos.DrawWireCube(CenterFor(_lengthBefore), SizeFor(_lengthBefore));

            // 攻击后：绿。
            Gizmos.color = new Color(0.35f, 0.9f, 0.48f, 0.9f);
            Gizmos.DrawWireCube(CenterFor(_lengthAfter), SizeFor(_lengthAfter));

            // 会动的那一头：从攻击前指向攻击后。
            Vector2 from = LeadingEdge(_lengthBefore);
            Vector2 to = LeadingEdge(_lengthAfter);
            Gizmos.color = new Color(0.35f, 0.9f, 0.48f, 1f);
            Gizmos.DrawLine(from, to);

            float length = Vector2.Distance(from, to);
            float head = Mathf.Clamp(length * 0.2f, 0.08f, 0.35f);
            Gizmos.DrawLine(to, to - axis * head + perp * (head * 0.5f));
            Gizmos.DrawLine(to, to - axis * head - perp * (head * 0.5f));

            // 固定端：白色小球。
            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(CenterFor(_lengthBefore) - axis * (_lengthBefore * 0.5f), 0.1f);

            if (!Application.isPlaying) return;

            // 播放中：当前形态（黄）。
            Gizmos.color = new Color(1f, 0.78f, 0.25f, 1f);
            Gizmos.DrawWireCube(CenterFor(_current), SizeFor(_current));

            // 被挡住：会动的那一头画个红球。
            if (!IsBlocked) return;

            Gizmos.color = new Color(1f, 0.3f, 0.25f, 1f);
            Gizmos.DrawWireSphere(LeadingEdge(_current), 0.16f);
        }
    }
}
