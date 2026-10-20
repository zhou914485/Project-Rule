using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 平移平台：在**起点 / 终点之间直线移动**的场景组件（滑板车、升降台、巡逻踏板都用它）。
    /// </summary>
    /// <remarks>
    /// <para><b>默认是「乘客触发的一趟往返」</b>（<see cref="PathMode.Ride"/>）：
    /// 平台停在起点待客 → **有人站上来**才出发 → 到终点 → 回到起点停住。
    /// 回到起点后只要车上还有人就不再待客，等他们下车才重新武装 ——
    /// 否则一个站在上面不走的乘客会让平台来回抖个不停。
    /// 另有循环往返 / 单程两种模式给别的机关用。</para>
    ///
    /// <para><b>只走直线、不旋转</b>：路径就是 <see cref="_start"/> → <see cref="_end"/> 一条线段，
    /// 位置按「已走过的距离 / 全长」插值。匀速，没有缓动 —— 缓动属于表现层，不在这里做。</para>
    ///
    /// <para><b>坐标是相对本物体的偏移</b>（<see cref="_pathIsRelative"/> = true）：
    /// 起点 / 终点存的是「离本物体摆放位置多远」，于是**拖动物体就是整条路径跟着走** ——
    /// 关卡整体挪位置时不会出现「平台搬了、终点还留在原地」。
    /// 编辑器工具栏里有两个「把起点/终点设为当前位置」按钮，摆好再点一下就行。</para>
    ///
    /// <para>⚠️ 参照点只在 <see cref="Awake"/> 记一次（那时平台还没被搬走），
    /// 之后播放中再怎么移动都不会重算 —— 否则平台一动，路径就跟着自己跑偏。
    /// 沿用的先例见 <see cref="SpringPlatform"/> / <see cref="RailLift"/>。
    /// 旧资产（<see cref="_pathIsRelative"/> = false）仍按**世界坐标**解释，用迁移工具一次性转过来。</para>
    ///
    /// <para><b>刚体是 Kinematic</b>：位移完全由代码决定，物理不会替它解算。
    /// 位置用 <c>Rigidbody2D.MovePosition</c> 提交 —— 它在**物理步进时**生效，
    /// 于是暂停（<c>Physics2D.simulationMode = Script</c>）时平台自动静止，
    /// 不用额外判暂停；<see cref="FixedUpdate"/> 里那句 <c>IsPaused</c> 只是
    /// 让「走了多远」这套账本跟位置保持一致（两边都不动）。</para>
    ///
    /// <para><b>时间规则会改它的速度</b>：每帧用的是 <c>Time.fixedDeltaTime</c>，
    /// 它随 <c>Time.timeScale</c> 缩放（<c>TimeManager.Synthesize</c> 写的那个）。
    /// 时间变慢 → 物理步数变少 → 平台跟着变慢，符合「改变的是世界」。</para>
    ///
    /// <para><b>站在上面的人会被带走</b>：本类实现 <see cref="IMovingSurface"/>，
    /// 承载方（玩家走 <c>ContactSensor</c> / <c>MotionMover</c>）每个物理帧把 <see cref="Velocity"/>
    /// 加进自己的位移里。平台这边**不主动推人** —— 谁踩上来谁自己读。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class MovingPlatform : MonoBehaviour, IMovingSurface
    {
        /// <summary>「落在表面上」的容差：物体边缘与站人面的最大允许间隙（米）。</summary>
        private const float RiderContactTolerance = 0.08f;

        /// <summary>到达端点之后怎么走。</summary>
        public enum PathMode
        {
            /// <summary>循环往返：到终点折回起点，不停。</summary>
            PingPong = 0,

            /// <summary>单程：走到终点就停住，不再移动。</summary>
            Once = 1,

            /// <summary>待客往返：站上去才出发 → 到终点 → 回起点停住。**默认**。</summary>
            Ride = 2,
        }

        /// <summary>待客模式的阶段。</summary>
        private enum RidePhase
        {
            /// <summary>停在起点等乘客。</summary>
            Waiting = 0,

            /// <summary>起点 → 终点。</summary>
            Outbound = 1,

            /// <summary>终点 → 起点。</summary>
            Returning = 2,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("路径（相对本物体的偏移）")]
        [Tooltip("起点。相对**本物体的摆放位置**的偏移 —— 拖动物体，整条路径跟着走。\n" +
                 "进入播放时平台会被搬到这里。")]
        [SerializeField] private Vector2 _start = Vector2.zero;

        [Tooltip("终点。和起点重合 = 不会动的平台。")]
        [SerializeField] private Vector2 _end = Vector2.zero;

        [Tooltip("旧资产的开关：不勾时上面两个值按**世界坐标**解释（迁移工具会把它勾上并换算成偏移）。")]
        [SerializeField, HideInInspector] private bool _pathIsRelative = false;

        [Tooltip("起始进度：0 = 从起点出发，1 = 从终点出发（单程模式下即终点）。\n" +
                 "放两台同向的平台时错开相位用。**待客模式忽略此项**（永远从起点出发）。")]
        [SerializeField, Range(0f, 1f)] private float _startProgress = 0f;

        [Header("节奏")]
        [Tooltip("移动速度（单位 / 秒）。")]
        [SerializeField, Min(0f)] private float _speed = 3f;

        [Tooltip("待客：站上去才出发，到终点再回起点停住。\n" +
                 "循环往返：不停。\n单程：走到终点就停。")]
        [SerializeField] private PathMode _mode = PathMode.Ride;

        [Tooltip("停在端点多久（秒）。待客模式下用在**终点**（返回前）；0 = 立刻掉头。")]
        [SerializeField, Min(0f)] private float _waitAtEnds = 0f;

        [Header("乘客触发（仅待客模式）")]
        [Tooltip("哪些层的东西算「站上来」。Trigger 永远不算。")]
        [SerializeField] private LayerMask _riderMask = ~0;

        [Tooltip("检测高度（米）：东西落在平台上表面往上一段里，才算站在上面。\n" +
                 "太小会漏掉悬空一丁点的乘客，太大会把旁边贴着的东西也算进来。")]
        [SerializeField, Min(0.02f)] private float _riderProbeHeight = 0.35f;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private Collider2D  _ownCollider;
        private readonly Collider2D[] _riderHits = new Collider2D[8];

        /// <summary>沿路径已走过的距离，落在 0 ~ <see cref="_length"/>。</summary>
        private float _travel;

        /// <summary>下一步往哪头走：+1 去终点，−1 回起点。</summary>
        private float _forward = 1f;

        /// <summary>端点停留的剩余秒数。</summary>
        private float _waitRemaining;

        /// <summary>单程模式走到头之后置位，从此不再移动。</summary>
        private bool _finished;

        /// <summary>待客模式当前处在哪一程。</summary>
        private RidePhase _phase = RidePhase.Waiting;

        /// <summary>
        /// 待客模式能不能出发。跑完一趟回到起点后先关掉，等车上清空才重新打开 ——
        /// 不然还站在车上的乘客会让它立刻再跑一趟。
        /// </summary>
        private bool _armed = true;

        private float _length;
        private Vector2 _lastTarget;

        /// <summary>
        /// 路径偏移的参照点：<see cref="Awake"/> 时的摆放位置。
        /// </summary>
        /// <remarks>
        /// <para>只在 <see cref="Awake"/> 记一次。平台每个物理帧都在动，之后再读 <c>transform.position</c>
        /// 就是把参照点拴在自己身上，路径会跟着自己漂。</para>
        /// <para>编辑模式下不缓存：直接读 <c>transform.position</c>，这样拖动物体时 Gizmo 与 Inspector
        /// 上的路径是**立刻**跟着走的。</para>
        /// </remarks>
        private Vector2 _pathAnchor;
        private bool _anchorCaptured;

        /// <summary>路径偏移的世界参照点。播放中 = 摆放位置，编辑中 = 当前位置。</summary>
        private Vector2 PathAnchor =>
            Application.isPlaying && _anchorCaptured ? _pathAnchor : (Vector2)transform.position;

        /// <summary>起点的世界坐标。</summary>
        private Vector2 StartWorld => _pathIsRelative ? PathAnchor + _start : _start;

        /// <summary>终点的世界坐标。</summary>
        private Vector2 EndWorld => _pathIsRelative ? PathAnchor + _end : _end;

        /// <summary>本物理帧的平台速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 ——
        /// 承载方加进自己的速度后，走出的距离与平台**逐帧相等**，不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>路径全长（单位）。0 = 起点终点重合。</summary>
        public float Length => _length;

        /// <summary>起点（世界坐标）。</summary>
        public Vector2 StartPoint => StartWorld;

        /// <summary>终点（世界坐标）。</summary>
        public Vector2 EndPoint => EndWorld;

        /// <summary>移动速度（单位 / 秒）。</summary>
        public float Speed => _speed;

        /// <summary>当前行走模式。</summary>
        public PathMode Mode => _mode;

        /// <summary>待客模式下是否正载着人跑（已出发、还没回到起点）。</summary>
        public bool OnDuty => _phase != RidePhase.Waiting;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            // ⚠️ 必须排在 Rebuild() 前面：它会把平台搬到起点，再读就拿不到摆放位置了。
            _pathAnchor = transform.position;
            _anchorCaptured = true;

            _body = GetComponent<Rigidbody2D>();
            _ownCollider = GetComponent<Collider2D>();
            ConfigureBody();
            Rebuild();
        }

        private void OnEnable()
        {
            // 重开一局 / 对象池取出时也要回到起点，不能接着上次的进度走。
            if (_body == null)
            {
                _body = GetComponent<Rigidbody2D>();
                _ownCollider = GetComponent<Collider2D>();
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

            if (_mode == PathMode.Ride) StepRide(dt);
            else StepAuto(dt);

            Vector2 target = Evaluate(_travel);
            Velocity = (target - _lastTarget) / dt;
            _lastTarget = target;

            _body.MovePosition(target);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>把「走了多远」推进 <paramref name="dt"/> 秒。端点停留在这里被消耗掉。</summary>
        /// <returns>本帧是否抵达端点。</returns>
        private bool Advance(float dt)
        {
            if (_waitRemaining > 0f)
            {
                _waitRemaining -= dt;
                if (_waitRemaining > 0f) return false;

                // 停留结束：这一帧剩下的时间不再折算成位移（最多丢掉一个物理步，肉眼看不出）。
                _waitRemaining = 0f;
            }

            if (_length <= 0f) return false;

            _travel += _speed * dt * _forward;

            // ⚠️ 两个端点都要带上方向判断：速度为 0 时 _travel 恒等于 0，
            //    不带方向就会被判成「抵达起点」，每帧折返一次。
            if (_forward > 0f && _travel >= _length)
            {
                _travel = _length;
                return true;
            }

            if (_forward < 0f && _travel <= 0f)
            {
                _travel = 0f;
                return true;
            }

            return false;
        }

        /// <summary>循环往返 / 单程：不等乘客，按时间自己走。</summary>
        private void StepAuto(float dt)
        {
            if (_finished) return;
            if (!Advance(dt)) return;

            if (_mode == PathMode.Once)
            {
                _finished = true;
                return;
            }

            // PingPong
            _forward = -_forward;
            _waitRemaining = _waitAtEnds;
        }

        /// <summary>
        /// 待客往返：起点待客 → 到终点 → 回起点停住。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么回到起点要「重新武装」而不是直接接着待客</b>：跑完一趟时乘客通常还站在上面，
        /// 直接待客会瞬间满足触发条件，平台就原地来回抖。所以先解除武装，
        /// 等平台上**没人**了才允许下一次出发。</para>
        /// <para>乘客中途跳下去也不会取消行程 —— 车照样把这一趟跑完再回起点。</para>
        /// </remarks>
        private void StepRide(float dt)
        {
            switch (_phase)
            {
                case RidePhase.Waiting:
                    if (!_armed)
                    {
                        // 车上清空了才重新待客。
                        if (!HasRider()) _armed = true;
                        return;
                    }

                    if (!HasRider()) return;

                    _phase = RidePhase.Outbound;
                    _forward = 1f;
                    _waitRemaining = 0f;
                    return;

                case RidePhase.Outbound:
                    if (!Advance(dt)) return;

                    _phase = RidePhase.Returning;
                    _forward = -1f;
                    _waitRemaining = _waitAtEnds;
                    return;

                case RidePhase.Returning:
                    if (!Advance(dt)) return;

                    _phase = RidePhase.Waiting;
                    _armed = false;
                    return;
            }
        }

        private Vector2 Evaluate(float travel)
        {
            Vector2 start = StartWorld;
            if (_length <= 0f) return start;
            return Vector2.LerpUnclamped(start, EndWorld, travel / _length);
        }

        /// <summary>
        /// 算出「站人面」外侧的探测盒（世界坐标）。站人面取**重力反方向**那一面 ——
        /// 「重力 × 反转」之后人是站在平台另一边的，检测得跟着翻。
        /// </summary>
        /// <remarks>只按上下两面处理：「上」在本作里永远是 ±Y（重力只会翻转 / 归零）。</remarks>
        private bool RiderProbeBox(out Vector2 center, out Vector2 size, out float face, out bool faceUp)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            face = 0f;
            faceUp = true;

            Collider2D own = _ownCollider;
            if (own == null) own = _ownCollider = GetComponent<Collider2D>();
            if (own == null) return false;

            Bounds b = own.bounds;

            Vector2 up = Physics2D.gravity.sqrMagnitude > 0.0001f
                ? -Physics2D.gravity.normalized
                : Vector2.up;

            // 探测带：从表面往「上」一段，里面再收一点皮，避免物体刚好贴着却没被判到。
            const float skin = 0.02f;
            faceUp = up.y >= 0f;
            face = faceUp ? b.max.y : b.min.y;

            float low  = faceUp ? face - skin : face - _riderProbeHeight;
            float high = faceUp ? face + _riderProbeHeight : face + skin;

            // 横向收一点：贴着侧面站的墙 / 箱子不算「站上来」。
            float width = Mathf.Max(0.05f, b.size.x - skin * 4f);

            center = new Vector2(b.center.x, (low + high) * 0.5f);
            size = new Vector2(width, high - low);
            return true;
        }

        /// <summary>站人面上有没有东西（玩家 / 敌人 / 箱子都算，Trigger 不算）。</summary>
        /// <remarks>
        /// 光「进了探测带」还不够 —— 还要**真的落在表面上**（物体靠平台那一侧的边缘贴着站人面）。
        /// 否则平台正上方 0.3 米处的天花板 / 横梁也会被当成乘客，平台自己就跑了。
        /// </remarks>
        private bool HasRider()
        {
            if (!RiderProbeBox(out Vector2 center, out Vector2 size, out float face, out bool faceUp)) return false;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_riderMask);

            int count = Physics2D.OverlapBox(center, size, 0f, filter, _riderHits);
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _riderHits[i];
                if (hit == null || hit == _ownCollider) continue;
                if (_body != null && hit.attachedRigidbody == _body) continue;   // 自己的子碰撞体

                // 物体朝平台那一侧的边缘。接地时它与站人面只差接触间隙（~0.01）。
                Bounds hb = hit.bounds;
                float standingEdge = faceUp ? hb.min.y : hb.max.y;
                if (Mathf.Abs(standingEdge - face) > RiderContactTolerance) continue;

                return true;
            }

            return false;
        }

        /// <summary>按配置把内部状态摆回初始位置，并立刻落到位。</summary>
        private void Rebuild()
        {
            _length = Vector2.Distance(StartWorld, EndWorld);
            _forward = 1f;
            _finished = false;
            _waitRemaining = 0f;
            _phase = RidePhase.Waiting;
            _armed = true;

            // 待客模式永远从起点出发；其余模式用起始进度错开相位。
            _travel = _mode == PathMode.Ride
                ? 0f
                : Mathf.Clamp01(_startProgress) * _length;

            // 单程 + 从终点出发 = 已经没有地方可去。
            if (_mode == PathMode.Once && _travel >= _length && _length > 0f)
            {
                _finished = true;
            }

            // 从终点出发的往返平台，第一步是往回走。
            if (_travel >= _length && _length > 0f) _forward = -1f;

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
        /// <param name="start">起点（**世界坐标**）。</param>
        /// <param name="end">终点（**世界坐标**）。</param>
        /// <remarks>相对模式在这里换算成偏移，调用方一律按世界坐标填 —— 编辑器工具不用关心存储形式。</remarks>
        public void SetPath(Vector2 start, Vector2 end)
        {
            if (_pathIsRelative)
            {
                Vector2 anchor = PathAnchor;
                _start = start - anchor;
                _end = end - anchor;
            }
            else
            {
                _start = start;
                _end = end;
            }

            // 立刻跟上，否则编辑模式下 Length 会一直是旧值（Rebuild 只在播放时跑）。
            _length = Vector2.Distance(start, end);
        }

        /// <summary>
        /// 只给编辑器迁移工具用：把当前按世界坐标存的端点就地换算成相对偏移。
        /// </summary>
        /// <returns>真的改了吗（已经是相对模式就返回 false，可重复调用）。</returns>
        public bool ConvertPathToRelative()
        {
            if (_pathIsRelative) return false;

            Vector2 anchor = transform.position;
            _start -= anchor;
            _end -= anchor;
            _pathIsRelative = true;
            _length = Vector2.Distance(StartWorld, EndWorld);
            return true;
        }

        private void Reset()
        {
            // 编辑器里 AddComponent 会调到这里：新组件默认走相对语义。
            _pathIsRelative = true;
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            _length = Vector2.Distance(StartWorld, EndWorld);
        }

        private void OnDrawGizmos()
        {
            Vector2 a = StartWorld;
            Vector2 b = EndWorld;
            float length = Vector2.Distance(a, b);

            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);

            if (length > 0f)
            {
                // 终点画个箭头，一眼看出往哪头去。
                Vector2 dir = (b - a) / length;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                float head = Mathf.Min(0.35f, length * 0.25f);

                Gizmos.DrawLine(b, b - dir * head + perp * head * 0.5f);
                Gizmos.DrawLine(b, b - dir * head - perp * head * 0.5f);
            }

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(a, 0.18f);

            Gizmos.color = new Color(1f, 0.5f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(b, 0.18f);

            // 待客模式：把「站人面」画出来 —— 踩进这个框才会出发。
            if (_mode == PathMode.Ride
                && RiderProbeBox(out Vector2 probeCenter, out Vector2 probeSize, out _, out _))
            {
                Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.25f);
                Gizmos.DrawWireCube(probeCenter, probeSize);
            }

            // 播放中额外把「现在走到哪了」标出来。
            if (!Application.isPlaying || length <= 0f) return;

            Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
            Gizmos.DrawWireSphere(Evaluate(_travel), 0.22f);
        }
    }
}
