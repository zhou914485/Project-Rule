using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Presentation.Player
{
    /// <summary>
    /// 把 <see cref="PlayerController"/> 的状态翻译成 Animator 参数。
    /// </summary>
    /// <remarks>
    /// <para><b>这一层只发「意图」，不发「状态」</b>。过渡条件、混合、播完再切这些事
    /// 全部由 <c>Hero.controller</c> 自己管 —— 这里只把玩法状态映射成参数，
    /// 然后 <c>SetFloat</c> / <c>SetBool</c> / <c>SetTrigger</c> 就结束了。
    /// 这正是《丝之歌》那套分工：<b>C# 当决策者、Animator 当执行者</b>。</para>
    ///
    /// <para><b>为什么不再 <c>CrossFade(状态名)</c></b>：那样等于绕开 Animator 自己的状态机，
    /// 于是拿不到 <c>Has Exit Time</c>（播完再走）、<c>AnyState</c>（谁能打断谁）、
    /// 过渡时长这些引擎免费给的东西 —— 之前那几个「动作冻在最后一帧」「落地没动画」
    /// 都是这么来的。参数驱动之后，这些改在图的配置里，不用写代码。</para>
    ///
    /// <para><b>两类参数，来源不同</b>：</para>
    /// <list type="bullet">
    /// <item><b>持续条件</b>（Speed / Grounded / Dashing / WallSliding / GravityFlipped / Rising）
    /// —— 每帧重写。它们描述「现在是什么」，重写不会丢。</item>
    /// <item><b>一次性事件</b>（Jump / WallJump / Land / Hurt / Die）—— 从玩法层的
    /// <b>边沿事件</b>发过来。它们描述「刚刚发生了什么」，靠每帧采样会漏
    /// （0.33 秒的落地只有 4 个物理帧），所以直接从源头发 <c>SetTrigger</c>。</item>
    /// </list>
    ///
    /// <para>玩法层不认识本类 —— 依赖方向是 Presentation → Gameplay，单向。</para>
    /// </remarks>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimationDriver : MonoBehaviour
    {
        // ==================================================================
        //  Animator 参数名
        // ==================================================================
        //
        // ⚠️ 这些常量是**唯一真相**：编辑器里的 HeroAnimatorBuilder 直接引用它们来建参数，
        //    所以改名只需要改这一处，图和代码不会各说各话。

        public const string ParamSpeed          = "Speed";

        /// <summary>是否按着左右方向键。`Idle ⇄ Run` 用它判定，**不用速度**。</summary>
        public const string ParamMoving         = "Moving";

        public const string ParamGrounded       = "Grounded";
        public const string ParamDashing        = "Dashing";
        public const string ParamWallSliding    = "WallSliding";
        public const string ParamGravityFlipped = "GravityFlipped";
        public const string ParamRising         = "Rising";

        /// <summary>
        /// 这次重力反转该不该播那条翻转动画 = 重力已反转 <b>且</b> <b>本轮</b>还没播过。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 这是本类<b>算出来</b>的参数，不是从玩法层透传的原始状态。之所以不直接在边上
        /// 挂「<c>GravityFlipped</c> 再加一个 <c>WeightlessPlayed</c>」：那样
        /// <c>→ Weightless</c> 与 <c>→ Fall</c> 两条出边的条件会<b>同时成立</b>，谁赢取决于
        /// 过渡在列表里的先后 —— 顺序一改行为就变。拆成同一个参数的正反两面之后，
        /// 两条边天然互斥，与顺序无关。</para>
        /// <para>它的反面（<c>IfNot</c>）就是「按当前重力滞空，但不用播翻转动画」。</para>
        /// </remarks>
        public const string ParamPlayFlip = "PlayFlip";

        public const string ParamJump     = "Jump";

        /// <summary>二段跳（空中段）。与 <see cref="ParamJump"/> 分开：起跳和空中那一下是两个动作。</summary>
        public const string ParamAirJump  = "AirJump";

        public const string ParamWallJump = "WallJump";
        public const string ParamLand     = "Land";
        public const string ParamHurt     = "Hurt";
        public const string ParamDie      = "Die";
        public const string ParamReacting = "Reacting";

        /// <summary>起手一刀（连段的第一下）。</summary>
        public const string ParamAttack     = "Attack";

        /// <summary>连段的下一刀（收招前再按一次）。</summary>
        public const string ParamAttackNext = "AttackNext";

        /// <summary>重击（下 + 攻击）。**不先进拔剑**，直接从 AnyState 进 <c>SlashDown</c>。</summary>
        public const string ParamHeavy = "HeavyAttack";

        /// <summary>
        /// 重力反转专用的那套**倒立版**滞空动画的 state 名。
        /// </summary>
        /// <remarks>
        /// ⚠️ 这是<b>状态名</b>、不是参数名，但同样要保持单一真相：建图脚本直接引用本常量
        /// 填进它的 state 列表，所以改名只需改这一处。
        /// </remarks>
        public const string StateWeightless = "Weightless";

        /// <summary>初始状态（地面待机）。复活时把状态机显式拉回这里 —— <c>Die</c> 没有出边，回不来。</summary>
        public const string StateIdle = "Idle";

        private static readonly int HSpeed          = Animator.StringToHash(ParamSpeed);
        private static readonly int HMoving         = Animator.StringToHash(ParamMoving);
        private static readonly int HGrounded       = Animator.StringToHash(ParamGrounded);
        private static readonly int HDashing        = Animator.StringToHash(ParamDashing);
        private static readonly int HWallSliding    = Animator.StringToHash(ParamWallSliding);
        private static readonly int HGravityFlipped = Animator.StringToHash(ParamGravityFlipped);
        private static readonly int HRising         = Animator.StringToHash(ParamRising);
        private static readonly int HPlayFlip       = Animator.StringToHash(ParamPlayFlip);

        private static readonly int HJump         = Animator.StringToHash(ParamJump);
        private static readonly int HAirJump      = Animator.StringToHash(ParamAirJump);
        private static readonly int HWallJump     = Animator.StringToHash(ParamWallJump);
        private static readonly int HLand         = Animator.StringToHash(ParamLand);
        private static readonly int HHurt         = Animator.StringToHash(ParamHurt);
        private static readonly int HDie          = Animator.StringToHash(ParamDie);
        private static readonly int HReacting     = Animator.StringToHash(ParamReacting);
        private static readonly int HurtState     = Animator.StringToHash("Hurt");
        private static readonly int DieState      = Animator.StringToHash("Die");
        private static readonly int IdleState     = Animator.StringToHash(StateIdle);
        private static readonly int HAttack       = Animator.StringToHash(ParamAttack);
        private static readonly int HAttackNext   = Animator.StringToHash(ParamAttackNext);
        private static readonly int HHeavy        = Animator.StringToHash(ParamHeavy);

        // ==================================================================
        //  序列化
        // ==================================================================

        [Header("引用（留空自动找）")]
        [SerializeField] private PlayerController _player;
        [SerializeField] private Animator         _animator;
        [SerializeField] private SpriteRenderer   _renderer;
        [SerializeField] private EntityStatus     _status;
        private float _hurtClipLength = .5833333f;
        private SnapshotSystem _reactionSnapshots;

        // ==================================================================
        //  运行时
        // ==================================================================

        private float _lastHealth;
        private bool  _warnedNoAnimator;
        private bool  _reactionPending;

        // ---------------- 死亡演出 → 复活等待 ----------------

        /// <summary>已经给 <see cref="PlayerController"/> 挂上「等死亡动画播完」的等待。</summary>
        private bool _deathWaitArmed;

        /// <summary>已经放过行（同一次死亡里不再重复挂）。</summary>
        private bool _deathWaitReleased;

        /// <summary>这次倒下已经过了多久（只算没暂停的时间）。</summary>
        private float _deathAge;

        /// <summary>死亡状态连 1 秒都没进（换过控制器 / 那一段没播起来）就放行，别把复活卡死。</summary>
        private const float DeathAnimationGrace = 1f;

        /// <summary>
        /// <b>本轮</b>重力反转里，那条翻转动画（<c>Weightless</c>）**已经播过**了。
        /// </summary>
        /// <remarks>
        /// <para><b>锁的粒度是「一次重力反转」，不是「一整局」</b>：重力一恢复成正常方向就解锁，
        /// 下次再反转又是一个新的「第一次」，照样播。</para>
        ///
        /// <para>为什么要这个锁：那条剪辑是「从站姿起、边播边把人物翻过去」的一次性动作
        /// （贴图最底下一行 14 帧，<c>m_LoopTime = 0</c>）。不锁的话，反转期间每次重新滞空都会
        /// 从第 1 帧重演一遍翻滚 —— 表现是「在天花板上落地 → <c>Land</c> → <c>Idle</c>，
        /// 走出边缘又弹回正立、再翻一次」。</para>
        ///
        /// <para>⚠️ 锁在<b>动画真的开始播</b>那一刻才落下（<c>IsWeightlessPlaying</c> 为真，
        /// 含正在淡入的那一帧），不是「重力一反转就落」：在地面上按反转、条件还没凑齐
        /// （<c>Grounded</c> 仍为 true）时不会白白浪费掉这次机会。</para>
        ///
        /// <para>不做持久化也不跨场景：跟着对象走，场景重载即重置。</para>
        /// </remarks>
        private bool _flipPlayed;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_player   == null) _player   = GetComponent<PlayerController>();
            if (_status   == null) _status   = GetComponent<EntityStatus>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();

            // 旧场景和运行时生成的玩家同样接入，无需逐份修改 Prefab/场景。
            if (_player != null && _player.GetComponent<PlayerAttackVfx>() == null)
                _player.gameObject.AddComponent<PlayerAttackVfx>();
            if (_player != null && _player.GetComponent<EntityDamageable>() != null
                && _player.GetComponent<PlayerHurtFeedback>() == null)
                _player.gameObject.AddComponent<PlayerHurtFeedback>();
            if (_animator != null && _animator.runtimeAnimatorController != null)
                foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
                    if (clip.name == "Hero_Hurt") _hurtClipLength = clip.length;
        }

        private void OnEnable()
        {
            _reactionPending = false;
            _deathWaitArmed = false;
            _deathWaitReleased = false;
            _deathAge = 0f;
            if (_player != null)
            {
                _player.OnJumped     += HandleJumped;
                _player.OnAirJumped  += HandleAirJumped;
                _player.OnWallJumped += HandleWallJumped;
                _player.OnLanded     += HandleLanded;
                _player.OnAttacked   += HandleAttacked;
                _player.OnHeavyAttacked += HandleHeavyAttacked;
            }

            if (_status != null)
            {
                _status.OnHealthChanged += HandleHealthChanged;
                _lastHealth = _status.Health;      // 起手对齐，否则第一帧会被当成「掉血」
            }
        }

        private void OnDisable()
        {
            if (_reactionSnapshots != null) _reactionSnapshots.OnRestored -= ResetReactionAfterRewind;
            _reactionSnapshots = null;
            // 驱动被关掉就把复活放行：等待只能由本类解除，没人解除会把人永远卡在死亡里。
            ReleaseDeathWait();

            if (_player != null)
            {
                _player.OnJumped     -= HandleJumped;
                _player.OnAirJumped  -= HandleAirJumped;
                _player.OnWallJumped -= HandleWallJumped;
                _player.OnLanded     -= HandleLanded;
                _player.OnAttacked   -= HandleAttacked;
                _player.OnHeavyAttacked -= HandleHeavyAttacked;
            }

            if (_status != null) _status.OnHealthChanged -= HandleHealthChanged;
        }

        private void Update()
        {
            if (_reactionSnapshots != SnapshotSystem.Instance)
            {
                if (_reactionSnapshots != null) _reactionSnapshots.OnRestored -= ResetReactionAfterRewind;
                _reactionSnapshots = SnapshotSystem.Instance;
                if (_reactionSnapshots != null) _reactionSnapshots.OnRestored += ResetReactionAfterRewind;
            }
            if (_animator == null)
            {
                if (!_warnedNoAnimator)
                {
                    _warnedNoAnimator = true;
                    Debug.LogWarning("[PlayerAnimationDriver] 找不到 Animator，动画不会播。", this);
                }
                return;
            }

            // 暂停：动画要一起冻住。D18 用 Physics2D.simulationMode 停物理、**不改** timeScale，
            // 所以这里必须显式停，否则拼装界面一开角色还在原地跑。
            bool hurtPlaying = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash == HurtState
                || _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == HurtState;
            _animator.speed = (_status != null && _status.IsPaused) || _player.IsInHurtImpactPause ? 0f
                : hurtPlaying ? _hurtClipLength / Mathf.Max(.1f, _player.HurtRecoveryDuration) : 1f;

            // 受伤/死亡期间仍更新移动参数，但低优先级 AnyState 过渡不能抢走演出。
            bool reacting = IsReactionState(_animator.GetCurrentAnimatorStateInfo(0))
                || _animator.IsInTransition(0) && IsReactionState(_animator.GetNextAnimatorStateInfo(0));
            if (reacting) _reactionPending = false;
            _animator.SetBool(HReacting, _reactionPending || reacting);

            TickDeathRespawn();
            PushContinuousParameters();
        }

        private static bool IsReactionState(AnimatorStateInfo state)
            => state.shortNameHash == HurtState || state.shortNameHash == DieState;

        private void ResetReactionAfterRewind()
        {
            if (_status != null && _status.Health > 0f) ResetAfterRespawn();
            if (_status != null) _lastHealth = _status.Health;
        }

        /// <summary>
        /// 朝向**必须放在 LateUpdate**，不能和上面的参数一起写在 <c>Update</c> 里。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么</b>：Animator 的精灵换帧曲线绑定的是**同一个</b> <see cref="SpriteRenderer"/>。
        /// 动画求值发生在 <c>Update</c> 之后，它会把整个绑定的渲染器属性集写回默认值 ——
        /// 于是 <c>Update</c> 里刚设好的 <c>flipX</c> 被同一帧覆盖回 <c>false</c>，
        /// 表现就是「往左走不翻面」（实测：把 Animator 停掉，手动写的 flipX 就能保持住）。
        /// 放到 <c>LateUpdate</c> 就落在动画写入之后，不会再被吃掉。</para>
        ///
        /// <para>⚠️ 不要去改成「用 <c>localScale.x = -1</c> 翻面」：<c>Hero_Run</c> / <c>Hero_Airborne</c>
        /// / <c>Hero_Die</c> 三个剪辑都在写 <c>localScale</c>，同样会被动画覆盖。</para>
        ///
        /// <para>⚠️ 素材原生朝右，所以朝左（<c>Facing &lt; 0</c>）时才翻。</para>
        ///
        /// <para><c>Facing</c> 已经是「换算后」的方向，「摩擦 × 反转」时自动是反的 —— 不用另外检测。</para>
        /// </remarks>
        private void LateUpdate()
        {
            if (_renderer == null || _player == null) return;

            // ⚠️ 重力反转时**整个身体已经在 PlayerController 里转了 180°**（为了让碰撞箱的「脚」
            //    那面翻到朝上那侧），Visual 作为子对象一起转过去了。所以视觉这边要给的是**抵消**：
            //
            //    ① 那次旋转把**左右也带翻了** → 朝向要异或回来，否则会左右镜像；
            //    ② Weightless 那条剪辑演的就是「正立 → 倒立」本身（贴图最底下一行 14 帧），
            //       它要的是**严格按原画播**，所以根对象那 180° 也得抵消掉。
            //
            //    ①② 各补一个镜像；两个镜像 + 根对象那 180° = 净值为零 —— 于是反转期间
            //    Weightless 按原画播：第 1 帧正立起步，边播边把人物翻过去。
            //
            // ⚠️ 判据必须是 `_player.IsBodyFlipped`（= 根对象**实际**转没转），**不是** Physics2D.gravity。
            //    重力在规则的 Update（顺序 −100）里就改了，身体要等下一个 FixedUpdate 才转 —— 差一帧。
            //    按重力判的话这两下补偿会超前生效，等于在没转的身体上多叠 180°，表现是
            //    「反转瞬间闪一帧倒立的人物」。详见 PlayerController.IsBodyFlipped 的说明。
            //
            // 放 LateUpdate 的理由和上面 flipX 那段原有说明完全相同（Animator 会覆盖渲染器属性）。
            bool flipped    = _player.IsBodyFlipped;
            bool weightless = IsWeightlessPlaying;

            // 锁：那条动画**真开始播了**（含正在淡入的那一帧）就把本轮这次机会用掉 ——
            // 反转期间再滞空只走 Fall，不会再翻滚一遍。
            // 重力恢复成正常方向时 PushContinuousParameters 会把锁清掉，下次反转重新可播。
            if (weightless) _flipPlayed = true;

            _renderer.flipX = (_player.Facing < 0f) != flipped;
            _renderer.flipY = flipped && weightless;
        }

        /// <summary>当前是否在播重力反转专用的那套**倒立版**滞空动画。</summary>
        /// <remarks>
        /// ⚠️ <b>「正在过渡进 Weightless」也得算进来</b>。过渡进行中
        /// <c>GetCurrentAnimatorStateInfo</c> 报的是<b>源</b>状态（Idle / Fall），目标状态在
        /// <c>GetNextAnimatorStateInfo</c> 里 —— 只看 Current 的话，整个淡入窗口
        /// （<c>Idle→Weightless</c> 0.10 秒 / <c>Fall→Weightless</c> 0.12 秒）里 <c>flipY</c> 都是
        /// <c>false</c>，可身体早就被根对象转了 180°。于是那段时间看到的是
        /// <b>「正常朝向的图被倒着播」</b>，读起来就像「反转之后还先播了一下 Fall」。
        /// </remarks>
        private bool IsWeightlessPlaying
        {
            get
            {
                if (_animator == null) return false;

                if (_animator.GetCurrentAnimatorStateInfo(0).IsName(StateWeightless)) return true;

                return _animator.IsInTransition(0)
                    && _animator.GetNextAnimatorStateInfo(0).IsName(StateWeightless);
            }
        }

        // ==================================================================
        //  持续条件（每帧重写）
        // ==================================================================

        private void PushContinuousParameters()
        {
            Vector2 velocity = _player.Velocity;

            // 地面 Blend 用的水平速度。**不做归一化** —— 阈值直接写「米/秒」更好读，
            // 也和 PlayerController 里 _moveSpeedMax 是同一个量纲。
            _animator.SetFloat(HSpeed, Mathf.Abs(velocity.x));

            // `Idle ⇄ Run` 看的是**有没有按方向键**，不是速度：速度快慢在冰面滑行、被击退、
            // 冲刺收尾时都会骗人 —— 那几种情况下人没在按方向，动画却会一直跑。
            _animator.SetBool(HMoving, Mathf.Abs(_player.MoveInput.x) > 0.01f);

            _animator.SetBool(HGrounded, _player.IsGrounded);
            _animator.SetBool(HDashing,  _player.IsDashing);
            _animator.SetBool(HWallSliding, _player.IsWallSliding);

            // 「重力是不是反的」和「这次该不该播那条翻转动画」是两件事 ——
            // 后者还要「本轮还没播过」。重力的原始值后面还有一条边要判（Weightless → Fall），
            // 所以两个都留着。见 ParamPlayFlip 与 _flipPlayed 的说明。
            bool gravityFlipped = Physics2D.gravity.y > 0f;

            // 回到正常重力 → 解锁：下一次反转又是一个新的「第一次」，动画照样播。
            //
            // ⚠️ 每帧无条件写（不是只在「刚恢复」那个边沿写）：从 Weightless 淡出到 Fall 的那几帧里
            //    LateUpdate 可能顺手又把锁落上（那时 Current 仍是 Weightless），下一帧这里会再清掉。
            //    反正 `PlayFlip = gravityFlipped && ...`，重力正常时前面那项就是 false，振荡不影响行为。
            if (!gravityFlipped) _flipPlayed = false;

            _animator.SetBool(HGravityFlipped, gravityFlipped);
            _animator.SetBool(HPlayFlip, gravityFlipped && !_flipPlayed);

            _animator.SetBool(HRising, Vector2.Dot(velocity, Up) > 0.1f);
        }

        /// <summary>当前「上」方向 = 重力反方向；重力失效（零向量）时退化为世界上方。</summary>
        private static Vector2 Up
            => Physics2D.gravity.sqrMagnitude > 0.0001f ? -Physics2D.gravity.normalized : Vector2.up;

        // ==================================================================
        //  一次性事件
        // ==================================================================

        private void HandleJumped()     => SetTrigger(HJump);
        private void HandleAirJumped()  => SetTrigger(HAirJump);
        private void HandleWallJumped() => SetTrigger(HWallJump);
        private void HandleLanded()     => SetTrigger(HLand);

        /// <summary>
        /// 挥了一刀。第一次是起手（进 <c>Slash1</c>），之后每一下是接续（进 <c>Slash2</c>）。
        /// </summary>
        /// <remarks>
        /// 用两个触发器而不是一个，是因为 <c>AnyState → Slash1</c> 不带任何附加条件 ——
        /// 共用一个 <c>Attack</c> 的话，连段中途再按会从第一刀重来。
        /// </remarks>
        private void HandleAttacked()
        {
            if (_player.AttackComboIndex <= 0)
            {
                // 新起手：先清掉上一轮可能残留的 AttackNext。
                // ⚠️ 触发器**只在过渡被走通时才消耗** —— 按晚了（图上已经回 Idle）那一下会一直挂着，
                //    等到下一次进 CD 时被它带走，凭空接出一刀。
                if (_animator != null) _animator.ResetTrigger(HAttackNext);
                SetTrigger(HAttack);
                return;
            }

            SetTrigger(HAttackNext);
        }

        /// <summary>
        /// 重击。**不走 <see cref="ParamAttack"/>** —— 那条是两刀连段（<c>Slash1 → CD → Slash2</c>）；
        /// 重击是直接劈下去的单发动作，所以给图上一条独立的 AnyState 过渡。
        /// </summary>
        private void HandleHeavyAttacked() => SetTrigger(HHeavy);

        /// <summary>掉血 → 受伤；掉到 0 → 死亡；从 0 回到正数 → 复活并切回初始态。</summary>
        private void HandleHealthChanged(float current, float max)
        {
            if (current <= 0f)
            {
                BeginReaction();
                SetTrigger(HDie);

                // 死亡当帧就挂上等待：玩法层的 TickRespawn 可能在同一次 Update 里跑在我们前面，
                // 等下一帧再挂就晚了一帧（_respawnDelay 配成 0 时会被直接复活走）。
                ArmDeathWait();
            }
            else if (current < _lastHealth)
            {
                BeginReaction();
                SetTrigger(HHurt);
            }
            else if (_lastHealth <= 0f)
            {
                // 复活（EntityDamageable.Revive / 快照回滚把血补回来）：
                // Die 是终点状态、没有出边，不显式重置就会一直停在最后一帧的尸体。
                ResetAfterRespawn();
            }

            _lastHealth = current;
        }

        // ==================================================================
        //  死亡演出 → 复活
        // ==================================================================

        /// <summary>
        /// 死亡演出 → 复活的握手：<c>Die</c> 播完了就放行玩法层复活。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不能只靠 <c>_respawnDelay</c></b>：<c>Hero_Die</c> 是 32 帧 @ 12fps
        /// = 2.67 秒，而倒计时默认 1.2 秒 —— 人还没倒下就被搬回存档点了。</para>
        /// <para>判据用 <c>normalizedTime &gt;= 1</c> 而不是秒表：暂停时 <c>Animator.speed = 0</c>，
        /// 秒表会走、动画不走；<c>normalizedTime</c> 跟着动画停，天然对齐。</para>
        /// <para>兜底：换上一个没有 <c>Die</c> 状态的控制器、或那一段根本没播起来时，
        /// <see cref="DeathAnimationGrace"/> 秒后放行 —— 宁可早一点复活，也不能永远卡在死亡里。</para>
        /// </remarks>
        private void TickDeathRespawn()
        {
            if (_player == null || _status == null || _animator == null) return;

            if (_status.Health > 0f)
            {
                ReleaseDeathWait();       // 复活 / 被其它路径救活：等待必须解除
                return;
            }

            ArmDeathWait();
            if (_deathWaitReleased || _status.IsPaused) return;   // 暂停时动画冻着，别拿秒表判

            _deathAge += Time.deltaTime;

            AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(0);
            bool inDie = current.shortNameHash == DieState
                      || _animator.IsInTransition(0)
                         && _animator.GetNextAnimatorStateInfo(0).shortNameHash == DieState;

            // Die 没有出边：current 落在 Die 且 normalizedTime 到 1 = 整段播完（正在淡入的那段不算）。
            bool finished = current.shortNameHash == DieState
                         && !_animator.IsInTransition(0)
                         && current.normalizedTime >= 1f;

            if (finished || (!inDie && _deathAge >= DeathAnimationGrace))
            {
                _deathWaitReleased = true;
                _player.ReleaseRespawnHold();
            }
        }

        /// <summary>挂上「等死亡动画」的等待；同一轮死亡只挂一次。</summary>
        private void ArmDeathWait()
        {
            if (_deathWaitArmed || _animator == null || _player == null) return;

            _deathWaitArmed = true;
            _deathWaitReleased = false;
            _deathAge = 0f;
            _player.HoldRespawn();
        }

        /// <summary>解除等待（幂等）。复活 / 驱动被关掉时调用。</summary>
        private void ReleaseDeathWait()
        {
            if (!_deathWaitArmed) return;

            _deathWaitArmed = false;
            _deathWaitReleased = false;
            _deathAge = 0f;
            if (_player != null) _player.ReleaseRespawnHold();
        }

        /// <summary>
        /// 复活：把动画状态机从终点的 <c>Die</c> 拉回初始态 <c>Idle</c>，并清掉残留的一次性输入。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 这里**必须**用 <c>Animator.Play</c>，不能靠参数过渡：<c>Die</c> 在图上没有出边
        /// （见 <c>HeroAnimatorBuilder.LinkReactions</c>），没有任何一条过渡能把人拉出来。</para>
        /// <para>不 CrossFade：复活是「回到初始态」而不是一次表演过渡，瞬切更可预测
        /// （也方便验证脚本在复活当帧就断言 state == Idle）。</para>
        /// </remarks>
        private void ResetAfterRespawn()
        {
            ReleaseDeathWait();
            if (_animator == null) return;

            _reactionPending = false;
            _animator.SetBool(HReacting, false);
            _animator.ResetTrigger(HJump);
            _animator.ResetTrigger(HAirJump);
            _animator.ResetTrigger(HWallJump);
            _animator.ResetTrigger(HLand);
            _animator.ResetTrigger(HHurt);
            _animator.ResetTrigger(HDie);
            _animator.ResetTrigger(HAttack);
            _animator.ResetTrigger(HAttackNext);
            _animator.ResetTrigger(HHeavy);

            _animator.Play(IdleState, 0, 0f);
        }

        private void BeginReaction()
        {
            if (_animator == null) return;
            // 立即保护等待求值的 Hurt/Die，并丢弃旧动作事件，避免演出结束后突然补播。
            _reactionPending = true;
            _animator.SetBool(HReacting, true);
            _animator.ResetTrigger(HJump);
            _animator.ResetTrigger(HAirJump);
            _animator.ResetTrigger(HWallJump);
            _animator.ResetTrigger(HLand);
            _animator.ResetTrigger(HAttack);
            _animator.ResetTrigger(HAttackNext);
            _animator.ResetTrigger(HHeavy);
        }

        private void SetTrigger(int hash)
        {
            // 事件可能在 Awake 之前到（同物体上的组件顺序不保证），也不能在没 Animator 时炸。
            if (_animator == null) return;
            if (hash != HHurt && hash != HDie && _animator.GetBool(HReacting)) return;
            _animator.SetTrigger(hash);
        }
    }
}
