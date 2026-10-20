using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 一个实体全部可调的机动参数。每物理帧由 <see cref="PlayerController"/> 从序列化字段重打包一次。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么参数留在 PlayerController、只打包过来</b>：三个资产
    /// （<c>LevelRuntime.prefab</c> / <c>Test_RuleSystem.unity</c> / <c>Test_RuleWheel.unity</c>）
    /// 里存着这些字段的**现值**，搬进 ScriptableObject 会全部丢失，得逐字段核对迁移。
    /// 所以**代码解耦与参数解耦分两步走** —— 这一步资产零改动。</para>
    /// <para><b>为什么是值类型、而且每帧重打包</b>：运行期改字段立刻生效，
    /// 包括调试时用反射改值那条流程。打包成本是几十个浮点拷贝，50Hz 下可以忽略。</para>
    /// <para>⚠️ 按值传参（不用 <c>in</c>）：MCP 的 <c>execute_code</c> 走 codedom 只到 C# 5，
    /// 会把 <c>in</c> 误读成 <c>ref</c> 而编译不过 —— 这点微优化不值得让工具链测不了这些方法。</para>
    /// </remarks>
    public struct MotionTuning
    {
        // ------------------------------------------------------------------
        //  移动
        // ------------------------------------------------------------------

        /// <summary>水平最大速度。按住方向键时速度朝它加速（不是瞬间到）。</summary>
        public float MoveSpeedMax;

        /// <summary>地面水平加速度。到全速的时间 ≈ 最大速度 / 本值。</summary>
        public float GroundAccel;

        /// <summary>
        /// 低摩擦地面（冰面）上的水平加速度。实际加速按脚下抓地系数在它与
        /// <see cref="GroundAccel"/> 之间插值：普通地面用前者，完全打滑用本值。
        /// </summary>
        public float LowFrictionGroundAccel;

        /// <summary>空中水平加速度：能左右改向，但不瞬移。0 = 空中完全锁死横向速度。</summary>
        public float AirAccel;

        /// <summary>空中无输入时的水平阻力。0 = 空中完整保持动量。</summary>
        public float AirDrag;

        /// <summary>地面减速度基准。实际减速 = 本值 × 脚下摩擦 × 摩擦规则缩放。</summary>
        public float FrictionDecel;

        // ------------------------------------------------------------------
        //  跳跃
        // ------------------------------------------------------------------

        /// <summary>第一段起跳速度。跳跃高度 = 本值² / (2 × 有效上升重力)。</summary>
        public float JumpSpeed;

        /// <summary>空中段的起跳速度。比第一段略弱，读起来像「再补一下」。</summary>
        public float AirJumpSpeed;

        /// <summary>空中还能再跳几次。0 = 只有一段跳；1 = 二段跳。</summary>
        public int MaxAirJumps;

        /// <summary>本实体自身的重力倍率（叠在规则改过的全局重力之上）。</summary>
        public float GravityScale;

        /// <summary>下落时的额外重力倍率 —— 落得比升得快，滞空更紧凑。1 = 上下对称。</summary>
        public float FallGravityMultiplier;

        /// <summary>下落速度上限（终端速度）。防止长距离下落越来越快、难以阅读。</summary>
        public float MaxFallSpeed;

        /// <summary>松开跳跃键时向上速度保留的比例 —— 可变跳跃高度。1 = 按固定高度跳。</summary>
        public float JumpCutMultiplier;

        /// <summary>土狼时间：走出平台边缘后仍可起跳的宽限秒数。</summary>
        public float CoyoteTime;

        /// <summary>跳跃缓冲：落地之前提前按下的跳跃仍然生效的秒数。</summary>
        public float JumpBuffer;

        // ------------------------------------------------------------------
        //  冲刺
        // ------------------------------------------------------------------

        /// <summary>冲刺速度的**峰值**。起手与收尾都低于它，见 <see cref="DashRampUp"/>。</summary>
        public float DashSpeed;

        /// <summary>冲刺持续秒数。**距离不是 峰值 × 时长** —— 要按曲线积分，见 <see cref="DashRampUp"/>。</summary>
        public float DashDuration;

        /// <summary>起手加速占冲刺时长的比例：从 <see cref="MoveSpeedMax"/> 线性升到 <see cref="DashSpeed"/>。0 = 瞬间到峰值。</summary>
        public float DashRampUp;

        /// <summary>收尾减速占冲刺时长的比例：从 <see cref="DashSpeed"/> 线性退回 <see cref="MoveSpeedMax"/>。0 = 结束时仍保持峰值。</summary>
        public float DashRampDown;

        /// <summary>冲刺结束时保留多少水平速度。1 = 冲完像被弹射出去。</summary>
        public float DashExitSpeedScale;

        /// <summary>两次冲刺之间的最小间隔。冲刺进行中再按也无效。</summary>
        public float DashCooldown;

        /// <summary>冲刺次数上限，落地回满。0 = 没有冲刺。</summary>
        public int MaxDashes;

        // ------------------------------------------------------------------
        //  贴墙 / 蹬墙
        // ------------------------------------------------------------------

        /// <summary>
        /// <b>贴墙能力总开关</b>：关掉后贴墙不下滑、也蹬不了墙。
        /// </summary>
        /// <remarks>它是**这个实体自己的能力**（序列化字段），不是玩家特权 —— 敌人配一下就有。
        /// 关掉时 <see cref="WallProbeDistance"/> 等参数全部失效，但保留着不碍事。</remarks>
        public bool WallClimbEnabled;

        /// <summary>横向墙面探测距离（米）。**这是「全高」探测**，用于顶墙分量预夹，不是能力判定。</summary>
        public float WallProbeDistance;

        /// <summary>
        /// 贴墙能力的中段圆探针半径（米）。探针位置 = 主碰撞体的**中段高度**、左右两条边缘。
        /// </summary>
        /// <remarks>只认中段的接触 —— 矮台、台阶、头顶横梁都不算墙。
        /// 与 <see cref="WallProbeDistance"/> 是两回事：那个是全高探测，管预夹。</remarks>
        public float WallClimbProbeRadius;

        /// <summary>贴墙时的下滑速度上限。远低于终端速度 —— 贴墙是「缓慢下滑」不是「挂在墙上」。</summary>
        public float WallSlideSpeed;

        /// <summary>贴到墙那一瞬间，下落速度被墙面摩擦吃掉的快慢（单位/秒²）。</summary>
        public float WallSlideImpactDecel;

        /// <summary>【朝墙外按】时弹离墙壁的水平速度 —— 「跳出去」用。</summary>
        public float WallJumpPushSpeed;

        /// <summary>【朝墙按 / 不按】时的推力 —— 「爬升」用。**必须足够小**，
        /// 小到方向键能在人离开墙之前把它拉回来，否则一面墙爬不上去。</summary>
        public float WallClimbPushSpeed;

        /// <summary>蹬墙后的向上速度。调大它是最划算的提升：弧线更高、滞空更久、水平距离白拿。</summary>
        public float WallJumpUpSpeed;

        // ------------------------------------------------------------------
        //  碰撞与位移
        // ------------------------------------------------------------------

        /// <summary>阻挡移动、并用于接地判定的层。地形与墙体都放这里。</summary>
        public LayerMask SolidLayer;
        public LayerMask BodyBlockLayer;

        /// <summary>接地探测距离（米）。</summary>
        public float GroundProbeDistance;

        /// <summary>贴地吸附距离：下坡时把角色按回地面，0 = 关闭。</summary>
        public float SurfaceAnchorDistance;

        /// <summary>可沿其滑行的最大坡面角度。竖直墙是 90°，所以必须小于 90 才会被挡住。</summary>
        public float SurfaceSlideAngle;

        // ------------------------------------------------------------------
        //  风场
        // ------------------------------------------------------------------

        /// <summary>
        /// 风场所在的层（<c>WindZone</c> 挂在上面）。值为 0 = 这个实体不响应风。
        /// </summary>
        /// <remarks>与 <see cref="SolidLayer"/> 一样是**实体自己的能力开关** ——
        /// 不配层的实体吹不动，配了就吹得动。</remarks>
        public LayerMask WindLayer;

        // ------------------------------------------------------------------
        //  推动（重物）
        // ------------------------------------------------------------------

        /// <summary>
        /// 推物体时施加的**力**（牛顿）。0 = 这个实体不会推。
        /// </summary>
        /// <remarks>
        /// ⚠️ 报的是力不是速度：物体那边除以自己的质量才是加速度（判据 ≈ <c>F &gt; μ·m·g</c>）。
        /// 所以「推不推得动」由**箱子的质量**决定，质量规则改的正是那个质量 ——
        /// 本参数不需要跟着规则变。
        /// </remarks>
        public float PushStrength;

        /// <summary>推动探测距离（米）。按住方向顶上去，这个距离内有可推物体就施力。</summary>
        public float PushProbeDistance;

        // ------------------------------------------------------------------
        //  击退
        // ------------------------------------------------------------------

        /// <summary>外部冲量每秒的衰减量。</summary>
        public float KnockbackDecay;

        // ------------------------------------------------------------------
        //  攻击连段
        // ------------------------------------------------------------------

        /// <summary>起手：从按键到「判定开窗」的秒数。</summary>
        public float AttackWindup;

        /// <summary>生效：判定窗的时长。只在开窗那一瞬做一次命中判定，不做持续判定。</summary>
        public float AttackActive;

        /// <summary>收招：这段时间里再按一次就接下一刀，不按就收剑。</summary>
        public float AttackRecover;

        /// <summary>连段最多打几刀。1 = 只有一下，不接。</summary>
        public int MaxCombo;

        // ------------------------------------------------------------------
        //  重击（不进连段的一次性单发）
        // ------------------------------------------------------------------

        /// <summary>起手：从按键到「判定开窗」的秒数。对齐刀刃的视觉接触时刻，见 PlayerController 的说明。</summary>
        public float HeavyWindup;

        /// <summary>生效：判定窗的时长。只在开窗那一瞬做一次命中判定。</summary>
        public float HeavyActive;

        /// <summary>收招：这段时间里出不了第二发。冷却从这里结束时起算。</summary>
        public float HeavyRecover;

        /// <summary>收招结束后的冷却秒数。0 = 没有冷却。</summary>
        public float HeavyCooldown;

        /// <summary>伤害**基础值** —— 由伤害处理模块按当前规则换算，调用方不许自己乘系数。</summary>
        public float HeavyDamage;

        /// <summary>击退基础值。比轻击大，重击才"推得动"。</summary>
        public float HeavyKnockback;

        /// <summary>判定盒尺寸。比轻击大一圈。</summary>
        public Vector2 HeavyBoxSize;

        /// <summary>判定盒相对角色中心的水平偏移（朝前）。</summary>
        public float HeavyBoxOffset;
    }
}
