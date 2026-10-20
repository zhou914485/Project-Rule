using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 插在石头里的剑：**「摩擦 × 反转」之后才拔得出来**。
    /// </summary>
    /// <remarks>
    /// <para><b>触发判据</b>：读属性管理器里的 <c>FrictionInverted</c> —— 也就是
    /// 「摩擦 × 反转」这一格。它**不是**「摩擦变小」：反转只把方向标志置位，
    /// 倍率仍是 1（见 <c>FrictionRule</c>），所以本组件刻意与 <c>RustyGear</c>
    /// （认「倍率 &lt; 1」）分开 —— 两个机关要的钥匙不同，解谜才不撞车。</para>
    ///
    /// <para><b>为什么反转能让剑松脱</b>：剑是**被碎石咬住**的 —— 咬住它的是静摩擦。
    /// 摩擦力的方向一反过来，碎石不再是「抓住剑」而是「把剑往外送」，
    /// 于是剑松了。这是策划给的机关读法（2026-10-18），机制上就是读那个标志位。</para>
    ///
    /// <para><b>为什么自己扫圈、不用触发器回调</b>：玩家是 Kinematic 刚体、位移走
    /// <c>Rigidbody2D.Slide</c>（<c>useAttachedTriggers = false</c>），接触 / 触发回调不可靠 ——
    /// 与 <c>ImpactButton</c> / <c>SpringPlatform</c> 同一个做法。</para>
    ///
    /// <para><b>谁算「取剑的人」</b>：圈里任一碰撞体，其父链上有 <c>PlayerController</c>
    /// 或 <c>IFactionMember.Faction == Player</c>。与其余判定一样，本组件不认识玩家这个类型，
    /// 只认「玩家阵营」这个通用标记。</para>
    ///
    /// <para>接线工具：菜单 <c>Tools/规则游戏/地图组件/⑫ 巨石断剑（重力举起 → 撞按钮 → 落地碎开）</c>。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SwordInStone : MonoBehaviour
    {
        /// <summary>阈值比较的容差：正常值 1.0 由常量精确写入，留一点余量挡浮点噪声。</summary>
        private const float ThresholdEpsilon = 0.001f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("解锁条件（摩擦规则）")]
        [Tooltip("是否要求摩擦规则先松脱。默认勾上 = 保留巨石断剑的摩擦解谜；\n" +
                 "教学剑 / 纯靠近获取的剑把它关掉即可。")]
        [SerializeField] private bool _requireFreeRule = true;

        [Tooltip("「摩擦 × 反转」是否算解锁。**默认勾上 —— 这是本机关设计的钥匙。**")]
        [SerializeField] private bool _acceptInverted = true;

        [Tooltip("「摩擦变小」是否也算解锁（削弱 0.3 / 失效 0）。默认**不勾** —— " +
                 "机关只认反转，免得削弱摩擦顺手就把剑拔了。")]
        [SerializeField] private bool _acceptWeakFriction;

        [Tooltip("摩擦倍率低到多少算「松脱」。正常 = 1，削弱 = 0.3，失效 = 0。只在勾了上一条时有用。")]
        [SerializeField, Min(0f)] private float _releaseThreshold = 1f;

        [Header("取出时解锁")]
        [Tooltip("拔出来时给取剑的人解锁哪项能力。默认攻击；留 None = 只播事件。")]
        [SerializeField] private PlayerAbility _grantAbility = PlayerAbility.Attack;

        [Header("取剑的人")]
        [Tooltip("是否要有人站在旁边才拔得出来。取消 = 规则一开剑自己飞出来（测试 / 过场用）。")]
        [SerializeField] private bool _requirePlayer = true;

        [Tooltip("取剑半径（世界单位）：以剑尖为圆心。")]
        [SerializeField, Min(0.1f)] private float _pullRadius = 1.6f;

        [Tooltip("在哪些层里找「取剑的人」。默认**全部层** —— 反正圈里的东西还要通过阵营检查" +
                 "（PlayerController / 玩家阵营）才算数，放宽层不会误触，但能容忍玩家被放在别的层。")]
        [SerializeField] private LayerMask _pullerLayers = ~0;

        [Header("拔出动作（白盒占位）")]
        [Tooltip("拔出用时（秒）。")]
        [SerializeField, Min(0f)] private float _pullDuration = 0.45f;

        [Tooltip("拔出来时向上飘多高（世界单位）。")]
        [SerializeField] private float _pullRise = 1.2f;

        [Tooltip("拔出来时转多少度。")]
        [SerializeField] private float _pullSpin = 240f;

        [Tooltip("拔完之后隐藏自己。接正式美术 / 拾取系统时取消勾选，改由那边接手。")]
        [SerializeField] private bool _hideAfterPull = true;

        [Header("白盒")]
        [Tooltip("剑的美术节点（会动的那个子物体）。生成工具会把 Art 挂上来。")]
        [SerializeField] private Transform _art;

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        [Header("拔出来时")]
        [Tooltip("拔出瞬间要接的事情（拿到剑 / 播特效 / 播台词）。把接收方拖进来即可。")]
        [SerializeField] private UnityEvent _onExtracted = new UnityEvent();

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>解锁状态翻转瞬间。表现层可以据此换提示（「锁死」↔「可拔」）。</summary>
        public event Action<bool> OnFreeChanged;

        /// <summary>拔出来瞬间（边沿）。**这时才真的拿到剑。**</summary>
        public event Action OnExtracted;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>规则闸门：摩擦规则够不够松（反转 / 或倍率够小）。</summary>
        public bool IsFree { get; private set; }

        /// <summary>距离闸门：圈里有没有「取剑的人」。</summary>
        public bool PlayerInRange { get; private set; }

        /// <summary>现在按一下能不能拔出来。</summary>
        public bool CanPull => !IsExtracted && !IsPulling && (!_requireFreeRule || IsFree)
                            && (!_requirePlayer || PlayerInRange);

        /// <summary>已经拔出来了。</summary>
        public bool IsExtracted { get; private set; }

        /// <summary>正在播拔出动作。</summary>
        public bool IsPulling { get; private set; }

        // ==================================================================
        //  运行时
        // ==================================================================

        private IRuleSystem _rules;
        private PlayerController _puller;
        private Vector3 _artRest;
        private float _pullClock;

        /// <summary>
        /// 探测命中缓存。
        /// </summary>
        /// <remarks>给得宽一点：剑边上一圈瓦砾（碎块落定后是静态碰撞体）也在这个圈里，
        /// 缓存太小的话人会被挤出命中列表、看着像「站在旁边也拔不出来」。</remarks>
        private readonly Collider2D[] _hits = new Collider2D[32];

        private bool _warnedNoRules;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_art != null) _artRest = _art.localPosition;
        }

        /// <summary>被 <c>GravityBoulder</c> 启用时走这里：重新订阅并**立刻**按当前规则评估一次
        /// —— 剑常常是在「摩擦 × 反转」已经开着的状态下露出来的。</summary>
        private void OnEnable()
        {
            Subscribe();
            Apply();
        }

        private void OnDisable() => Unsubscribe();

        private void FixedUpdate()
        {
            if (IsExtracted || IsPulling) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            PlayerInRange = Probe();

            if (!CanPull) return;

            BeginPull();
        }

        private void Update()
        {
            if (!IsPulling) return;

            // 暂停冻结：与其它按帧推进的系统读同一个出口（暂停时返回 0）。
            float dt = TimeManager.Instance != null
                ? TimeManager.Instance.DeltaTime
                : UnityEngine.Time.deltaTime;
            if (dt <= 0f) return;

            _pullClock += dt;

            float duration = Mathf.Max(_pullDuration, 0.0001f);
            float t = Mathf.Clamp01(_pullClock / duration);
            float eased = 1f - (1f - t) * (1f - t);   // 起手快、收尾慢

            if (_art != null)
            {
                _art.localPosition = _artRest + new Vector3(0f, _pullRise * eased, 0f);
                _art.localRotation = Quaternion.Euler(0f, 0f, _pullSpin * eased);
            }

            if (t >= 1f) FinishPull();
        }

        // ==================================================================
        //  规则
        // ==================================================================

        /// <summary>
        /// 读当前摩擦规则重新评估。由 <c>IRuleSystem.OnRulesChanged</c> 自动触发，也可以手动调用。
        /// </summary>
        public void Apply()
        {
            if (_rules == null) _rules = RuleSystem.Instance;
            if (_rules == null) return;

            IAttributeManager attrs = _rules.Attributes;
            if (attrs == null) return;

            SetFree(IsFreeUnder(attrs.Values, _acceptInverted, _acceptWeakFriction, _releaseThreshold));
        }

        /// <summary>
        /// 摩擦规则够不够松（纯函数，便于断言）。
        /// </summary>
        /// <remarks>反转与「倍率变小」是两件事，所以拆成两个开关 ——
        /// 默认只要反转，与 <c>RustyGear</c>（只要倍率小）刻意错开。</remarks>
        public static bool IsFreeUnder(in AttributeValues values, bool acceptInverted,
                                       bool acceptWeakFriction, float releaseThreshold)
        {
            if (acceptInverted && values.FrictionInverted) return true;
            return acceptWeakFriction && values.FrictionScale < releaseThreshold - ThresholdEpsilon;
        }

        private void SetFree(bool free)
        {
            if (IsFree == free) return;

            IsFree = free;
            RefreshLabel();
            OnFreeChanged?.Invoke(free);
        }

        // ==================================================================
        //  取剑
        // ==================================================================

        /// <summary>圈里有没有「取剑的人」。</summary>
        private bool Probe()
        {
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(_pullerLayers);

            int count = Physics2D.OverlapCircle(transform.position, _pullRadius, filter, _hits);
            for (int i = 0; i < count; i++)
            {
                if (!PlayerLookup.IsPlayer(_hits[i])) continue;

                _puller = _hits[i].GetComponentInParent<PlayerController>();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 拔剑。两个闸门都过了才动手。
        /// </summary>
        /// <param name="ignoreDistance">
        /// 跳过「有没有人站在旁边」这一条。给剧本 / 测试用 ——
        /// 正式玩法里**不要**传 true，否则规则一开剑自己就飞出来了。
        /// </param>
        /// <returns>确实开始拔了返回 true。</returns>
        public bool TryPull(bool ignoreDistance = false)
        {
            if (IsExtracted || IsPulling) return false;
            if (_requireFreeRule && !IsFree) return false;
            if (_requirePlayer && !ignoreDistance && !PlayerInRange) return false;

            BeginPull();
            return true;
        }

        private void BeginPull()
        {
            IsPulling = true;
            _pullClock = 0f;

            // 剑已经松了，别再让提示写着「锁死」。
            RefreshLabel();
        }

        private void FinishPull()
        {
            IsPulling = false;
            IsExtracted = true;

            GrantAbility();
            RefreshLabel();
            OnExtracted?.Invoke();
            _onExtracted.Invoke();

            // 事件先发再隐藏：订阅方可能还要读剑的位置。
            if (_hideAfterPull) gameObject.SetActive(false);

            // 剑拔出来了是永久进度，必须落进世界状态（存档点存档时整体带走）。
            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, true);
        }

        private void Start()
        {
            // 读档恢复：存档里剑已经被拔走 —— 直接补上能力、隐藏剑，不要再播一遍拔剑动画。
            if (!RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) return;

            IsExtracted = true;
            GrantAbility();
            RefreshLabel();
            OnExtracted?.Invoke();
            if (_hideAfterPull) gameObject.SetActive(false);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "extracted");

        // ==================================================================
        //  订阅
        // ==================================================================

        private void Subscribe()
        {
            if (_rules != null) return;

            _rules = RuleSystem.Instance;
            if (_rules != null)
            {
                _rules.OnRulesChanged += Apply;
                return;
            }

            if (!_warnedNoRules)
            {
                _warnedNoRules = true;
                Debug.LogWarning(
                    "[SwordInStone] RuleSystem 尚未初始化，剑不会响应摩擦规则。" +
                    "检查场景里有没有挂 RuleSystemDriver。", this);
            }
        }

        private void Unsubscribe()
        {
            if (_rules == null) return;

            _rules.OnRulesChanged -= Apply;
            _rules = null;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒的两处引用（剑的美术节点 / 标签）。</summary>
        public void SetWhitebox(Transform art, TextMesh label)
        {
            _art = art;
            if (_art != null) _artRest = _art.localPosition;

            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(bool free)
        {
            return free ? "剑 · 可拔" : "剑 · 锁死";
        }

        private void OnValidate() => RefreshLabel();

        /// <summary>把状态写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏（与 <c>ImpactButton</c> 同一处理）。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = IsExtracted ? "剑 · 已取" : LabelText(IsFree || !_requireFreeRule);
            if (_label.text != wanted) _label.text = wanted;
        }

        /// <summary>拔出来时把能力写进玩家账本；没有账本就退回玩家自己的位标记。</summary>
        private void GrantAbility()
        {
            PlayerProgress progress = _puller != null
                ? _puller.GetComponent<PlayerProgress>()
                : PlayerProgress.Find();

            if (progress != null)
            {
                progress.UnlockAbility(_grantAbility);
                return;
            }

            _puller?.UnlockAbility(_grantAbility);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.55f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, _pullRadius);
        }
    }
}
