using System;
using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 单向可破坏门：**站在可破坏的那一侧、面朝门挥一刀，门就碎；另一侧是一堵实心的墙**。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="OneWayGate"/> 的区别</b>：单向门是**让路** —— 玩家走过去，门还留着，
    /// 别人和回头路照样被挡；本门是**碎掉** —— 砍一刀，从此这条路对谁都通。
    /// 两者共用同一个方向枚举（<see cref="OneWayGate.PassDirection"/>）：
    /// 「可破坏方向」和单向门的「允许通过方向」是同一个朝向 —— 都是「这扇门朝哪边开」，
    /// 关卡里换组件时方向不用重新想。</para>
    ///
    /// <para><b>怎么才算「砍开了」</b>：玩家中心进到判定区、还站在**进侧**（可破坏的那一侧）、
    /// 且**面朝门** —— 这时挥刀才算。见 <see cref="InBreakZone"/> 与 <see cref="FacesGate"/>。
    /// 背对着门砍、绕着门走到另一侧砍、或者只是走过去撞上，都不算。</para>
    ///
    /// <para><b>怎么知道玩家挥刀了</b>：订阅 <see cref="PlayerController.OnAttacked"/> /
    /// <see cref="PlayerController.OnHeavyAttacked"/>（轻击连段的每一下、以及重击起手都会发）。
    /// 这两个是 C# 事件、发在玩家自己的物理帧里，是**边沿**语义 ——
    /// 比每帧轮询「他是不是正在攻击」干净：连段状态会持续好几个帧，
    /// 轮询会把「挥完刀之后才走进判定区」也算成命中。</para>
    ///
    /// <para><b>做法：砍开时把自己的碰撞体摘掉。</b>挡路的就是本物体上的
    /// <see cref="BoxCollider2D"/>（层 = <c>Ground</c>）。玩家的位移走 <c>Rigidbody2D.Slide</c>，
    /// 它只看得到「世界里有没有这块碰撞体」—— 摘掉就是真放行，玩家侧不需要认识本组件
    /// （与 <c>OneWayGate</c> / <c>MovingPlatform</c> 同一思路）。</para>
    ///
    /// <para><b>判定区是算出来的，不另挂范围碰撞体</b>：以自身碰撞体的包围盒为中心，
    /// 沿四个方向各外扩 <see cref="_reach"/>（与 <c>OneWayGate</c> 同一套）。
    /// 门的范围天生由门的尺寸推导而来，多挂一个物体就多一处会不同步的地方。</para>
    ///
    /// <para><b>只认玩家</b>：敌人 / 子弹两个方向都被挡（和单向门一样，这是玩家的机关）。
    /// 门要做得比玩家的跳跃高度高，或者上面有天花板 —— 否则他能从门顶上跳过去。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class BreakableGate : MonoBehaviour
    {
        /// <summary>补找玩家的节流：找不到时约半秒试一次，别每帧全场搜一遍。</summary>
        private const float ResolveRetrySeconds = 0.5f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("可破坏方向")]
        [Tooltip("玩家站在这一侧、面朝门挥刀，门会被砍碎；从另一侧来是一堵实心的墙。")]
        [SerializeField] private OneWayGate.PassDirection _pass = OneWayGate.PassDirection.ToRight;

        [Tooltip("判定区：以碰撞体的包围盒为中心，四个方向各外扩这么多（世界单位）。\n" +
                 "玩家中心进到这里、站在可破坏的一侧、面朝门挥刀，门就碎。\n" +
                 "默认 1 约等于「贴到门、以及再退开一步」的范围。")]
        [SerializeField, Min(0.1f)] private float _reach = 1f;

        [Header("引用")]
        [Tooltip("谁能砍碎它。留空则自动找场景里的 PlayerController" +
                 "（玩家若是关卡流程里才实例化的，会自己补找并补订阅）。")]
        [SerializeField] private PlayerController _player;

        [Header("白盒")]
        [Tooltip("白盒方块：砍碎后隐藏。接正式美术时把它指向门板本体 —— 「碎掉」就是把它藏起来。")]
        [SerializeField] private SpriteRenderer _art;

        [Tooltip("白盒标签：文字随可破坏方向自动刷新。**接正式美术时把这个引用清空**。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private BoxCollider2D _box;
        private Collider2D _playerCollider;
        private float _nextResolve;
        private bool _warnedNoPlayer;
        private bool _subscribed;
        private bool _broken;

        /// <summary>是不是已经被砍碎了（碰撞体摘掉，这条路永久通了）。</summary>
        public bool IsBroken => _broken;
        public event Action OnBroken;

        /// <summary>可破坏方向。</summary>
        public OneWayGate.PassDirection Pass => _pass;

        /// <summary>可破坏方向的单位向量 —— 门的所有判定都在这条轴上算。</summary>
        public Vector2 Axis
        {
            get
            {
                switch (_pass)
                {
                    case OneWayGate.PassDirection.ToLeft: return Vector2.left;
                    case OneWayGate.PassDirection.ToUp:   return Vector2.up;
                    case OneWayGate.PassDirection.ToDown: return Vector2.down;
                    default:                              return Vector2.right;
                }
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _box = GetComponent<BoxCollider2D>();
            if (_box == null)
            {
                Debug.LogError("[BreakableGate] 同物体上没有 BoxCollider2D —— 门挡不住任何人。", this);
            }
            else if (_box.isTrigger)
            {
                Debug.LogWarning("[BreakableGate] 碰撞体勾了 Is Trigger，它不是墙。门需要一块实心碰撞体。", this);
            }

            _nextResolve = 0f;
            TryResolvePlayer();

            SetBroken(false);      // 一开始一定是完好的
        }

        /// <summary>重开一局 / 对象池取出时把门装回去，不能接着上一局的破碎状态；订阅也要重新挂上。</summary>
        private void OnEnable()
        {
            SetBroken(false);
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void FixedUpdate()
        {
            // 砍碎之后就没这一页了；玩家晚实例化时在这里补找（找到就补订阅）。
            if (_broken) return;
            if (_playerCollider == null) TryResolvePlayer();
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>玩家挥刀（轻击 / 重击共用）。判定通过就把门砍碎。</summary>
        private void OnPlayerAttacked()
        {
            if (_broken) return;
            if (!TryGetPlayerBounds(out Bounds body)) return;

            Bounds gate = GateBounds();
            if (!InBreakZone(gate, body.center, Axis, _reach)) return;
            if (!FacesGate(gate.center, body.center, _player.Facing)) return;

            SetBroken(true);
        }

        /// <summary>
        /// 「够得着吗」的判据（纯函数，便于断言）：玩家中心在判定区内，
        /// 而且**还站在进侧** —— 也就是可破坏的那一侧。
        /// </summary>
        /// <remarks>
        /// <para>进侧 = <c>along &lt; 0</c>：玩家中心还在这条轴「进得来」的那半边。
        /// 判定区被门心切成两半，只有这一半能砍 —— 另一侧的人被门板挡着，
        /// 本来也走不进来，这条是那件事的第二道保险。</para>
        /// <para>按轴系算，不依赖 <c>Bounds.Contains</c>（那个第三根轴也会参与比较，
        /// z 只要有一点点差就整条判空）。</para>
        /// </remarks>
        public static bool InBreakZone(Bounds gate, Vector2 playerCenter, Vector2 axis, float reach)
        {
            var perp = new Vector2(-axis.y, axis.x);
            var toPlayer = playerCenter - (Vector2)gate.center;
            float along  = Vector2.Dot(toPlayer, axis);
            float across = Vector2.Dot(toPlayer, perp);

            if (along >= 0f) return false;

            float r = Mathf.Max(0.1f, reach);
            return Mathf.Abs(along)  <= HalfExtent(gate, axis)  + r
                && Mathf.Abs(across) <= HalfExtent(gate, perp) + r;
        }

        /// <summary>
        /// 挥刀的方向对不对（纯函数，便于断言）：**面朝门，不能背对着砍**。
        /// </summary>
        /// <remarks>
        /// <para>比较的就是玩家的 <c>Facing</c> —— 和攻击盒的朝向是同一个量
        /// （<c>PerformAttackHit</c> 也按它摆盒子），所以「判定说打得着」和「这一刀挥向哪边」
        /// 永远一致。</para>
        /// <para>横版角色只有左右朝向，所以门正在**正上 / 正下方**时不做朝向判定
        /// （<c>dx ≈ 0</c>）—— 竖直方向的门（<c>ToUp</c> / <c>ToDown</c>）只管位置。</para>
        /// </remarks>
        public static bool FacesGate(Vector2 gateCenter, Vector2 playerCenter, float facing)
        {
            float dx = gateCenter.x - playerCenter.x;
            if (Mathf.Abs(dx) < 0.01f) return true;

            return Mathf.Sign(dx) * facing > 0f;
        }

        private void SetBroken(bool broken)
        {
            bool changed = _broken != broken;
            _broken = broken;
            if (_box != null) _box.enabled = !broken;
            if (_art != null) _art.enabled = !broken;
            if (changed && broken) OnBroken?.Invoke();
        }

        private void Subscribe()
        {
            if (_subscribed || _player == null) return;

            _player.OnAttacked      += OnPlayerAttacked;
            _player.OnHeavyAttacked += OnPlayerAttacked;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            // 玩家先被销毁时 _player 是「假 null」，这里会直接跳过 ——
            // 事件源都没了，摘不摘已经无所谓。
            if (!_subscribed || _player == null) return;

            _player.OnAttacked      -= OnPlayerAttacked;
            _player.OnHeavyAttacked -= OnPlayerAttacked;
            _subscribed = false;
        }

        /// <summary>玩家身体碰撞体的当前包围盒（世界坐标）。</summary>
        private bool TryGetPlayerBounds(out Bounds body)
        {
            if (_playerCollider == null || !_playerCollider.gameObject.activeInHierarchy)
            {
                body = default;
                return false;
            }

            body = _playerCollider.bounds;
            return true;
        }

        /// <summary>解析玩家。找不到时按 <see cref="ResolveRetrySeconds"/> 节流重试 ——
        /// 关卡流程可能比门晚实例化玩家。</summary>
        private void TryResolvePlayer()
        {
            if (_nextResolve > 0f)
            {
                _nextResolve -= UnityEngine.Time.fixedDeltaTime;
                return;
            }
            _nextResolve = ResolveRetrySeconds;

            if (_player == null)
            {
                _player = FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
            }

            if (_player != null) _playerCollider = _player.GetComponent<Collider2D>();

            if (_playerCollider == null && !_warnedNoPlayer)
            {
                _warnedNoPlayer = true;
                Debug.LogWarning("[BreakableGate] 没找到玩家（场景里没有 PlayerController），" +
                                 "这门不会被砍开。玩家若是运行时才实例化的，本组件会自己补找。", this);
            }

            Subscribe();
        }

        /// <summary>
        /// 门的世界包围盒。<b>从碰撞体自己的 size / offset 算</b>，不读 <c>Collider2D.bounds</c> ——
        /// 砍碎后碰撞体是 disabled 的，那条路的返回值会塌掉（编辑模式的 gizmo 也还没 Awake 过）。
        /// </summary>
        /// <remarks>⚠️ 按**轴对齐**算（门是方块，<see cref="_pass"/> 也是四个正交方向之一）。
        /// 斜着摆的门不支持。</remarks>
        private Bounds GateBounds()
        {
            if (_box == null) _box = GetComponent<BoxCollider2D>();
            if (_box == null) return new Bounds(transform.position, Vector3.zero);

            Vector3 scale = transform.lossyScale;
            var size = new Vector2(Mathf.Abs(_box.size.x * scale.x), Mathf.Abs(_box.size.y * scale.y));
            var center = (Vector2)transform.TransformPoint(_box.offset);
            return new Bounds(center, size);
        }

        /// <summary>包围盒在 <paramref name="axis"/> 方向上的半长（轴正交时就是对应的那个 extents）。</summary>
        private static float HalfExtent(Bounds bounds, Vector2 axis)
        {
            return Mathf.Abs(axis.x) * bounds.extents.x + Mathf.Abs(axis.y) * bounds.extents.y;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：改可破坏方向并立刻刷新白盒标签。</summary>
        public void SetPass(OneWayGate.PassDirection pass)
        {
            _pass = pass;
            RefreshLabel();
        }

        /// <summary>只给编辑器工具用：接上白盒的两处引用（方块 / 标签）。</summary>
        public void SetWhitebox(SpriteRenderer art, TextMesh label)
        {
            _art = art;
            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(OneWayGate.PassDirection pass)
        {
            switch (pass)
            {
                case OneWayGate.PassDirection.ToLeft: return "破门 ←";
                case OneWayGate.PassDirection.ToUp:   return "破门 ↑";
                case OneWayGate.PassDirection.ToDown: return "破门 ↓";
                default:                              return "破门 →";
            }
        }

        private void OnValidate()
        {
            if (_reach < 0.1f) _reach = 0.1f;
            RefreshLabel();
        }

        /// <summary>把方向写进白盒标签。**只在文字真的变了才写** —— 否则每次脚本重编译
        /// 都会把场景标脏，提交里全是噪声（与 <c>OneWayGate</c> 同一处理）。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_pass);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            Bounds gate = GateBounds();
            Vector2 axis = Axis;

            // 实心的那一块：没砍碎之前挡路的就是它。
            Gizmos.color = new Color(1f, 0.62f, 0.22f, 0.9f);
            Gizmos.DrawWireCube(gate.center, gate.size);

            // 判定区：玩家中心进到这里、面朝门挥刀，门就碎。
            Gizmos.color = new Color(1f, 0.45f, 0.35f, 0.5f);
            Gizmos.DrawWireCube(gate.center, gate.size + new Vector3(_reach * 2f, _reach * 2f, 0f));

            // 可破坏方向：从「进侧」指向门里的箭头 —— 站在它的起点这一侧，面朝门砍。
            Gizmos.color = new Color(1f, 0.9f, 0.25f, 1f);
            float arm = HalfExtent(gate, axis) + 0.6f;
            var from = (Vector2)gate.center - axis * arm;
            var to   = (Vector2)gate.center + axis * arm;
            Gizmos.DrawLine(from, to);

            var perp = new Vector2(-axis.y, axis.x);
            const float head = 0.3f;
            Gizmos.DrawLine(to, to - axis * head + perp * head * 0.6f);
            Gizmos.DrawLine(to, to - axis * head - perp * head * 0.6f);
        }
    }
}
