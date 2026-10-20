using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 接触：每物理帧的接地 / 左墙 / 右墙 / 脚下摩擦系数探测（处理顺序的 ①），
    /// 顺带解析「上」方向。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么「上」方向住在这里</b>：探测得先知道哪边是下 —— 地面扫描朝 <c>-Up</c>、
    /// 墙面扫描朝世界左右。把它拆出去只会让每个调用方多传一个参数进来。
    /// 「上」= 重力反方向；重力失效（零向量）时沿用上一次的方向，避免退化成零向量。</para>
    /// <para><b>只查刚体自身的全部碰撞体</b>，不需要额外的地面检测子物体。</para>
    /// <para>只做判定，不改速度、不读输入、不读规则 —— 规则改的是全局重力，
    /// 本类只是把结果读出来。</para>
    /// </remarks>
    public sealed class ContactSensor
    {
        private readonly Rigidbody2D _body;
        private readonly Collider2D  _bodyCollider;      // 主碰撞体：中段探针的几何来源
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];
        private readonly RaycastHit2D[] _wallHits   = new RaycastHit2D[4];
        private readonly Collider2D[]   _climbHits  = new Collider2D[4];

        private bool    _grounded;
        private bool    _wallLeft;
        private bool    _wallRight;
        private bool    _ceiling;
        private bool    _climbLeft;
        private bool    _climbRight;
        private bool    _wasGrounded;
        private float   _groundFriction = 1f;
        private Collider2D _groundCollider;
        private Vector2 _up = Vector2.up;
        private Vector2 _carrierVelocity;
        private Vector2 _launchVelocity;

        public ContactSensor(Rigidbody2D body, Collider2D bodyCollider)
        {
            _body         = body;
            _bodyCollider = bodyCollider;
        }

        /// <summary>是否站在支撑面上。</summary>
        public bool Grounded => _grounded;

        /// <summary>脚下支撑面的碰撞体（静态瓦片 / 移动平台 / 弹簧……）。没探到时为 <c>null</c>。</summary>
        /// <remarks>安全落点记忆用它判断「踩的是不是静态瓦片地面」；它只是本帧的读数，不缓存跨帧。</remarks>
        public Collider2D GroundCollider => _groundCollider;

        /// <summary>
        /// 左侧是否贴着竖墙。<b>全高探测</b>（整个碰撞体的形状往外扫）—— 只用于顶墙分量预夹。
        /// </summary>
        /// <remarks>贴墙**能力**的判据是 <see cref="ClimbLeft"/>，不是这个。</remarks>
        public bool WallLeft => _wallLeft;

        /// <summary>
        /// 右侧是否贴着竖墙。<b>全高探测</b> —— 只用于顶墙分量预夹。
        /// </summary>
        public bool WallRight => _wallRight;

        /// <summary>
        /// 左侧**中段**是否贴着可爬的竖墙 —— <b>贴墙下滑 / 蹬墙跳的判据</b>。
        /// </summary>
        /// <remarks>只认中段的接触，所以矮台 / 台阶 / 头顶横梁都不算墙。</remarks>
        public bool ClimbLeft => _climbLeft;

        /// <summary>右侧**中段**是否贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳的判据。</summary>
        public bool ClimbRight => _climbRight;

        /// <summary>脚下这块地的摩擦系数（来自它的 <c>PhysicsMaterial2D</c>）。</summary>
        public float GroundFriction => _groundFriction;

        /// <summary>脚下这块地自己正在走的速度（会动的支撑面），静止地形为 <see cref="Vector2.zero"/>。</summary>
        public Vector2 CarrierVelocity => _carrierVelocity;

        /// <summary>脚下的面正在**把接触者推出去**时的速度（弹簧平台 / 弹跳板），平时为 <see cref="Vector2.zero"/>。</summary>
        public Vector2 LaunchVelocity => _launchVelocity;

        /// <summary>当前「上」方向 = 重力反方向。</summary>
        public Vector2 Up => _up;

        /// <summary>
        /// 跑一次探测。**每物理帧一次**，且必须在所有消费接触状态的步骤之前。
        /// </summary>
        public State Sense(MotionTuning tuning)
        {
            UpdateUp();

            _wasGrounded = _grounded;
            _grounded  = ProbeGround(tuning);
            _wallLeft  = ProbeWall(Vector2.left, tuning);
            _wallRight = ProbeWall(Vector2.right, tuning);
            _ceiling   = ProbeCeiling(tuning);

            // 中段探针：只认「身体中段那一小块」旁边的墙（贴墙能力的判据）。
            Bounds b = _bodyCollider.bounds;
            float climbRadius = ClimbProbeRadius(tuning);
            _climbLeft  = ProbeClimb(new Vector2(b.min.x,  b.center.y), climbRadius, tuning);
            _climbRight = ProbeClimb(new Vector2(b.max.x,  b.center.y), climbRadius, tuning);

            return new State
            {
                Grounded       = _grounded,
                WallLeft       = _wallLeft,
                WallRight      = _wallRight,
                Ceiling        = _ceiling,
                GroundFriction = _groundFriction,
                GroundCollider = _groundCollider,
                CarrierVelocity = _carrierVelocity,
                LaunchVelocity  = _launchVelocity,
                Up             = _up,
                JustLanded     = _grounded && !_wasGrounded,
                ClimbLeft      = _climbLeft,
                ClimbRight     = _climbRight,
            };
        }

        /// <summary>
        /// 头顶有没有天花板：沿「上」方向做一次碰撞体扫描，找**法线朝下**的接触。
        /// </summary>
        /// <remarks>
        /// <para>用途只有一个：让 <see cref="MotionMover"/> 在起跳顶到天花板时把向上的速度夹掉。</para>
        /// <para>⚠️ 不夹的话，<c>Rigidbody2D.Slide</c> 会把位移**截到「缝」那么长**，
        /// 但 <c>remainingVelocity</c> 照样报 <c>(0,0)</c> —— 于是 <c>MotionMover</c> 的 ⑩
        /// 以为「移动成功了」，不清零向上的速度。表现是**顶着天花板挂住**，
        /// 直到重力把那点上升速度吃光才掉下来（实测：起跳 16.5 要 0.22 秒，读起来像「粘住」）。</para>
        /// </remarks>
        private bool ProbeCeiling(MotionTuning tuning)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            int count = _body.Cast(_up, filter, _groundHits, tuning.GroundProbeDistance);
            for (int i = 0; i < count; i++)
            {
                // 法线朝下才是天花板；朝上的是支撑面，朝水平的是墙。
                if (Vector2.Dot(_groundHits[i].normal, _up) < -0.5f) return true;
            }
            return false;
        }

        /// <summary>
        /// 实际使用的中段探针半径：**必须比「顶墙预夹距离」更大一截**。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 这不是留余量，是**必须**。⑨′ 的顶墙预夹（<c>MotionMover</c>）在离墙
        /// <c>WallProbeDistance</c>（默认 0.08 米）以内就把横向速度清零，所以玩家**永远停在离墙
        /// 0 ~ 0.08 米**的地方，根本摸不到墙。探针半径比它小（原默认 0.06）时，停在这条带里的人
        /// <c>WallLeft</c> 为真、<c>ClimbLeft</c> 为假 —— 既不贴墙下滑也不蹬墙跳，而预夹又每帧清速
        /// 让他靠不过去，**形成死锁**。</para>
        ///
        /// <para>实测证据（2026-10-18，瓦片关卡贴左墙连续按跳）：
        /// <c>gapL=0.081 wallL=True climbL=False</c>，x 坐标 30 秒纹丝不动。</para>
        ///
        /// <para>取 <c>WallProbeDistance + 0.03</c>：预夹距离已含接触偏移，实测差 0.001~0.01，
        /// 3 厘米足够覆盖。注意这**不改变**「只认中段」这条语义（矮台 / 台阶仍然不算墙）。</para>
        /// </remarks>
        private static float ClimbProbeRadius(MotionTuning tuning)
            => Mathf.Max(tuning.WallClimbProbeRadius, tuning.WallProbeDistance + 0.03f);

        /// <summary>
        /// 中段圆探针：在指定点做一个圆重叠查询，看旁边有没有可爬的竖墙。
        /// </summary>
        /// <remarks>
        /// <para>为什么不复用 <see cref="ProbeWall"/> 的 Cast：那个是**整个碰撞体**往外扫，
        /// 「下半身贴着矮台」也会算成墙 —— 会在平台上误触发贴墙下滑。
        /// 能力判定只认**中段那一小块**，所以矮台 / 台阶 / 头顶横梁都不算墙。</para>
        /// <para>用 <c>SolidLayer</c> 过滤，天然排除玩家自己（玩家在 Default 层，不在 Ground 层）。</para>
        /// <para>⚠️ 它**不读法线**：圆探针只回答「那个点旁边有没有东西」，没有方向可判 ——
        /// 这正是它比 Cast 简单的地方（Cast 那套「法线要顶着扫描方向」的坑没有了）。</para>
        /// </remarks>
        private bool ProbeClimb(Vector2 point, float radius, MotionTuning tuning)
        {
            if (radius <= 0f) return false;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            return Physics2D.OverlapCircle(point, radius, filter, _climbHits) > 0;
        }

        /// <summary>
        /// 查脚下**那一格瓦片**自己声明的摩擦系数（<see cref="RuleGame.Gameplay.Scene.SurfaceTile"/>）。
        /// </summary>
        /// <remarks>
        /// <para>命中的碰撞体是 Tilemap 时，把它换算成格子去取瓦片；不是 Tilemap（普通碰撞体）就返回
        /// <c>NaN</c> 表示「这块地不表态」，交给碰撞体的材质决定。</para>
        /// <para>⚠️ 探测点取在**接触点往里一点**：命中点正好压在格子的上边界上，
        /// 直接用 <c>WorldToCell</c> 会掉进上面那一格（空的），读到 null。</para>
        /// </remarks>
        private static float ReadTileFriction(RaycastHit2D hit)
        {
            if (!(hit.collider is UnityEngine.Tilemaps.TilemapCollider2D tilemapCollider)) return float.NaN;

            var map = tilemapCollider.GetComponent<UnityEngine.Tilemaps.Tilemap>();
            if (map == null) return float.NaN;

            Vector2 probe = hit.point - hit.normal * 0.05f;
            var tile = map.GetTile(map.WorldToCell(probe));
            return tile is RuleGame.Gameplay.Scene.SurfaceTile surface ? surface.Friction : float.NaN;
        }

        /// <summary>沿当前「上」方向的反向做一次碰撞体扫描，看脚下有没有支撑面。</summary>
        /// <remarks>
        /// <para>顺带记住脚下这块地的摩擦系数（<see cref="_groundFriction"/>），供减速使用；
        /// 以及它是哪一种**特殊支撑面**：会动的（<see cref="IMovingSurface"/>，承载）
        /// 和会弹的（<see cref="ISpringSurface"/>，发射）。</para>
        /// <para>⚠️ 脚下是移动平台 / 弹簧时，这里**照常算接地** —— 承载与发射都不是让玩家脱离地面，
        /// 而是地面本身带着他走 / 把他推走。判据与普通地面完全一致。</para>
        /// </remarks>
        private bool ProbeGround(MotionTuning tuning)
        {
            _groundFriction  = 1f;                  // 没探到面、或那块地没挂材质 → 按普通地面算
            _groundCollider  = null;                 // 没探到面 → 没有支撑体
            _carrierVelocity = Vector2.zero;        // 没探到会动的面 → 没人带
            _launchVelocity  = Vector2.zero;        // 没探到会弹的面 → 没人弹

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            int count = _body.Cast(-_up, filter, _groundHits, tuning.GroundProbeDistance);
            for (int i = 0; i < count; i++)
            {
                // 只有法线朝着「上」的接触才算支撑面；墙面法线是水平的，会被排除。
                if (Vector2.Dot(_groundHits[i].normal, _up) <= 0.5f) continue;

                // 地形用 PhysicsMaterial2D 表达材质。但一整张 Tilemap 只有一个碰撞体，
                // 没法逐格设摩擦 —— 所以瓦片自己也可以声明（SurfaceTile，冰面那种）。
                var material = _groundHits[i].collider.sharedMaterial;
                if (material != null) _groundFriction = material.friction;

                float surface = ReadTileFriction(_groundHits[i]);
                if (!float.IsNaN(surface)) _groundFriction = surface;

                // 会动的支撑面（滑板车 / 升降台）把速度带上，由 MotionMover 并进位移。
                // 走接口而不是具体类型：玩家不需要认识任何场景组件。
                var carrier = _groundHits[i].collider.GetComponentInParent<IMovingSurface>();
                if (carrier != null) _carrierVelocity = carrier.Velocity;

                // 会弹的面（弹簧平台）报的是**要推我出去的速度**，由 VelocitySolver 落到竖直轴上。
                // 两个接口各管一半：那个管位置跟随，这个管速度。
                var spring = _groundHits[i].collider.GetComponentInParent<ISpringSurface>();
                if (spring != null) _launchVelocity = spring.LaunchVelocity;

                _groundCollider = _groundHits[i].collider;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 朝 <paramref name="direction"/> 做一次碰撞体扫描，看贴不贴着一面**竖着的墙**。
        /// </summary>
        /// <remarks>
        /// <para>只认竖墙：接触面的法线要**基本垂直于「上」**。法线朝上/朝下的是地面或天花板，
        /// 不算墙 —— 否则站在地上也会被判成「贴着墙」。</para>
        /// <para>⚠️ 还要确认这面墙**真的在探测的那一侧**。<c>Rigidbody2D.Cast</c> 在形状已经
        /// 与某个碰撞体接触时，会把它当成 <c>distance = 0</c> 的命中返回，**不管你朝哪个方向扫**。
        /// 于是贴着右墙时「朝左扫」也会命中那面右墙 —— 不判方向的话两侧都会报「有墙」，
        /// 玩家按左就会往右弹、直接弹进墙里。判据：法线要**顶着**扫描方向。</para>
        /// <para>方向用世界坐标的左右，不用 <see cref="_up"/> 推出来的「左右」：
        /// 重力反转翻的是上下，不翻左右，所以墙永远是竖的。</para>
        /// </remarks>
        private bool ProbeWall(Vector2 direction, MotionTuning tuning)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            int count = _body.Cast(direction, filter, _wallHits, tuning.WallProbeDistance);
            for (int i = 0; i < count; i++)
            {
                Vector2 normal = _wallHits[i].normal;

                if (Mathf.Abs(Vector2.Dot(normal, _up)) > 0.5f) continue;   // 地面 / 天花板
                if (Vector2.Dot(normal, direction) > -0.5f) continue;       // 在另一侧，不是这面

                return true;
            }
            return false;
        }

        /// <summary>「上」方向 = 重力反方向；重力失效（零向量）时沿用上一次的方向。</summary>
        private void UpdateUp()
        {
            if (Physics2D.gravity.sqrMagnitude > 0.0001f)
                _up = -Physics2D.gravity.normalized;
        }

        /// <summary>
        /// 本物理帧的接触快照。
        /// </summary>
        /// <remarks>
        /// <para><see cref="JustLanded"/> 是**边沿**（false → true），表现层靠它播一次落地动作 ——
        /// 靠每帧采样接地状态是拿不到的（0.33 秒的落地只有 4 个物理帧）。</para>
        /// <para>⚠️ 用「公开字段 + 对象初始化器」而不是 <c>init</c> 访问器：本工程的 Unity
        /// 配置里没有 <c>System.Runtime.CompilerServices.IsExternalInit</c>，<c>init</c> 编译不过
        /// （CS0518）。与 <c>JumpSolver.Decision</c> 那几个结论结构保持一致。</para>
        /// </remarks>
        public struct State
        {
            /// <summary>脚下有支撑面。</summary>
            public bool Grounded;

            /// <summary>脚下支撑面的碰撞体（没有支撑面时为 <c>null</c>）。</summary>
            public Collider2D GroundCollider;

            /// <summary>左侧贴着竖墙（**全高**探测，只用于顶墙分量预夹）。</summary>
            public bool WallLeft;

            /// <summary>右侧贴着竖墙（**全高**探测，只用于顶墙分量预夹）。</summary>
            public bool WallRight;

            /// <summary>头顶贴着天花板 —— 用于夹掉向上的速度。</summary>
            public bool Ceiling;

            /// <summary>脚下这块地的摩擦系数（来自它的 <c>PhysicsMaterial2D</c>）。</summary>
            public float GroundFriction;

            /// <summary>
            /// 脚下这块地自己正在走的速度（会动的支撑面 / 移动平台）。静止地形为 <see cref="Vector2.zero"/>。
            /// </summary>
            /// <remarks><see cref="MotionMover"/> 会把它加进交给 <c>Slide</c> 的速度里 ——
            /// 玩家于是与平台**同步位移**。它**不是玩家的速度**，不该写回自己的速度状态。</remarks>
            public Vector2 CarrierVelocity;

            /// <summary>
            /// 脚下的面正在**把接触者推出去**时的速度（弹簧平台 / 弹跳板），平时为 <see cref="Vector2.zero"/>。
            /// </summary>
            /// <remarks>与 <see cref="CarrierVelocity"/> 正好相反：那个是「跟着走」，**不是**自己的速度；
            /// 这个是「被推走」，由 <c>VelocitySolver</c> 当作一步变化量**写进自己的速度** ——
            /// 所以人离开面之后仍然带着它飞。</remarks>
            public Vector2 LaunchVelocity;

            /// <summary>当前「上」方向 = 重力反方向。</summary>
            public Vector2 Up;

            /// <summary>本帧刚落地（false → true 的边沿）。</summary>
            public bool JustLanded;

            /// <summary>左侧**中段**贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳用它，不用 <see cref="WallLeft"/>。</summary>
            public bool ClimbLeft;

            /// <summary>右侧**中段**贴着可爬的竖墙。</summary>
            public bool ClimbRight;
        }
    }
}
