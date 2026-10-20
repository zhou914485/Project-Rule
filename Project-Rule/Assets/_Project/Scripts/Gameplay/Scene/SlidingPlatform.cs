using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 开关驱动的滑动平台：开关**开** → 滑到终点，开关**关** → 滑回起点。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="MovingPlatform"/> 的分工</b>：那个按**时间**自己走（待客 / 循环 / 单程）；
    /// 本类不自己决定去哪，**只听开关**（<see cref="ISwitchTarget"/>）。
    /// 接法与门 / 电梯 / 拳击手套完全一样：把本组件拖到 <c>CityButton</c> / <c>CityLever</c> 的
    /// <c>FollowMotor</c> 上。</para>
    ///
    /// <para><b>位置跟开关状态走，不是「按一下走一趟」</b>：开关一开，平台就以
    /// <see cref="_speed"/> 滑向终点；半路又关掉，它会**就地掉头**滑回起点（不会先跑到终点再回来）。
    /// 所以拨一次拉杆 = 滑过去，再拨回来 = 滑回来。</para>
    ///
    /// <para><b>开局位置必须与开关的初始状态一致</b>：<c>CitySwitch.IsOn</c> 默认是关，
    /// 平台默认就停在起点；如果那个开关开局就是开的（<c>IsOn</c> 勾着），
    /// 把 <see cref="_startsAtEnd"/> 也勾上，否则平台会在开局自己滑一趟。</para>
    ///
    /// <para><b>只走直线、不旋转</b>：路径就是 <see cref="_start"/> → <see cref="_end"/>，
    /// 按「已走过的距离 / 全长」插值。匀速，没有缓动。</para>
    ///
    /// <para><b>刚体是 Kinematic</b>：位置用 <c>Rigidbody2D.MovePosition</c> 提交，
    /// 暂停（<c>Physics2D.simulationMode = Script</c>）时自动静止；
    /// <see cref="FixedUpdate"/> 里那句 <c>IsPaused</c> 只是让速度账本与位置保持一致。</para>
    ///
    /// <para><b>时间规则会改它的速度</b>：用的是 <c>Time.fixedDeltaTime</c>，它随
    /// <c>Time.timeScale</c> 缩放 —— 时间变慢，平台跟着变慢。</para>
    ///
    /// <para><b>站在上面的人会被带走</b>：实现 <see cref="IMovingSurface"/>，
    /// 承载方（玩家走 <c>ContactSensor</c> / <c>MotionMover</c>）每物理帧把
    /// <see cref="Velocity"/> 加进自己的位移里。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SlidingPlatform : MonoBehaviour, IMovingSurface, ISwitchTarget
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("路径（世界坐标）")]
        [Tooltip("起点（开关关闭时停在这里）。进入播放时平台会被搬到这里。")]
        [SerializeField] private Vector2 _start = Vector2.zero;

        [Tooltip("终点（开关打开时停在这里）。和起点重合 = 不会动的平台。")]
        [SerializeField] private Vector2 _end = Vector2.zero;

        [Header("开关")]
        [Tooltip("开局停在终点，对应「开关开局就是开的」（CitySwitch 的 IsOn 勾着）。\n" +
                 "开关默认是关 —— 不勾就停在起点。")]
        [SerializeField] private bool _startsAtEnd = false;

        [Header("节奏")]
        [Tooltip("移动速度（单位 / 秒）。")]
        [SerializeField, Min(0f)] private float _speed = 3f;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;

        /// <summary>沿路径已走过的距离，落在 0 ~ <see cref="_length"/>。</summary>
        private float _travel;

        /// <summary>当前该去哪一头：true = 终点，false = 起点。</summary>
        private bool _targetEnd;

        private float _length;
        private Vector2 _lastTarget;

        /// <summary>本物理帧的平台速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 —— 承载方加进去后不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>路径全长（单位）。0 = 起点终点重合。</summary>
        public float Length => _length;

        /// <summary>起点（世界坐标）。</summary>
        public Vector2 StartPoint => _start;

        /// <summary>终点（世界坐标）。</summary>
        public Vector2 EndPoint => _end;

        /// <summary>移动速度（单位 / 秒）。</summary>
        public float Speed => _speed;

        /// <summary>开关当前要求的去向：true = 终点。</summary>
        public bool TargetEnd => _targetEnd;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            ConfigureBody();
            Rebuild();
        }

        private void OnEnable()
        {
            // 重开一局 / 对象池取出时也要回到开局位置，不能接着上次的进度走。
            if (_body == null)
            {
                _body = GetComponent<Rigidbody2D>();
                ConfigureBody();
            }
            Rebuild();
        }

        private void FixedUpdate()
        {
            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            if (_length > 0f)
            {
                float goal = _targetEnd ? _length : 0f;
                _travel = Mathf.MoveTowards(_travel, goal, _speed * dt);
            }

            Vector2 target = Evaluate(_travel);
            Velocity = (target - _lastTarget) / dt;
            _lastTarget = target;

            _body.MovePosition(target);
        }

        // ==================================================================
        //  开关
        // ==================================================================

        /// <summary>
        /// 开关驱动入口（<see cref="ISwitchTarget"/>）：开 → 去终点，关 → 回起点。
        /// </summary>
        /// <remarks>半路反向是允许的：平台从当前位置直接掉头，不会先跑完原来那一程。</remarks>
        public void SetSwitchState(bool on) => _targetEnd = on;

        // ==================================================================
        //  内部
        // ==================================================================

        private Vector2 Evaluate(float travel)
        {
            if (_length <= 0f) return _start;
            return Vector2.LerpUnclamped(_start, _end, travel / _length);
        }

        /// <summary>按配置把内部状态摆回开局位置，并立刻落到位。</summary>
        private void Rebuild()
        {
            _length = Vector2.Distance(_start, _end);
            _targetEnd = _startsAtEnd;
            _travel = _startsAtEnd ? _length : 0f;

            Vector2 target = Evaluate(_travel);
            _lastTarget = target;
            Velocity = Vector2.zero;
            _body.position = target;
        }

        private void ConfigureBody()
        {
            // Kinematic：位移由代码决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值平台会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：不动内部账本，直接改两个端点。</summary>
        public void SetPath(Vector2 start, Vector2 end)
        {
            _start = start;
            _end = end;
            // 立刻跟上，否则编辑模式下 Length 会一直是旧值（Rebuild 只在播放时跑）。
            _length = Vector2.Distance(_start, _end);
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            _length = Vector2.Distance(_start, _end);
        }

        private void OnDrawGizmos()
        {
            Vector2 a = _start;
            Vector2 b = _end;
            float length = Vector2.Distance(a, b);

            Gizmos.color = new Color(0.75f, 0.55f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);

            if (length > 0f)
            {
                // 两头都画箭头：开关能让它往任一头走。
                Vector2 dir = (b - a) / length;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                float head = Mathf.Min(0.35f, length * 0.25f);

                Gizmos.DrawLine(b, b - dir * head + perp * head * 0.5f);
                Gizmos.DrawLine(b, b - dir * head - perp * head * 0.5f);
                Gizmos.DrawLine(a, a + dir * head + perp * head * 0.5f);
                Gizmos.DrawLine(a, a + dir * head - perp * head * 0.5f);
            }

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(a, 0.18f);

            Gizmos.color = new Color(1f, 0.5f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(b, 0.18f);

            // 播放中额外把「现在走到哪了」标出来。
            if (!Application.isPlaying || length <= 0f) return;

            Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
            Gizmos.DrawWireSphere(Evaluate(_travel), 0.22f);
        }
    }
}
