using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Interaction;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 玩家移动控制。<b>只做编排</b>：接线、按顺序调用八个职能类、把它们的结论翻译成事件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是 Kinematic</b>（架构决策 D1）：移动完全由代码决定，手感可控。
    /// 代价是引擎不再替我们挡住静态地形 —— <c>MovePosition</c> 自己不处理碰撞 ——
    /// 所以位移必须走 <c>Rigidbody2D.Slide</c>。刚体配置与 Slide 都归
    /// <see cref="MotionMover"/>。</para>
    /// <para><b>重力自己积分</b>，并且直接读 <c>Physics2D.gravity</c>：重力规则改的是这个全局值，
    /// 本类无需改一行就会跟着变 —— 「规则作用于世界本身」就是这样落地的。
    /// <b>风场同理</b>：外部组件（<c>WindZone</c>）报力，<see cref="WindSensor"/> 除以质量变成加速度，
    /// 与重力一起进解算 —— 这是 Kinematic 实体接受外力的唯一通道（引擎的力对它无效）。</para>
    /// <para><b>八个职能类</b>（都住在本目录，都是普通 C# 类，构造函数注入）：</para>
    /// <list type="bullet">
    /// <item><see cref="PlayerInput"/> —— 输入与门禁，产出本帧意图</item>
    /// <item><see cref="ContactSensor"/> —— 接地 / 左右墙 / 脚下摩擦 / 「上」方向</item>
    /// <item><see cref="WindSensor"/> —— 我在哪个风场里、受多大力（力 ÷ 质量 → 加速度）</item>
    /// <item><see cref="JumpSolver"/> —— 土狼时间、跳跃缓冲、三档起跳优先级</item>
    /// <item><see cref="DashSolver"/> —— 冲刺计时、次数、冷却、方向</item>
    /// <item><see cref="AttackSolver"/> —— 攻击连段三阶段（判定本身留在本类）</item>
    /// <item><see cref="VelocitySolver"/> —— 速度解算，**无状态纯函数**</item>
    /// <item><see cref="MotionMover"/> —— **唯一持有速度的地方**，位移与碰撞响应</item>
    /// </list>
    /// <para>依赖是单向的：本类认识它们，它们互相不认识，只通过「传进去的参数、返回来的结果」交接。
    /// 所以规则读值（<see cref="FrictionScale"/> / <see cref="FrictionInverted"/>）留在本类 ——
    /// <see cref="VelocitySolver"/> 因此**不认识规则系统**。</para>
    /// <para><b>边沿走返回，状态走属性</b>：一次性动作（起跳、起冲、挥刀、落地）靠返回值带出来，
    /// 持续条件（接地、剩几段跳、连段打到第几刀）走只读属性。</para>
    /// <para>⚠️ 类名与公开面**冻结**：三个资产（<c>LevelRuntime.prefab</c> /
    /// <c>Test_RuleSystem.unity</c> / <c>Test_RuleWheel.unity</c>）序列化了本组件，
    /// <c>PlayerAnimationDriver</c> / <c>P3DebugOverlay</c> / <c>RuleWheelPresenter</c> 等
    /// 六个文件引用它。重构只搬内脏，不换壳。</para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class PlayerController : MonoBehaviour, IKnockbackReceiver, IRuleSelectionInputGate, IMomentumSource
    {
        private readonly HashSet<object> _movementHolders = new HashSet<object>();
        public bool MovementHeld => _movementHolders.Count > 0;
        /// <summary>拥抱只限制自主移动，保留挥剑反击；多只敌人各自释放自己的限制。</summary>
        public void SetMovementHeld(object owner, bool held)
        {
            if (held) _movementHolders.Add(owner);
            else _movementHolders.Remove(owner);
        }
        // ==================================================================
        //  序列化字段
        // ==================================================================
        //
        // ⚠️ 字段名与顺序**不要动**：三个资产里存着这些值的现值，
        //    改名等于让它们全部回落到代码默认值。

        [Header("输入")]
        [Tooltip("拖入 Assets/_Project/Settings/InputSystem_Actions.inputactions")]
        [SerializeField] private InputActionAsset _inputActions;

        [Header("移动")]
        [Tooltip("水平最大速度。按住方向键时速度会朝这个值加速（不是瞬间到）。")]
        [SerializeField] private float _moveSpeedMax = 8f;

        [Tooltip("**攻击期间**（含收招 / CD）的水平最大速度。出刀时人走不快，打击才有分量。" +
                 "见 IsAttacking：轻击连段与重击都算。")]
        [SerializeField] private float _attackMoveSpeedMax = 4f;

        [Tooltip("地面水平加速度。到全速的时间 ≈ 最大速度 / 本值（200 → 约 0.04 秒，近乎瞬时）。")]
        [SerializeField] private float _groundAccel = 200f;

        [Tooltip("**低摩擦地面（冰面）**上的水平加速度。实际加速按脚下抓地系数在「本值」与 `_groundAccel` 之间插值：" +
                 "普通地面（摩擦 1）用 `_groundAccel`，完全打滑（摩擦 0）用本值，中间线性过渡。" +
                 "减速永远比这更慢 —— 冰面上是「起得慢、停得更慢」。")]
        [SerializeField] private float _lowFrictionGroundAccel = 40f;

        [Tooltip("起跳速度。跳跃高度 = 本值² / (2 × 有效上升重力)。")]
        [SerializeField] private float _jumpSpeed    = 13.6f;

        [Tooltip("空中还能再跳几次。0 = 只有一段跳；1 = 二段跳。" +
                 "它是**实体自己的能力**（本组件对环境里任何实体都可用），不是玩家特权。")]
        [SerializeField, Range(0, 3)] private int _maxAirJumps = 1;

        [Tooltip("空中段的起跳速度。比第一段略弱 —— 读起来像「再补一下」而不是「再来一次」。" +
                 "两段一样高会让二段跳显得像没落地过。")]
        [SerializeField] private float _airJumpSpeed = 12.2f;

        [Tooltip("玩家自身的重力倍率（在规则改过的全局重力之上再乘一次）。" +
                 "它与跳跃速度共同决定「跳跃高度」和「上升用时」：高度 = v²/(2g)，用时 = v/g。" +
                 "想更快就同时加大两者（高度不变、时间变短）。")]
        [SerializeField] private float _gravityScale = 3.83f;

        [Tooltip("下落时的额外重力倍率 —— 落得比升得快，滞空更紧凑。1 = 上下对称。")]
        [SerializeField, Range(1f, 4f)] private float _fallGravityMultiplier = 2f;

        [Tooltip("下落速度上限（终端速度）。防止长距离下落越来越快、难以阅读。")]
        [SerializeField] private float _maxFallSpeed = 30f;

        [Tooltip("地面减速度基准。实际减速度 = 本值 × 脚下地面材质的摩擦系数 × 摩擦规则缩放。" +
                 "所以「冰面」= 给地面挂一个摩擦很小的 PhysicsMaterial2D。")]
        [SerializeField] private float _frictionDecel = 40f;

        [Tooltip("空中水平加速度：能左右改向，但不瞬移。0 = 空中完全锁死横向速度；" +
                 "调到 1000 左右约等于「空中和地面一样跟手」。")]
        [SerializeField] private float _airAccel = 40f;

        [Tooltip("空中无输入时的水平阻力。0 = 空中完整保持动量；调大就是空中能急停。")]
        [SerializeField] private float _airDrag = 3f;

        [Tooltip("松开跳跃键时向上速度保留的比例 —— 可变跳跃高度。1 = 按固定高度跳。")]
        [SerializeField, Range(0.1f, 1f)] private float _jumpCutMultiplier = 0.45f;

        [Tooltip("土狼时间：走出平台边缘后仍可起跳的宽限秒数。")]
        [SerializeField] private float _coyoteTime = 0.1f;

        [Tooltip("跳跃缓冲：落地之前提前按下的跳跃仍然生效的秒数。")]
        [SerializeField] private float _jumpBuffer = 0.12f;

        [Header("冲刺")]
        [Tooltip("冲刺速度。远高于 _moveSpeedMax，要的就是爆发感。")]
        [SerializeField] private float _dashSpeed = 26f;

        [Tooltip("冲刺持续秒数。冲刺距离 = 本值 × _dashSpeed（26 × 0.18 ≈ 4.7 米）。")]
        [SerializeField] private float _dashDuration = 0.18f;

        [Tooltip("冲刺结束时保留多少速度。1 = 冲完像被弹射出去，0.35 = 收得住。")]
        [SerializeField, Range(0f, 1f)] private float _dashExitSpeedScale = 0.35f;

        [Tooltip("冲刺次数上限，落地回满。0 = 没有冲刺。")]
        [SerializeField, Range(0, 3)] private int _maxDashes = 1;

        [Tooltip("两次冲刺之间的最小间隔。冲刺进行中再按也无效。")]
        [SerializeField] private float _dashCooldown = 0.25f;

        [Header("蹬墙跳")]
        [Tooltip("**贴墙能力总开关**（贴墙下滑 + 蹬墙跳）。这是**这个实体自己的能力**，" +
                 "不是玩家特权 —— 敌人配一下就有、关掉就没有。")]
        [SerializeField] private bool _wallClimbEnabled = true;

        [Tooltip("横向墙面探测距离（米）。**全高探测**，用于顶墙分量预夹（防止 Slide 把整笔位移取消）。")]
        [SerializeField] private float _wallProbeDistance = 0.08f;

        [Tooltip("贴墙能力的**中段圆探针**半径（米）。探针位置 = 主碰撞体的中段高度、左右两条边缘。\n" +
                 "只认中段的接触 —— 矮台 / 台阶 / 头顶横梁都不算墙，所以不会在平台上误触发贴墙。\n" +
                 "与上面那条是两回事：那条是全高探测，管预夹。")]
        [SerializeField] private float _wallClimbProbeRadius = 0.06f;

        [Tooltip("贴墙时的下滑速度上限。远低于 _maxFallSpeed —— 贴墙是「缓慢下滑」，不是「挂在墙上」。" +
                 "调大 = 贴墙停留时间短、掉得快；调小 = 有更多时间看准再蹬。")]
        [SerializeField] private float _wallSlideSpeed = 6f;

        [Tooltip("贴到墙那一瞬间，下落速度被「墙面摩擦」吃掉的快慢（单位/秒²）。" +
                 "调大 = 一碰墙就被截停（越接近瞬间夹住）；调小 = 先冲下去一点再被拉住。")]
        [SerializeField] private float _wallSlideImpactDecel = 60f;

        [Tooltip("【朝墙外按】时弹离墙壁的水平速度 —— 「跳出去」用。")]
        [SerializeField] private float _wallJumpPushSpeed = 9f;

        [Tooltip("【朝墙按 / 不按】时的推力 —— 「爬升」用。**必须足够小**，" +
                 "小到方向键能在人离开墙之前把它拉回来，否则一面墙爬不上去（会越跳越远）。")]
        [SerializeField] private float _wallClimbPushSpeed = 4f;

        [Tooltip("蹬墙后的向上速度。**调大它是最划算的提升** —— 弧线更高、滞空更久，" +
                 "水平距离会跟着白拿，比单纯加水平推力更接近《空洞骑士》的质感。")]
        [SerializeField] private float _wallJumpUpSpeed = 15f;

        [Header("碰撞")]
        [Tooltip("阻挡移动、并用于接地判定的层。地形与墙体都放这里。")]
        [SerializeField] private LayerMask _solidLayer;
        [Tooltip("仅阻挡身体移动的层，不授予接地和蹬墙。默认 Enemy（层 9）。")]
        [SerializeField] private LayerMask _bodyBlockLayer = 1 << 9;

        [Tooltip("接地探测距离（米）。")]
        [SerializeField] private float _groundProbeDistance = 0.08f;

        [Tooltip("贴地吸附距离：下坡时把角色按回地面，0 = 关闭。")]
        [SerializeField] private float _surfaceAnchorDistance = 0.05f;

        [Tooltip("可沿其滑行的最大坡面角度（引擎限制 0~90）。**必须给 90**。" +
                 "给小于 90 的值时，竖直墙（90°）会把整个滑动取消 —— 不是只挡住顶墙那一份，" +
                 "而是把沿墙下滑的竖直分量一起丢掉，表现为「速度满速但位置不动」。")]
        [SerializeField] private float _surfaceSlideAngle = 90f;

        [Header("风场")]
        [Tooltip("风场所在的层（WindZone 挂在上面）。留空 = 这个实体不响应风；" +
                 "和 _solidLayer 一样是**实体自己的能力开关**。")]
        [SerializeField] private LayerMask _windLayer;

        [Header("击退")]
        [Tooltip("外部冲量每秒的衰减量。")]
        [SerializeField] private float _knockbackDecay = 30f;

        [Header("受击节奏")]
        [Tooltip("角色命中定格的游戏秒数；不改变全局时间倍率。")]
        [SerializeField, Range(0f, .12f)] private float _hurtImpactPause = .055f;
        [Tooltip("定格之后的短硬直；期间保留重力、风和碰撞，阻止自主动作。")]
        [SerializeField, Range(.1f, .5f)] private float _hurtRecovery = .22f;
        [Tooltip("受击硬直内的外部击退衰减；较慢收速，让接触伤害也能弹开约一个身位。")]
        [SerializeField, Min(0f)] private float _hurtKnockbackDecay = 20f;

        [Header("推动（重物）")]
        /// <summary>
        /// 推重物时施加的力（牛顿）。0 = 这个实体不会推。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 这里报的是**力**，不是速度 —— 物体那边除以自己的质量得到加速度
        /// （与 <see cref="WindSensor"/> 同一条分工）。所以「推不推得动」由箱子的
        /// <c>Rigidbody2D.mass</c> 与地面摩擦决定，而质量规则改的正是那个质量。</para>
        ///
        /// <para><b>判据：<c>F &gt; μ · m · g</c></b>（μ = 箱子与地面的合并摩擦，默认 0.5）。
        /// 取 60 N 时：8 kg 的铁箱推得动（净 21 N，约 0.27 g —— 缓慢但明确地在挪）；
        /// 「质量 × 增强」（3 倍 → 24 kg）要 118 N 才推得动，于是**变成一堵墙**；
        /// 「质量 × 削弱 / 失效」（0.3 / 0.05 倍）则是一推就走、一推就飞。</para>
        /// </remarks>
        [Tooltip("推重物时施加的**力**（牛顿）。0 = 这个实体不会推。")]
        [SerializeField] private float _pushStrength = 60f;

        /// <summary>
        /// 推动探测距离（米）。
        /// </summary>
        /// <remarks>
        /// ⚠️ 必须**明显大于玩家停在墙前的距离**。⑨′ 的顶墙预夹会在离墙面
        /// <c>_wallProbeDistance</c>（0.08）时就清掉横向速度，实测玩家会停在箱子前约 0.10 米 ——
        /// 探测距离贴着这个数给的话（第一版是 0.12），换个接近速度或蹭着拐角就会**够不着箱子**
        /// 而推不动。0.15 留出 50% 余量，代价只是「离着一点也能推」，看不出差别。
        /// </remarks>
        [Tooltip("推动探测距离（米）。按住方向顶上去，这个距离内有可推物体就施力。")]
        [SerializeField] private float _pushProbeDistance = 0.15f;

        [Header("能力解锁")]
        [Tooltip("这个实体当前已解锁的能力。\n" +
                 "代码默认给 All，保证现有测试场景不被锁住；正式开局由 PlayerProgress.StartNewGame() 设成 None。")]
        [SerializeField] private PlayerAbility _unlockedAbilities = PlayerAbility.All;

        [Header("状态")]
        [Tooltip("同一物体上的状态组件；暂停状态从这里读。留空则自动取。")]
        [SerializeField] private EntityStatus _status;

        [Header("攻击")]
        [SerializeField] private CombatSystem _combat;
        [SerializeField] private float     _attackDamage    = 10f;
        [SerializeField] private float     _attackKnockback = 6f;
        [SerializeField] private Vector2   _attackBoxSize   = new Vector2(1.2f, 1f);
        [SerializeField] private float     _attackOffset    = 0.8f;

        // ------------------------------------------------------------------
        //  投掷（2026-10-18 原型：纸飞机飞刀）
        // ------------------------------------------------------------------

        [Header("投掷")]
        [Tooltip("飞刀预制体（Prefabs/PaperPlaneKnife）。留空 = 这个实体不会投掷。")]
        [SerializeField] private ThrownKnife _knifePrefab;

        [Tooltip("出手点相对身体中心的水平距离（米）。给少了会从身体里长出来。")]
        [SerializeField] private float _knifeOffset = 0.55f;

        [Tooltip("出手点的高度偏移（米）。0 = 身体中心。")]
        [SerializeField] private float _knifeHeight = 0.15f;

        [Tooltip("飞刀初速度（米/秒）。与重力合起来决定射程与弧线。")]
        [SerializeField] private float _knifeSpeed = 16f;

        // 飞刀的伤害不在这里配：按策划 5.2 公式由 ThrownKnife 用「质量 × 速度档位」现算
        // （D_raw = 5 × 质量 × 速度档位），全局倍率由伤害规则乘、减防在承受方。

        [Tooltip("飞刀的击退数值。")]
        [SerializeField] private float _knifeKnockback = 4f;

        [Tooltip("两次投掷之间的最小间隔（秒）。")]
        [SerializeField] private float _knifeCooldown = 0.35f;

        private float _knifeCooldownLeft;

        // ------------------------------------------------------------------
        //  攻击节奏：起手 / 生效 / 收招三段
        // ------------------------------------------------------------------

        /// <summary>
        /// 起手：从按键到「判定开窗」的基准秒数。**它要对齐刀刃的视觉接触时刻**。
        /// </summary>
        /// <remarks>
        /// <para>按攻击**直接**进 <c>Slash1</c>（拔剑那一段已删）。当前片子里刀刃挥出是
        /// 它的**第 2 帧** → 12fps 下 <b>0.167 秒</b>。</para>
        /// <para>⚠️ <b>美术重导出 Slash1 之后这个数就失效了</b>：拿动画测试窗逐帧走一遍，
        /// 看哪一帧刀刃扫出去（美术包围盒宽度会突增），那个帧号 ÷ 12 就是新值。</para>
        /// </remarks>
        public const float AttackWindupBase = 0.17f;

        /// <summary>
        /// 生效：从「判定开窗」到「下一次按键可以兑现成接续」的基准时长。
        /// </summary>
        /// <remarks>
        /// <para>它**不是**判定窗的宽度（判定只在开窗那一瞬做一次）。真正的作用是决定
        /// <c>起手 + 生效</c> —— 也就是**第二刀从哪一刻开始算**。</para>
        /// <para>对齐依据：两块刀刃的接触时刻之差 = <c>起手 + 生效</c>。
        /// 当前 <c>Slash1</c> 接触 0.167、<c>Slash2</c> 接触 0.616，差 <b>0.449</b> → 0.17 + 0.28 ✓</para>
        /// </remarks>
        public const float AttackActiveBase = 0.28f;

        /// <summary>
        /// 收招：连段窗口的尾巴，也是「下一次能出刀」的闸门。
        /// </summary>
        /// <remarks>
        /// <para><b>起手 + 生效 + 收招 = 代码一个循环 = 一刀的动画长度</b>。
        /// 当前 <c>Slash1(0.333) + AttackCd1(0.2) = 0.533</c> 秒，所以
        /// 0.17 + 0.28 + <b>0.05</b> = 0.50 ✓ 略早于动画，不会卡。</para>
        /// <para>⚠️ 这个总数**不能比动画长**。长了的话动画已经回 Idle、代码还在收招段里，
        /// 那段时间按下的攻击会被 <c>AttackSolver</c> 当"连段已满"直接**吞掉**（不缓冲），
        /// 表现就是「第二刀播完要愣一下才能再出刀」。</para>
        /// </remarks>
        public const float AttackRecoverBase = 0.05f;

        [Tooltip("起手：从按键到「判定开窗」的秒数。")]
        [SerializeField] private float _attackWindup = AttackWindupBase;

        [Tooltip("生效：判定窗的时长。**只在开窗那一瞬做一次命中判定**，不做持续判定 —— " +
                 "一刀砍两次会把无敌帧和伤害规则都算乱。")]
        [SerializeField] private float _attackActive = AttackActiveBase;

        [Tooltip("收招：这段时间里**再按一次就接下一刀**，不按就收剑。" +
                 "起手 / 生效段按下会先存着，到收招段再兑现（连段手感靠它）。")]
        [SerializeField] private float _attackRecover = AttackRecoverBase;

        [Tooltip("连段最多打几刀。1 = 只有一下，不接。")]
        [SerializeField, Range(1, 4)] private int _maxCombo = 2;

        // ------------------------------------------------------------------
        //  重击：单发，不进连段
        // ------------------------------------------------------------------

        /// <summary>
        /// 重击起手。**要对齐刀刃的视觉接触时刻**，和 <see cref="AttackWindupBase"/> 同一条道理。
        /// </summary>
        /// <remarks>⚠️ 这个数是量出来的、不是拍的：把 <c>SlashDown</c> 逐帧走一遍，
        /// 记下刀刃扫到目标那一帧 k（12fps），接触真时 = k / 12 秒。</remarks>
        public const float HeavyWindupBase = 0.32f;

        /// <summary>重击生效：判定窗时长。</summary>
        public const float HeavyActiveBase = 0.14f;

        /// <summary>重击收招：这段时间里出不了第二发。</summary>
        public const float HeavyRecoverBase = 0.36f;

        /// <summary>重击冷却：从收招结束起算。整轮 ≈ 起手 + 生效 + 收招 + 冷却 ≈ 2.0 秒。</summary>
        public const float HeavyCooldownBase = 1.2f;

        [Tooltip("重击起手：从按键到「判定开窗」的秒数。")]
        [SerializeField] private float _heavyWindup = HeavyWindupBase;

        [Tooltip("重击生效：判定窗时长。只在开窗那一瞬做一次命中判定。")]
        [SerializeField] private float _heavyActive = HeavyActiveBase;

        [Tooltip("重击收招：这段时间里出不了第二发。冷却从这里结束时起算。")]
        [SerializeField] private float _heavyRecover = HeavyRecoverBase;

        [Tooltip("重击冷却秒数。0 = 没有冷却。")]
        [SerializeField] private float _heavyCooldown = HeavyCooldownBase;

        [Tooltip("重击伤害**基础值** —— 换算由伤害处理模块负责，这里不许先乘系数。")]
        [SerializeField] private float _heavyDamage = 24f;

        [Tooltip("重击击退基础值。比轻击大，重击才「推得动」。")]
        [SerializeField] private float _heavyKnockback = 12f;

        [Tooltip("重击判定盒尺寸。比轻击大一圈。")]
        [SerializeField] private Vector2 _heavyBoxSize = new Vector2(1.4f, 1.2f);

        [Tooltip("重击判定盒相对角色中心的水平偏移（朝前）。")]
        [SerializeField] private float _heavyBoxOffset = 0.9f;

        [Header("能量")]
        [Tooltip("每命中一个目标恢复的能量（策划案 §四：攻击小怪回能）。未命中不给。")]
        [SerializeField] private float _energyPerHit = 4f;

        [Header("落点 / 重生")]
        [Tooltip("生命归零后自动回到最近一次记录的落点（PlayerSpawnPoint）。" +
                 "关掉 = 死亡不复活，交给关卡脚本处理。")]
        [SerializeField] private bool _respawnOnDeath = true;

        [Tooltip("倒下到复活之间的**最短**停顿（秒）。实际复活还要等死亡动画播完 —— " +
                 "两者取晚的那个（表现层用 HoldRespawn / ReleaseRespawnHold 挂等待）。")]
        [SerializeField, Min(0f)] private float _respawnDelay = 1.2f;

        // ==================================================================
        //  自身
        // ==================================================================

        /// <summary>本实体的刚体。质量住在它的 <c>mass</c> 上 —— 质量规则改的就是它（见 <see cref="Momentum"/>）。</summary>
        private Rigidbody2D _body;

        /// <summary>死亡标记的持有者；复活要清它。同物体上没有就不清（只回血）。</summary>
        private EntityDamageable _damageable;
        private SnapshotSystem _hurtSnapshots;
        private float _hurtPauseLeft, _hurtRecoveryLeft;
        public bool IsHurt => _hurtPauseLeft > 0f || _hurtRecoveryLeft > 0f;
        public bool IsInHurtImpactPause => _hurtPauseLeft > 0f;
        public float HurtRecoveryDuration => _hurtRecovery;

        // ---------------- 落点（内存，不落盘 —— 约束 C5） ----------------

        /// <summary>离复活还差多少秒。倒下那一帧重置成 <see cref="_respawnDelay"/>。</summary>
        private float   _respawnCountdown;

        /// <summary>上一帧是不是已经倒下。用来只在死亡那一帧重置倒计时。</summary>
        private bool    _wasDead;

        /// <summary>
        /// 死亡演出还没播完，<b>倒计时到点也不许复活</b>。
        /// </summary>
        /// <remarks>
        /// <para>只有表现层能判「动画播完了」，而玩法层不认识 Animator —— 所以由
        /// <c>PlayerAnimationDriver</c> 在倒下时挂上、<c>Die</c> 播完时解除（见
        /// <see cref="HoldRespawn"/> / <see cref="ReleaseRespawnHold"/>）。</para>
        /// <para>没有 Animator / 没有驱动时没人挂这个等待，复活就只看
        /// <see cref="_respawnDelay"/> —— 老行为不变。</para>
        /// </remarks>
        private bool    _respawnHeld;

        /// <summary>当前落点的世界坐标。</summary>
        private Vector2 _respawnPosition;

        // ==================================================================
        //  职能类
        // ==================================================================

        /// <summary>「下键按下」闩锁：用掉一次要等松开才复位。见 <see cref="ConsumeDownPress"/>。</summary>
        private bool _dropArmed = true;

        /// <summary>初始化是否已经完整跑过一次（见 <see cref="EnsureInit"/>）。</summary>
        private bool _initialized;

        /// <summary>请求的产生时刻：超过 0.12 秒还没人取走就丢弃。</summary>
        private float _dropRequestTime;

        private PlayerInput   _input;
        private ContactSensor _contacts;
        private JumpSolver    _jump;
        private DashSolver    _dash;
        private AttackSolver  _attack;
        private HeavyAttackSolver _heavy;
        private MotionMover   _mover;
        private WindSensor    _wind;
        private PushSensor    _push;

        /// <summary>攻击盒查开关用的缓冲（一次挥砍最多 16 个）。</summary>
        private readonly Collider2D[] _switchHits = new Collider2D[16];

        /// <summary>这次判定里已经翻过的开关 —— 同一个开关只吃一次。</summary>
        private readonly HashSet<IContactSwitch> _pressedSwitches = new HashSet<IContactSwitch>();

        // ==================================================================
        //  表现层事件
        // ==================================================================
        //
        // ⚠️ 这些是**边沿事件**（一次性），不是状态 —— 状态用下面那些只读属性读。
        //    分成两种是因为表现层两样都要：「现在是什么」（持续条件，每帧重写就够）
        //    和「刚刚发生了什么」（一次性动作）。后者靠每帧采样很容易漏掉或重复计一次
        //    （比如 0.33 秒的落地只有 4 个物理帧），所以直接从源头发事件。
        //
        //    事件名与签名**冻结**：PlayerAnimationDriver 订阅着它们。

        /// <summary>**地面跳 / 土狼跳**瞬间。</summary>
        /// <remarks>二段跳走的是 <see cref="OnAirJumped"/>，不在这里发 —— 两者的表现不同
        /// （一个是蹬地起跳、一个是空中再翻一下），合并成一个事件表现层就分不出来了。</remarks>
        public event Action OnJumped;

        /// <summary>**空中段（二段跳）**瞬间：跳数还没用完时在空中再按一次跳。</summary>
        public event Action OnAirJumped;

        /// <summary>蹬墙跳瞬间。与 <see cref="OnJumped"/> 分开：它有两档推力、表现也不同。</summary>
        public event Action OnWallJumped;

        /// <summary>落地瞬间（接地判定 false → true）。</summary>
        public event Action OnLanded;

        /// <summary>冲刺开始瞬间（次数已扣、方向已定）。</summary>
        public event Action OnDashed;

        /// <summary>
        /// 挥出一刀 —— 连段的**第一下和之后每一下**都会发。
        /// </summary>
        /// <remarks>表现层用 <see cref="AttackComboIndex"/> 区分是起手还是接续
        /// （起手播拔剑、接续播下一段）。</remarks>
        public event Action OnAttacked;

        /// <summary>
        /// 重击起手瞬间。与 <see cref="OnAttacked"/> 分开：它不进连段，
        /// 表现层要播的是另一段动作（<c>SlashDown</c>），而且**不先播拔剑**。
        /// </summary>
        public event Action OnHeavyAttacked;

        /// <summary>实际判定开窗；表现层直接使用同一次攻击的世界范围，空挥也会通知。</summary>
        public event Action<PlayerAttackCue> OnAttackStruck;

        /// <summary>刚解锁一项能力（参数是新增的那几位）。</summary>
        public event Action<PlayerAbility> AbilityUnlocked;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake() => EnsureInit("Awake");

        /// <summary>
        /// **幂等**初始化：先把所有子系统补齐，缺哪个建哪个。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 为什么要幂等：<c>Awake</c> 有可能**半路没跑完**（流式加载 / 场景切换把我们打断，
        /// 或者组件是运行时才加的）。那时 <c>_body</c> 已经赋值、<c>_input</c> 还是 null，
        /// 结果就是「这个玩家什么都不响应」+ 每帧 <c>NullReferenceException</c>（实测踩到过）。</para>
        /// <para>补齐之后会打一条 warning，方便看出「当初 Awake 确实没跑完」。</para>
        /// </remarks>
        private bool EnsureInit(string where)
        {
            if (_initialized && _input != null) return true;

            if (_status == null) _status = GetComponent<EntityStatus>();
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_damageable == null) _damageable = GetComponent<EntityDamageable>();

            var bodyCollider = GetComponent<Collider2D>();

            if (_input == null) _input = new PlayerInput(_inputActions);
            if (_input == null) return false;

            _contacts ??= new ContactSensor(_body, bodyCollider);
            _jump     ??= new JumpSolver();
            _dash     ??= new DashSolver();
            _attack   ??= new AttackSolver();
            _heavy    ??= new HeavyAttackSolver();
            _mover    ??= new MotionMover(_body);          // 刚体的 Kinematic 配置在这里面
            _wind     ??= new WindSensor(_body, bodyCollider);
            _push     ??= new PushSensor(_body);

            _dash.Reset(BuildTuning());                 // 起手就有冲刺，不必先落地一次
            _initialized = true;

            if (!_input.IsReady)
                Debug.LogWarning("[PlayerController] 未找到输入动作，请把 InputSystem_Actions 拖到 _inputActions。", this);

            if (where != "Awake")
                Debug.LogWarning($"[PlayerController] 初始化没跑完，已在 {where} 补全（输入层 {(_input.IsReady ? "ok" : "缺动作")}）。", this);

            return true;
        }

        private void OnEnable()
        {
            EnsureInit("OnEnable");     // 组件被重新启用时补齐（Awake 可能根本没跑过）
            _input?.Enable();
            if (_damageable != null) _damageable.OnDamageTaken += HandleDamageTaken;
            BindHurtSnapshots();
        }

        private void OnDisable()
        {
            _input?.Disable();
            if (_damageable != null) _damageable.OnDamageTaken -= HandleDamageTaken;
            if (_hurtSnapshots != null) _hurtSnapshots.OnRestored -= ClearHurt;
            _hurtSnapshots = null;
            ClearHurt();
        }

        private void BindHurtSnapshots()
        {
            if (_hurtSnapshots == SnapshotSystem.Instance) return;
            if (_hurtSnapshots != null) _hurtSnapshots.OnRestored -= ClearHurt;
            _hurtSnapshots = SnapshotSystem.Instance;
            if (_hurtSnapshots != null) _hurtSnapshots.OnRestored += ClearHurt;
        }

        private void HandleDamageTaken(RuleGame.Infrastructure.DamageInfo damage)
        {
            if (!isActiveAndEnabled || !EnsureInit(nameof(HandleDamageTaken))) return;
            MotionTuning tuning = BuildTuning();
            _dash.Cancel(tuning);
            _attack.Cancel();
            _heavy.Cancel(tuning);
            _jump.ClearBufferedInput();
            _input.Tick(true);
            _mover.CancelSelfMotion();
            _mover.FreezeStep();
            _hurtPauseLeft = _hurtImpactPause;
            _hurtRecoveryLeft = _hurtRecovery;
        }

        private void ClearHurt()
        {
            _hurtPauseLeft = _hurtRecoveryLeft = 0f;
            _dash?.Cancel(BuildTuning());
            _attack?.Cancel();
            _heavy?.Cancel(BuildTuning());
            _jump?.ClearBufferedInput();
            _mover?.Halt();
            _mover?.FreezeStep();
        }

        // ==================================================================
        //  输入（视觉帧）
        // ==================================================================

        private void Update()
        {
            BindHurtSnapshots();
            // 暂停 / 阵亡 / 受击 / UI 接管及门禁恢复帧都清空动作输入。
            _input.Tick(IsPaused || IsDead || IsHurt || _input.IsSuppressed);

            // 倒计时与「落点」有关，与输入无关，所以放在门禁之外。
            TickRespawn();
        }

        /// <summary>
        /// 时间恒定（定点回溯）的控制入口。
        /// </summary>
        /// <remarks>
        /// <para>回溯键**独占一个键**（架构 D16）：键盘 Q / 手柄 LB，见 <c>PlayerInput.Tick</c>。
        /// 窗口由时间恒定的规则类持有，没开窗口时这里什么都不做。</para>
        /// <para>取用方式是接口查询而不是直接引用时间规则类 —— 玩家侧不认识规则系统内部。</para>
        /// </remarks>
        private static void TryRewindTime()
        {
            if (RuleSystem.Instance is ITimeRewindControl control) control.TryRewind();
        }

        // ==================================================================
        //  物理（固定帧）
        // ==================================================================

        /// <summary>
        /// 让身体「顺着」当前重力：重力朝上时整个根对象转 180°。
        /// </summary>
        /// <remarks>
        /// <para>转的是**根对象**，所以 <c>Collider2D</c> 的 <c>offset</c> 一并翻过去 ——
        /// 「脚」（原本在碰撞箱偏下那侧）翻转后正好朝上，贴天花板时接触的就是它。
        /// 视觉（<c>Visual</c> 子对象）自动跟着转，不需要另外处理。</para>
        /// <para>⚠️ 必须在 <c>ContactSensor.Sense</c> **之前**调用：探测扫的是碰撞体的当前形状，
        /// 转完再探才拿得到新朝向下的接触。</para>
        /// <para>Kinematic 刚体上的 <c>FreezeRotation</c> 只挡物理转动，代码写 <c>transform</c> 照样生效。</para>
        /// <para>失重（重力为零向量）不转：没有重力就没有「哪边是上」。</para>
        /// <para>结果同时记进 <see cref="IsBodyFlipped"/>，供表现层对齐——见那个属性的说明。</para>
        /// </remarks>
        private void ApplyGravityFacing()
        {
            IsBodyFlipped = Physics2D.gravity.y > 0f;

            transform.localRotation = IsBodyFlipped
                ? Quaternion.Euler(0f, 0f, 180f)
                : Quaternion.identity;
        }

        private void FixedUpdate()
        {
            // 初始化没跑完就先补全（Awake 可能被流式加载打断过）：补不上才跳过这一帧。
            // ⚠️ 不挡的话 _input.Sample() 每帧抛 NRE，FixedUpdate 后面的代码全都不执行 ——
            //    表现就是「这个玩家什么都不响应」。
            if (!EnsureInit(nameof(FixedUpdate))) return;

            // 暂停：速度清零、整帧不跑。冲刺计时与连段计时因此天然冻结
            //（文档 §5）—— 不需要额外的暂停分支。
            if (IsPaused)
            {
                if (IsHurt) _mover.FreezeStep();
                else _mover.Halt();
                return;
            }

            float dt = Time.fixedDeltaTime;
            if (IsInHurtImpactPause)
            {
                _hurtPauseLeft = Mathf.Max(0f, _hurtPauseLeft - dt);
                _input.Sample();
                _mover.FreezeStep();
                return;
            }

            // 参数每物理帧重打包一次（运行期改字段立刻生效），这一帧内共用同一份。
            MotionTuning tuning = BuildTuning();

            // ⓪ 朝向：先按重力把身体摆正，后面所有探测与位移都基于转过的碰撞箱。
            ApplyGravityFacing();

            // ① 接触：Sense 内部先解出「上」方向（探测得先知道哪边是下），再扫地面与左右墙。
            PlayerInput.State   input   = _input.Sample();
            if (IsHurt || IsDead) input = default;
            // 时间恒定：窗口内的定点回溯。窗口没开时 TryRewind() 返回 false，是空操作。
            // 放在物理帧：回溯会直接写回所有实体的刚体位置，与物理步进同一节拍更安全。
            if (input.RewindPressed) TryRewindTime();

            // 关卡流程锁移动时只清移动意图，跳跃松手与攻击仍然透传。
            if (MovementHeld) input = new PlayerInput.State(Vector2.zero, false, input.JumpReleased, false,
                                                           input.AttackPressed, throwPressed: input.ThrowPressed);
            ContactSensor.State contact = _contacts.Sense(tuning);
            if (contact.JustLanded) OnLanded?.Invoke();

            // ①′ 风场：另一路环境输入。报的是力，除以质量后才进解算（见 WindSensor）。
            Vector2 windAccel = _wind.Sense(tuning);
            float   windDrag  = _wind.Drag;
            float   windVerticalDamping = _wind.VerticalDamping;
            bool    windInField = _wind.InField;

            // ③ 冲刺：计时 / 次数 / 冷却 / 方向。Dashing 是后面几步的总开关。
            DashSolver.Decision dash = _dash.Advance(tuning, input, contact, _mover.Facing, dt);
            if (dash.Started) OnDashed?.Invoke();

            // ③′ 攻击：轻击连段与重击共用这一个按键，**按意图路由**。
            //
            // ⚠️ 判「意图」而不是「合法性」：下+攻击**就是**一次重击意图，
            //    不合法（冷却中 / 空中 / 冲刺中 / 轻击在跑）就**丢掉**，
            //    绝不回退成一次轻击 —— 否则冷却期按下去会莫名其妙挥出一刀轻击。
            bool attackPressed = input.AttackPressed && HasAbility(PlayerAbility.Attack);
            bool heavyIntent  = HeavyAttackSolver.IsHeavyIntent(input.Move, attackPressed, contact.Up);
            bool lightPressed = attackPressed && !heavyIntent;

            // 轻击在跑就不给重击：ComboIndex 直到收招结束才回到 −1，
            // 所以 `>= 0` 恰好等价于「连段还在三段里」（不用给 AttackSolver 加新 API）。
            HeavyAttackSolver.Decision heavy = _heavy.Advance(
                tuning, heavyIntent,
                HeavyAttackSolver.CanStart(_attack.ComboIndex >= 0, contact.Grounded, dash.Dashing),
                dt);
            if (heavy.Swing)  OnHeavyAttacked?.Invoke();
            if (heavy.Strike)
                PerformAttackHit(_heavyDamage, _heavyKnockback, _heavyBoxSize, _heavyBoxOffset, true);

            // ③″ 攻击连段：起手 → 生效 → 收招。判定本身留在本类（要用 CombatSystem 与位置）。
            AttackSolver.Decision attack = _attack.Advance(tuning, lightPressed, dt);
            if (attack.Swing)  OnAttacked?.Invoke();
            if (attack.Strike)
                PerformAttackHit(_attackDamage, _attackKnockback, _attackBoxSize, _attackOffset);

            // ③‴ 投掷（飞刀原型）：冷却按物理帧推进，飞行与命中归 ThrownKnife 自己。
            _knifeCooldownLeft = Mathf.Max(0f, _knifeCooldownLeft - dt);
            if (input.ThrowPressed) ThrowKnife();

            // 「下 + 跳」= **下钻**，不是跳：跳键这一帧被吃掉，转成一次性请求交给脚下的跳穿平台。
            // 必须在 JumpSolver.Solve 之前改掉 input，否则人会先起跳、再谈穿不穿过去。
            //
            // ⚠️ 这里**不再**检查「有没有站稳」（Grounded / 竖直速度）：
            // 「下钻」改回**平台自己判**：玩家只提供「下键刚按下」这个一次性意图，
            // 不再和跳键绑在一起（那套把跳键吃掉的做法反而多了几条互相干扰的路径）。
            // 平台侧 = 站在我上面 + 取到这个按下沿 → 放行。

            // ②⑦⑧ 跳跃：跳数与土狼时间一直推进；冲刺期间只推进计数、不消费输入。
            JumpSolver.Decision jump = _jump.Solve(tuning, input, contact, dash.Dashing, dt);
            switch (jump.Kind)
            {
                case JumpSolver.JumpKind.Ground: OnJumped?.Invoke();     break;
                case JumpSolver.JumpKind.Wall:   OnWallJumped?.Invoke(); break;
                case JumpSolver.JumpKind.Air:    OnAirJumped?.Invoke();  break;
            }

            // ④④′⑤⑤′⑥⑦⑧ 速度解算：纯计算。速度从参数进、从返回值出。
            var frame = new MotionFrame(tuning, input, contact, jump, dash,
                                        Physics2D.gravity, windAccel, windDrag, windVerticalDamping,
                                        windInField, _mover.Facing, FrictionScale, FrictionInverted);

            _mover.Apply(VelocitySolver.Solve(_mover.Velocity, frame, dt));   // 唯一一次写速度

            // ⑧⑨⑩ 击退衰减 → Slide 解算接触 → 撞墙撞顶清分量
            _mover.Move(tuning, contact, dt);

            // ⑪ 推动：把**力**交给前方的可推物体。放在位移之后 ——
            //    它不影响本帧的位移，推不动时由 Slide 正常把玩家挡在箱子前面。
            //    方向取 _mover.Facing（**换算后**的朝向）：摩擦反转时按右往左走，
            //    推的方向要跟着人实际去的方向，不能跟按键。
            _push.Push(tuning, input.Move, _mover.Facing);
            _hurtRecoveryLeft = Mathf.Max(0f, _hurtRecoveryLeft - dt);
        }

        // ==================================================================
        //  对外只读状态
        // ==================================================================
        //
        // ⚠️ 全部对「协作对象还不存在」做了保护。
        //    这些属性在 **Awake 之前**就会被读到：编辑模式的 gizmo、P3DebugOverlay 的 OnGUI、
        //    以及 play 模式切换的那一瞬间（域刚重载完、Awake 还没跑）。
        //    重构前它们读的是普通字段，默认值天然安全；现在解引用协作对象，不加保护就是 NRE。
        //    ⚠️ 保护方式是**返回安全默认值，不是惰性创建** —— `new MotionMover(...)` 会去配置
        //       刚体（bodyType / interpolation），在编辑模式里读一下属性就改场景，那是脏场景。

        /// <summary>是否站在支撑面上（供踩踏判定与调试用）。</summary>
        public bool IsGrounded => _contacts != null && _contacts.Grounded;

        /// <summary>
        /// 身体现在是不是已经按重力倒过来了（<see cref="ApplyGravityFacing"/> 的结果）。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>表现层要判「人是不是倒的」，读这个，<u>不要</u>读 <c>Physics2D.gravity</c>。</b>
        /// 重力在规则的 <c>Update</c>（<c>RuleSystemDriver</c> 顺序 −100）里就已经改了，
        /// 而身体要到<b>下一个</b> <c>FixedUpdate</c> 才转 —— 两者差一帧。</para>
        /// <para>按重力判断的话，视觉那侧的补偿（<c>flipX</c> / <c>flipY</c>）会<b>超前</b>一帧生效：
        /// 根对象还没转就先叠上了「抵消 180°」的那两下，合计是净 180° 旋转 ——
        /// 表现就是「反转瞬间闪一帧倒立的人物」，然后才进 Weightless。</para>
        /// <para>读这个属性则天然与根对象同步：它只在真正转的那一刻变。</para>
        /// </remarks>
        public bool IsBodyFlipped { get; private set; }

        /// <summary>
        /// 当前速度（自己维护的那个，<b>不含</b>击退等外部冲量）。
        /// </summary>
        /// <remarks>给表现层用（动画判上升/下降、跑步快慢）。玩法逻辑不要读它做判断 ——
        /// 那样会把「速度怎么算」泄漏到外面。</remarks>
        public Vector2 Velocity => _mover != null ? _mover.Velocity : Vector2.zero;
        /// <summary>移动平台入口。自身速度仍由 MotionMover 管理，平台只贡献本物理帧的承载位移。</summary>
        public void CarryOnPlatform(Vector2 displacement, Collider2D platform) => _mover?.CarryOnPlatform(displacement, platform);

        /// <summary>
        /// 本物理帧的动量（kg·单位/秒），方向 = 运动方向。见 <see cref="IMomentumSource"/>。
        /// </summary>
        /// <remarks>
        /// <para>质量取 <c>Rigidbody2D.mass</c> —— 与风力那条路（<c>WindSensor</c> 的「力 ÷ 质量」）
        /// 是同一个质量，质量规则改的也是它。于是「质量 × 增强」让同样的一撞更重 ——
        /// 撞击按钮（<c>ImpactButton</c>）之类的机关读这个值，一行都不用改。</para>
        /// <para>⚠️ 与 <see cref="Velocity"/> 一样**不含击退**：被击退撞到机关上不算动量。
        /// 刚体还没配好的时候（Awake 之前 / 编辑器读属性）返回零向量。</para>
        /// </remarks>
        public Vector2 Momentum
            => _mover != null && _body != null ? _mover.Velocity * _body.mass : Vector2.zero;

        /// <summary>
        /// 当前朝向：+1 右，−1 左。
        /// </summary>
        /// <remarks>取的是**换算后**的方向，所以「摩擦 × 反转」生效时按右往左走，
        /// 这里自动是 −1 —— 表现层不需要自己判断方向。</remarks>
        public float Facing => _mover != null ? _mover.Facing : 1f;

        /// <summary>输入层是否已经初始化好（_input 非空）。给场景组件筛掉空壳实例用。</summary>
        public bool IsInputReady => _input != null;

        /// <summary>本帧读到的**原始**移动输入（未经摩擦反转换算）。供调试浮层显示。</summary>
        public Vector2 MoveInput => _input != null ? _input.Move : Vector2.zero;

        /// <summary>
        /// 把玩家**直接挪到平台另一侧**（跳穿平台用）。
        /// </summary>
        /// <remarks>
        /// <para>为什么不靠「关碰撞体 + 等重力」：玩家是 Kinematic + <c>Slide</c>，竖直速度会被接地判定钳成 0，
        /// 而接地又可能来自**旁边那一格**（胶囊 0.76 宽 > 一格 0.5）。实测过：平台碰撞体明明关了，
        /// <c>vy</c> 还是 0、人一动不动。直接挪过去与接地判定、相邻格、重力方向全部无关。</para>
        /// <para>目标位置若有别的实心几何挡着就不挪（交给「放开平台碰撞体」那条路兜底），不会把人塞进墙里。</para>
        /// </remarks>
        public void DisplaceThrough(Vector2 direction, float distance, Collider2D released)
        {
            if (_mover == null || _body == null) return;
            if (distance <= 0f) return;

            Vector2 from = transform.position;
            Vector2 to = from + direction.normalized * distance;

            var body = GetComponent<Collider2D>();
            if (body != null)
            {
                // 目标位置被实心几何占着就不挪。
                Bounds b = body.bounds;
                var hits = Physics2D.OverlapBoxAll(to, new Vector2(b.size.x * 0.9f, b.size.y * 0.9f), 0f,
                                                  Physics2D.AllLayers);
                for (int i = 0; i < hits.Length; i++)
                {
                    var c = hits[i];
                    if (c == null || c == body || c.isTrigger) continue;
                    if (c == released) continue;
                    if (c.GetComponent<RuleGame.Gameplay.Scene.OneWayPlatform>() != null) continue;
                    // 只在意玩家会撞的层。⚠️ _solidLayer 是 **LayerMask**（位掩码），
                    // 不能和 layer 索引直接比较（8 vs 256 永远不相等 → 一律判定为阻挡，位移永远不生效）。
                    if ((_solidLayer.value & (1 << c.gameObject.layer)) == 0) continue;
                    return;
                }
            }

            transform.position = new Vector3(to.x, to.y, transform.position.z);
            _body.position = to;                 // autoSyncTransforms 关着，刚体位置要自己同步
            _mover.Halt();                       // 挪完这一帧别再叠位移
        }

        /// <summary>当前是否按着「下」（相对重力的下，或世界坐标朝下）。</summary>
        public bool DropDownHeld
        {
            get
            {
                Vector2 m = MoveInput;
                return Vector2.Dot(m, DownDirection) > 0.5f || m.y < -0.5f;
            }
        }

        /// <summary>
        /// 「按一下下键」的一次性意图：**每按一次只能取走一次**，松开才复位。
        /// </summary>
        /// <remarks>
        /// <para>跳穿平台用它当触发。必须是「按下沿」而不是「按住」：按住不放会在落到下一层时
        /// 立刻把那一层也放行（穿过一层、停在第三层）。</para>
        /// <para>闩锁住在**玩家**身上，所以不依赖平台脚本的执行顺序。</para>
        /// </remarks>
        public bool ConsumeDownPress()
        {
            if (!DropDownHeld)
            {
                _dropArmed = true;      // 松开 → 下次按下重新武装
                return false;
            }

            if (!_dropArmed) return false;

            _dropArmed = false;
            return true;
        }

        /// <summary>「下」= 重力方向（零重力时按世界下方算）。</summary>
        public static Vector2 DownDirection => Physics2D.gravity.sqrMagnitude > 0.0001f
            ? Physics2D.gravity.normalized
            : Vector2.down;

        /// <summary>
        /// 左侧是否贴着竖墙（**全高**探测；供调试浮层显示）。只用于顶墙分量预夹。
        /// </summary>
        public bool WallLeft => _contacts != null && _contacts.WallLeft;

        /// <summary>右侧是否贴着竖墙（**全高**探测）。只用于顶墙分量预夹。</summary>
        public bool WallRight => _contacts != null && _contacts.WallRight;

        /// <summary>左侧**中段**是否贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳的判据。</summary>
        public bool ClimbLeft => _contacts != null && _contacts.ClimbLeft;

        /// <summary>右侧**中段**是否贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳的判据。</summary>
        public bool ClimbRight => _contacts != null && _contacts.ClimbRight;

        /// <summary>是否正在贴墙缓慢下滑（供调试浮层显示）。</summary>
        public bool IsWallSliding => _mover != null && _mover.WallSliding;

        /// <summary>本物理帧受到的风力（牛顿，**未**除以质量）。供调试浮层对账用。</summary>
        public Vector2 WindForce => _wind != null ? _wind.Force : Vector2.zero;

        /// <summary>
        /// 本物理帧的风加速度（风力 ÷ 质量）。
        /// </summary>
        /// <remarks>质量规则生效时，它应与 <see cref="WindForce"/> 反向变化（力不变、质量变 → 加速度变）
        /// —— 这是验证「质量改变浮空高度」最直接的读数。</remarks>
        public Vector2 WindAcceleration => _wind != null ? _wind.Acceleration : Vector2.zero;

        /// <summary>本物理帧的空气阻力系数（风场给的，每秒速度衰减率）。</summary>
        public float WindDrag => _wind != null ? _wind.Drag : 0f;

        /// <summary>本物理帧的竖直阻尼系数（风场给的，每秒速度衰减率）。只作用于竖直方向。</summary>
        public float WindVerticalDamping => _wind != null ? _wind.VerticalDamping : 0f;

        /// <summary>本物理帧是否在生效中的风场里（风场内的重力统一为上升重力）。</summary>
        public bool InWindField => _wind != null && _wind.InField;

        /// <summary>本实体的重力倍率（供风场 Gizmo 预览平衡高度用）。</summary>
        public float GravityScale => _gravityScale;

        /// <summary>总跳数上限 = 第一段 + 空中段。锁住二段跳时只算第一段。</summary>
        public int MaxJumps => JumpSolver.MaxJumps(HasAbility(PlayerAbility.AirJump) ? _maxAirJumps : 0);

        /// <summary>这个实体当前已解锁的能力位。</summary>
        public PlayerAbility UnlockedAbilities => _unlockedAbilities;

        /// <summary>这个实体有没有解锁某一项能力。可以一次传多位（全部命中才返回 true）。</summary>
        public bool HasAbility(PlayerAbility ability)
            => ability == PlayerAbility.None || (_unlockedAbilities & ability) == ability;

        /// <summary>解锁一项能力。已经解锁时返回 false，不会重复广播。</summary>
        public bool UnlockAbility(PlayerAbility ability)
        {
            if (ability == PlayerAbility.None) return false;

            PlayerAbility next = _unlockedAbilities | ability;
            if (next == _unlockedAbilities) return false;

            _unlockedAbilities = next;
            AbilityUnlocked?.Invoke(ability);
            return true;
        }

        /// <summary>只给开局 / 读档用：整体覆盖能力位。正常解锁走 <see cref="UnlockAbility"/>。</summary>
        public void SetUnlockedAbilities(PlayerAbility abilities) => _unlockedAbilities = abilities;

        /// <summary>本次落地以来还剩几段跳（供 HUD / 调试浮层显示）。</summary>
        public int JumpsLeft => Mathf.Max(MaxJumps - (_jump != null ? _jump.JumpsUsed : 0), 0);

        /// <summary>正在冲刺。</summary>
        public bool IsDashing => _dash != null && _dash.IsDashing;

        /// <summary>还能冲几次（供 HUD / 调试浮层显示）。</summary>
        public int DashesLeft => _dash != null ? _dash.DashesLeft : 0;

        /// <summary>冲刺次数上限。</summary>
        public int MaxDashes => _maxDashes;

        /// <summary>当前连段打到第几刀（0 起数）；没在出招时是 −1。</summary>
        public int AttackComboIndex => _attack != null ? _attack.ComboIndex : -1;

        /// <summary>重击正在挥（起手 / 生效 / 收招都算）。</summary>
        public bool IsHeavyBusy => _heavy != null && _heavy.IsBusy;

        /// <summary>重击冷却剩余秒数，0 = 可以放（供 HUD / 调试浮层显示）。</summary>
        public float HeavyCooldownLeft => _heavy != null ? _heavy.CooldownLeft : 0f;

        /// <summary>暂停中不读输入、不推进物理。</summary>
        /// <remarks>暂停状态由 <c>EntityStatus.SetPaused</c> 驱动 —— 暂停持有者（P1 的时间管理器）负责调用。</remarks>
        private bool IsPaused => _status != null && _status.IsPaused;

        /// <summary>
        /// 生命归零：**不再接受任何输入**（移动 / 跳 / 冲刺 / 攻击 / 重击全断），但物理照常推进。
        /// </summary>
        /// <remarks>
        /// <para>判据取 <c>Health &lt;= 0</c> 而不是 <c>EntityDamageable.IsDead</c>：前者对**任何**掉血路径都成立
        /// （伤害链路 / 调试浮层直接改血 / 快照回滚），而且和 <c>PlayerAnimationDriver</c> 里
        /// 「掉到 0 → 播 Die」用的是同一个判据，两边不会各判各的。</para>
        /// <para>⚠️ <b>不像暂停那样整帧 <c>return</c></b>：那样人会**冻在半空**。
        /// 这里是「不许操作」，不是「时间停止」—— 重力、碰撞、击退、规则影响都还在，
        /// 所以空中阵亡会照常落地。</para>
        /// <para>已经在跑的冲刺 / 挥砍不会被打断，只是不再接受新的输入 —— 自然收尾。</para>
        /// </remarks>
        private bool IsDead => _status != null && _status.Health <= 0f;

        // ==================================================================
        //  落点 / 重生（内存，不落盘 —— 约束 C5）
        // ==================================================================

        /// <summary>是否已经有一个可用的落点（出生或走近过某个 <c>PlayerSpawnPoint</c>）。</summary>
        public bool HasRespawn { get; private set; }

        /// <summary>
        /// 当前落点所属点位的 id。
        /// </summary>
        /// <remarks>只用于「换了落点才重置倒计时」这条判据；落盘时也只需要它 ——
        /// 位置永远由场景里的点提供，不进存档。</remarks>
        public string RespawnId { get; private set; }

        /// <summary>当前落点的世界坐标（只读）；要改请用 <see cref="SetRespawn"/>。</summary>
        public Vector2 RespawnPosition => _respawnPosition;

        /// <summary>
        /// 正在出招：轻击连段（起手 / 生效 / 收招，含两刀之间的 CD）**或**重击。
        /// </summary>
        /// <remarks>
        /// <para>用来把水平最大速度压到 <c>_attackMoveSpeedMax</c>（见 <c>BuildTuning</c>）。</para>
        /// <para>轻击取 <c>ComboIndex &gt;= 0</c>：<c>AttackSolver</c> 直到收招结束才把它置回 −1，
        /// 所以这个判据恰好等于「三段里任意一段」，不需要给它加新 API。</para>
        /// </remarks>
        private bool IsAttacking
            => (_attack != null && _attack.ComboIndex >= 0)
            || (_heavy  != null && _heavy.IsBusy);

        // ==================================================================
        //  落点 / 重生
        // ==================================================================

        /// <summary>
        /// 记录一个落点。由 <c>PlayerSpawnPoint</c> 在玩家走进激活半径时调用。
        /// </summary>
        /// <remarks>
        /// <para>同一个点、同一个位置被反复调用是常态（玩家在点边上走来走去），
        /// 所以**只有真正换了落点**才重置复活倒计时 —— 否则站在点上刷会一直把倒计时拉满。</para>
        /// <para>位置存在玩家自己的内存里：约束 C5 禁止序列化 Transform / 物理状态。</para>
        /// </remarks>
        public void SetRespawn(string id, Vector2 position)
        {
            bool changed = !HasRespawn
                        || RespawnId != id
                        || (position - _respawnPosition).sqrMagnitude > 0.0001f;

            HasRespawn       = true;
            RespawnId        = id;
            _respawnPosition = position;

            if (changed) _respawnCountdown = _respawnDelay;
        }

        /// <summary>
        /// 瞬移到指定位置：清速度 → 写刚体位姿 → 同步物理变换 → 对齐 transform。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 速度必须清：不清的话 <see cref="MotionMover"/> 里的旧速度会在落地第一帧把人再推走。</para>
        /// <para>⚠️ 必须 <c>Physics2D.SyncTransforms()</c>：本工程的 <c>autoSyncTransforms</c> 是关的，
        /// 不同步的话这一帧的探测与 Slide 仍在旧位置上算 —— 实测会卡在墙里或穿过地板。</para>
        /// </remarks>
        public void TeleportTo(Vector2 position)
        {
            ClearHurt();
            _mover?.Halt();

            if (_body != null)
            {
                _body.position = position;
                _body.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms();
            }

            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        /// <summary>
        /// 回到最近一次记录的落点并复活。<b>返回 false = 还没有落点</b>（只打一条警告）。
        /// </summary>
        public bool Respawn()
        {
            if (!HasRespawn)
            {
                Debug.LogWarning("[PlayerController] 还没有任何落点，无法复活。先让玩家走到一个 PlayerSpawnPoint。", this);
                return false;
            }

            _respawnHeld      = false;          // 显式复活（调试键 / 关卡脚本）不等演出
            TeleportTo(_respawnPosition);
            _respawnCountdown = _respawnDelay;

            // 有 EntityDamageable 就交给它（清死亡标记 + 回满血 + 给无敌）；没有就只补血。
            if (_damageable != null) _damageable.Revive();
            else if (_status != null && _status.Health <= 0f) _status.ApplyHealthDelta(_status.HealthMax);

            return true;
        }

        /// <summary>
        /// 死亡演出还没播完：<b>复活倒计时到点也先在原地等着</b>。由表现层（动画驱动）调用。
        /// </summary>
        /// <remarks>
        /// <para>为什么要这一层握手：复活是玩法层的事，但「<c>Die</c> 播完了没有」只有
        /// Animator 知道；玩法层不引用 Animator（依赖方向 Presentation → Gameplay），
        /// 所以由表现层主动告诉它。<c>Die</c> 剪辑 2.67 秒，光靠倒计时（1.2 秒）会
        /// 在人还没倒下时就把人搬回存档点。</para>
        /// <para>幂等：重复调用只会刷新等待，不会叠加。</para>
        /// </remarks>
        public void HoldRespawn() => _respawnHeld = true;

        /// <summary>死亡演出播完了，放行复活；复活 / 非死亡状态下调用也安全（幂等）。</summary>
        public void ReleaseRespawnHold() => _respawnHeld = false;

        /// <summary>现在是否被死亡演出挡着不让复活（供动画驱动 / 验证脚本读）。</summary>
        public bool IsRespawnHeld => _respawnHeld;

        /// <summary>死亡后的复活倒计时。暂停期间不推进（与其它冷却同一条规则）。</summary>
        /// <remarks>
        /// <para>倒计时到了也还要过 <see cref="_respawnHeld"/> 这一关：死亡动画没播完就复活，
        /// 表现是「人还没倒下就被搬回存档点」，而且 Animator 会停在 <c>Die</c> 的最后一帧
        /// （那个状态没有出边）。</para>
        /// </remarks>
        private void TickRespawn()
        {
            bool dead = IsDead;

            // 倒下那一帧才重置倒计时：不依赖「上一次走到存档点是什么时候」。
            if (dead && !_wasDead) _respawnCountdown = _respawnDelay;
            _wasDead = dead;

            if (!_respawnOnDeath || !HasRespawn || !dead) return;

            if (IsPaused)
            {
                _respawnCountdown = _respawnDelay;   // 暂停不是「继续跑倒计时」
                return;
            }

            _respawnCountdown -= Time.deltaTime;
            if (_respawnCountdown > 0f) return;

            if (_respawnHeld) return;                // 表现层还没说「死亡动画播完了」

            Respawn();
        }

        // ==================================================================
        //  输入门禁
        // ==================================================================

        /// <summary>UI 仅接管输入；慢时间里的重力、碰撞与外部冲量仍走原 FixedUpdate。</summary>
        public IDisposable BlockGameplayInput() => _input.Block();

        /// <summary>其他交互入口只读现有门禁，避免规则选择器打开时同时开始交谈。</summary>
        public bool IsGameplayInputSuppressed => _input == null || _input.IsSuppressed;

        /// <summary>
        /// 是否有别的系统<b>正拿着</b>输入门禁（过场 / 传送 / 对话 / 轮盘）。
        /// </summary>
        /// <remarks>
        /// <para>与 <see cref="IsGameplayInputSuppressed"/> 只差「释放后的恢复帧」：那个恢复帧对
        /// 玩家动作是必要的（同一次确认键不能再挥一刀），但对「能不能开轮盘」是多余的 ——
        /// 轮盘自己关掉的那一帧就应该能重新打开。</para>
        /// </remarks>
        public bool IsInputBlocked => _input == null || _input.IsBlocked;

        // ==================================================================
        //  击退
        // ==================================================================

        /// <summary>外部冲量入口（击退）。见 <see cref="IKnockbackReceiver"/>。</summary>
        public void AddImpulse(Vector2 impulse) => _mover.AddImpulse(impulse);

        // ==================================================================
        //  攻击判定
        // ==================================================================

        /// <summary>
        /// 生效段的唯一命中判定。轻击与重击共用这一个出口，只差传进来的那组参数。
        /// </summary>
        /// <remarks>
        /// <para>走正式伤害链路：<b>传的是基础值</b>，最终伤害由伤害处理模块按当前规则算 ——
        /// 重击就是传一个更大的基础伤害，**绝不在这一侧先乘系数**（会绕过伤害规则）。</para>
        /// <para>命中对象由<b>受击箱</b>决定（<c>CombatSystem</c> 固定查 <c>Hurtbox</c> 层），
        /// 这里不再配 mask —— 挡自伤 / 挡同阵营的规则也收在 <c>CombatSystem.CanHit</c> 里。</para>
        /// <para>放在速度解算**之前**调用，所以读到的朝向是上一帧末的 ——
        /// 与重构前一致（原来 ③′ 也在 ④ 之前）。</para>
        /// </remarks>
        private void PerformAttackHit(float damage, float knockback, Vector2 boxSize, float boxOffset, bool heavy = false)
        {
            // 发起方只组织「打哪儿、打多大」，最终伤害由伤害处理模块按当前规则算。
            Vector2 center = (Vector2)transform.position + new Vector2(_mover.Facing * boxOffset, 0f);
            OnAttackStruck?.Invoke(new PlayerAttackCue(center, boxSize, _mover.Facing,
                heavy, heavy ? -1 : AttackComboIndex));

            // 机关开关（拉杆 / 按钮）：打中就翻一下。
            // ⚠️ 单独一条路，**不进伤害管线** —— 开关不是 IDamageable，
            //    所以「伤害 × 失效」不会把机关一起关掉（DamageProcessor 失效时连命中都不算）。
            PressSwitches(center, boxSize);

            if (_combat == null) return;

            int hits = _combat.PerformAttack(this, center, boxSize, damage, knockback);

            // 能量恢复来源之一：命中即回，未命中不给（策划案 §四）。
            if (hits > 0 && _status != null)
                _status.RestoreEnergy(_energyPerHit * hits);
        }

        /// <summary>
        /// 攻击盒里有没有「可被撞击的开关」（拉杆 / 按钮）—— 打中就交给它。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不去 <c>CombatSystem</c></b>：那里是伤害判定（只查受击箱），
        /// 开关不是伤害目标；混进去会连带让「伤害 × 失效」把机关也关掉。</para>
        /// <para>查询与 <c>CityInteractor</c> 同一条做法：不配 mask，靠 <see cref="IContactSwitch"/>
        /// 认对象。开关的碰撞体是 Trigger，所以 <c>useTriggers = true</c>。</para>
        /// <para>一次挥砍只翻一次：同一个开关按引用去重（重击的盒子更宽，容易罩到同一根拉杆）。</para>
        /// </remarks>
        private void PressSwitches(Vector2 center, Vector2 size)
        {
            var filter = new ContactFilter2D { useTriggers = true };
            int count = Physics2D.OverlapBox(center, size, 0f, filter, _switchHits);

            _pressedSwitches.Clear();
            for (int i = 0; i < count; i++)
            {
                var target = _switchHits[i].GetComponentInParent<IContactSwitch>();
                if (target == null || !_pressedSwitches.Add(target)) continue;

                // 传**玩家自己的位置**（不是攻击盒中心）：多档拉杆按「站在它左边 / 中间 / 右边」选档，
                // 攻击盒中心永远在身前 0.8 米，拿它分区会整体偏一档。
                target.ImpactByContact(transform.position);
            }
        }

        // ==================================================================
        //  投掷
        // ==================================================================

        /// <summary>
        /// 投出一把飞刀（白盒外观是纸飞机）。
        /// </summary>
        /// <remarks>
        /// <para><b>本类只决定三件事</b>：从哪出手、朝哪飞、多快。飞行（积分重力 / 扫掠命中 /
        /// 撞到就消失 / 按接触式开关）全在 <see cref="ThrownKnife"/> 里 ——
        /// 想换成石头、回旋镖或敌人投的同一把刀，只要换预制体与 owner，这里不用改。</para>
        /// <para>方向取 <see cref="_mover.Facing"/>（**换算后**的朝向），与推动同一条理由：
        /// 「摩擦 × 反转」生效时按右是往左走，刀也得朝人实际去的方向飞。</para>
        /// <para>冷却没到 / 没配预制体时**静默吞掉**这一次按键，不排队 ——
        /// 与攻击「不合法就丢掉、不回退」的规矩一致。</para>
        /// </remarks>
        private void ThrowKnife()
        {
            if (!HasAbility(PlayerAbility.Throw)) return;
            if (_knifePrefab == null || _knifeCooldownLeft > 0f) return;

            _knifeCooldownLeft = _knifeCooldown;

            Vector2 origin = (Vector2)transform.position + new Vector2(_mover.Facing * _knifeOffset, _knifeHeight);
            ThrownKnife knife = Instantiate(_knifePrefab, origin, Quaternion.identity);
            knife.Launch(this, _combat, new Vector2(_mover.Facing, 0f), _knifeSpeed, _knifeKnockback);
        }

        /// <summary>攻击判定体 + 贴墙探针：调参时直接看得到范围与朝向。</summary>
        private void OnDrawGizmosSelected()
        {
            // 轻击（橙）与重击（红）各画一个 —— 重击的盒更大、偏移更远，
            // 不画出来就没法目视调 _heavyBoxSize / _heavyBoxOffset。
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.8f);
            Vector2 center = (Vector2)transform.position + new Vector2(Facing * _attackOffset, 0f);
            Gizmos.DrawWireCube(center, _attackBoxSize);

            Gizmos.color = new Color(1f, 0.2f, 0.35f, 0.8f);
            Vector2 heavyCenter = (Vector2)transform.position + new Vector2(Facing * _heavyBoxOffset, 0f);
            Gizmos.DrawWireCube(heavyCenter, _heavyBoxSize);

            // 贴墙能力的**中段圆探针**。它的位置是从主碰撞体 bounds 推出来的（没有实体碰撞体），
            // 所以必须画出来才看得见 —— 这也是判断「探针高度合不合适」的唯一直观手段。
            var collider = GetComponent<Collider2D>();
            if (collider == null || _wallClimbProbeRadius <= 0f) return;

            Bounds b = collider.bounds;
            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(new Vector3(b.min.x, b.center.y, transform.position.z), _wallClimbProbeRadius);
            Gizmos.DrawWireSphere(new Vector3(b.max.x, b.center.y, transform.position.z), _wallClimbProbeRadius);
        }

        // ==================================================================
        //  参数打包 / 规则读值
        // ==================================================================

        /// <summary>
        /// 把序列化字段打成 <see cref="MotionTuning"/>。
        /// </summary>
        /// <remarks><b>每物理帧重打包</b>：这样运行期改字段立刻生效，包括调试时用反射改值那条流程。
        /// 几十个浮点的拷贝，50Hz 下可以忽略。</remarks>
        private MotionTuning BuildTuning() => new MotionTuning
        {
            // 攻击期间把「目标水平速度」压低 —— 这就是「出刀时走不快」的全部实现。
            // 注意 MoveSpeedMax 在 VelocitySolver 里是 MoveTowards 的**目标值**，不是硬夹：
            // 已经在高速（冲刺 / 被击退）时不会瞬间被掐到 4，而是按加速度收敛过去。
            MoveSpeedMax         = IsAttacking ? _attackMoveSpeedMax : _moveSpeedMax,
            GroundAccel          = _groundAccel,
            LowFrictionGroundAccel = _lowFrictionGroundAccel,
            AirAccel             = _airAccel,
            AirDrag              = _airDrag,
            FrictionDecel        = _frictionDecel,

            JumpSpeed            = _jumpSpeed,
            AirJumpSpeed         = _airJumpSpeed,
            MaxAirJumps          = HasAbility(PlayerAbility.AirJump) ? _maxAirJumps : 0,
            GravityScale         = _gravityScale,
            FallGravityMultiplier = _fallGravityMultiplier,
            MaxFallSpeed         = _maxFallSpeed,
            JumpCutMultiplier    = _jumpCutMultiplier,
            CoyoteTime           = _coyoteTime,
            JumpBuffer           = _jumpBuffer,

            DashSpeed            = _dashSpeed,
            DashDuration         = _dashDuration,
            DashExitSpeedScale   = _dashExitSpeedScale,
            DashCooldown         = _dashCooldown,
            MaxDashes            = _maxDashes,

            WallClimbEnabled     = _wallClimbEnabled && HasAbility(PlayerAbility.WallClimb),
            WallProbeDistance    = _wallProbeDistance,
            WallClimbProbeRadius = _wallClimbProbeRadius,
            WallSlideSpeed       = _wallSlideSpeed,
            WallSlideImpactDecel = _wallSlideImpactDecel,
            WallJumpPushSpeed    = _wallJumpPushSpeed,
            WallClimbPushSpeed   = _wallClimbPushSpeed,
            WallJumpUpSpeed      = _wallJumpUpSpeed,

            SolidLayer           = _solidLayer,
            BodyBlockLayer       = _bodyBlockLayer,
            GroundProbeDistance  = _groundProbeDistance,
            SurfaceAnchorDistance = _surfaceAnchorDistance,
            SurfaceSlideAngle    = _surfaceSlideAngle,

            WindLayer            = _windLayer,

            KnockbackDecay       = IsHurt ? _hurtKnockbackDecay : _knockbackDecay,

            PushStrength         = _pushStrength,
            PushProbeDistance    = _pushProbeDistance,

            AttackWindup         = _attackWindup,
            AttackActive         = _attackActive,
            AttackRecover        = _attackRecover,
            MaxCombo             = _maxCombo,

            HeavyWindup          = _heavyWindup,
            HeavyActive          = _heavyActive,
            HeavyRecover         = _heavyRecover,
            HeavyCooldown        = _heavyCooldown,
            HeavyDamage          = _heavyDamage,
            HeavyKnockback       = _heavyKnockback,
            HeavyBoxSize         = _heavyBoxSize,
            HeavyBoxOffset       = _heavyBoxOffset,
        };

        /// <summary>当前摩擦缩放（1.0 = 正常）。规则系统未就绪时退化为 1。</summary>
        private static float FrictionScale
        {
            get
            {
                var attrs = RuleSystem.Instance?.Attributes;
                return attrs?.Values.FrictionScale ?? 1f;
            }
        }

        /// <summary>摩擦方向是否被规则反转（摩擦 × 反转）。规则系统未就绪时视为未反转。</summary>
        /// <remarks>
        /// 敌人侧的控制器要读同一个标志才能一起反转 —— 这条规则的语义是「地面给你的力的方向」，
        /// 对所有「按意图移动」的实体都成立（石头这类没有意图的动态物体不响应，见 <c>RuleAffectedBody</c>）。
        /// </remarks>
        private static bool FrictionInverted
        {
            get
            {
                var attrs = RuleSystem.Instance?.Attributes;
                return attrs != null && attrs.Values.FrictionInverted;
            }
        }
    }
}
