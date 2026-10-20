using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 按钮控制的**虚实平台**：实化时是一块能站的地面，虚化时只剩半透明轮廓、谁都能穿过去。
    /// </summary>
    /// <remarks>
    /// <para><b>怎么切换</b>：实现 <see cref="ISwitchTarget"/> —— 把本组件拖到
    /// <c>CityButton</c> / <c>CityLever</c> 的 <c>FollowMotor</c> 上就接好了。
    /// 一个按钮要同时管好几块，就把平台们接到 <see cref="PhasePlatformGroup"/> 上、把**组**拖给按钮
    /// （生成工具默认这么接：每块平台顶上摆一个按钮，按任意一个翻**全部**）。
    /// ⚠️ 按钮那边必须勾 **Toggle**（切换型）：不勾的按钮按一次就锁死，不会来回切。</para>
    ///
    /// <para><b>实化 / 虚化怎么实现</b>：虚化 = 摘掉自己的 <see cref="BoxCollider2D"/>
    /// （层 = <c>Ground</c>）。玩家的位移走 <c>Rigidbody2D.Slide</c>、纸飞机走自己的扫掠，
    /// 两边都只认「世界里有没有这块碰撞体」—— 摘掉就是真穿过去，
    /// 玩家 / 飞刀 / 敌人都不需要认识本组件（与 <c>OneWayGate</c> 同一条路子）。
    /// 外观只在 <see cref="_art"/> 上淡到半透明，**不换层、不用 PlatformEffector2D**。</para>
    ///
    /// <para><b>按钮是点按式</b>：每次按下都翻一次，<b>不看按钮自己的开 / 关状态</b>
    /// （见 <see cref="SetSwitchState"/>）。平台在切换后会把接在自己身上的按钮复位成「未按下」
    /// （<c>PhaseButtonRelease</c>），所以任何一块平台上的按钮在任何时候都能再按。</para>
    ///
    /// <para><b>初始状态</b>只由 <see cref="_startsSolid"/> 决定；之后每按一次翻一次。
    /// 同一组里初始状态不同的两块平台会一直保持相反 —— 开局就一实一虚，按一下一起翻过去。</para>
    ///
    /// <para><b>按下之后隔 <see cref="_switchDelay"/> 秒（默认 1 秒）才切换</b>：
    /// 按钮在平台上，玩家按下后还有时间跳走 —— 碰撞体和外观都等倒计时结束才一起变
    /// （外观再从当前颜色淡到目标色，见 <see cref="_fadeSeconds"/>）。
    /// 同一个目标状态重复打进来不会重置倒计时。</para>
    ///
    /// <para><b>实化时玩家正占着这块空间</b>（例如他站在虚化平台的位置上按了按钮）：
    /// 默认等他自己走出来再实化。直接把碰撞体塞进 Kinematic 玩家身上，
    /// <c>Slide</c> 不会把人排出去，只会把人卡在墙里（<c>SpringBackBody</c> 踩过同一个坑）。</para>
    ///
    /// <para>⚠️ 不参与时间回溯（没有刚体、没进世界注册表）—— 与多数地图组件一致。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PhasePlatform : MonoBehaviour, ISwitchTarget
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("初始状态")]
        [Tooltip("开局是**实化**（能站、挡路）还是**虚化**（半透明、穿得过去）。\n" +
                 "按一下按钮就翻到另一态，再按一下翻回来。")]
        [SerializeField] private bool _startsSolid = true;

        [Header("表现")]
        [Tooltip("负责外观的 SpriteRenderer（白盒子方块 / 正式美术）。留空则自动取本物体或子物体上的第一个。\n" +
                 "玩法判定不读它 —— 它只负责「实 / 虚」的颜色与透明度。")]
        [SerializeField] private SpriteRenderer _art;

        [Tooltip("实化时的颜色。")]
        [SerializeField] private Color _solidTint = new Color(0.62f, 0.86f, 1f, 1f);

        [Tooltip("虚化时的颜色（alpha 就是可见强度，默认 0.28 —— 还看得见轮廓，但明显是虚的）。")]
        [SerializeField] private Color _etherealTint = new Color(0.62f, 0.86f, 1f, 0.28f);

        [Tooltip("切换后外观淡入淡出耗时（秒）。0 = 瞬间。碰撞体与外观在倒计时结束的当帧一起切换，只有颜色在过渡。")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 0.16f;

        [Header("白盒")]
        [Tooltip("白盒标签：文字随状态自动写「实化 / 虚化」。**接正式美术时把这个引用清空**。")]
        [SerializeField] private TextMesh _label;

        [Header("切换节奏")]
        [Tooltip("按下按钮后隔多久才真正实化 / 虚化（秒）。给玩家留出从平台上跳走的时间。0 = 立刻切换。")]
        [SerializeField, Min(0f)] private float _switchDelay = 0.5f;

        [Header("安全")]
        [Tooltip("实化时如果玩家正占着平台的空间，等他自己走出来再实化 —— 直接塞进去会把人卡在墙里。")]
        [SerializeField] private bool _waitForPlayerToClear = true;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>缩一圈再做重叠查询：<c>OverlapBox</c> 把「刚好贴着」也算重叠，
        /// 平台挨着地面 / 玩家站在边上时会误判成「被人占着」。单位 = 世界单位。</summary>
        private const float ClearMargin = 0.05f;

        private BoxCollider2D _box;
        private readonly Collider2D[] _probe = new Collider2D[8];

        /// <summary>当前是不是实化（碰撞体开着）。</summary>
        private bool _solid;

        /// <summary>有一个「按下后才生效」的切换正在倒计时。</summary>
        private bool _hasQueued;

        /// <summary>倒计时结束后要切到哪一态（true = 实化）。</summary>
        private bool _queuedSolid;

        /// <summary>倒计时已经走了多久。</summary>
        private float _queuedTimer;

        /// <summary>倒计时到了，但玩家还占着平台空间 —— 等他走开再实化。</summary>
        private bool _waitingForClear;

        /// <summary>同一帧里多路来源（近战 + 纸飞机 / 多个按钮）只算一次按下。</summary>
        private int _lastSwitchFrame = -1;

        /// <summary>这次按下之后要把接在本平台上的按钮复位成「未按下」。</summary>
        private bool _releaseButtons;

        /// <summary>接在本平台上的按钮缓存（点按式复位用）。</summary>
        private CitySwitch[] _buttons;

        /// <summary>外观的实化程度：0 = 完全虚化，1 = 完全实化。只驱动颜色过渡。</summary>
        private float _solidity;

        /// <summary>现在是不是实化状态（表现层 / 编辑器读它）。</summary>
        public bool IsSolid => _solid;

        /// <summary>开局是不是实化。</summary>
        public bool StartsSolid => _startsSolid;

        /// <summary>按下按钮之后、真正切换之前的倒计时是不是还在走。</summary>
        public bool IsSwitchPending => _hasQueued;

        /// <summary>距离真正切换还剩几秒（没在倒计时时是 0）。</summary>
        public float PendingRemaining => _hasQueued ? Mathf.Max(0f, _switchDelay - _queuedTimer) : 0f;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _box = GetComponent<BoxCollider2D>();
            if (_box == null)
            {
                Debug.LogError("[PhasePlatform] 同物体上没有 BoxCollider2D —— 这块平台虚化 / 实化都不会改变任何碰撞。", this);
            }
            else if (_box.isTrigger)
            {
                Debug.LogWarning("[PhasePlatform] 碰撞体勾了 Is Trigger，它不是一块能站的地面。", this);
            }

            if (_art == null) _art = ResolveArt();

            ApplySolid(_startsSolid);
            ApplyVisualImmediate();
            RefreshLabel();
        }

        /// <summary>重开一局 / 对象池取出：回到初始状态，不能接着上一局的翻转态。</summary>
        private void OnEnable()
        {
            if (_box == null) _box = GetComponent<BoxCollider2D>();

            _hasQueued = false;
            _waitingForClear = false;
            _queuedTimer = 0f;
            _lastSwitchFrame = -1;
            _releaseButtons = false;

            ApplySolid(_startsSolid);
            ApplyVisualImmediate();
            RefreshLabel();
        }

        /// <summary>倒计时与外观过渡。</summary>
        /// <remarks>碰撞体不在这里动 —— 倒计时一到就 <see cref="ApplySolidNow"/>，玩法状态必须当帧生效。</remarks>
        private void Update()
        {
            if (TimeManager.Instance?.IsPaused == true) return;

            StepQueuedSwitch();
            StepFade();
        }

        /// <summary>被推迟的实化：等玩家离开平台所占的空间。</summary>
        private void FixedUpdate()
        {
            if (!_waitingForClear) return;
            if (TimeManager.Instance?.IsPaused == true) return;
            if (PlayerOccupiesSpace()) return;

            _waitingForClear = false;
            ApplySolid(true);
        }

        /// <summary>点按式按钮的复位：按完立刻变回「未按下」，下一次按才是完整的一次按下。</summary>
        private void LateUpdate()
        {
            if (!_releaseButtons) return;
            _releaseButtons = false;
            PhaseButtonRelease.ReleaseAll(this, ref _buttons);
        }

        // ==================================================================
        //  ISwitchTarget
        // ==================================================================

        /// <summary>
        /// **点按式**：每次调用都翻一次当前状态，然后按 <see cref="_switchDelay"/> 倒计时再真正切换。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>不看 <paramref name="on"/></b>：按钮用 <c>CitySwitch</c> 的单次模式（<c>Toggle = false</c>），
        /// 每次按下都送 <c>true</c>；平台把「一次调用」当成「一次按下」，自己翻转。
        /// 这样每块平台上的按钮互相独立，谁也不会因为自己记着开 / 关而吃掉一次按下。</para>
        /// <para>同一帧里多路来源（近战 + 纸飞机 / 多个按钮）只算一次按下。</para>
        /// <para>倒计时期间再按一次会**从队列里的目标态**继续翻（两次快按 = 取消），
        /// 而不是从当前实际状态翻 —— 否则「连按两下」会变成只翻一次。</para>
        /// </remarks>
        public void SetSwitchState(bool on)
        {
            if (Time.frameCount == _lastSwitchFrame) return;   // 同一帧只算一次按下
            _lastSwitchFrame = Time.frameCount;

            bool target = !(_hasQueued ? _queuedSolid : _solid);
            QueueSolid(target);
            _releaseButtons = true;                            // 按完让按钮回到「未按下」，任何时刻都能再按
        }

        /// <summary>排一次切换：0 延迟立即生效，否则起倒计时。</summary>
        private void QueueSolid(bool solid)
        {
            if (_switchDelay <= 0f)
            {
                ApplySolidNow(solid);
                return;
            }

            _hasQueued = true;
            _queuedSolid = solid;
            _queuedTimer = 0f;
            _waitingForClear = false;                          // 重新计时，之前那份「等玩家让开」作废
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>倒计时到点：真正切换。玩家占着空间时（只可能发生在实化方向）先挂起。</summary>
        private void StepQueuedSwitch()
        {
            if (!_hasQueued) return;

            _queuedTimer += Time.deltaTime;
            if (_queuedTimer < _switchDelay) return;

            ApplySolidNow(_queuedSolid);
        }

        /// <summary>真正切换。实化前先确认玩家没占着平台的空间。</summary>
        private void ApplySolidNow(bool solid)
        {
            _hasQueued = false;
            _waitingForClear = false;

            if (!solid)
            {
                ApplySolid(false);      // 虚化随时都能立刻生效
                return;
            }

            if (_waitForPlayerToClear && PlayerOccupiesSpace())
            {
                _waitingForClear = true;
                Debug.Log("[PhasePlatform] 玩家正占着平台的空间，等他离开再实化。", this);
                return;
            }

            ApplySolid(true);
        }

        /// <summary>外观过渡。</summary>
        private void StepFade()
        {
            float target = _solid ? 1f : 0f;
            if (Mathf.Approximately(_solidity, target)) return;

            _solidity = _fadeSeconds <= 0f
                ? target
                : Mathf.MoveTowards(_solidity, target, Time.deltaTime / _fadeSeconds);
            ApplyVisual();
        }

        private void ApplySolid(bool solid)
        {
            _solid = solid;
            if (_box != null) _box.enabled = solid;     // 挡路的就是它
            RefreshLabel();
        }

        private void ApplyVisualImmediate()
        {
            _solidity = _solid ? 1f : 0f;
            ApplyVisual();
        }

        /// <summary>没接 <see cref="_art"/> 时自己找：优先取名为 <c>Art</c> 的子物体。</summary>
        /// <remarks>平台顶面上站着一个 CityButton（它自带 SpriteRenderer）——
        /// 直接 <c>GetComponentInChildren</c> 可能抓到按钮的精灵，把按钮淡成半透明。</remarks>
        private SpriteRenderer ResolveArt()
        {
            Transform art = transform.Find("Art");
            if (art != null && art.TryGetComponent(out SpriteRenderer named)) return named;

            return GetComponentInChildren<SpriteRenderer>(true);
        }

        private void ApplyVisual()
        {
            if (_art == null) return;

            // 只在真的变了才写：OnValidate 每次脚本重编译都会跑，无条件赋值会把场景标脏。
            Color wanted = Color.Lerp(_etherealTint, _solidTint, _solidity);
            if (_art.color != wanted) _art.color = wanted;
        }

        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_solid);
            if (_label.text != wanted) _label.text = wanted;
        }

        /// <summary>白盒标签该写什么。生成工具与本类共用它，免得两处写歪。</summary>
        public static string LabelText(bool solid)
        {
            return solid ? "实化" : "虚化";
        }

        /// <summary>玩家（身体或受击箱）是不是正压在平台所占的空间里。</summary>
        /// <remarks>碰撞体此时可能是 disabled 的，所以按 <c>size × lossyScale</c> 自己算世界盒，
        /// 不用 <c>Collider2D.bounds</c>（那条路会塌）。</remarks>
        private bool PlayerOccupiesSpace()
        {
            if (_box == null) return false;

            Vector3 scale = transform.lossyScale;
            var size = new Vector2(Mathf.Abs(_box.size.x * scale.x),
                                   Mathf.Abs(_box.size.y * scale.y)) - Vector2.one * ClearMargin * 2f;
            if (size.x <= 0f || size.y <= 0f) return false;

            var center = (Vector2)transform.TransformPoint(_box.offset);
            var filter = new ContactFilter2D { useTriggers = true };
            int count = Physics2D.OverlapBox(center, size, transform.eulerAngles.z, filter, _probe);

            for (int i = 0; i < count; i++)
            {
                if (PlayerLookup.IsPlayer(_probe[i])) return true;
            }
            return false;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具 / Inspector 用：换初始状态并立刻摆出来（运行时不改当前状态）。</summary>
        public void SetStartsSolid(bool solid)
        {
            _startsSolid = solid;
            if (Application.isPlaying) return;

            ApplySolid(solid);
            ApplyVisualImmediate();
        }

        /// <summary>只给编辑器工具 / Inspector 用：接上白盒的另两处引用（外观 / 标签）。</summary>
        public void SetWhitebox(SpriteRenderer art, TextMesh label)
        {
            _art = art;
            _label = label;
            ApplyVisualImmediate();
            RefreshLabel();
        }

        /// <summary>编辑期就把初始状态如实摆出来：场景里一眼看得出开局是实是虚。</summary>
        private void OnValidate()
        {
            if (Application.isPlaying) return;

            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_art == null) _art = ResolveArt();

            if (_box != null && _box.enabled != _startsSolid) _box.enabled = _startsSolid;
            _solid = _startsSolid;
            ApplyVisualImmediate();
            RefreshLabel();
        }

        private void OnDrawGizmos()
        {
            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_box == null) return;

            // 绿 = 实化（挡路），青 = 虚化（穿得过去）。
            Gizmos.color = _solid
                ? new Color(0.35f, 0.9f, 0.55f, 0.9f)
                : new Color(0.35f, 0.85f, 1f, 0.35f);

            Vector3 scale = transform.lossyScale;
            var size = new Vector3(Mathf.Abs(_box.size.x * scale.x),
                                   Mathf.Abs(_box.size.y * scale.y), 1f);
            Gizmos.DrawWireCube(transform.TransformPoint(_box.offset), size);
        }
    }
}
