using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 弹簧平台：**底端锚定、顶端站人**的竖直弹簧 —— 被压到「重力 = 弹簧力」的平衡位置后**停在那里**，
    /// 载重拿走才回弹到自然长度。
    /// </summary>
    /// <remarks>
    /// <para><b>它是什么</b>：一个**真正的弹簧-质量-阻尼系统**。压缩深度 <c>x</c>（米）是唯一的自由度，
    /// 运动方程 <c>m·x″ = F_载重 − k·x − c·x′</c> 每物理帧积分。
    /// 所以「压下去」和「弹回来」都不是插值：会加速、会振荡（阻尼小的时候），
    /// 最后停在 <c>x = F/k</c> 的平衡点上。</para>
    ///
    /// <para><b>压缩深度由胡克定律决定</b>：<c>x = 载重·g / k</c>，夹在 <c>0 ~ 行程</c>。
    /// 平台位置 = 锚点 + (自然长度 − x) —— 所以「多少力压多深」，不是压到一个固定的档位。
    /// k 调大压得浅，k 调小压得深；重力规则会改 g，于是同样的东西在不同规则下压得不一样深。</para>
    ///
    /// <para><b>三个阶段</b>：
    /// <list type="bullet">
    /// <item><b>Idle</b> —— 空载，停在自然长度；</item>
    /// <item><b>Compressing</b> —— 有载重，往平衡位置走，**然后停在那里**；</item>
    /// <item><b>Releasing</b> —— 载重消失，回弹到自然长度。</item>
    /// </list></para>
    ///
    /// <para><b>不带弹射</b>：早先的版本会在压到底后停一下、再把人弹飞，与静力学平衡是冲突的 ——
    /// 「载重消失」的那一刻东西已经离开了，没有可弹的对象。要弹跳板请另做一个组件
    /// （<see cref="ISpringSurface"/> 接口与玩家侧的 ⑥′ 还留着，实现它就能弹人）。</para>
    ///
    /// <para><b>承重靠形状扫掠判定，不靠接触回调</b>：与 <c>Tube</c> / <c>OneWayGate</c> 同一约定 ——
    /// 玩家是 Kinematic 刚体且 <c>useAttachedTriggers = false</c>，接触 / 触发回调不可靠。
    /// 每物理帧从平台形状向上扫 <see cref="_riderProbe"/> 那么远，把命中的
    /// **带刚体的东西的质量加起来** —— 玩家、铁箱、石头都算，静态地形没有刚体，压不动它。
    /// 质量规则改的就是 <c>Rigidbody2D.mass</c>，所以会自动生效。</para>
    ///
    /// <para><b>锚点按摆放位置推出来</b>：<c>锚点 = 摆放位置 − (0, 自然长度)</c>，开局只算一次。
    /// 拖动物体时锚点跟着搬 —— 不需要单独摆一个"底部固定点"。</para>
    ///
    /// <para><b>方向恒为世界竖直</b>：弹簧只做上下伸缩，锚点的 <c>x</c> 就是平台的 <c>x</c>。
    /// 不要旋转根物体 —— 位姿由代码按锚点解算，旋转只会被下一帧覆盖掉。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时物理已停步进（<c>Physics2D.simulationMode = Script</c>），
    /// 弹簧也必须停 —— 读 <c>TimeManager.IsPaused</c> 早退（单一真相是 TimeManager）；
    /// 积分用 <c>Time.fixedDeltaTime</c>，随 <c>Time.timeScale</c> 缩放 —— 世界变慢，弹簧也跟着变慢。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑥ 铁丝弹簧（可伸缩弹簧平台）。</para>
    ///
    /// <para><b>为什么要抢在玩家之前执行</b>：承接方（<c>ContactSensor</c> ①）读到的是
    /// <see cref="Velocity"/>，而它按 <see cref="IMovingSurface"/> 的契约必须是**本物理帧**的速度。
    /// 脚本执行顺序不定，若本组件排在玩家之后，玩家读到的就是**上一帧**的速度 ——
    /// 匀速的平移平台无所谓（速度不变，等价），但**弹簧是加速运动的**，加速段会逐帧落后一点点。
    /// 现在这点落后被玩家的贴地吸附盖住了（实测不可见），但高速平台（弹射器 / 快速电梯）会放大它。
    /// 排到 <c>RuleSystemDriver</c>（−100）之后、默认（0）之前。</para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class SpringPlatform : MonoBehaviour, IMovingSurface
    {
        /// <summary>弹簧当前处在哪一段。</summary>
        private enum Phase
        {
            /// <summary>空载，停在自然长度。</summary>
            Idle = 0,

            /// <summary>有人压着，正在往压缩长度走。</summary>
            Compressing = 1,

            /// <summary>载重消失，正在回弹到自然长度。</summary>
            Releasing = 2,
        }

        /// <summary>每隔多少「弧度」拆一个积分子步：<c>ω·h</c> 超过它就把这一步拆开。</summary>
        /// <remarks>半隐式欧拉在 <c>ω·dt &gt; 2</c> 时会发散。弹簧刚度可以调得很大（k=5000 → ω≈70，
        /// 50Hz 下 ω·dt≈1.4），不拆的话平台会**炸开**而不是压缩。0.2 远低于发散门槛，代价只是几个浮点乘。</remarks>
        private const float MaxSubstepAngle = 0.2f;

        /// <summary>子步上限：k 调到离谱时也不至于一帧算上千次。</summary>
        private const int MaxSubsteps = 8;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("弹簧")]
        [Tooltip("自然长度：空载时平台**中心**在锚点上方多高。\n" +
                 "锚点 = 物体摆放位置往下 _restLength —— **拖动物体就能整体搬走**，锚点跟着走。")]
        [SerializeField, Min(0.05f)] private float _restLength = 2f;

        [Tooltip("压到底时平台中心在锚点上方多高（硬限位）。\n" +
                 "它和自然长度之差 = 最大压缩行程，弹簧最多缩这么多。")]
        [SerializeField, Min(0f)] private float _compressedLength = 0.8f;

        [Header("物理")]
        [Tooltip("劲度系数 k（N/m）。**真的按胡克定律算**：载重 m 时压缩深度 x = m·g / k。\n" +
                 "k 越大越硬、压得越浅。默认 16：1 kg 的玩家压 0.61 m，8 kg 的铁箱直接压到底（1.2 m）。\n" +
                 "⚠️ 重力规则会改它 —— 重力越大，同样的东西压得越深。")]
        [SerializeField, Min(0.1f)] private float _springConstant = 16f;

        [Tooltip("阻尼（N·s/m）。越小越会来回振荡；临界阻尼 ≈ 2√(k·m)（k=16、m=1 时约 8）。\n" +
                 "默认 5 ≈ 0.6 倍临界：有弹性但很快停下。")]
        [SerializeField, Min(0f)] private float _damping = 5f;

        [Tooltip("空载回弹时用的等效质量（kg）。弹簧自己没重量，回弹快慢需要一个质量来算；\n" +
                 "它只影响**没有载重时**回原长的速度，不影响压缩深度。")]
        [SerializeField, Min(0.01f)] private float _unloadedMass = 1f;

        [Header("承重")]
        [Tooltip("承重探测条的高度（世界单位）：在平台上表面往上扫这么高的一条扁盒子。\n" +
                 "太矮会漏掉刚踩上来的一瞬，太高会把从上方掠过的东西一起算进来。")]
        [SerializeField, Min(0.05f)] private float _riderProbe = 0.35f;

        [Header("摩擦")]
        [Tooltip("平台表面的摩擦系数。1 = 普通地面；比 1 大更「涩」—— 站上去起步 / 刹车更抓地，\n" +
                 "小于 1 像冰面。运行时改这个值会立刻生效。\n" +
                 "⚠️ 摩擦规则仍然照常叠加：它在外层再乘一个倍率（增强 ×3 / 削弱 ×0.3 / 失效 ×0）。")]
        [SerializeField, Min(0f)] private float _friction = 1.5f;

        [Header("白盒")]
        [Tooltip("弹簧柱的美术。**会被沿 Y 轴拉伸**：缩放 y = 锚点到平台底面的距离。\n" +
                 "所以这张图要按「1 世界单位高」做（与白盒方块一致）。**接正式美术时清空玩法不读它。**")]
        [SerializeField] private Transform _springVisual;

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        /// <summary>参数模型版本。0 = 旧的「归一化压缩」那套（k=90 / 阻尼 12 是它的默认值）。</summary>
        /// <remarks>物理模型的 k 量纲完全不同（N/m），旧值拿过来玩家只压 0.1 米、看起来像坏掉了。
        /// 这里做一次性迁移：**只在还是旧默认值时**改，玩家自己调过的值不动。</remarks>
        [SerializeField, HideInInspector] private int _modelVersion = 0;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private Collider2D  _plankCollider;
        private readonly RaycastHit2D[] _riderHits  = new RaycastHit2D[8];
        private readonly Rigidbody2D[]  _loadBodies = new Rigidbody2D[8];

        private Phase _phase = Phase.Idle;

        /// <summary>本物理帧的载重（kg）：平台上方所有刚体的质量之和。</summary>
        private float _loadMass;

        /// <summary>压缩深度（米，0 ~ <see cref="Travel"/>）。0 = 自然长度。**唯一的自由度。**</summary>
        private float _squeeze;

        /// <summary>压缩深度的变化率（米/秒）。弹簧系统的另一半状态。</summary>
        private float _squeezeSpeed;

        /// <summary>世界锚点：开局按摆放位置算出来，之后不再变。</summary>
        /// <remarks>锚点**不单独配置** —— 它就是「摆放位置往下 <see cref="_restLength"/>」。
        /// 于是拖动物体时锚点跟着搬，不会像以前那样把平台拽回场景原点。</remarks>
        private Vector2 _anchor;

        /// <summary>锚点是否已经按摆放位置算过。开局只算一次 —— 之后平台自己会动，不能重算。</summary>
        private bool _anchored;

        /// <summary>本实例**独占**的物理材质 —— 平台表面的摩擦就写在它上面。</summary>
        /// <remarks>⚠️ 不能直接改 <c>Collider2D.sharedMaterial</c>：那是共享的，
        /// 场景里放三个弹簧、各想调不同摩擦时会互相覆盖。运行时 new 一份自己用。</remarks>
        private PhysicsMaterial2D _ownedMaterial;

        /// <summary>已经写进材质的摩擦值。与 <see cref="_friction"/> 不同才重新写。</summary>
        private float _appliedFriction;

        /// <summary>本物理帧的平台速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 ——
        /// 承载方加进自己的速度后，走出的距离与平台**逐帧相等**，不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>当前长度（锚点到平台中心的竖直距离）。</summary>
        public float CurrentLength => _restLength - _squeeze;

        /// <summary>压缩量 0 ~ 1（表现层要抖屏幕 / 播音效可以读它）。</summary>
        public float Compression => Travel > 0f ? Mathf.Clamp01(_squeeze / Travel) : 0f;

        /// <summary>压缩深度（米）。真实物理量：载重 m 时平衡在 m·g/k。</summary>
        public float Squeeze => _squeeze;

        /// <summary>本物理帧压在上面的总质量（kg）。没有任何刚体压着就是 0。</summary>
        public float LoadMass => _loadMass;

        /// <summary>当前阶段名（调试 / Inspector 显示用）。</summary>
        public string PhaseName => _phase.ToString();

        /// <summary>现在是不是在回弹。</summary>
        public bool IsReleasing => _phase == Phase.Releasing;

        /// <summary>底端锚点（世界坐标）。由摆放位置往下 <see cref="RestLength"/> 推出。</summary>
        public Vector2 AnchorPoint => _anchor;

        /// <summary>自然长度：空载时平台中心在锚点上方多高。</summary>
        public float RestLength => _restLength;

        /// <summary>压缩长度：压到底时平台中心在锚点上方多高。</summary>
        public float CompressedLength => _compressedLength;

        /// <summary>行程 = 自然长度 − 压缩长度。0 = 这个弹簧不会动。</summary>
        public float Travel => Mathf.Max(0f, _restLength - _compressedLength);

        /// <summary>
        /// 给定载重时的**静力学平衡压缩深度**（米）：<c>x = m·g / k</c>，夹在行程内。
        /// </summary>
        /// <remarks>重力读的是 <c>Physics2D.gravity</c>，所以重力规则会直接改变它 ——
        /// 世界变重，同样的东西压得更深；世界变轻，几乎压不动。</remarks>
        public float EquilibriumSqueeze(float mass)
        {
            if (mass <= 0f) return 0f;

            float g = Mathf.Max(0f, -Vector2.Dot(Physics2D.gravity, Vector2.up));
            return Mathf.Clamp(mass * g / Mathf.Max(_springConstant, 0.01f), 0f, Travel);
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _plankCollider = GetComponent<Collider2D>();
            MigrateLegacyTuning();
            ConfigureBody();
            ApplyFriction();

            // 每次进入 play 都按**当前摆放位置**重新算锚点。
            // ⚠️ 本工程的 Enter Play Mode 不一定重载域，`_anchored` 可能残留上一次 play 的值。
            _anchored = false;
            CaptureAnchor();

            Rebuild();
        }

        private void OnEnable()
        {
            // ⚠️ **编辑模式什么都不做**（除了刷新柱子）。
            //    OnEnable 在编辑模式下也会跑 —— 以前这里会调 Rebuild()，它用锚点写
            //    `_body.position`，于是**一拖动物体就被搬回锚点**（手动加的组件锚点默认正好是
            //    (0,0) = 场景原点）。这正是「拖动时固定在原点」的真正原因。
            if (!Application.isPlaying)
            {
                UpdateSpringVisual();
                return;
            }

            // 重开一局 / 对象池取出时也要回到自然长度，不能接着上次的压缩量走。
            if (_body == null)
            {
                _body = GetComponent<Rigidbody2D>();
                _plankCollider = GetComponent<Collider2D>();
                ConfigureBody();
            }
            ApplyFriction();
            CaptureAnchor();
            Rebuild();
        }

        /// <summary>
        /// 按**摆放位置**推出世界锚点：锚点在平台正下方 <see cref="_restLength"/> 处。
        /// </summary>
        /// <remarks>
        /// <para>锚点只算一次：之后再调它就会跟着平台自己动，越跑越远。</para>
        /// <para>⚠️ 这正是「拖动物体时弹簧不会弹回原点」的实现 —— 以前锚点是独立的世界坐标字段，
        /// 拖物体它不跟着走，运行时平台就被拽回锚点（默认是 <c>(0,0)</c> = 场景原点）。</para>
        /// </remarks>
        private void CaptureAnchor()
        {
            if (_anchored) return;

            _anchor = (Vector2)transform.position - Vector2.up * _restLength;
            _anchored = true;
        }

        private void OnDestroy()
        {
            // 运行时 new 出来的材质不归任何资产管，跟着组件一起清掉。
            if (_ownedMaterial == null) return;

            if (_plankCollider != null && _plankCollider.sharedMaterial == _ownedMaterial)
            {
                _plankCollider.sharedMaterial = null;
            }

            Destroy(_ownedMaterial);
            _ownedMaterial = null;
        }

        private void FixedUpdate()
        {
            // 运行期改摩擦立刻生效（调试时常这么干）。值没变时只是一次浮点比较。
            ApplyFriction();

            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定（与 MovingPlatform / Tube 同一处理）。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            _loadMass = ProbeLoadMass();
            Step(dt, _loadMass);
            ApplyPosition(dt);
            UpdateSpringVisual();
        }

        // ==================================================================
        //  状态机 + 积分
        // ==================================================================

        /// <summary>推进状态机，再按**牛顿第二定律**积分压缩深度。</summary>
        private void Step(float dt, float loadMass)
        {
            bool loaded = loadMass > 0f;

            // 沿压缩方向的重力加速度（世界重力 · 锚点方向）。重力反转时为负 → 取 0，
            // 东西不会往「天空」那边压弹簧。
            float gAlong = Mathf.Max(0f, -Vector2.Dot(Physics2D.gravity, Vector2.up));

            // 载重压下来的力（N）。静态平衡时它正好等于弹簧力 k·x —— 这就是压缩深度的来源。
            float loadForce = loadMass * gAlong;

            AdvancePhase(loaded);

            // 运动方程：m·x″ = F_载重 − k·x − c·x′
            // 平衡点 x = F_载重 / k，所以「多少力就压多深」，不是归一化到某个固定位置。
            float mass = loaded ? loadMass : _unloadedMass;
            Integrate(dt, loadForce, Mathf.Max(mass, 0.01f));
        }

        private void AdvancePhase(bool loaded)
        {
            switch (_phase)
            {
                case Phase.Idle:
                    if (loaded) _phase = Phase.Compressing;
                    break;

                case Phase.Compressing:
                    // 只要还有载重就停在平衡位置 —— **没有自动释放**。
                    if (!loaded) _phase = Phase.Releasing;
                    break;

                case Phase.Releasing:
                    // 回弹途中又有东西压上来 → 重新压。
                    if (loaded)
                    {
                        _phase = Phase.Compressing;
                        break;
                    }
                    if (_squeeze <= 0f && _squeezeSpeed <= 0f) _phase = Phase.Idle;
                    break;
            }
        }

        /// <summary>
        /// 半隐式欧拉积分一步：<c>x″ = (F_载重 − k·x − c·x′) / m</c>。
        /// </summary>
        /// <remarks>
        /// <para><b>这就是胡克定律的动力学形式</b>。载重恒定、没有阻尼时它会在
        /// <c>x = F/k</c> 附近来回振荡；有阻尼时收敛到那个平衡点。</para>
        /// <para>两端都有**硬限位**（<c>0 ~ 行程</c>）：平台不会缩进锚点里，也不会被"拉"过自然长度。
        /// 撞到限位时把朝外的速度归零，就是「能量被限位吃掉」。</para>
        /// <para>刚度大 / 质量小时按 <see cref="MaxSubstepAngle"/> 拆子步，否则会数值发散。</para>
        /// </remarks>
        private void Integrate(float dt, float loadForce, float mass)
        {
            float omega = Mathf.Sqrt(_springConstant / mass);
            int substeps = Mathf.Clamp(
                Mathf.CeilToInt(omega * dt / MaxSubstepAngle), 1, MaxSubsteps);
            float h = dt / substeps;
            float travel = Travel;

            for (int i = 0; i < substeps; i++)
            {
                float accel = (loadForce - _springConstant * _squeeze - _damping * _squeezeSpeed) / mass;

                _squeezeSpeed += accel * h;
                _squeeze      += _squeezeSpeed * h;

                if (_squeeze <= 0f)
                {
                    _squeeze = 0f;
                    if (_squeezeSpeed < 0f) _squeezeSpeed = 0f;
                }
                else if (_squeeze >= travel)
                {
                    _squeeze = travel;
                    if (_squeezeSpeed > 0f) _squeezeSpeed = 0f;
                }
            }
        }

        /// <summary>把当前长度写成位置，并顺带算出本物理帧的速度。</summary>
        private void ApplyPosition(float dt)
        {
            Vector2 previous = _body.position;
            Vector2 target   = new Vector2(_anchor.x, _anchor.y + CurrentLength);

            Velocity = (target - previous) / dt;
            _body.MovePosition(target);
        }

        // ==================================================================
        //  承重探测
        // ==================================================================

        /// <summary>平台上方压着多少质量（kg）。没东西压着就是 0。</summary>
        /// <remarks>
        /// <para><b>判据是「有没有刚体」，不是层。</b>能压动弹簧的是**质量**，而质量住在
        /// <c>Rigidbody2D.mass</c> 上：静态地形没有刚体 —— 压不动它；
        /// 玩家（Kinematic）、铁箱 / 石头（Dynamic）都有刚体，一律按质量算。</para>
        /// <para>⚠️ <b>不能按层筛</b>：铁箱和地形都在 <c>Ground</c> 层，按层放行会把地形也算成载重；
        /// 而只勾 <c>Default</c> 又会漏掉铁箱。</para>
        /// <para>⚠️ <b>必须用「平台形状向上扫」，不能头顶罩一个扁盒子。</b>
        /// 盒子的 x 范围比平台宽一圈，弹簧被压到底时盒子会降到旁边站着的敌人 / 铁箱的高度，
        /// 框边缘扫到一点点就算载重 —— 于是人走了、载重还在，弹簧**永远压着不回弹**（实测踩过：
        /// 敌人在平台外 0.9 单位处就中招）。形状扫掠只认**真的压在平台正上方**的东西。</para>
        /// <para>同一个物体的多个碰撞体（身体 + 受击箱等）只按刚体算一次，不重复累加。</para>
        /// <para>质量规则写的就是 <c>Rigidbody2D.mass</c>，所以「铁箱 × 削弱」会直接变成压不动的轻箱子，
        /// 不需要本组件做任何特判。</para>
        /// <para>用**形状扫掠**而不是接触 / 触发回调：玩家是 Kinematic 刚体且
        /// <c>useAttachedTriggers = false</c>，回调不可靠（与 <c>Tube</c> / <c>OneWayGate</c> 同一约定）。</para>
        /// </remarks>
        private float ProbeLoadMass()
        {
            if (_plankCollider == null) return 0f;

            // 不按层筛：能压动它的只有带刚体的东西，地形天然被排除。
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };

            // 从平台形状向上扫：距离内与平台正上方有重叠的碰撞体才会命中。
            // ⚠️ ignoreSiblingColliders 默认 true —— 同一刚体上的其他碰撞体自动排除。
            int count = _plankCollider.Cast(Vector2.up, filter, _riderHits, _riderProbe);

            float plankTop = _plankCollider.bounds.max.y;
            int bodyCount = 0;
            float mass = 0f;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _riderHits[i].collider;
                if (hit == null) continue;
                if (hit.transform.IsChildOf(transform)) continue;      // 别把自己算进去

                // 只认「站上来」的东西：底部不能明显低于平台顶面。
                // ⚠️ 这条不能省 —— 站在平台**旁边**的敌人，碰撞体可能伸进平台的水平范围，
                //    向上扫会命中它（distance 0）。不看底部的话，弹簧会一直以为有人压着、
                //    永远不回弹（实测：平台外 0.9 单位的 Dummy，压到底时正好被扫到）。
                if (hit.bounds.min.y < plankTop - _riderProbe * 0.5f) continue;

                Rigidbody2D body = hit.attachedRigidbody;
                if (body == null) continue;                             // 静态地形：没有质量，压不动

                // 同一个刚体的碰撞体一律不算 —— 比 IsChildOf 更强的排除：
                // 碰撞体挂在别的物体上、但共用本平台的刚体时，前者挡不住。
                if (body == _body) continue;

                if (Contains(bodyCount, body)) continue;                // 别的物体的多个碰撞体只算一次
                if (bodyCount >= _loadBodies.Length) break;

                _loadBodies[bodyCount++] = body;
                mass += Mathf.Max(0f, body.mass);
            }

            return mass;
        }

        /// <summary>这个刚体是否已经在前 <paramref name="count"/> 个里了（线性查找，最多 8 个）。</summary>
        private bool Contains(int count, Rigidbody2D body)
        {
            for (int i = 0; i < count; i++)
            {
                if (_loadBodies[i] == body) return true;
            }
            return false;
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>回到自然长度、清空状态。开局 / 对象池取出时调用。</summary>
        private void Rebuild()
        {
            _phase        = Phase.Idle;
            _squeeze      = 0f;
            _squeezeSpeed = 0f;

            // 载重也要清：重开时若东西正好压在平台上，留着上一局的旧值会让第一帧按它算力。
            _loadMass = 0f;

            Velocity = Vector2.zero;

            if (_body != null)
            {
                _body.position = new Vector2(_anchor.x, _anchor.y + _restLength);
            }

            UpdateSpringVisual();
        }

        /// <summary>编辑期用的锚点：总是按**当前摆放位置**推出来的那个。</summary>
        /// <remarks>⚠️ 编辑模式不要读 <see cref="_anchor"/> —— 本工程的 Enter Play Mode 不一定重载域，
        /// 退出 play 后那个字段还是上一次运行的值，拖动物体时柱子会错位。</remarks>
        private Vector2 EditorAnchor()
            => Application.isPlaying && _anchored
                ? _anchor
                : (Vector2)transform.position - Vector2.up * _restLength;

        /// <summary>
        /// 把弹簧柱按当前长度拉伸。**锚点不动、柱顶跟着平台底面走。**
        /// </summary>
        /// <remarks>
        /// <para>用**根的局部坐标**而不是世界坐标：根（平台）每个物理帧都在动，
        /// 写成世界坐标的话柱子会留在原地、和平台分家。换算成局部坐标，它才会跟着走。</para>
        /// <para>柱子是 1 世界单位高的图（与白盒方块一致），所以缩放 y 直接就是高度。</para>
        /// </remarks>
        private void UpdateSpringVisual()
        {
            if (_springVisual == null) return;

            // ⚠️ 编辑模式下 `transform` 可能刚被搬过（生成器摆位 / 改参数），
            //    而 `Collider2D.bounds` 是**缓存值**，不 SyncTransforms 就会读到搬家前的包围盒 ——
            //    算出来的柱高会莫名其妙多出一段（实测多出整整一个位移量）。
            //    运行时不用同步：物理步刚跑过，bounds 本来就是新的。
            if (!Application.isPlaying) Physics2D.SyncTransforms();

            // 编辑模式下 Awake 没跑过，_plankCollider 还是 null —— 就地补一次，
            // 免得摆位时柱子高度按「平台中心」算、和平台面糊在一起。
            Collider2D plank = _plankCollider != null ? _plankCollider : GetComponent<Collider2D>();
            float plankBottomY = plank != null ? plank.bounds.min.y : transform.position.y;
            Vector2 anchor = EditorAnchor();
            float height = Mathf.Max(plankBottomY - anchor.y, 0.05f);

            _springVisual.localPosition = new Vector3(anchor.x - transform.position.x,
                                                      anchor.y - transform.position.y + height * 0.5f,
                                                      0f);

            Vector3 scale = _springVisual.localScale;
            scale.y = height;
            _springVisual.localScale = scale;
        }

        /// <summary>把 <see cref="_friction"/> 写进一份**本实例独占**的物理材质。</summary>
        /// <remarks>
        /// <para><b>为什么必须独占</b>：<c>Collider2D.sharedMaterial</c> 是共享的 —— 直接改它，
        /// 所有引用同一材质的物体会一起变。场景里放三个弹簧、各想调不同摩擦时就互相覆盖了。</para>
        /// <para><b>为什么非得挂在物理材质上</b>：玩家侧读地面摩擦走的是
        /// <c>Collider2D.sharedMaterial.friction</c>（见 <c>ContactSensor.ProbeGround</c>），
        /// 没有材质就一律按 1（普通地面）算 —— 这个参数没有别的落点。</para>
        /// <para><b>与摩擦规则不冲突</b>：规则只在外面乘一个倍率（<c>VelocitySolver</c> 里的
        /// <c>GroundFriction × FrictionScale</c>），不碰这里的材质。所以「弹簧 × 摩擦增强」
        /// 是 1.5 × 3 = 4.5，仍然成立。</para>
        /// </remarks>
        private void ApplyFriction()
        {
            if (_plankCollider == null) return;

            if (_ownedMaterial == null)
            {
                _ownedMaterial = new PhysicsMaterial2D(name + " (Friction)");
                _ownedMaterial.bounciness = 0f;     // 平台本身不弹，弹射归弹簧管
                _plankCollider.sharedMaterial = _ownedMaterial;
                _appliedFriction = float.NaN;       // NaN 与任何值都不相等：强制写一次
            }

            if (Mathf.Approximately(_appliedFriction, _friction)) return;

            _ownedMaterial.friction = Mathf.Max(0f, _friction);
            _appliedFriction = _friction;
        }

        private void ConfigureBody()
        {
            // Kinematic：位移完全由代码决定，重力 / 摩擦都不参与。
            _body.bodyType      = RigidbodyType2D.Kinematic;
            _body.gravityScale  = 0f;
            _body.linearDamping = 0f;
            _body.constraints   = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值弹簧会一格一格跳（压下去的动画最明显）。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒标签的引用。</summary>
        public void SetWhitebox(Transform springVisual, TextMesh label)
        {
            _springVisual = springVisual;
            _label = label;
            RefreshLabel();
            UpdateSpringVisual();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float springConstant, float restLength)
        {
            return "弹簧 k" + springConstant.ToString("0.#")
                 + " · " + restLength.ToString("0.#") + "m";
        }

        private void OnValidate()
        {
            // 压缩长度超过自然长度没有意义（弹簧变成往上顶的杆），就地夹住。
            if (_compressedLength > _restLength) _compressedLength = _restLength;
            if (_restLength < 0.05f) _restLength = 0.05f;
            if (_riderProbe < 0.05f) _riderProbe = 0.05f;

            MigrateLegacyTuning();
            RefreshLabel();

            if (Application.isPlaying) return;

            // 编辑期改了长度 / 拖了位置：柱子跟着更新。锚点是按摆放位置推出来的，所以拖动物体
            // 柱子会一起搬走（这正是「不会弹回原点」的那条）。
            UpdateSpringVisual();
        }

        /// <summary>
        /// 旧参数模型（归一化压缩）→ 物理模型的一次性迁移。
        /// </summary>
        /// <remarks>只改**旧默认值**（k=90 / 阻尼 12），玩家自己调过的数不动。</remarks>
        private void MigrateLegacyTuning()
        {
            if (_modelVersion >= 1) return;

            if (Mathf.Approximately(_springConstant, 90f)) _springConstant = 16f;
            if (Mathf.Approximately(_damping, 12f)) _damping = 5f;

            _modelVersion = 1;
        }

        /// <summary>把当前的 k / 自然长度写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_springConstant, _restLength);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            var plank = GetComponent<Collider2D>();
            Vector2 anchor = EditorAnchor();

            // 弹簧轴：从锚点到自然长度处。
            Gizmos.color = new Color(0.85f, 0.75f, 0.95f, 0.9f);
            Gizmos.DrawLine(anchor, new Vector3(anchor.x, anchor.y + _restLength, 0f));

            // 行程范围：压缩长度那一端的标记。
            Gizmos.color = new Color(1f, 0.45f, 0.45f, 0.9f);
            Gizmos.DrawWireSphere(new Vector3(anchor.x, anchor.y + _compressedLength, 0f), 0.12f);

            // 锚点。
            Gizmos.color = new Color(0.55f, 1f, 0.75f, 0.95f);
            Gizmos.DrawWireSphere(anchor, 0.18f);

            if (Application.isPlaying)
            {
                // 播放中：当前位置 + 承重探测条。
                Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
                Gizmos.DrawWireSphere(transform.position, 0.2f);
            }

            if (plank == null) return;

            Bounds b = plank.bounds;
            var center = new Vector3(b.center.x, b.max.y + _riderProbe * 0.5f, 0f);
            // 和实际探测一致：平台**本身**的宽度，不是外扩一圈的盒子。
            var size   = new Vector3(b.size.x, _riderProbe, 0f);
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.5f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
