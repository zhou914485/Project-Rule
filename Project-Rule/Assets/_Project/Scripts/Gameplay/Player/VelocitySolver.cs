using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 速度解算：重力 + 终端速度（④）、贴墙下滑（④′）、水平（⑤）、贴地抵消（⑥）、
    /// 弹簧发射（⑥′）、跳跃落地（⑦⑧）。
    /// </summary>
    /// <remarks>
    /// <para><b>无状态，且不认识任何对象</b>：速度从参数进、从返回值出。
    /// 不持有 <c>Rigidbody2D</c>、不读 <c>RuleSystem</c>、不读 <c>Physics2D.gravity</c>
    /// （重力由 <see cref="MotionFrame.Gravity"/> 传进来）—— 所以它能在 EditMode 里
    /// 直接断言，不需要场景、不需要物理步。</para>
    ///
    /// <para><b>两条轴，最后只合一次</b>：进来时先把速度拆成
    /// <c>alongUp</c>（沿「上」轴的分量）与 <c>lateral</c>（横向，即 <c>x</c>）。
    /// 之后每个逻辑<b>只碰自己那条轴</b>，两条轴各自演化，<see cref="Solve"/> 的最后两行
    /// 才把它们拼回一个向量。所以「一个速度被十几处轮流改写」这件事不存在了。</para>
    ///
    /// <para><b>竖直链是「变化量」的折叠，横向链是「决策」</b>：</para>
    /// <list type="bullet">
    /// <item>竖直方向的每一步（重力 / 终端速度 / 贴墙 / 贴地抵消 / 起跳 / 削速）
    /// 都是<b>叠加性</b>的，各自算一个 Δ 交给调用方 <c>+=</c>。
    /// 它们的 Δ <b>必须按顺序算</b> —— 终端速度要读重力之后的值、削速要读起跳之后的值。
    /// 各算各的再用同一个基线相加会改掉物理行为。</item>
    /// <item>唯一一处「谁覆盖谁」的判定是<b>竖直轴的所有权</b>：墙真的在托着时竖直轴归墙独占，
    /// 重力与终端速度被屏蔽（见 <see cref="VerticalDelta"/>）。</item>
    /// <item>横向是三档<b>互斥</b>的（冲刺接管 / 接地 / 空中），不存在「叠加」，
    /// 所以它返回<b>新的横向速度</b>而不是 Δ —— 硬做成 Δ 反而会掩盖互斥这件事。</item>
    /// </list>
    ///
    /// <para><b>唯一写速度的地方是 <see cref="MotionMover"/></b>：本类只算，不算完就落地。</para>
    ///
    /// <para>⚠️ <b>下面各步的顺序即语义</b>（<c>Doc/玩家动作设计.md</c> §6），
    /// 它决定了各种状态谁覆盖谁。改动前先读那张表。</para>
    ///
    /// <para>⚠️ <b>隐含前提：竖直走 <c>up</c> 轴、横向走 <c>x</c></b>。两条轴要正交，
    /// 得靠 <c>up</c> 是轴对齐的。现在成立 —— <see cref="ContactSensor"/> 里
    /// <c>up = -gravity.normalized</c>，而 <c>GravityRule</c> 只赋 <c>(0,±9.81)</c> 及其倍数、
    /// 从不旋转。将来真要做斜向重力，横向那一档得跟着改走「右」轴。</para>
    ///
    /// <para>按值传参（不用 <c>in</c>）：MCP 的 <c>execute_code</c> 只到 C# 5，
    /// 会把 <c>in</c> 误读成 <c>ref</c> 而编译不过。</para>
    /// </remarks>
    public static class VelocitySolver
    {
        /// <summary>接地反向输入时的最低制动加速度。低于它的抓地力会让转向拖沓到读不出来。</summary>
        private const float MinimumTurnBrakeDecel = 12f;

        /// <summary>
        /// 算一帧的速度。
        /// </summary>
        /// <param name="velocity">上一帧末的速度（<b>不含</b>击退等外部冲量）。</param>
        public static Result Solve(Vector2 velocity, MotionFrame frame, float dt)
        {
            MotionTuning t   = frame.Tuning;
            Vector2      up  = frame.Contact.Up;
            bool         dashing = frame.Dash.Dashing;
            float        facing  = frame.Facing;

            // ── 拆轴：进来就分成两条独立的量，全程各自演化，最后只合一次。
            float alongUp = Vector2.Dot(velocity, up);   // 竖直分量（沿「上」轴）
            float lateral = velocity.x;                  // 横向分量

            // ③ 冲刺收尾：结束那一帧收横向速度。放在重力之前 —— 重力只作用于竖直轴，
            //    两者互不干扰，与原实现的先后一致（原来 EndDash 跑在冲刺计时那一步里）。
            if (frame.Dash.JustEnded) lateral *= t.DashExitSpeedScale;

            // ③ 起冲那一刻定朝向。只改符号来源，不碰速度本身。
            if (frame.Dash.Started && !Mathf.Approximately(frame.Dash.Direction.x, 0f))
                facing = Mathf.Sign(frame.Dash.Direction.x);

            // ── 竖直链：④④′ 先一次定完「谁拥有竖直轴」，再各算自己的变化量。
            bool wallSliding;
            alongUp += VerticalDelta(t, frame, alongUp, dt, dashing, out wallSliding);  // ④④′ 重力 / 终端 / 贴墙

            // ── 横向链：三档互斥，返回新的横向速度（不是变化量）。
            lateral = Horizontal(t, frame, lateral, dt, ref facing);                    // ⑤ 水平
            // ⑤′ 风（水平分量）：以变化量叠加，不受摩擦反转影响（风不是摩擦）。
            //    接地时地面摩擦每帧把它往回拉，两者自然对抗 —— 冰面上风就吹得动人。
            //    冲刺期间不叠加：冲刺的方向由 DashSolver 独占，风不该把它吹歪。
            if (!dashing)
            {
                lateral += frame.WindAccel.x * dt;
                lateral -= lateral * Mathf.Clamp01(frame.WindDrag * dt);   // 空气阻力
            }

            // ── 竖直链续。必须排在横向之后：蹬墙跳会整体接管横向。
            alongUp += SurfaceCancelDelta(frame.Contact, alongUp);                      // ⑥ 贴地抵消
            alongUp += LaunchDelta(frame, alongUp);                                     // ⑥′ 弹簧发射
            alongUp += JumpDelta(frame, alongUp, ref lateral, ref facing);              // ⑦ 起跳
            alongUp += JumpCutDelta(t, frame, alongUp);                                 // ⑧ 松键削速

            // ── 合成：本类唯一一次组装成向量。
            Vector2 result = SetAlong(velocity, up, alongUp);   // 竖直沿「上」轴写
            result.x       = lateral;                           // 横向直接写 x

            return new Result(result, facing, wallSliding);
        }

        // ==================================================================
        //  竖直链：各算各的变化量
        // ==================================================================

        /// <summary>
        /// ④ 重力这一帧贡献的变化量。冲刺期间<b>不积分</b>（竖直速度原样保留，既不归零也不加速）。
        /// **在风场里统一用上升重力**（不吃下落重力加成）。
        /// </summary>
        /// <remarks>
        /// <para>沿「上」轴的分量取 <c>Dot(gravity, up)</c>。因为 <c>up = -gravity.normalized</c>，
        /// 重力与「上」轴恒平行、垂直分量恒为 0，所以这个投影是无损的 —— 与原实现
        /// 「整个 gravity 向量加到 velocity 上」逐位等价。</para>
        /// <para>⚠️ <b>为什么风场里要统一重力</b>：「上升 ×1、下落 ×2」是**方向依赖**的力 ——
        /// 它不是保守力，每上下一个来回都会净做正功 <c>(g_落 − g_升) × 行程</c>。
        /// 于是风里会出现两个平衡点（上升 / 下落各一个），中间是一个**不稳定的浮空带**：
        /// 玩家在两个平衡点之间**自激振荡**，阻尼再大也只能把振幅压小、消不掉。
        /// 统一成上升重力后力场变保守，只有一个平衡点，阻尼能把振荡真正收掉。</para>
        /// </remarks>
        private static float GravityDelta(MotionTuning t, Vector2 gravity, Vector2 up,
                                          float alongUp, float dt, bool dashing, bool inWindField)
        {
            if (dashing) return 0f;

            // 落得比升得快，滞空更紧凑。风场里不分上升 / 下落 —— 见上面的说明。
            float scale = inWindField ? 1f : (alongUp < 0f ? t.FallGravityMultiplier : 1f);
            return Vector2.Dot(gravity, up) * t.GravityScale * scale * dt;
        }

        /// <summary>④ 终端速度：下落超过上限时把差值补回来。<b>返回 0 表示没触发。</b></summary>
        private static float TerminalFallDelta(MotionTuning t, float alongUp)
            => alongUp < -t.MaxFallSpeed ? -t.MaxFallSpeed - alongUp : 0f;

        /// <summary>
        /// ④④′ 竖直轴这一帧的变化量：重力、终端速度、墙面摩擦。
        /// </summary>
        /// <remarks>
        /// <para><b>谁拥有竖直轴在这里一次定完</b>：墙真的在托着时，竖直轴归墙<b>独占</b> ——
        /// 重力与终端速度被<b>屏蔽</b>，只剩墙面摩擦把速度收向 <c>WallSlideSpeed</c>。
        /// 这就是文档那句「贴到墙……收到 <c>_wallSlideSpeed</c>，之后等速缓降」的实现。</para>
        ///
        /// <para>⚠️ <b>为什么屏蔽而不是事后扣掉</b>：重力加速度是
        /// <c>9.81 × GravityScale × FallGravityMultiplier</c>（默认 48.8 m/s²，2026-10-20 前是 75.1），
        /// 与 <c>WallSlideImpactDecel</c>（默认 60）同一量级。不屏蔽的话两者对拉，净值只剩
        /// <b>11.2 m/s²</b>，收向 −6 要 1 秒以上（旧参数下重力反而更大，净值向下 15.1，
        /// 实测速度稳定在 −28.8，贴墙等于没减速）。屏蔽之后 0.4 秒收到 −6 并保持。</para>
        ///
        /// <para>⚠️ <b>屏蔽必须带「墙真的在托着」这个条件</b>：<see cref="wallSliding"/> 只表示
        /// 「贴墙且在往下」，速度在 <c>−WallSlideSpeed</c> 与 0 之间时墙<b>并不吸收</b>。
        /// 只判 <c>wallSliding</c> 就屏蔽重力，人会<b>浮在空中</b>。</para>
        ///
        /// <para>⚠️ <b>判定要在算重力之前做</b>：否则墙面收速会从「已经被重力拉过」的值起算，
        /// 屏蔽就只是事后扣一个数，等于没屏蔽。</para>
        /// </remarks>
        private static float VerticalDelta(MotionTuning t, MotionFrame frame, float alongUp, float dt,
                                           bool dashing, out bool wallSliding)
        {
            Vector2 move = frame.Input.Move;

            // ⚠️ 用**中段**探针（Climb），不是全高的 Wall —— 矮台 / 台阶不该触发贴墙下滑。
            bool pressingIntoWall = (frame.Contact.ClimbLeft && move.x < -0.01f)
                                 || (frame.Contact.ClimbRight && move.x > 0.01f);

            // 贴墙能力关掉时，本帧根本不算「在贴墙」—— 竖直轴照常归重力。
            wallSliding = t.WallClimbEnabled
                       && !frame.Contact.Grounded && !dashing && alongUp <= 0f && pressingIntoWall;

            // 墙在托着：竖直轴归它独占，重力与终端速度不参与。
            // ⚠️ 边界用 `<=` 而不是 `<`：速度正好落在 WallSlideSpeed 上时也算「在托着」。
            //    用 `<` 的话那一帧判定为「没托着」→ 重力把它推下去 → 墙面只按 ImpactDecel 拉回一点，
            //    于是速度在 WallSlideSpeed 与 WallSlideSpeed+重力步 之间来回抖（实测 −6.0 ↔ −7.5）。
            if (wallSliding && alongUp <= -t.WallSlideSpeed)
                return Mathf.MoveTowards(alongUp, -t.WallSlideSpeed, t.WallSlideImpactDecel * dt) - alongUp;

            // 否则重力照常积分，再压终端速度（终端速度要读重力之后的值）。
            float delta = GravityDelta(t, frame.Gravity, frame.Contact.Up, alongUp, dt, dashing,
                                       frame.WindInField)
                        + WindVerticalDelta(frame, alongUp, dt, dashing);
            return delta + TerminalFallDelta(t, alongUp + delta);
        }

        /// <summary>
        /// ④′ 风的竖直分量这一帧贡献的变化量（沿「上」轴投影）：**加速度 + 空气阻力 + 竖直阻尼**。
        /// **与重力同待遇**：冲刺期间不积分 —— 冲刺是一条平直横线，竖直方向不接受任何外力。
        /// </summary>
        /// <remarks>
        /// <para>阻力项把速度朝 0 收：「停留位置」靠的就是它 —— 只有力的平衡而没有任何耗散时，
        /// 从低处进来的物体会带着一路攒下的动能冲过平衡点（实测出场速度 18 m/s，永远停不住）。</para>
        /// <para><b>竖直阻尼（<see cref="MotionFrame.WindVerticalDamping"/>）与空气阻力叠加</b>，
        /// 只在这条竖直链里读：它就是「y 方向速度逐渐减少」的旋钮。只有各向同性阻力时竖直方向欠阻尼
        /// （阻尼比 ≈ 0.26），人会绕着平衡高度上下弹好几次；加一项竖直阻尼把这笔动能收掉。
        /// 横向链（⑤′）**不读它** —— 那是空气阻力的事。</para>
        /// <para>衰减量取 <c>Clamp01(系数 × dt)</c> 而不是裸乘：系数被调得极大时也只是
        /// 「这一帧速度归零」，不会过冲成反向。</para>
        /// <para>贴墙下滑时本函数**根本不会被调用到**：竖直轴归墙独占的那个分支在上面提前
        /// <c>return</c> 了。这就是「重力在哪不生效，风就在哪不生效」的全部实现 ——
        /// 想改成「风吹得动贴墙的人」，把风从那条 return 里放出来即可。</para>
        /// <para>终端速度不夹风：它只压下落（<c>alongUp &lt; -MaxFallSpeed</c>），
        /// 风把人往上送多少就是多少 —— 被吹飞也是一条合法的出场方式。</para>
        /// </remarks>
        private static float WindVerticalDelta(MotionFrame frame, float alongUp, float dt, bool dashing)
        {
            if (dashing) return 0f;

            float delta = Vector2.Dot(frame.WindAccel, frame.Contact.Up) * dt;

            // 阻尼：朝 0 收敛（与速度反向）。空气阻力 + 竖直阻尼，两者叠加。
            float damping = frame.WindDrag + frame.WindVerticalDamping;
            delta -= alongUp * Mathf.Clamp01(damping * dt);
            return delta;
        }

        /// <summary>⑥ 贴地时抵消朝地面累积的速度。<b>返回 0 表示不在接地或本来就在上升。</b></summary>
        /// <remarks>不抵消的话，站在地上每一帧都会往地里攒一点速度，离开地面的那一瞬间才一次性弹出来。</remarks>
        private static float SurfaceCancelDelta(ContactSensor.State contact, float alongUp)
            => contact.Grounded && alongUp < 0f ? -alongUp : 0f;

        /// <summary>
        /// ⑥′ 弹簧发射：脚下的面（弹簧平台）正在把接触者推出去时，竖直速度归它接管。
        /// <b>返回 0 表示没被弹、或已经比它快了。</b>
        /// </summary>
        /// <remarks>
        /// <para><b>只往上接管</b>：弹簧报的速度比当前速度小就不动它。这条判据让「弹射」不会变成
        /// 「减速」—— 正在以更快的速度上升时踩到弹簧，不会被它拽慢。</para>
        /// <para><b>排在起跳之前</b>：同一帧既被弹又按跳时，玩家的意图（跳跃）优先 ——
        /// 起跳的 Δ 会把竖直速度整体重写掉。</para>
        /// <para>⚠️ 发射速度是**世界坐标**向量，这里沿「上」轴投影。重力反转时
        /// <c>Contact.Up</c> 朝下，点积为负 —— 弹簧不生效。这是有意的：锚点钉在世界下方，
        /// 重力反转后人「落」在天花板上，此时把它往锚点方向推只会往地里压。</para>
        /// </remarks>
        private static float LaunchDelta(MotionFrame frame, float alongUp)
        {
            float launch = Vector2.Dot(frame.Contact.LaunchVelocity, frame.Contact.Up);

            // ⚠️ launch <= 0 必须先短路：「没在发射」时接口给的是 0（见 ISpringSurface），
            //    而下落中 alongUp 是负的 —— 少了这一条，`0 > alongUp` 恒成立，
            //    每帧把竖直速度拉回 0，角色**永远悬在空中、不落地**（实测踩到过）。
            //    它同时覆盖「弹簧方向与重力相反」（点积为负），与上面那条说明一致。
            if (launch <= 0f) return 0f;

            return launch > alongUp ? launch - alongUp : 0f;
        }

        /// <summary>
        /// ⑦ 起跳贡献的变化量。蹬墙跳额外接管横向与朝向。
        /// </summary>
        /// <remarks>速度大小由 <see cref="JumpSolver"/> 定好放在结论里，本函数只负责把它落到分量上。</remarks>
        private static float JumpDelta(MotionFrame frame, float alongUp,
                                       ref float lateral, ref float facing)
        {
            switch (frame.Jump.Kind)
            {
                case JumpSolver.JumpKind.Wall:
                    // 蹬墙跳：朝墙外弹开 + 向上。**横向整体重写**（旧的水平速度作废），
                    // 竖直沿「上」轴写，所以重力反转时它照样是「离开墙、朝世界的上方去」。
                    lateral = frame.Jump.PushSpeed;
                    facing  = Mathf.Sign(frame.Jump.PushSpeed);   // 朝向跟着弹开方向转，攻击判定才对得上前方
                    return frame.Jump.AlongUpSpeed - alongUp;

                case JumpSolver.JumpKind.Ground:
                case JumpSolver.JumpKind.Air:
                    return frame.Jump.AlongUpSpeed - alongUp;

                default:
                    return 0f;
            }
        }

        /// <summary>⑧ 可变跳跃高度：提前松键就把上升速度削掉，跳得矮一些。</summary>
        /// <remarks>只有真在上升时才削 —— 下落中松键不该把它往下拽。</remarks>
        private static float JumpCutDelta(MotionTuning t, MotionFrame frame, float alongUp)
            => frame.Jump.JustCut && alongUp > 0f ? alongUp * (t.JumpCutMultiplier - 1f) : 0f;

        // ==================================================================
        //  横向链：一次决策
        // ==================================================================

        /// <summary>
        /// ⑤ 横向速度：这一帧谁拥有横向 —— 冲刺接管，否则接地逻辑，否则空中逻辑。
        /// </summary>
        /// <remarks>三档<b>互斥</b>，所以返回的是「该是多少」而不是「变了多少」。</remarks>
        private static float Horizontal(MotionTuning t, MotionFrame frame, float lateral, float dt,
                                        ref float facing)
        {
            Vector2 move = frame.Input.Move;

            if (frame.Dash.Dashing)
            {
                // 冲刺**只改水平速度**：竖直方向的大小不动（那是重力与跳跃的领地）。
                // 大小按梯形曲线走（起手加速 / 中段峰值 / 收尾减速），见 DashSolver.ResolveDashSpeed。
                return frame.Dash.Direction.x * DashSolver.ResolveDashSpeed(t, frame.Dash.Progress);
            }

            if (frame.Contact.Grounded)
            {
                float frictionScale = frame.FrictionScale;    // 摩擦规则缩放：加速与减速都要乘

                if (Mathf.Abs(move.x) > 0.01f)
                {
                    // 摩擦反转（摩擦 × 反转）：摩擦力的方向翻转 → 地面把你往「按键的反方向」送，
                    // 于是按右就往左走。**只作用于接地时** —— 语义是摩擦力的方向，空中没有摩擦。
                    // 所以跳跃可以「逃出」反转区。这也是它与被砍掉的「方向」属性的区别：
                    // 改的是世界给你的力，不是你的按键映射（策划案 D3）。
                    float direction = ResolveMoveDirection(move.x, frame.FrictionInverted);
                    facing = Mathf.Sign(direction);

                    // 反向输入先按抓地力刹到零，再加速反向；低摩擦面因此保留惯性。
                    if (lateral * direction < -0.01f)
                    {
                        float turnBrake = Mathf.Max(t.FrictionDecel * frame.Contact.GroundFriction,
                                                    MinimumTurnBrakeDecel) * frictionScale;
                        return Mathf.MoveTowards(lateral, 0f, turnBrake * dt);
                    }

                    // 加速也吃脚下的材质：冰面上起步打滑（明显更慢），普通地面照旧。
                    // 与减速共用同一个抓地系数，所以「摩擦 × 加强」能把冰面拉回普通地面，
                    // 「摩擦 × 减弱」也会让普通地面的起步变迟缓。
                    float accel = Mathf.Lerp(t.LowFrictionGroundAccel, t.GroundAccel,
                                             Mathf.Clamp01(ResolveGrip(frame.Contact, frictionScale)));

                    return Mathf.MoveTowards(lateral, direction * t.MoveSpeedMax, accel * dt);
                }

                // 减速度 = 基准 × 抓地力（地形摩擦 × 摩擦规则缩放）。
                // 两者相乘：地形可以做成冰面，规则也能把整个世界变成冰面。
                // ⚠️ 反转**不翻这里的符号** —— 减速力反向会变成"越滑越快"，直接失控。
                float grip = ResolveGrip(frame.Contact, frictionScale);

                return Mathf.MoveTowards(lateral, 0f, t.FrictionDecel * grip * dt);
            }

            if (Mathf.Abs(move.x) > 0.01f)
            {
                facing = Mathf.Sign(move.x);
                return Mathf.MoveTowards(lateral, move.x * t.MoveSpeedMax, t.AirAccel * dt);
            }

            return Mathf.MoveTowards(lateral, 0f, t.AirDrag * dt);
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>
        /// 脚下的有效抓地系数：地形摩擦 × 摩擦规则缩放。加速与减速共用同一个值。
        /// </summary>
        /// <remarks>
        /// <para>「摩擦加强」要把**完全打滑**的地面拉回普通地面：冰面 ×加强 → 抓地 1（= 普通地面）。
        /// 纯乘法做不到（0 × 3 还是 0），所以加强时按「离完全打滑还差多少」补一份。
        /// 「减弱」刻意不补：冰面 ×减弱 仍然是冰面，符合"减弱拿冰面没办法"的直觉。</para>
        /// </remarks>
        private static float ResolveGrip(ContactSensor.State contact, float frictionScale)
        {
            float grip = contact.GroundFriction * frictionScale;

            if (frictionScale > 1f)
            {
                grip += 1f - Mathf.Min(contact.GroundFriction, 1f);
            }

            return grip;
        }

        /// <summary>把速度沿 <paramref name="axis"/> 的分量设为 <paramref name="value"/>，其余分量不动。</summary>
        private static Vector2 SetAlong(Vector2 velocity, Vector2 axis, float value)
            => velocity - axis * Vector2.Dot(velocity, axis) + axis * value;

        /// <summary>
        /// 把原始横向输入换算成「本帧该往哪个方向走」。
        /// </summary>
        /// <remarks>
        /// <para>摩擦反转时取反 —— 这是「摩擦 × 反转」在移动上的全部实现。</para>
        /// <para>抽成纯函数是为了**可测**：真机验证要靠真按键，而注入按键在编辑器失焦时会被
        /// Input System 重置；手动调 <c>FixedUpdate</c> 又会因为里面的 <c>Rigidbody2D.Slide</c>
        /// 在物理步之外调用而卡死。所以把决策留在这里单独断言。</para>
        /// </remarks>
        public static float ResolveMoveDirection(float rawInput, bool frictionInverted)
            => frictionInverted ? -rawInput : rawInput;

        /// <summary>
        /// 解算结果。
        /// </summary>
        public readonly struct Result
        {
            /// <summary>解算后的速度。**这是唯一一份被算出来的速度。**</summary>
            public readonly Vector2 Velocity;

            /// <summary>朝向：+1 右，−1 左。取的是**换算后**的方向，所以「摩擦 × 反转」
            /// 生效时按右往左走，这里自动是 −1。</summary>
            public readonly float Facing;

            /// <summary>本帧是否在贴墙缓降。</summary>
            public readonly bool WallSliding;

            public Result(Vector2 velocity, float facing, bool wallSliding)
            {
                Velocity    = velocity;
                Facing      = facing;
                WallSliding = wallSliding;
            }
        }
    }
}
