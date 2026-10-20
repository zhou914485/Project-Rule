using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 单向门：只允许顺着一个方向穿过去，反方向是一堵实心的墙。
    /// </summary>
    /// <remarks>
    /// <para><b>做法：放行时把自己的碰撞体整个摘掉。</b> 挡路的就是本物体上的
    /// <see cref="BoxCollider2D"/>（层 = <c>Ground</c>）。玩家的位移走 <c>Rigidbody2D.Slide</c>，
    /// 它只看得到「世界里有没有这块碰撞体」—— 摘掉就是真放行，玩家侧不需要认识本组件
    /// （与 <c>MovingPlatform</c> 走 <c>IMovingSurface</c> 是同一个思路）。</para>
    ///
    /// <para><b>为什么不用 PlatformEffector2D</b>：它的 one-way 只在**物理求解**里生效，
    /// 而玩家的刚体是 Kinematic、位移由 <c>Slide</c> 查询式解算 —— effector 根本管不到它。
    /// 而且它管的是「沿自身法线的一个角度区间」，做成竖直门板得拧 <c>rotationalOffset</c>，
    /// 在场景里读不出来。</para>
    ///
    /// <para><b>为什么不用触发器回调</b>：与 <c>RevealRegion</c> / <c>Tube</c> 同一约定 ——
    /// 玩家是 Kinematic + <c>Slide</c>，接触 / 触发回调不可靠。这里每物理帧做一次**包围盒判定**，
    /// 与刚体类型无关，也不需要门自带刚体（它自己不动）。</para>
    ///
    /// <para><b>判定区是算出来的，不另挂范围碰撞体</b>：以自身碰撞体的包围盒为中心，
    /// 沿四个方向各外扩 <see cref="_reach"/>。门的范围天生由门的尺寸推导而来，
    /// 多挂一个物体就多一处会不同步的地方。（<c>Tube</c> 那两个判定箱是独立的美术尺寸，
    /// 才需要显式挂。）</para>
    ///
    /// <para><b>状态机只有两条</b>，都在 <see cref="Axis"/> 这条轴上算
    /// （<c>along</c> = 玩家中心相对门中心、顺着通过方向的位移）：</para>
    /// <list type="bullet">
    /// <item><b>开</b>：玩家中心在判定区内，且 <c>along &lt; 0</c> —— 也就是他还在**进得去的那一侧**。</item>
    /// <item><b>关</b>：玩家**完全穿过去了**（碰撞体尾边越过门的远边），或者走出了判定区。</item>
    /// </list>
    /// <para>「完全穿过去」用的是玩家碰撞体的**尾边**，所以绝不会在他还压在门上时关门 ——
    /// 那是这类机关唯一会把人夹住 / 弹飞的翻车点。走出判定区那条路另加了一道「与门不重叠」的保险，
    /// 见 <see cref="FixedUpdate"/>。</para>
    ///
    /// <para><b>竖直方向（<see cref="PassDirection.ToUp"/> / <see cref="PassDirection.ToDown"/>）</b>：
    /// 判定完全不变，只是把轴换成竖直的 —— 门这时是一块**横板**。
    /// <c>ToUp</c> = 从下往上钻得过去，整个人越过之后碰撞体恢复，于是能站在上面、掉不回来；
    /// <c>ToDown</c> 反过来，只能往下钻、爬不回。
    /// 白盒尺寸必须跟着方向走（水平门竖板、竖直门横板），见 <c>OneWayGateBuilder</c>。
    /// 注意通过方向是**固定的**，不跟重力翻 —— 重力规则反转后，门依然只按这条路放行
    /// （要「实心面跟着重力翻」的是 ⑪ 跳穿平台，不是这扇门）。</para>
    ///
    /// <para><b>只认玩家</b>：敌人 / 子弹两个方向都会被挡（设计上单向门是玩家的通路）。
    /// 另外水平门要做得比玩家的跳跃高度高，或者上面有天花板 —— 否则他能从门顶上跳过去。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class OneWayGate : MonoBehaviour
    {
        /// <summary>允许通过的方向。</summary>
        public enum PassDirection
        {
            /// <summary>从左往右穿（右边的人回不来）。</summary>
            ToRight = 0,

            /// <summary>从右往左穿。</summary>
            ToLeft = 1,

            /// <summary>从下往上穿（跳上去可以，掉不回来）。</summary>
            ToUp = 2,

            /// <summary>从上往下穿。</summary>
            ToDown = 3,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("通过方向")]
        [Tooltip("允许顺着哪个方向穿过去。反方向是一堵实心的墙。")]
        [SerializeField] private PassDirection _pass = PassDirection.ToRight;

        [Header("判定")]
        [Tooltip("判定区：以碰撞体的包围盒为中心，四个方向各外扩这么多（世界单位）。\n" +
                 "必须大于玩家碰撞体的半宽 / 半高（约 0.6）—— 否则人还压在门上就会被判成「已离开判定区」。\n" +
                 "填得越大，门在越远的地方就开始让路。")]
        [SerializeField, Min(0.1f)] private float _reach = 1.5f;

        [Header("引用")]
        [Tooltip("谁算数。留空则自动找场景里的 PlayerController（玩家若是关卡流程里才实例化的，会自己补找）。")]
        [SerializeField] private Transform _player;

        [Header("白盒")]
        [Tooltip("白盒状态色：放行时染成 _openTint，关上时还原它自己本来的颜色。\n" +
                 "**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private SpriteRenderer _indicator;

        [Tooltip("放行时白盒方块染成什么颜色。")]
        [SerializeField] private Color _openTint = new Color(0.42f, 1f, 0.55f, 1f);

        [Tooltip("白盒标签：文字随通过方向自动刷新。**接正式美术时把这个引用清空**。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private BoxCollider2D _box;
        private Collider2D _playerCollider;

        /// <summary>白盒方块本来的颜色（<c>Awake</c> 时抓一次，关门时还原它）。</summary>
        private Color _idleTint = Color.white;

        private bool _open;

        /// <summary>补找玩家的节流计时：找不到时约半秒试一次，别每帧全场搜一遍。</summary>
        private float _nextResolve;

        private bool _warnedNoPlayer;

        private const float ResolveRetrySeconds = 0.5f;

        /// <summary>现在是不是放行状态（碰撞体摘掉了）。表现层要播开合动画就读它。</summary>
        public bool IsOpen => _open;

        /// <summary>允许通过的方向。</summary>
        public PassDirection Pass => _pass;

        /// <summary>
        /// 通过方向是不是竖直的（<see cref="PassDirection.ToUp"/> / <see cref="PassDirection.ToDown"/>）。
        /// </summary>
        /// <remarks>白盒工具用它决定「横板还是竖板」；玩法判定本身只认 <see cref="Axis"/>。</remarks>
        public bool PassesVertically
        {
            get { return _pass == PassDirection.ToUp || _pass == PassDirection.ToDown; }
        }

        /// <summary>通过方向的单位向量 —— 门的所有判定都在这条轴上算。</summary>
        public Vector2 Axis
        {
            get
            {
                switch (_pass)
                {
                    case PassDirection.ToLeft: return Vector2.left;
                    case PassDirection.ToUp:   return Vector2.up;
                    case PassDirection.ToDown: return Vector2.down;
                    default:                   return Vector2.right;
                }
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _box = GetComponent<BoxCollider2D>();
            if (_box == null)
            {
                Debug.LogError("[OneWayGate] 同物体上没有 BoxCollider2D —— 单向门挡不住任何人。", this);
            }
            else if (_box.isTrigger)
            {
                Debug.LogWarning("[OneWayGate] 碰撞体勾了 Is Trigger，它不是墙。单向门需要一块实心碰撞体。", this);
            }

            if (_indicator != null) _idleTint = _indicator.color;

            _nextResolve = 0f;
            TryResolvePlayer();

            SetOpen(false);      // 一开始一定是关着的
        }

        /// <summary>重开一局 / 对象池取出时回到「关着」，不能接着上一局的开启状态。</summary>
        private void OnEnable() => SetOpen(false);

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，谁也不动 —— 门保持原状（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true) return;

            if (_playerCollider == null) TryResolvePlayer();

            if (!TryGetPlayerBounds(out Bounds body))
            {
                // 玩家没了 / 被禁用：别把门留着开着。
                if (_open) SetOpen(false);
                return;
            }

            Vector2 axis = Axis;
            var perp = new Vector2(-axis.y, axis.x);
            Bounds gate = GateBounds();

            var toPlayer = (Vector2)body.center - (Vector2)gate.center;
            float along  = Vector2.Dot(toPlayer, axis);     // 负 = 还在进得去的那一侧
            float across = Vector2.Dot(toPlayer, perp);

            float gateHalfAlong  = HalfExtent(gate, axis);
            float gateHalfAcross = HalfExtent(gate, perp);
            float bodyHalfAlong  = HalfExtent(body, axis);
            float bodyHalfAcross = HalfExtent(body, perp);

            // 判定区：把门的包围盒往四个方向各外扩 _reach。按轴系算，不依赖 Bounds.Contains
            // （那个第三根轴也会参与比较，z 只要有一点点差就整条判空）。
            float reach = Mathf.Max(0.1f, _reach);
            bool inZone = Mathf.Abs(along)  <= gateHalfAlong  + reach
                       && Mathf.Abs(across) <= gateHalfAcross + reach;

            if (_open)
            {
                // 已经放行 —— 关门只有两条路，两条都保证「他确实已经离开门了」：
                //   ① 完全穿过去：尾边越过门的远边（哪怕还贴着门站着，也算他过去了）；
                //   ② 走出判定区，且**与门不重叠**。
                // ② 里那个重叠条件是一道保险：_reach 万一被填得太小，宁可让门一直开着，
                // 也不能在他还压在门上时关门 —— 那会把他夹住 / 弹飞。
                bool passedThrough = along - bodyHalfAlong > gateHalfAlong;
                bool overlapsGate  = Mathf.Abs(along)  < gateHalfAlong  + bodyHalfAlong
                                  && Mathf.Abs(across) < gateHalfAcross + bodyHalfAcross;

                if (passedThrough || (!inZone && !overlapsGate)) SetOpen(false);
            }
            else if (inZone && along < 0f)
            {
                // 只从进得去的那一侧开。另一侧过来的玩家到不了这里 —— 他被挡在门外，
                // along 是正的，门不会为他开。
                SetOpen(true);
            }
        }

        // ==================================================================
        //  内部
        // ==================================================================

        private void SetOpen(bool open)
        {
            _open = open;
            if (_box != null) _box.enabled = !open;
            if (_indicator != null) _indicator.color = open ? _openTint : _idleTint;

            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, open);
        }

        private void Start()
        {
            // 读档恢复：这扇单向门在存档里是开的。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) SetOpen(true);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "open");

        /// <summary>玩家身体碰撞体的当前包围盒（世界坐标）。</summary>
        private bool TryGetPlayerBounds(out Bounds body)
        {
            if (_playerCollider == null || !_playerCollider.gameObject.activeInHierarchy)
            {
                body = default;
                return false;
            }

            body = _playerCollider.bounds;
            return true;
        }

        /// <summary>解析玩家。找不到时按 <see cref="ResolveRetrySeconds"/> 节流重试 ——
        /// 关卡流程可能比门晚实例化玩家。</summary>
        private void TryResolvePlayer()
        {
            if (_nextResolve > 0f)
            {
                _nextResolve -= UnityEngine.Time.fixedDeltaTime;
                return;
            }
            _nextResolve = ResolveRetrySeconds;

            if (_player == null)
            {
                var controller = FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
                if (controller != null) _player = controller.transform;
            }

            if (_player != null) _playerCollider = _player.GetComponent<Collider2D>();

            if (_playerCollider == null && !_warnedNoPlayer)
            {
                _warnedNoPlayer = true;
                Debug.LogWarning("[OneWayGate] 没找到玩家（场景里没有 PlayerController），" +
                                 "这扇门不会为任何人让路。玩家若是运行时才实例化的，本组件会自己补找。", this);
            }
        }

        /// <summary>
        /// 门的世界包围盒。<b>从碰撞体自己的 size / offset 算</b>，不读 <c>Collider2D.bounds</c> ——
        /// 放行时碰撞体是 disabled 的，那条路的返回值会塌掉（编辑模式的 gizmo 也还没 Awake 过）。
        /// </summary>
        /// <remarks>⚠️ 按**轴对齐**算（门是方块，<see cref="_pass"/> 也是四个正交方向之一）。
        /// 斜着摆的门不支持。</remarks>
        private Bounds GateBounds()
        {
            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_box == null) return new Bounds(transform.position, Vector3.zero);

            Vector3 scale = transform.lossyScale;
            var size = new Vector2(Mathf.Abs(_box.size.x * scale.x), Mathf.Abs(_box.size.y * scale.y));
            var center = (Vector2)transform.TransformPoint(_box.offset);
            return new Bounds(center, size);
        }

        /// <summary>包围盒在 <paramref name="axis"/> 方向上的半长（轴正交时就是对应的那个 extents）。</summary>
        private static float HalfExtent(Bounds bounds, Vector2 axis)
        {
            return Mathf.Abs(axis.x) * bounds.extents.x + Mathf.Abs(axis.y) * bounds.extents.y;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：改通过方向并立刻刷新白盒标签。</summary>
        public void SetPass(PassDirection pass)
        {
            _pass = pass;
            RefreshLabel();
        }

        /// <summary>只给编辑器工具用：接上白盒的另两处引用（状态色 / 标签）。</summary>
        public void SetWhitebox(SpriteRenderer indicator, TextMesh label)
        {
            _indicator = indicator;
            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(PassDirection pass)
        {
            switch (pass)
            {
                case PassDirection.ToLeft: return "单向门 ←";
                case PassDirection.ToUp:   return "单向门 ↑";
                case PassDirection.ToDown: return "单向门 ↓";
                default:                   return "单向门 →";
            }
        }

        private void OnValidate()
        {
            if (_reach < 0.1f) _reach = 0.1f;
            RefreshLabel();
        }

        /// <summary>把方向写进白盒标签。**只在文字真的变了才写** —— 否则每次脚本重编译
        /// 都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_pass);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            Bounds gate = GateBounds();
            Vector2 axis = Axis;

            // 实心的那一块：挡路的就是它。
            Gizmos.color = new Color(1f, 0.38f, 0.32f, 0.9f);
            Gizmos.DrawWireCube(gate.center, gate.size);

            // 判定区：玩家中心进到这里，门就开始让路。
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.5f);
            Gizmos.DrawWireCube(gate.center, gate.size + new Vector3(_reach * 2f, _reach * 2f, 0f));

            // 通过方向：从「进侧」指向门里的黄箭头。
            Gizmos.color = new Color(1f, 0.9f, 0.25f, 1f);
            float arm = HalfExtent(gate, axis) + 0.6f;
            var from = (Vector2)gate.center - axis * arm;
            var to   = (Vector2)gate.center + axis * arm;
            Gizmos.DrawLine(from, to);

            var perp = new Vector2(-axis.y, axis.x);
            const float head = 0.3f;
            Gizmos.DrawLine(to, to - axis * head + perp * head * 0.6f);
            Gizmos.DrawLine(to, to - axis * head - perp * head * 0.6f);
        }
    }
}
