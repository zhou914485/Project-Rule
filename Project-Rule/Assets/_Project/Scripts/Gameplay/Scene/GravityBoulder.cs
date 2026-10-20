using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 巨石：**被重力规则举起来 → 撞响天花板按钮 → 重力恢复后砸回地面碎开**，碎块里露出一把剑。
    /// </summary>
    /// <remarks>
    /// <para><b>完整流程</b>（策划 2026-10-18 提出的机关，四个组件各管一段）：
    /// <list type="number">
    /// <item>「重力 × 反转」把 <c>Physics2D.gravity</c> 翻正 —— 本组件是 <b>Dynamic 刚体</b>，
    /// 一行代码都不写就往上飞（与铁箱同一条路线，见 <c>PushableBody</c>）。</item>
    /// <item>飞到天花板**碰到积木城的 CityButton**（<c>CitySwitch</c> 实现
    /// <see cref="IContactSwitch"/>）—— 按钮被按下，巨石随之**开裂**（<see cref="State.Armed"/>）。
    /// 这是机关的关键一步：<b>没被震裂的石头摔下来只是响一声</b>（见 <see cref="_requireArming"/>）。</item>
    /// <item>规则到期 / 被主动取消，重力恢复 → 巨石落回地面，**落地**速度超过
    /// <see cref="_breakSpeed"/> 就 <see cref="Shatter"/>：撒一圈碎块 + 就地露出剑。</item>
    /// </list></para>
    ///
    /// <para><b>判据是「落地速度」</b>：石头质量固定，只需要回答「摔得够不够狠」——
    /// 速度就够，而且**不受质量规则影响**，于是「质量 × 削弱」不会意外变成第二把钥匙。</para>
    ///
    /// <para>⚠️ <b>「撞到按钮上」绝不算落地</b>：只有接触点在巨石中心**下方**（从下面接住它）
    /// 才认（见 <see cref="IsSupportedFromBelow"/>），而且速度取的是**自己**在物理步进前那一帧的
    /// 向下速度，不读 <c>Collision2D.relativeVelocity</c> —— 后者符号约定一记反，
    /// 举起来顶到天花板那一刻就会被当成落地，石头在半空中就碎了。</para>
    ///
    /// <para><b>为什么必须先撞按钮</b>：否则「举起来再摔下去」就够了，天花板上的按钮纯属装饰，
    /// 玩家不需要把石头举到那个高度。把「震裂」挂在按钮上，两个动作才都成为必要条件。</para>
    ///
    /// <para><b>为什么不用碰撞回调判「站没站住」</b>：这里是 Dynamic 刚体，接触回调可靠
    /// （玩家是 Kinematic + <c>Slide</c>，才需要自己扫探测盒）。所以落地判定直接用
    /// <c>OnCollisionEnter2D</c>，不额外扫。</para>
    ///
    /// <para>⚠️ <b>必须落 <c>Ground</c> 层</b>（层 8）：玩家的 <c>Rigidbody2D.Slide</c> 与接地探测
    /// 只认这一层；「落地」的层过滤（<see cref="_breakLayers"/>）默认也只看它。</para>
    ///
    /// <para>⚠️ <b>不休眠</b>（<c>NeverSleep</c>）：Unity 2D 刚体静止 0.5 秒后休眠，
    /// 而休眠的刚体不响应重力变化 —— 石头会在重力反转后原地悬空。
    /// 与 <c>PushableBody</c> 同一处理。</para>
    ///
    /// <para><b>不参与时间回溯</b>：与其余地图组件（<c>PushableBody</c> / <c>ImpactButton</c> /
    /// <c>BreakableGate</c>）一致 —— 巨石是「世界的地形」，不是实体。</para>
    ///
    /// <para>接线工具：菜单 <c>Tools/规则游戏/地图组件/⑫ 巨石断剑（生成预制体）</c>。
    /// 关卡里的按钮**不打包在预制体里** —— 单独拖一个 <c>Prefabs/BrickCity/CityButton.prefab</c>
    /// 到巨石上方即可（CityButton 的碰撞体是 Trigger，巨石会**穿过**它继续上升，所以天花板要自己摆）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class GravityBoulder : MonoBehaviour, ISwitchTarget
    {
        /// <summary>三层形态。只有 <see cref="Armed"/> 之后才摔得碎。</summary>
        public enum State
        {
            /// <summary>完整的石头。举起来撞按钮之前摔不碎。</summary>
            Intact,

            /// <summary>被按钮震裂了（裂痕出现）。这时落地才会碎。</summary>
            Armed,

            /// <summary>已经碎成块、剑也露出来了。本组件随之停用。</summary>
            Shattered,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("碎裂条件")]
        [Tooltip("落地速度阈值（单位/秒）：砸到地面时**向下**的相对速度超过它才碎。\n" +
                 "参考：从 2 米落下约 6.3、从 4 米约 8.9、从 6 米约 10.8。" +
                 "默认 6 ≈「从 1.8 米以上落下就碎」。")]
        [SerializeField, Min(0f)] private float _breakSpeed = 6f;

        [Tooltip("砸在哪些层上算「落地」。默认 Ground（层 8）—— 地形、铁箱、按钮都在这一层。")]
        [SerializeField] private LayerMask _breakLayers = 1 << 8;

        [Tooltip("是否必须先被按钮震裂才碎。勾上 = 完整机关流程（举起 → 撞按钮 → 落下碎开）；\n" +
                 "取消 = 举起来摔下来就碎，天花板按钮只当普通开关用。")]
        [SerializeField] private bool _requireArming = true;

        [Header("震裂来源")]
        [Tooltip("碰到「接触式开关」（积木城的 CityButton / 摇杆）就被震裂 —— " +
                 "巨石被举起来顶到天花板按钮那一下就是靠它。\n" +
                 "取消勾选 = 只能靠别的机关在代码里调 Arm()。")]
        [SerializeField] private bool _armByContactSwitch = true;

        [Header("锁：没按钮就不动")]
        [Tooltip("勾上 = 开局是**锁死**的静态石头：不吃重力、不被规则举起来、也摔不碎。\n" +
                 "配法：把这个巨石（GravityBoulder 组件）**拖到 CityButton 的 FollowMotor 上** —— 与配 Gate 一样；\n" +
                 "按钮一打开就解锁。防止玩家随手放几次重力反转就把石头提前震碎。")]
        [SerializeField] private bool _startAnchored = true;

        [Tooltip("锁死时的颜色（白盒占位）：比正常暗一点，读作「还封着」。")]
        [SerializeField] private Color _anchoredTint = new Color(0.60f, 0.58f, 0.56f, 1f);

        [Header("调试")]
        [Tooltip("把每次碰撞的判据打到 Console（排查「为什么碎了 / 为什么没碎」用；正式关卡关掉）。")]
        [SerializeField] private bool _logImpacts;

        [Header("碎块")]
        [Tooltip("碎块模板：一个**禁用中**的方块（本巨石的子物体）。碎开时按它克隆一圈。" +
                 "留空 = 只露剑、不撒碎块。")]
        [SerializeField] private Transform _fragmentTemplate;

        [Tooltip("碎块数量。8 块 ≈ 一圈撒开、中心还看得见剑。")]
        [SerializeField, Range(0, 24)] private int _fragmentCount = 8;

        [Tooltip("碎块飞出去的速度（单位/秒）。")]
        [SerializeField, Min(0f)] private float _fragmentSpeed = 3.2f;

        [Tooltip("碎块生成时的环半径：从巨石中心往外这么多，避免全叠在一个点上。")]
        [SerializeField, Min(0f)] private float _fragmentRing = 0.7f;

        [Tooltip("向上偏置：碎块除了往外飞，还往上抛一点（× 速度）。0 = 纯水平炸开。")]
        [SerializeField, Range(0f, 1f)] private float _fragmentUpBias = 0.5f;

        [Header("露出来的剑")]
        [Tooltip("藏在石头里的剑（**开局禁用**）。碎开时移到巨石底部正中并启用它。\n" +
                 "它的原点 = 剑尖（插进地面的那一点）。")]
        [SerializeField] private GameObject _sword;

        [Header("白盒")]
        [Tooltip("巨石本体的渲染器。震裂时给它换一个颜色当「裂了」的提示。")]
        [SerializeField] private SpriteRenderer _art;

        [Tooltip("震裂后的颜色（白盒占位）。接正式美术后连这个字段一起清空。")]
        [SerializeField] private Color _armedTint = new Color(0.86f, 0.60f, 0.52f, 1f);

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        [Header("碎开时")]
        [Tooltip("碎开瞬间要接的事情。把接收方拖进来即可，不需要写代码。")]
        [SerializeField] private UnityEvent _onShattered = new UnityEvent();

        [Tooltip("震裂瞬间要接的事情（表现：镜头抖一下、音效、碎屑）。")]
        [SerializeField] private UnityEvent _onArmed = new UnityEvent();

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>被震裂瞬间（边沿）。这时起，落地才会碎。</summary>
        public event Action OnArmed;

        /// <summary>碎开瞬间（边沿）。剑已经露出来了，碎块也已经撒了。</summary>
        public event Action OnShattered;

        /// <summary>解锁瞬间（边沿）。这时起它才开始吃重力。</summary>
        public event Action OnUnsealed;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>当前形态。</summary>
        public State CurrentState { get; private set; }

        /// <summary>最近一次落地的向下速度（调试用：拿它对着阈值看差多少）。</summary>
        public float LastImpactSpeed { get; private set; }

        /// <summary>最近一次碰到 Ground 层东西的对方名字（调试用：「刚才撞的是谁」）。</summary>
        public string LastContactName { get; private set; }

        /// <summary>
        /// 最近一次接触是不是**从下面接住**的（接触点落在巨石中心下方）。
        /// </summary>
        /// <remarks>只有这一种才算「落地」。顶到天花板 / 撞到按钮时它是 <c>false</c> ——
        /// 那正是「砸到按钮上不碎」的那条判据。</remarks>
        public bool LastContactWasFromBelow { get; private set; }

        /// <summary>现在是不是**锁死**的（静态、不吃重力、动不了）。</summary>
        public bool IsAnchored => _anchored;

        /// <summary>
        /// 当前这个形态摔不摔得碎。
        /// </summary>
        /// <remarks>锁死时一律不碎（它根本动不了）；<see cref="_requireArming"/> 关掉时，完整态也碎。</remarks>
        public bool IsBreakable => !_anchored
                                   && (CurrentState == State.Armed
                                       || (!_requireArming && CurrentState == State.Intact));

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private BoxCollider2D _box;

        /// <summary>现在锁没锁（静态石头）。</summary>
        private bool _anchored;

        /// <summary>
        /// 本次下落过程中的最大向下速度 —— **碎不碎只看它**。
        /// </summary>
        /// <remarks><c>FixedUpdate</c> 跑在物理步进**之前**，所以这里记到的正是撞击前那一帧的速度。
        /// 不再用 <c>Collision2D.relativeVelocity</c>：那个量的符号约定一记反，
        /// 巨石被举起来撞天花板那一刻就会被当成「落地」。</remarks>
        private float _peakFallSpeed;

        /// <summary>「接触点在中心下方」的容差：避免正好齐平的水平擦碰被算成落地。</summary>
        private const float BelowTolerance = 0.1f;

        private Color _artRestColor = Color.white;
        private bool _hasArtColor;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _box = GetComponent<BoxCollider2D>();

            if (_body != null)
            {
                // 与 PushableBody 同一套：受重力、不翻滚、不休眠、不隧穿。
                _body.bodyType = RigidbodyType2D.Dynamic;
                _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
                _body.sleepMode = RigidbodySleepMode2D.NeverSleep;
                _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

            CacheArtColor();
            SetState(State.Intact);

            _anchored = _startAnchored;
            ApplyAnchor();
        }

        /// <summary>重开一局 / 对象池取出时也要回到「完整」态，不能接着上一局的碎。</summary>
        private void OnEnable()
        {
            SetState(State.Intact);
            _peakFallSpeed = 0f;
            LastContactName = null;
            LastContactWasFromBelow = false;

            // 重开一局回来也重新锁上（上一局的解锁不该带过来）。
            _anchored = _startAnchored;
            ApplyAnchor();
        }

        private void FixedUpdate()
        {
            // 锁死状态：等按钮来驱动（<see cref="SetSwitchState"/>），这里什么都不做。
            if (_anchored) return;

            if (_body == null || CurrentState == State.Shattered) return;

            // 记录本次下落的最大速度。FixedUpdate 跑在物理步进**之前**，
            // 所以这里记到的就是撞击前那一帧的速度 —— 碎不碎只看它，不看 relativeVelocity。
            float down = -_body.linearVelocity.y;
            if (down <= 0.05f) _peakFallSpeed = 0f;
            else if (down > _peakFallSpeed) _peakFallSpeed = down;
        }

        // ==================================================================
        //  震裂
        // ==================================================================

        // ==================================================================
        //  锁 / 解锁
        // ==================================================================

        /// <summary>
        /// 解锁：从**静态**变成**动态**刚体 —— 从这一刻起才吃重力、才被规则举得起来。
        /// </summary>
        /// <remarks>
        /// <para>由按钮驱动：把这块石头拖到 <c>CityButton</c> 的 <c>FollowMotor</c> 上，
        /// 按钮一打开就会走 <see cref="SetSwitchState"/> → 这里。也可以被别的机关 / UnityEvent 调。
        /// 重复调用是幂等的（只生效一次）。</para>
        /// <para><b>为什么要这一道锁</b>：玩家在关卡里会反复放「重力 × 反转」赶路，
        /// 石头要是从头就吃重力，会被顺手举起来撞到按钮、再摔下来碎掉 —— 机关还没开始就结束了。</para>
        /// </remarks>
        public void Unseal()
        {
            if (!_anchored) return;

            _anchored = false;
            ApplyAnchor();

            OnUnsealed?.Invoke();
        }

        /// <summary>给 <c>UnityEvent&lt;bool&gt;</c> 用：传 true = 解锁。传 false 不会重新锁上。</summary>
        public void SetUnsealed(bool unsealed)
        {
            if (unsealed) Unseal();
        }

        /// <summary>
        /// 开关驱动入口（<see cref="ISwitchTarget"/>）：按钮打开 → 解锁。
        /// </summary>
        /// <remarks>拖到 <c>CityButton.FollowMotor</c> 上就通了 —— 与 Gate / 电梯同一条配法。
        /// 关掉（false）不会重新锁上：这块石头一旦松了就不再封回去。</remarks>
        public void SetSwitchState(bool on)
        {
            if (on) Unseal();
        }

        /// <summary>把刚体切到「锁死 / 活动」对应的状态。</summary>
        private void ApplyAnchor()
        {
            if (_body == null) return;

            if (_anchored)
            {
                // 先清速度再换类型：静态刚体不接受速度写入。
                _body.linearVelocity = Vector2.zero;
                _body.angularVelocity = 0f;
                _body.bodyType = RigidbodyType2D.Static;   // 不吃重力、不被规则举、也不会触发碰撞回调
                _peakFallSpeed = 0f;
            }
            else
            {
                _body.bodyType = RigidbodyType2D.Dynamic;
                _body.gravityScale = 1f;
                _body.sleepMode = RigidbodySleepMode2D.NeverSleep;
                _body.WakeUp();
            }

            ApplyPose();
        }

        /// <summary>
        /// 震裂（裂痕出现）。**这一步之后落地才会碎。**
        /// </summary>
        /// <remarks>
        /// 巨石**碰到接触式开关**（<see cref="IContactSwitch"/>，例如积木城的 CityButton）时自动调用，
        /// 也可以被别的机关在代码里调（例如敌人砸了一下）。重复调用是幂等的。
        /// </remarks>
        public void Arm()
        {
            if (CurrentState != State.Intact) return;

            SetState(State.Armed);
            OnArmed?.Invoke();
            _onArmed.Invoke();
        }

        // ==================================================================
        //  碎裂
        // ==================================================================

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision == null) return;

            Touch(collision.collider);   // 实心的接触式开关也认（CityButton 默认是 Trigger）

            if (CurrentState == State.Shattered) return;
            if ((_breakLayers.value & (1 << collision.gameObject.layer)) == 0) return;

            // 记下这一下「撞的是谁、从哪边」—— 调试用，也进 LastContact* 只读状态。
            LastContactName = collision.gameObject.name;
            LastContactWasFromBelow = IsSupportedFromBelow(collision);

            if (_logImpacts)
            {
                Debug.Log($"[GravityBoulder] {name} 撞到 {LastContactName}（层 {collision.gameObject.layer}）：" +
                          $"从下面接住={LastContactWasFromBelow}，下落峰值={_peakFallSpeed:0.##}，" +
                          $"阈值={_breakSpeed:0.##}，形态={CurrentState}", this);
            }

            if (!IsBreakable) return;
            if (!LastContactWasFromBelow) return;   // ⚠️ 只有「从下面接住」才算落地

            LastImpactSpeed = _peakFallSpeed;
            if (!ShouldBreak(_peakFallSpeed, _breakSpeed)) return;

            Shatter();
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (collision != null) Untouch(collision.collider);
        }

        // ==================================================================
        //  接触式开关（被举起来顶到按钮那一下）
        // ==================================================================

        private void OnTriggerEnter2D(Collider2D other) => Touch(other);

        private void OnTriggerExit2D(Collider2D other) => Untouch(other);

        /// <summary>
        /// 碰到「接触式开关」（<see cref="IContactSwitch"/>）就**按下去 + 震裂自己**。
        /// </summary>
        /// <remarks>巨石不认识按钮的具体类型，只认这个接口 —— 与 <c>IPushable</c> / <c>IMovingSurface</c>
        /// 同一条耦合方式。按钮那边（<c>CitySwitch</c>）自己不扫描：**谁压过来谁报**。</remarks>
        private void Touch(Collider2D other)
        {
            if (!_armByContactSwitch || other == null) return;

            var contactSwitch = other.GetComponentInParent<IContactSwitch>();
            if (contactSwitch == null) return;

            contactSwitch.PressByContact();
            Arm();
        }

        private void Untouch(Collider2D other)
        {
            if (!_armByContactSwitch || other == null) return;

            var contactSwitch = other.GetComponentInParent<IContactSwitch>();
            if (contactSwitch == null) return;

            contactSwitch.ReleaseByContact();
        }

        /// <summary>
        /// 这一下碰撞是不是**从下面接住**（接触点落在巨石中心下方）。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>这就是「砸到按钮上不碎」的那条判据</b>：巨石被举起来顶到天花板 / 按钮时，
        /// 接触点在它**上面**，绝不该当成落地。</para>
        /// <para>为什么不看 <c>ContactPoint2D.normal</c> 的符号：那个法线「从谁指向谁」在 2D / 3D /
        /// 各版本里的约定很容易记反，而「接触点比我的中心低」是**一眼可验的几何事实**。</para>
        /// </remarks>
        private bool IsSupportedFromBelow(Collision2D collision)
        {
            float centerY = transform.position.y;
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (IsContactBelow(collision.GetContact(i).point.y, centerY)) return true;
            }

            return false;
        }

        /// <summary>接触点在不在中心下方（纯函数，便于断言）。</summary>
        public static bool IsContactBelow(float contactY, float centerY)
        {
            return contactY < centerY - BelowTolerance;
        }

        /// <summary>
        /// 落地速度够不够碎（纯函数，便于断言）。
        /// </summary>
        /// <param name="impactSpeed">落地那一瞬间**向下**的速度（单位/秒）。</param>
        /// <param name="breakSpeed">阈值。</param>
        public static bool ShouldBreak(float impactSpeed, float breakSpeed)
        {
            return impactSpeed >= breakSpeed;
        }

        /// <summary>
        /// 碎开：撒碎块 + 露出剑 + 停用自己。重复调用是幂等的。
        /// </summary>
        /// <remarks>落地速度够、或被别的机关在代码里调（例如 Boss 一巴掌拍碎它）。</remarks>
        public void Shatter()
        {
            if (CurrentState == State.Shattered) return;

            Bounds bounds = BoxBounds();
            var bottomCenter = new Vector3(bounds.center.x, bounds.min.y, 0f);

            SetState(State.Shattered);

            // ⚠️ 先关掉自己的碰撞体：碎块是「往下打射线找地面」落地的，
            //    石头自己的碰撞体还在的话，射线会从石头内部打到自己、碎块当场就贴住原地。
            if (_box != null) _box.enabled = false;

            SpawnFragments(bounds.center);
            RevealSword(bottomCenter);

            OnShattered?.Invoke();
            _onShattered.Invoke();

            // 整个巨石停用：碎块已经独立出去、剑是兄弟节点，都不受影响。
            gameObject.SetActive(false);
        }

        /// <summary>按模板克隆一圈碎块，各自往外 + 往上抛。</summary>
        private void SpawnFragments(Vector3 center)
        {
            if (_fragmentTemplate == null || _fragmentCount <= 0) return;

            // 碎块挂在巨石的父物体下（机关根），不跟着巨石一起被停用。
            Transform home = transform.parent;

            for (int i = 0; i < _fragmentCount; i++)
            {
                // 均匀铺一圈（确定性，便于复现）—— 不掷骰子，同一个机关每次碎法一样。
                float angle = (i + 0.5f) / _fragmentCount * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                var at = center + new Vector3(dir.x * _fragmentRing, dir.y * _fragmentRing, 0f);

                GameObject clone = Instantiate(_fragmentTemplate.gameObject, at, Quaternion.identity, home);
                clone.name = _fragmentTemplate.name;
                clone.SetActive(true);

                var fragment = clone.GetComponent<BoulderFragment>();
                if (fragment != null)
                {
                    var launch = dir * _fragmentSpeed + Vector2.up * (_fragmentSpeed * _fragmentUpBias);
                    // 角速度按序号左右交替，看着像被炸开而不是整齐平移。
                    fragment.Launch(launch, (i % 2 == 0 ? 1f : -1f) * _fragmentSpeed * 60f);
                }
            }
        }

        /// <summary>把剑移到巨石底部正中（剑尖贴着地面），然后启用它。</summary>
        private void RevealSword(Vector3 bottomCenter)
        {
            if (_sword == null) return;

            _sword.transform.position = bottomCenter;
            _sword.SetActive(true);
        }

        // ==================================================================
        //  形态
        // ==================================================================

        /// <summary>切换形态。只在状态真的变了才做 —— 重复调用是幂等的。</summary>
        private void SetState(State state)
        {
            if (CurrentState == state) return;

            CurrentState = state;
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (_art != null)
            {
                CacheArtColor();

                if (CurrentState == State.Armed)     _art.color = _armedTint;
                else if (_anchored)                  _art.color = _anchoredTint;
                else                                 _art.color = _artRestColor;
            }

            RefreshLabel();
        }

        private void CacheArtColor()
        {
            if (_hasArtColor || _art == null) return;
            _artRestColor = _art.color;
            _hasArtColor = true;
        }

        // ==================================================================
        //  几何
        // ==================================================================

        /// <summary>
        /// 巨石的世界包围盒。<b>从碰撞体自己的 size / offset 算</b>，不读 <c>Collider2D.bounds</c> ——
        /// 编辑模式的 gizmo 在 Awake 之前就会跑（与 <c>ImpactButton</c> 同一处理）。
        /// </summary>
        private Bounds BoxBounds()
        {
            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_box == null) return new Bounds(transform.position, Vector3.zero);

            Vector3 scale = transform.lossyScale;
            var size = new Vector2(Mathf.Abs(_box.size.x * scale.x), Mathf.Abs(_box.size.y * scale.y));
            var center = (Vector2)transform.TransformPoint(_box.offset);
            return new Bounds(center, size);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒的两处引用（本体渲染器 / 标签）。</summary>
        public void SetWhitebox(SpriteRenderer art, TextMesh label)
        {
            _art = art;
            _hasArtColor = false;
            CacheArtColor();

            _label = label;
            RefreshLabel();
        }

        /// <summary>只给编辑器工具用：接上机关的两处引用（剑 / 碎块模板）。</summary>
        public void SetLinks(GameObject sword, Transform fragmentTemplate)
        {
            _sword = sword;
            _fragmentTemplate = fragmentTemplate;
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(State state) => LabelText(state, anchored: false);

        /// <summary>白盒标签（带锁定状态）：锁死时的文案优先。</summary>
        public static string LabelText(State state, bool anchored)
        {
            if (anchored) return "击碎巨石 · 锁死";

            switch (state)
            {
                case State.Armed:     return "击碎巨石 · 开裂";
                case State.Shattered: return "击碎巨石 · 已碎";
                default:              return "击碎巨石";
            }
        }

        private void OnValidate() => RefreshLabel();

        /// <summary>把形态写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声（与 <c>ImpactButton</c> 同一处理）。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(CurrentState, _anchored);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            Bounds bounds = BoxBounds();

            Gizmos.color = new Color(0.62f, 0.60f, 0.58f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            // 碎块撒开的那一圈：一眼看出碎块会落在哪。
            if (_fragmentTemplate == null || _fragmentCount <= 0) return;

            Gizmos.color = new Color(0.85f, 0.55f, 0.35f, 0.8f);
            for (int i = 0; i < _fragmentCount; i++)
            {
                float angle = (i + 0.5f) / _fragmentCount * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var at = bounds.center + new Vector3(dir.x, dir.y, 0f) * _fragmentRing;
                Gizmos.DrawWireSphere(at, 0.2f);
                Gizmos.DrawLine(at, at + (Vector3)(dir * 0.6f + Vector2.up * 0.6f * _fragmentUpBias));
            }
        }
    }
}
