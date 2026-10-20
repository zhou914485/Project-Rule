using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 跳穿平台（单向）：只能从**实心面**落地，从背面可以直接穿过去。
    /// </summary>
    /// <remarks>
    /// <para><b>主动跳穿</b>：站在实心面上**同时按住下 + 按跳**（正常重力下 = S + 空格）就放行一小段时间，
    /// 玩家自己钻下去。跳键这一帧被玩家吃掉，不会先起跳。重力翻转后「下」跟着翻，组合键不变。</para>
    ///
    /// <para><b>可翻转 / 可旋转</b>：实心面默认取「重力反方向」（<c>_followGravity</c>）——
    /// 重力规则一翻，玩家从下方掉上来也能落地。关掉它就用物体的 <c>up</c> 当实心面，
    /// 把物体转 180°（或任意角度）就能改单向。</para>
    ///
    /// <para><b>为什么不用 PlatformEffector2D</b>：玩家是 Kinematic + <c>Rigidbody2D.Slide</c>，
    /// 位移和接地探测都走 shape cast，不交给物理求解器裁决 —— 效应器对它不可靠。
    /// 这里按「玩家在实心面的哪一侧」直接开关碰撞体，判定和移动系统完全一致。</para>
    ///
    /// <para><b>必须放 Ground 层</b>：玩家的 <c>_solidLayer</c> 只扫这一层，放错层就是踩空的空气。</para>
    /// </remarks>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class OneWayPlatform : MonoBehaviour
    {
        [Header("实心面朝向")]
        [Tooltip("开：实心面永远背着重力（重力规则翻转后自动跟着翻）。关：用物体的 up 当实心面，旋转物体即可改单向")]
        [SerializeField] private bool _followGravity = true;

        [Tooltip("把实心面反过来（重力模式 / 旋转模式都生效）")]
        [SerializeField] private bool _invertNormal;

        [Tooltip("要跟着翻面的视觉子物体（留空 = 不翻）。只影响表现，不影响判定")]
        [SerializeField] private Transform _visual;

        [Header("判定")]
        [Tooltip("玩家中心越过实心面多少才恢复实心。太小会贴着平台抖，太大会「陷进去」一点")]
        [SerializeField] private float _reenableMargin = 0.04f;

        [Tooltip("找玩家的重试间隔（玩家可能是运行时生成的）")]
        [SerializeField] private float _searchInterval = 0.5f;

        [Header("主动跳穿")]
        [Tooltip("站在实心面上「按住下 + 按跳」（正常重力下 = S + 空格）就放行")]
        [SerializeField] private bool _dropOnDown = true;

        [Tooltip("放行时长（秒）：这段时间对玩家不实心，够他穿过去")]
        [SerializeField] private float _dropDuration = 0.35f;

        [Header("重物（拼装件）")]
        [Tooltip("勾上后：Dynamic 的拼装件从背面过来会被放行（能穿上来），从实心面过来照常落地。取消勾选 = 对重物是实心墙。")]
        [SerializeField] private bool _passFitBlocks = true;

        private BoxCollider2D _collider;
        private PlayerController _player;
        private float _nextSearch;
        /// <summary>正在放行（等玩家掉到实心面以下）。</summary>
        private bool _dropping;

        /// <summary>这次放行开始的时间（兜底超时用）。</summary>
        private float _dropStart;
        private bool _solid = true;
        private bool _visualFlipped;

        /// <summary>诊断日志节流（静态：所有平台共用，避免刷屏）。</summary>
        private static float _diagNext;

        /// <summary>此刻被本平台放行的件（已经关掉碰撞的那几个）。</summary>
        private readonly System.Collections.Generic.HashSet<Collider2D> _passingBlocks =
            new System.Collections.Generic.HashSet<Collider2D>();

        /// <summary>实心面上方有件压着 —— 这时平台对谁都必须是实心的，否则件会掉下去。</summary>
        private bool _blockAbove;

        /// <summary>此刻对玩家是否实心。</summary>
        public bool IsSolid => _solid;

        /// <summary>主动跳穿的放行时长（<see cref="OneWayField"/> 批量生成时会覆盖它）。</summary>
        public float DropDuration
        {
            get => _dropDuration;
            set => _dropDuration = Mathf.Max(0.05f, value);
        }

        /// <summary>实心面朝向：从平台中心指向「能站在上面」的那一侧。</summary>
        public Vector2 SolidNormal
        {
            get
            {
                if (_followGravity && Physics2D.gravity.sqrMagnitude > 0.0001f)
                {
                    Vector2 n = -Physics2D.gravity.normalized;
                    return _invertNormal ? -n : n;
                }

                Vector2 up = transform.up;
                return _invertNormal ? -up : up;
            }
        }

        /// <summary>场上所有跳穿平台（运行时注册；玩家按「下+跳」时用来问「有没有平台认定我在上面」）。</summary>
        private static readonly System.Collections.Generic.List<OneWayPlatform> s_all
            = new System.Collections.Generic.List<OneWayPlatform>();

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>「这一对碰撞被忽略」的截止时间（平台自己恢复用，避免卡住）。</summary>
        private float _ignoredUntil;

        /// <summary>被忽略的玩家碰撞体（恢复时要用；玩家暂时找不到也能恢复）。</summary>
        private Collider2D _ignoredBody;

        /// <summary>
        /// 直接把「玩家 ↔ 脚下那些跳穿平台」这一对碰撞**忽略掉** <paramref name="seconds"/> 秒。
        /// </summary>
        /// <remarks>
        /// <para>和「平台自己关碰撞体」是两条独立通道：这条不依赖平台的状态机，
        /// 只用 <c>Physics2D.IgnoreCollision</c> 按碰撞体对生效，件和别的实体完全不受影响。</para>
        /// <para>⚠️ 必须先确认玩家**真的站在这块上面**（<see cref="Claims"/>），否则会把整排平台一起忽略掉。</para>
        /// </remarks>
        public static int ReleaseFor(PlayerController player, float seconds)
        {
            if (player == null || seconds <= 0f) return 0;

            var body = player.GetComponent<Collider2D>();
            if (body == null) return 0;

            int count = 0;
            for (int i = 0; i < s_all.Count; i++)
            {
                var p = s_all[i];
                if (p == null || !p.isActiveAndEnabled || p._collider == null) continue;
                if (!p.Claims(player)) continue;
                Physics2D.IgnoreCollision(body, p._collider, true);
                p._ignoredUntil = UnityEngine.Time.time + seconds;
                count++;
            }
            return count;
        }

        /// <summary>
        /// 让**所有脚底下的平台**一起放行（横向不做筛选）。
        /// </summary>
        /// <remarks>
        /// <para>为什么必须是一批：玩家胶囊宽 0.76，而一格只有 0.5 —— 人永远同时压在两格上。
        /// 只放行认领的那一块，接地探测会命中旁边那块（还是实心）→ 人不动，看起来就是「按了没反应」。</para>
        /// <para>横向不筛选的代价是「同一高度整排都会放行」，但玩家本来也只会在自己脚底下这一排，
        /// 而且每条放行都带 0.8~2 秒兜底，不会永久失效。</para>
        /// </remarks>
        private static void BeginDrop(PlayerController player)
        {
            if (player == null) return;

            var body = player.GetComponent<Collider2D>();
            if (body == null) return;

            for (int i = 0; i < s_all.Count; i++)
            {
                var p = s_all[i];
                if (p == null || !p.isActiveAndEnabled || p._collider == null) continue;
                if (!p.IsBelow(player)) continue;

                p._dropping = true;
                p._dropStart = UnityEngine.Time.time;
                p._ignoredUntil = UnityEngine.Time.time + Mathf.Clamp(p._dropDuration * 3f, 0.8f, 2f);
                p._ignoredBody = body;

                Physics2D.IgnoreCollision(body, p._collider, true);
            }
        }

        /// <summary>玩家是不是在实心面这一侧、且在「踩得到」的距离内（**不做横向筛选**）。</summary>
        private bool IsBelow(PlayerController player)
        {
            if (player == null || _collider == null) return false;

            Vector2 normal = SolidNormal;
            Vector2 center = SelfCenter();
            Vector2 delta = (Vector2)player.transform.position - center;
            float side = Vector2.Dot(delta, normal);

            if (side <= 0f || side > Reach(normal, player)) return false;

            // 横向：只放行**玩家胶囊压得到**的那一两格。
            // 不筛的话会把同一高度的整排平台（日志里一直排到 x=200）全部放开。
            var body = player.GetComponent<Collider2D>();
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            float offset = Mathf.Abs(Vector2.Dot(delta, tangent));
            float reachX = HalfExtent(_collider, tangent)
                           + (body != null ? HalfExtent(body, tangent) : 0.4f);

            return offset <= reachX;
        }

        /// <summary>有没有任意一块跳穿平台认定「玩家站在我上面」。给玩家判断要不要吃掉跳键用。</summary>
        public static bool AnyClaims(PlayerController player)
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                var p = s_all[i];
                if (p != null && p.isActiveAndEnabled && p.Claims(player)) return true;
            }
            return false;
        }

        /// <summary>
        /// 玩家是不是站在我上面（**纯几何**，不做物理查询）。
        /// </summary>
        /// <remarks>
        /// <para>为什么不用射线 / 形状扫描：人站在平台上时两者互相压着，
        /// 形状扫描对「起始就重叠」的碰撞体不一定报命中 —— 那正是「狂按空格偶尔才生效」的原因。</para>
        /// <para>判据 = 玩家中心在实心面这一侧，且距离不超过「平台半厚 + 玩家半身高 + 一点余量」。
        /// 余量不能太大：上下叠放的平台，下面那块必须被排除。</para>
        /// </remarks>
        public bool Claims(PlayerController player)
        {
            if (player == null || _collider == null) return false;

            Vector2 normal = SolidNormal;

            // 碰撞体刚建好、还没被物理同步过时 bounds 可能是 0 —— 退回 transform 原点，别拿世界原点算距离。
            Vector2 center = SelfCenter();
            Vector2 delta = (Vector2)player.transform.position - center;
            float side = Vector2.Dot(delta, normal);

            if (side <= 0f || side > Reach(normal, player)) return false;

            // ⚠️ 还要判**横向**：同一排相邻的格子高度一样，只看「在上方」会让左右邻居也认领，
            //    请求被邻居吃掉 → 玩家脚下那块照样实心 → 表现就是「按了没反应 / 要狂按好几次」。
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            float offset = Mathf.Abs(Vector2.Dot(delta, tangent));
            float halfWidth = HalfExtent(_collider, tangent);

            // 半个身位的容差：站在两块交界处时两边都算「踩着」，谁先跑谁放行，都不会卡住。
            return offset <= halfWidth + 0.12f;
        }

        private void Awake()
        {
            _collider = GetComponent<BoxCollider2D>();
        }

        private void FixedUpdate()
        {
            if (_collider == null) return;

            Vector2 normal = SolidNormal;
            UpdateVisual(normal);

            bool solid = EvaluatePlayer(normal);
            UpdateBlocks(normal);

            // 有件压在实心面上时必须是实心的，否则件会连人带板一起掉下去。
            SetSolid(solid || _blockAbove);
        }

        /// <summary>玩家这一侧要不要实心（单向 + 主动跳穿）。</summary>
        private bool EvaluatePlayer(Vector2 normal)
        {
            // ⚠️ 找不到玩家时**保持放行**，不要变成实心：
            // 变成实心就是「两面都过不去」，重力反转 / 暂停 / 玩家重建之后整排平台会卡死在那儿。
            if ((_player == null || !_player.isActiveAndEnabled || !_player.IsInputReady) && !TryFindPlayer())
                return false;

            // 用**碰撞体中心**而不是 transform 原点：平台被摆歪 / 碰撞体带 offset 时原点不可靠。
            Vector2 center = _collider != null ? (Vector2)_collider.bounds.center : (Vector2)transform.position;
            Vector2 delta = (Vector2)_player.transform.position - center;
            float side = Vector2.Dot(delta, normal);

            // 玩家那边开的「忽略这一对碰撞」到期了就恢复，别把人永久卡穿。
            if (_ignoredUntil > 0f && UnityEngine.Time.time > _ignoredUntil)
            {
                _ignoredUntil = 0f;
                var bodyCollider = _ignoredBody != null
                    ? _ignoredBody
                    : (_player != null ? _player.GetComponent<Collider2D>() : null);
                if (bodyCollider != null && _collider != null)
                    Physics2D.IgnoreCollision(bodyCollider, _collider, false);
                _ignoredBody = null;
            }

            // ① 主动跳穿：放行窗口内一律不实心，等玩家掉下去。
            // 放行不是「固定时长」，而是**一直放到玩家真的掉到实心面以下**。
            // 用固定时长的话，窗口结束时人还卡在平台那一格里 → 碰撞体一恢复就把人顶回去，
            // 表现就是「按一次没反应、狂按好几次才下去」（实测）。
            if (_dropping)
            {
                float maxWindow = Mathf.Clamp(_dropDuration * 3f, 0.8f, 2f);

                // 必须等**整个人**都掉到平台下面才恢复：只看「中心过线」的话，
                // 胶囊还有半个身子在平台那一格里，碰撞体一恢复就把人顶回去（这就是「按一次没反应」）。
                var body = _player.GetComponent<Collider2D>();
                float clear = -_reenableMargin - SelfExtent(normal)
                              - (body != null ? HalfExtent(body, normal) : 0.6f);

                if (side < clear || UnityEngine.Time.time - _dropStart > maxWindow)
                {
                    _dropping = false;
                    return true;        // 已经穿过去（或被兜底超时）→ 恢复实心
                }
                return false;
            }

            // 站在**这一块**上 + 这一帧有「下钻」请求（按住下 + 按跳）→ 开一个放行窗口。
            // 请求在玩家身上生成、取走即清零：从上层掉到中层时那个组合已经用过了，
            // 中层不会跟着放行（否则会穿过一层、停在第三层）。
            // 「站在上面」用纯几何的 Claims（不做物理查询 —— 扫描对「贴着」的碰撞体不可靠）。
            if (_dropOnDown && Claims(_player) && _player.ConsumeDownPress())
            {
                // ⚠️ 要放行的是**脚底下所有**平台，不只是认领的那一块：
                //    玩家胶囊宽 0.76 > 一格 0.5，永远同时压着两格；只放行一格的话，
                //    接地探测会命中旁边那块（仍实心）→ 人照样站在那儿不动（实测 vy 恒为 0）。
                BeginDrop(_player);

                // 再把玩家**直接挪到平台另一侧**：这条路与接地钳制 / 相邻格 / 重力方向全部无关，
                // 是唯一能保证「按一次就过去」的做法（见 PlayerController.DisplaceThrough）。
                var bodyCollider = _player.GetComponent<Collider2D>();
                float halfBody = bodyCollider != null ? HalfExtent(bodyCollider, normal) : 0.6f;
                float pass = SelfExtent(normal) + halfBody + 0.06f;
                _player.DisplaceThrough(-normal, pass, _collider);
#if UNITY_EDITOR
                Debug.LogWarning($"[下钻] 平台放行：{name} side={side:0.00} drop={_dropDuration:0.00}");
#endif
                return false;
            }

#if UNITY_EDITOR
            // 玩家发了请求但这一块没接 —— 用来定位「请求发出来了却没反应」。
            if (_player.DropDownHeld && UnityEngine.Time.time >= _diagNext)
            {
                _diagNext = UnityEngine.Time.time + 0.5f;
                Debug.LogWarning($"[下钻] 平台没接：{name} claims={Claims(_player)} side={side:0.00} " +
                                 $"reach={Reach(normal, _player):0.00} dropOnDown={_dropOnDown} solid={_solid} blockAbove={_blockAbove}");
            }
#endif

            // ② 单向：玩家在背面就放行，等他整个越过实心面再恢复，落地才不抖。
            return _solid ? side >= -_reenableMargin : side > _reenableMargin;
        }

        /// <summary>
        /// 拼装件：**只对那一对碰撞体**开关碰撞 —— 件在背面就放行，能穿上来；越过实心面就恢复，能落住。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不用 <c>collider.enabled</c></b>：那是整块平台的开关，件一穿就把玩家一起关了。
        /// <c>Physics2D.IgnoreCollision</c> 按「碰撞体对」生效，件和玩家各管各的。</para>
        ///
        /// <para><b>放行到哪为止</b>：件心越过「实心面 + 半个件厚」才算过来 ——
        /// 提早恢复会在平面上卡一下（件被顶回去），延后恢复件会直接穿下去。</para>
        /// </remarks>
        private void UpdateBlocks(Vector2 normal)
        {
            _blockAbove = false;

            System.Collections.Generic.IReadOnlyList<FitBlock> blocks = FitBlock.All;
            for (int i = 0; i < blocks.Count; i++)
            {
                FitBlock block = blocks[i];
                if (block == null || block.Body == null) continue;

                Collider2D body = block.Body.GetComponent<Collider2D>();
                if (body == null || body.isTrigger) continue;

                // ⚠️ 件用**平台自己的 up**，不跟重力翻 —— 跟重力翻的话，
                // 重力一反转「实心面」就跑到下面去了，从井底升上来的球反而正撞实心面。
                // 跳穿平台对件的语义很简单：**从下面顶上来就放行，落到上面就接住**。
                Vector2 up = transform.up;

                Vector2 delta = (Vector2)block.transform.position - (Vector2)transform.position;
                float side = Vector2.Dot(delta, up);

                // 放行条件 = **正在朝上过来**（v·up > 0）且还没整个越过。
                // 只看位置不行：接触点恰好等于「件心越过平面」那一刻，恢复碰撞会把件卡在面里。
                // 加一段余量，等件真正离开再恢复 —— 停住（v≈0）的件立刻恢复，所以能落住。
                float clearance = HalfExtent(body, up) + HalfExtent(_collider, up);
                bool rising = Vector2.Dot(block.Body.linearVelocity, up) > 0.05f;
                bool pass = _passFitBlocks && rising && side < clearance + 0.30f;

                if (side >= clearance) _blockAbove = true;

                if (pass)
                {
                    if (_passingBlocks.Add(body)) Physics2D.IgnoreCollision(body, _collider, true);
                }
                else if (_passingBlocks.Remove(body))
                {
                    Physics2D.IgnoreCollision(body, _collider, false);
                }
            }
        }

        /// <summary>
        /// <summary>
        /// 平台碰撞体的世界中心 + 沿某个方向的半尺寸。
        /// </summary>
        /// <remarks>
        /// ⚠️ 不用 <c>collider.bounds</c>：工程关着 <c>autoSyncTransforms</c>，运行时刚建的碰撞体
        /// 或者刚被移动过的，<c>bounds</c> 可能还是旧值 / 零 —— 那会让「站在上面」判成 false（实测踩到）。
        /// 用 <c>transform</c> + <c>size</c> 算，跟物理同步状态无关。
        /// </remarks>
        private Vector2 SelfCenter()
        {
            return _collider != null
                ? (Vector2)transform.TransformPoint(_collider.offset)
                : (Vector2)transform.position;
        }

        /// <summary>平台碰撞体沿 <paramref name="dir"/> 的半尺寸（用 size 算，不读 bounds）。</summary>
        private float SelfExtent(Vector2 dir)
        {
            if (_collider == null) return 0.1f;

            Vector2 size = _collider.size;
            Vector3 scale = transform.lossyScale;
            return Mathf.Abs(dir.x) * Mathf.Abs(size.x * scale.x) * 0.5f
                 + Mathf.Abs(dir.y) * Mathf.Abs(size.y * scale.y) * 0.5f;
        }

        /// <summary>站立时玩家中心到平台中心的最大允许距离。</summary>
        private float Reach(Vector2 normal, PlayerController player)
        {
            float reach = _reenableMargin + 0.2f + SelfExtent(normal);

            // ⚠️ 必须用**传进来的**玩家：字段 _player 可能还没解析好（刚实例化 / 刚重生），
            //    那时身体半高会退回 0.3 → Reach 变小 → 明明站在上面却 claims=False（实测踩到过）。
            var target = player != null ? player : _player;
            var body = target != null ? target.GetComponent<Collider2D>() : null;
            reach += body != null ? HalfExtent(body, normal) : 0.6f;

            return reach;
        }

        /// <summary>碰撞体沿 <paramref name="normal"/> 方向的半尺寸（对旋转也成立）。</summary>
        private static float HalfExtent(Collider2D collider, Vector2 normal)
        {
            if (collider == null) return 0f;

            Bounds b = collider.bounds;
            if (b.extents.sqrMagnitude < 0.000001f) return 0.1f;     // 还没实例化时给个兜底

            return Mathf.Abs(normal.x) * b.extents.x + Mathf.Abs(normal.y) * b.extents.y;
        }

        private void SetSolid(bool solid)
        {
            if (_solid == solid) return;

            _solid = solid;
            if (_collider != null) _collider.enabled = solid;

            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, solid);
        }

        private void Start()
        {
            // 读档恢复：存档里这块单向平台是「放行」状态。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.TryGet(SaveStateKey, out string saved) && saved == "0") SetSolid(false);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "solid");

        /// <summary>重力翻转时把视觉一起翻过来：否则玩家会看到「平台还是正的，人却挂在下面」。</summary>
        private void UpdateVisual(Vector2 normal)
        {
            if (_visual == null) return;

            bool flip = normal.y < 0f;
            if (flip == _visualFlipped) return;

            _visualFlipped = flip;
            _visual.localRotation = flip ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
        }

        /// <summary>
        /// 找玩家：取**最近的、已经初始化好的**那个。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 不能只取「随便一个」：运行时可能同时存在**空壳实例**（<c>_input</c> 还是 null、
        /// 位置停在预制体原点、每帧抛 NRE），随机取到它就会表现成「按了完全没反应 / 偶尔才成功」。</para>
        /// <para>取最近的那个同时解决「多玩家」和「空壳在远处」两种情况。</para>
        /// </remarks>
        private bool TryFindPlayer()
        {
            if (UnityEngine.Time.time < _nextSearch) return false;
            _nextSearch = UnityEngine.Time.time + Mathf.Max(0.05f, _searchInterval);

            PlayerController best = null;
            float bestSqr = float.MaxValue;
            Vector2 self = transform.position;

            foreach (var p in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
            {
                if (p == null || !p.isActiveAndEnabled || !p.IsInputReady) continue;

                float d = ((Vector2)p.transform.position - self).sqrMagnitude;
                if (d >= bestSqr) continue;

                bestSqr = d;
                best = p;
            }

            if (best == null) return false;

            _player = best;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 normal = SolidNormal;
            Gizmos.color = _solid ? Color.cyan : new Color(1f, 1f, 1f, 0.35f);
            Vector3 from = transform.position;
            Gizmos.DrawLine(from, from + (Vector3)(normal * 0.8f));
            Gizmos.DrawSphere(from + (Vector3)(normal * 0.8f), 0.06f);
        }
    }
}
