using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 生锈齿轮：锈死的机关 —— **摩擦变小 / 失效之后才转得动**，转起来 = 把机关激活。
    /// </summary>
    /// <remarks>
    /// <para><b>触发判据</b>：读属性管理器里的摩擦倍率，**低于 1（正常）就转** ——
    /// 「摩擦 × 削弱」（0.3）与「摩擦 × 失效」（0）命中。「增强」（3）转不动属正常；
    /// <b>「反转」也不转</b> —— 它只置位方向标志，倍率仍是 1（见 <c>FrictionRule</c>）。</para>
    ///
    /// <para><b>为什么是「读属性」而不是「听玩家释放了什么」</b>：摩擦倍率写的是**世界法则**，
    /// 不分敌我 —— 敌人释放的摩擦削弱一样让它转。本组件不认识玩家，也不认识规则来源。</para>
    ///
    /// <para><b>与 <see cref="ImpactButton"/> 的关系</b>：它俩是同一类开关，出口形态刻意一致 ——
    /// C# 事件（<see cref="OnActivated"/> / <see cref="OnDeactivated"/>）+ Inspector 的
    /// UnityEvent（不写代码就能接）+ 锁存开关。</para>
    ///
    /// <para><b>与暂停的关系</b>：转动推进读 <see cref="ITimeManager.DeltaTime"/>（暂停时 0），
    /// 与其它按帧推进的系统一致。判定本身是事件驱动（规则变化时重读一次），不占每帧。</para>
    ///
    /// <para>TODO（**转起来之后触发什么还没定**）：接在 <see cref="OnActivated"/> 上（代码），
    /// 或在 Inspector 的「转动时」里拖接收方（不写代码）。白盒阶段的「转动」是齿轮自转；
    /// 正式美术接入后填「转动帧」（两帧循环）自动切换成帧动画。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class RustyGear : MonoBehaviour
    {
        /// <summary>阈值比较的容差：正常值 1.0 由常量精确写入，留一点余量挡浮点噪声。</summary>
        private const float ThresholdEpsilon = 0.001f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("触发判据")]
        [Tooltip("摩擦倍率低到多少算「松脱」。正常 = 1，削弱 = 0.3，失效 = 0。\n" +
                 "默认 1.0 ≈「只要摩擦比正常小就转」；增强（3）与反转（倍率仍为 1）不触发。")]
        [SerializeField, Min(0f)] private float _releaseThreshold = 1f;

        [Tooltip("激活后是否锁存：勾上 = 转过一次就永久保持（开关型机关）；\n" +
                 "取消 = 摩擦恢复后自己停转、机关复位（跟着规则窗口走）。")]
        [SerializeField] private bool _latching;

        [Header("形态")]
        [Tooltip("齿轮本体（会转的那个子物体）。生成工具会把 Art 挂上来。")]
        [SerializeField] private Transform _art;

        [Tooltip("静止帧。留空 = 停转时不动它的 sprite。")]
        [SerializeField] private Sprite _idleSprite;

        [Tooltip("转动循环帧（美术：两帧循环就好）。填 2 帧以上就播帧动画；\n" +
                 "留空则用下面的「占位自转」表现转动。")]
        [SerializeField] private Sprite[] _spinFrames = new Sprite[0];

        [Tooltip("转动帧率（帧/秒）。只在填了转动帧时生效。")]
        [SerializeField, Min(0.1f)] private float _spinFps = 6f;

        [Tooltip("没有美术帧时的占位：齿轮每秒自转的角度。")]
        [SerializeField] private float _placeholderDegreesPerSecond = 120f;

        [Header("白盒")]
        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        [Header("转动时（TODO：做什么还没定）")]
        [Tooltip("齿轮转起来瞬间要接的事情。把接收方拖进来即可，不需要写代码。")]
        [SerializeField] private UnityEvent _onActivated = new UnityEvent();

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>转起来瞬间（边沿）。**转起来要触发什么还没定**，见类注释的 TODO。</summary>
        public event Action OnActivated;

        /// <summary>重新卡死瞬间（边沿）。只有 <see cref="_latching"/> 关掉时才会发生。</summary>
        public event Action OnDeactivated;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>现在是在转（激活态）还是锈死（静止态）。</summary>
        public bool IsActivated { get; private set; }

        // ==================================================================
        //  运行时
        // ==================================================================

        private IRuleSystem _rules;
        private SpriteRenderer _renderer;
        private float _frameClock;
        private bool _warnedNoRules;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_art != null) _renderer = _art.GetComponent<SpriteRenderer>();

            ApplyIdleSprite();   // 一开始一定是锈死态
        }

        /// <summary>重开一局 / 对象池取出时也要回到锈死态，不能接着上一局的转动；
        /// 订阅重新挂上，并立刻按当前的摩擦倍率评估一次（规则可能本来就开着）。</summary>
        private void OnEnable()
        {
            SetActivated(false);
            Subscribe();
            Apply();
        }

        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            // 规则系统晚于本组件就绪时补订阅（关卡流程可能先实例化场景组件）。
            if (_rules == null) Subscribe();

            if (!IsActivated) return;

            // 暂停冻结：与其它按帧推进的系统读同一个出口（暂停时返回 0）。
            float dt = TimeManager.Instance != null
                ? TimeManager.Instance.DeltaTime
                : UnityEngine.Time.deltaTime;
            if (dt <= 0f) return;

            AdvanceSpin(dt);
        }

        private void Start()
        {
            // 读档恢复：齿轮在存档里已经转起来了。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) SetActivated(true);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "activated");

        // ==================================================================
        //  评估
        // ==================================================================

        /// <summary>
        /// 读当前摩擦倍率重新评估。由 <see cref="IRuleSystem.OnRulesChanged"/> 自动触发，也可以手动调用。
        /// </summary>
        public void Apply()
        {
            if (_rules == null) _rules = RuleSystem.Instance;
            if (_rules == null) return;

            // 锁存之后就没这一页了：不再看规则表。
            if (IsActivated && _latching) return;

            IAttributeManager attrs = _rules.Attributes;
            if (attrs == null) return;

            float scale = attrs.Values.FrictionScale;
            SetActivated(ShouldRelease(scale, _releaseThreshold));
        }

        /// <summary>
        /// 摩擦倍率够不够「松脱」（纯函数，便于断言）。
        /// </summary>
        /// <remarks>正常 / 增强 / 反转的倍率都 ≥ 1，所以「小于阈值」一个判据就分开了
        /// 削弱（0.3）与失效（0）。</remarks>
        public static bool ShouldRelease(float frictionScale, float threshold)
        {
            return frictionScale < threshold - ThresholdEpsilon;
        }

        // ==================================================================
        //  形态
        // ==================================================================

        /// <summary>切换状态。只在状态真的变了才做 —— 重复调用是幂等的。</summary>
        private void SetActivated(bool activated)
        {
            if (IsActivated == activated) return;

            IsActivated = activated;
            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, activated);

            if (activated)
            {
                _frameClock = 0f;
                ApplySpinFrame();   // 立刻显示第 0 帧，不等下一个 Update
                OnActivated?.Invoke();
                _onActivated.Invoke();
            }
            else
            {
                ApplyIdleSprite();
                OnDeactivated?.Invoke();
            }
        }

        /// <summary>推进转动：有美术帧就播帧动画，没有就自转占位。</summary>
        private void AdvanceSpin(float dt)
        {
            if (HasSpinFrames())
            {
                _frameClock += dt * _spinFps;
                int frame = Mathf.FloorToInt(_frameClock) % _spinFrames.Length;
                if (_renderer != null) _renderer.sprite = _spinFrames[frame];
                return;
            }

            if (_art != null && !Mathf.Approximately(_placeholderDegreesPerSecond, 0f))
            {
                _art.Rotate(0f, 0f, -_placeholderDegreesPerSecond * dt);
            }
        }

        /// <summary>至少两帧才算「转动循环」—— 单帧转不起来。</summary>
        private bool HasSpinFrames()
        {
            return _spinFrames != null && _spinFrames.Length >= 2 && _spinFrames[0] != null;
        }

        private void ApplySpinFrame()
        {
            if (!HasSpinFrames()) return;
            if (_renderer != null) _renderer.sprite = _spinFrames[0];
        }

        private void ApplyIdleSprite()
        {
            if (_idleSprite == null || _renderer == null) return;
            _renderer.sprite = _idleSprite;
        }

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
                    "[RustyGear] RuleSystem 尚未初始化，齿轮不会响应规则变化。" +
                    "检查场景里有没有挂 RuleSystemDriver。", this);
            }
        }

        private void Unsubscribe()
        {
            if (_rules == null) return;

            // OnRulesChanged 是规则系统的事件，源还在就不会是「假 null」。
            _rules.OnRulesChanged -= Apply;
            _rules = null;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒的两处引用（齿轮本体 / 标签）。</summary>
        public void SetWhitebox(Transform art, TextMesh label)
        {
            _art = art;
            _renderer = _art != null ? _art.GetComponent<SpriteRenderer>() : null;

            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float releaseThreshold)
        {
            return "齿轮 <" + releaseThreshold.ToString("0.#");
        }

        private void OnValidate()
        {
            RefreshLabel();
        }

        /// <summary>把阈值写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声（与 <c>ImpactButton</c> 同一处理）。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_releaseThreshold);
            if (_label.text != wanted) _label.text = wanted;
        }
    }
}
