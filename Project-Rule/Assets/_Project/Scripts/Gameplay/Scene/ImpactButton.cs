using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 巨大按钮：**动量够大的一撞把它按下去** —— 判据是「质量 × 速度」沿按压方向的分量，
    /// 不是「上面站没站着人」。按下后做什么见类注释末尾的 TODO。
    /// </summary>
    /// <remarks>
    /// <para><b>它和压力板的区别</b>：压力板看**重量**（站上去就响应），本按钮看**动量** ——
    /// 走上去、慢慢蹭过去都不算，得**砸**上去（从高处落下）或**撞**上去（冲刺）。</para>
    ///
    /// <para><b>与规则系统的联动（一行额外代码都没有）</b>：动量里的质量读的是
    /// <c>Rigidbody2D.mass</c>，而质量规则写的就是它。于是「质量 × 增强」让铁箱变 3 倍重，
    /// 同一个推法撞出来的动量就是 3 倍 —— 原本撞不动的按钮变成可解了；
    /// 反过来「质量 × 削弱」能把本来撞得动的变得撞不动。可解性随规则改变，
    /// 与 <c>PushableBody</c>（推箱子）同一条路线。</para>
    ///
    /// <para><b>为什么每物理帧自己扫探测盒、不用碰撞回调</b>：玩家是 Kinematic 刚体、
    /// 位移走 <c>Rigidbody2D.Slide</c>（<c>useAttachedTriggers = false</c>），接触 / 触发回调不可靠。
    /// 与 <c>SpringPlatform</c> 的承重探测同一个做法。</para>
    ///
    /// <para><b>动量怎么取</b>：Dynamic 刚体（铁箱）直接 <c>mass × linearVelocity</c>；
    /// Kinematic 实体（玩家 / 敌人）问 <see cref="IMomentumSource"/> —— 它们的
    /// <c>linearVelocity</c> 是死的（位移走 <c>Rigidbody2D.Slide</c>，模拟之后仍是 (0,0)），
    /// 只有自己知道自己在动多快。</para>
    ///
    /// <para><b>阈值怎么定</b>（当前默认质量下，单位 kg·单位/秒）：
    /// <list type="bullet">
    /// <item>玩家走路 1 × 8 = 8　·　冲刺 1 × 26 = 26　·　从高处落下 1 × 30 = 30</item>
    /// <item>铁箱推着走 8 × 3 ≈ 24　·　掉下来砸 8 × 30 = 240</item>
    /// <item>小怪走路 5 × 1.6 = 8（撞不动）　·　被击退 / 从高处掉下来才够</item>
    /// </list>
    /// 默认 20 ≈「走路撞不动、冲刺刚好撞得动、重物砸下来必中」。</para>
    ///
    /// <para>⚠️ 玩家当前 <c>mass = 1</c> 且**没挂** <c>RuleAffectedBody</c>，所以玩家自己的
    /// 动量不受质量规则影响 —— 想让「质量 × 增强」也改变玩家撞按钮的力度，
    /// 给玩家挂上那个组件即可（摩擦那一栏可以关）。</para>
    ///
    /// <para>TODO（**按下之后触发什么还没定**）：接在 <see cref="OnPressed"/> 上（代码），
    /// 或在 Inspector 的「按下时」里拖接收方（不写代码）。白盒阶段的「形态」只是把帽
    /// 沿按压方向压下去 <see cref="_travel"/>。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class ImpactButton : MonoBehaviour
    {
        /// <summary>默认探测层：Default（玩家）+ Ground（铁箱等动态物）+ Enemy（敌人）。</summary>
        private const int DefaultTriggerLayers = 1 | (1 << 8) | (1 << 9);

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("按压方向")]
        [Tooltip("按钮帽被压下去的方向，也是动量取分量的轴。\n" +
                 "默认「朝下」＝ 物体从上方砸下来把它压下去；装在墙上横着被撞的按钮改成对应的方向。")]
        [SerializeField] private OneWayGate.PassDirection _press = OneWayGate.PassDirection.ToDown;

        [Header("触发（动量 = 质量 × 速度）")]
        [Tooltip("触发阈值（kg·单位/秒）：物体动量沿按压方向的分量超过它才算数。\n" +
                 "参考：玩家走路 8 · 冲刺 26 · 从高处落下 30；铁箱推着走 ≈24 · 砸下来 240。\n" +
                 "默认 20 ≈「走路撞不动、冲刺刚好撞得动、重物砸下来必中」。")]
        [SerializeField, Min(0f)] private float _momentumThreshold = 20f;

        [Tooltip("谁按得动它。默认勾了 Default（玩家）+ Ground（铁箱等动态物）+ Enemy（敌人）。\n" +
                 "⚠️ 实体在哪个层就要勾哪个层 —— 漏了它就在探测范围外，撞上去什么都不会发生。\n" +
                 "静态地形没有动量，勾了也不会触发。")]
        [SerializeField] private LayerMask _triggerLayer = DefaultTriggerLayers;

        [Tooltip("探测提前量（世界单位）：从按钮被撞的那一面往外扩多少。\n" +
                 "它必须罩得住快速物体**一帧**的位移，否则会穿过去而没被记到 ——\n" +
                 "下落最快 30 单位/秒 → 一帧 0.6，所以别小于 0.6。")]
        [SerializeField, Min(0.1f)] private float _probeReach = 0.8f;

        [Tooltip("按下后是否锁存。勾上 = 触发一次就永久保持按下（开关型机关）；\n" +
                 "取消 = 碰撞的东西离开探测区后自己弹回来（承重型）。")]
        [SerializeField] private bool _latching = true;

        [Header("形态")]
        [Tooltip("按钮帽（会动的那个子物体）。生成工具会把 Cap 挂上来。")]
        [SerializeField] private Transform _cap;

        [Tooltip("按下时帽沿按压方向下沉的距离（世界单位）。")]
        [SerializeField, Min(0f)] private float _travel = 0.18f;

        [Header("白盒")]
        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        [Header("按下时（TODO：做什么还没定）")]
        [Tooltip("按下瞬间要接的事情。把接收方拖进来即可，不需要写代码。")]
        [SerializeField] private UnityEvent _onPressed = new UnityEvent();

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>按下瞬间（边沿）。**按下去要触发什么还没定**，见类注释的 TODO。</summary>
        public event Action OnPressed;

        /// <summary>弹回瞬间（边沿）。只有 <see cref="_latching"/> 关掉时才会发生。</summary>
        public event Action OnReleased;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>现在是按下态还是弹起态。</summary>
        public bool IsPressed { get; private set; }

        /// <summary>最近一次触发是谁按的（调试 / 表现用，可能是 <c>null</c>）。</summary>
        public GameObject LastImpactor { get; private set; }

        /// <summary>最近一次触发时的动量（kg·单位/秒）。调试用：拿它对着阈值看差多少。</summary>
        public float LastMomentum { get; private set; }

        /// <summary>按压方向的单位向量 —— 帽下沉的方向，也是动量取分量的轴。</summary>
        public Vector2 Axis
        {
            get
            {
                switch (_press)
                {
                    case OneWayGate.PassDirection.ToLeft:  return Vector2.left;
                    case OneWayGate.PassDirection.ToUp:    return Vector2.up;
                    case OneWayGate.PassDirection.ToRight: return Vector2.right;
                    default:                               return Vector2.down;
                }
            }
        }

        // ==================================================================
        //  运行时
        // ==================================================================

        private BoxCollider2D _box;

        /// <summary>探测命中缓存。同时挤进按钮前两个以上活动物体的机会很小。</summary>
        private readonly Collider2D[] _hits = new Collider2D[8];

        /// <summary>帽的弹起态局部位置。摆位时是作者摆好的那个值，按下就是它 + 行程。</summary>
        private Vector3 _capRest;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _box = GetComponent<BoxCollider2D>();
            if (_box == null)
            {
                Debug.LogError("[ImpactButton] 同物体上没有 BoxCollider2D —— 谁也没法撞到它。", this);
            }
            else if (_box.isTrigger)
            {
                Debug.LogWarning("[ImpactButton] 碰撞体勾了 Is Trigger，它挡不住人 —— " +
                                 "按钮需要一块实心碰撞体（人 / 箱子才停在它表面、动量才算得准）。", this);
            }

            if (_cap != null) _capRest = _cap.localPosition;

            SetPressed(false);   // 一开始一定是弹起态
        }

        /// <summary>重开一局 / 对象池取出时也要回到弹起态，不能接着上一局的按下。</summary>
        private void OnEnable() => SetPressed(false);

        private void FixedUpdate()
        {
            // 暂停：物理已停步进（Physics2D.simulationMode = Script），判定也跟着停 —— 保持当前形态。
            if (TimeManager.Instance?.IsPaused == true) return;

            // 锁存之后就没这一页了：不再探测。
            if (IsPressed && _latching) return;

            float best = Measure(out GameObject source, out bool occupied);

            if (!IsPressed)
            {
                if (best >= _momentumThreshold) SetPressed(true, source, best);
            }
            else if (!occupied)
            {
                // 只有非锁存才会走到这里：压着的东西都走了，自己弹回来。
                SetPressed(false);
            }
        }

        // ==================================================================
        //  探测
        // ==================================================================

        /// <summary>
        /// 扫一次探测区，返回其中**最大的沿轴动量**。
        /// </summary>
        /// <param name="source">动量最大的那个东西（可能是 <c>null</c>）。</param>
        /// <param name="occupied">探测区里有没有非自己的对象（非锁存模式靠它判「该不该弹回」）。</param>
        private float Measure(out GameObject source, out bool occupied)
        {
            source = null;
            occupied = false;

            ProbeGeometry(out Vector2 center, out Vector2 size);

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_triggerLayer);

            int count = Physics2D.OverlapBox(center, size, 0f, filter, _hits);

            Vector2 axis = Axis;
            float best = 0f;

            for (int i = 0; i < count; i++)
            {
                Collider2D col = _hits[i];
                if (col == null) continue;

                // 别把按钮自己算进来：探测区贴着自身表面，浮点误差可能蹭到。
                if (col.transform.IsChildOf(transform)) continue;

                occupied = true;

                float momentum = MomentumAlong(col, axis);
                if (momentum > best)
                {
                    best = momentum;
                    Rigidbody2D body = col.attachedRigidbody;
                    source = body != null ? body.gameObject : col.gameObject;
                }
            }

            return best;
        }

        /// <summary>
        /// 一个碰撞体**沿按压方向**的动量（纯函数，便于断言）。
        /// </summary>
        /// <remarks>
        /// <para>取的是分量不是模长：从旁边蹭过去（速度垂直于按压轴）不该算「撞」。</para>
        /// <para>速度来源分两条：实现了 <see cref="IMomentumSource"/> 的实体自报
        /// （Kinematic —— 它们刚体上的 <c>linearVelocity</c> 是死的，位移走 <c>MovePosition</c>）；
        /// 其余 Dynamic 刚体直接 <c>mass × linearVelocity</c>。静态地形没有动量。</para>
        /// </remarks>
        private static float MomentumAlong(Collider2D collider, Vector2 axis)
        {
            Rigidbody2D body = collider.attachedRigidbody;
            if (body == null) return 0f;
            if (body.bodyType == RigidbodyType2D.Static) return 0f;

            var source = body.GetComponent<IMomentumSource>();
            Vector2 momentum = source != null
                ? source.Momentum
                : body.linearVelocity * body.mass;

            float along = Vector2.Dot(momentum, axis);
            return along > 0f ? along : 0f;
        }

        /// <summary>
        /// 探测区：贴在按钮**迎撞的那一面**外侧的一条，从表面向外扩 <see cref="_probeReach"/>。
        /// </summary>
        /// <remarks>OverlapBox 是轴对齐的，所以把「沿轴 / 垂直」两个分量摊回 x / y
        /// （与 <c>BreakableGate</c> 的判定区同一套算法）。</remarks>
        private void ProbeGeometry(out Vector2 center, out Vector2 size)
        {
            Bounds bounds = BoxBounds();
            Vector2 axis  = Axis;
            var perp = new Vector2(-axis.y, axis.x);

            float along  = HalfExtent(bounds, axis);
            float across = HalfExtent(bounds, perp);

            // ⚠️ 是**减**：Axis 是帽下沉的方向（＝撞进来那东西的**运动**方向），
            //    而它在按钮的**来向**那一侧 —— 也就是 −Axis 那一侧。
            center = (Vector2)bounds.center - axis * (along + _probeReach * 0.5f);

            size = new Vector2(
                Mathf.Abs(axis.x) * _probeReach + Mathf.Abs(perp.x) * across * 2f,
                Mathf.Abs(axis.y) * _probeReach + Mathf.Abs(perp.y) * across * 2f);
        }

        // ==================================================================
        //  形态
        // ==================================================================

        /// <summary>切换形态。只在状态真的变了才做 —— 重复调用是幂等的。</summary>
        private void SetPressed(bool pressed, GameObject impactor = null, float momentum = 0f)
        {
            if (IsPressed == pressed) return;

            IsPressed = pressed;
            ApplyCapPose();
            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, pressed);

            if (pressed)
            {
                LastImpactor = impactor;
                LastMomentum = momentum;
                OnPressed?.Invoke();
                _onPressed.Invoke();
            }
            else
            {
                OnReleased?.Invoke();
            }
        }

        private void Start()
        {
            // 读档恢复：存档里这个按钮是按下的（压板 / 撞针都算）。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) SetPressed(true);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "pressed");

        /// <summary>把帽摆到当前形态该在的位置。</summary>
        /// <remarks>⚠️ 按**轴对齐**算：方向直接当世界方向用在局部坐标上。
        /// 根物体带旋转的按钮不支持（与 <c>BreakableGate</c> 同一约定）。</remarks>
        private void ApplyCapPose()
        {
            if (_cap == null) return;

            Vector2 axis = Axis;
            _cap.localPosition = _capRest + new Vector3(axis.x, axis.y, 0f) * (IsPressed ? _travel : 0f);
        }

        // ==================================================================
        //  几何
        // ==================================================================

        /// <summary>
        /// 按钮的世界包围盒。<b>从碰撞体自己的 size / offset 算</b>，不读 <c>Collider2D.bounds</c> ——
        /// 编辑模式的 gizmo 在 Awake 之前就会跑（与 <c>BreakableGate</c> 同一处理）。
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

        /// <summary>包围盒在 <paramref name="axis"/> 方向上的半长。</summary>
        private static float HalfExtent(Bounds bounds, Vector2 axis)
        {
            return Mathf.Abs(axis.x) * bounds.extents.x + Mathf.Abs(axis.y) * bounds.extents.y;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：改按压方向并立刻刷新白盒标签。</summary>
        public void SetPress(OneWayGate.PassDirection press)
        {
            _press = press;
            RefreshLabel();
        }

        /// <summary>只给编辑器工具用：接上白盒的两处引用（帽 / 标签）。</summary>
        public void SetWhitebox(Transform cap, TextMesh label)
        {
            _cap = cap;
            if (_cap != null) _capRest = _cap.localPosition;

            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float momentumThreshold)
        {
            return "按钮 ≥" + momentumThreshold.ToString("0.#");
        }

        private void OnValidate()
        {
            RefreshLabel();
        }

        /// <summary>把阈值写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_momentumThreshold);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            Bounds bounds = BoxBounds();

            // 实心的那一块：按钮本体。
            Gizmos.color = new Color(0.85f, 0.35f, 0.32f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            // 探测区：贴在被撞的那一面外侧。
            ProbeGeometry(out Vector2 center, out Vector2 size);
            Gizmos.color = new Color(1f, 0.75f, 0.28f, 0.5f);
            Gizmos.DrawWireCube(center, size);

            // 撞进来的方向：箭头从探测区外缘指向按钮表面 —— 物体顺着它来。
            Vector2 axis = Axis;
            var perp = new Vector2(-axis.y, axis.x);
            Gizmos.color = new Color(1f, 0.9f, 0.25f, 1f);

            float edge = HalfExtent(bounds, axis);
            var from = (Vector2)bounds.center - axis * (edge + _probeReach);
            var to   = (Vector2)bounds.center - axis * edge;
            Gizmos.DrawLine(from, to);

            const float head = 0.3f;
            Gizmos.DrawLine(to, to - axis * head + perp * head * 0.6f);
            Gizmos.DrawLine(to, to - axis * head - perp * head * 0.6f);
        }
    }
}
