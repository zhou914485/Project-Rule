using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 破旧金属水管：**上下两根实心金属壁**夹出一段**空腔**（中间的 sprite 不挡人），
    /// 玩家从侧面走进空腔，管子就绕 sprite 中心**顺时针**翻转一次。
    /// </summary>
    /// <remarks>
    /// <para><b>结构（这就是它与单向门最大的不同）</b>：挡路的是上下两根**实心**碰撞体，
    /// 中间的 sprite 区域**没有碰撞** —— 玩家从管口走进去，站进管腔里才会触发。
    /// 所以 Inspector 里那两个字段拖的是**实心碰撞体**，不是 Trigger
    /// （上一版把它们当「范围判定箱」强制设成 Trigger，方向做反了）。</para>
    ///
    /// <para><b>判定区 = 两壁之间的空腔</b>：上壁的下沿到下壁的上沿、两壁 x 范围的并集。
    /// 玩家的身体包围盒与它相交就触发 —— 不用另挂判定箱，判定区天生由两根壁的位置推导
    /// （与 <c>OneWayGate</c> 「判定区算出来」同一套思路）。</para>
    ///
    /// <para><b>长度可调</b>：<see cref="_length"/> 在 Inspector 里实时生效 —— 两根壁和整根视觉
    /// 一起沿管子方向伸缩，管腔高度 / 壁厚不变（壁与视觉的 sprite 约定为 1×1 单位，
    /// 长度直接写进 <c>localScale.x</c>）。</para>
    ///
    /// <para><b>为什么不用触发器回调</b>：与 <c>OneWayGate</c> / <c>RevealRegion</c> 同一约定 ——
    /// 玩家是 Kinematic 刚体 + <c>Rigidbody2D.Slide</c>，默认不与静态碰撞体产生接触 / 触发回调。
    /// 这里每物理帧做一次**包围盒判定**，与刚体类型无关。</para>
    ///
    /// <para><b>为什么绕 sprite 中心转，而不是绕 transform 原点</b>：美术给的是整幅画布，
    /// sprite 的轴心不一定落在画面中心，绕轴心转会整个甩出去。触发那一瞬间先把 sprite 的中心
    /// 锁成支点，之后每一帧的位姿都按它解算。⚠️ 支点只能在**还没转**的时候锁一次 ——
    /// 转起来之后 bounds 是转过之后的包围盒，值会跑。</para>
    ///
    /// <para><b>只认玩家</b>：敌人走进管腔不会触发（和单向门一样，这是给玩家的机关）。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时物理已停步进，翻转也必须停 —— 读
    /// <c>TimeManager.IsPaused</c> 早退（单一真相是 TimeManager）；翻转计时用
    /// <c>Time.fixedDeltaTime</c>，随 <c>Time.timeScale</c> 缩放 —— 世界变慢，管子也跟着变慢。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Tube : MonoBehaviour
    {
        /// <summary>补找玩家的节流：找不到时约半秒试一次，别每帧全场搜一遍。</summary>
        private const float ResolveRetrySeconds = 0.5f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("结构（上下两根实心金属壁）")]
        [Tooltip("上壁：**实心**碰撞体（层 = Ground），玩家站得住、也挡得住人。\n" +
                 "不要勾 Is Trigger —— 它是一堵墙，不是判定范围。")]
        [SerializeField] private Collider2D _wallTop;

        [Tooltip("下壁：**实心**碰撞体。与上壁夹出的空腔就是玩家能走进去的地方。")]
        [SerializeField] private Collider2D _wallBottom;

        [Header("尺寸")]
        [Tooltip("管子长度（世界单位）。两根壁和整根视觉一起伸缩 —— 只沿管子方向变长，\n" +
                 "管腔高度 / 壁厚不变。最短 0.5。")]
        [SerializeField, Min(0.5f)] private float _length = 2.5f;

        [Tooltip("整根管子的视觉（半透明底色），跟着长度一起伸缩。留空则只伸缩两根壁。")]
        [SerializeField] private Transform _art;

        [Header("翻转")]
        [Tooltip("转过的角度（度）。正数 = 屏幕上顺时针；负数 = 逆时针。")]
        [SerializeField] private float _angleDegrees = 90f;

        [Tooltip("转完用几秒。0 = 瞬间到位。")]
        [SerializeField, Min(0f)] private float _turnSeconds = 0.35f;

        [Header("引用")]
        [Tooltip("谁算数。留空则自动找场景里的 PlayerController（玩家若是关卡流程里才实例化的，会自己补找）。")]
        [SerializeField] private Transform _player;

        [Header("白盒")]
        [Tooltip("白盒标签：文字随翻转角度自动刷新。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private Collider2D _playerCollider;

        /// <summary>一次性：转过一次就不再响应。</summary>
        private bool _used;

        /// <summary>正在转（<see cref="_turnSeconds"/> &gt; 0 时才会亮）。</summary>
        private bool _turning;

        /// <summary>已转过的进度，0 ~ 1。</summary>
        private float _turned;

        /// <summary>支点：触发那一瞬间锁定的 sprite 中心（世界坐标）。</summary>
        private Vector2 _pivot;

        /// <summary>触发瞬间的位姿 —— 之后每一帧的位姿都是从它绕 <see cref="_pivot"/> 解出来的。</summary>
        private Vector3 _startPosition;
        private Quaternion _startRotation;
        private Vector3 _startOffset;

        /// <summary>初始位姿（<c>Awake</c> 抓一次）：重开一局 / 对象池取出时用它复位。</summary>
        private Vector3 _homePosition;
        private Quaternion _homeRotation;

        /// <summary>管腔（两壁之间，世界坐标）：玩家身体进来就触发。<c>Awake</c> 按初始位姿推一次。</summary>
        private Bounds _cavity;
        private bool _ready;

        private float _nextResolve;
        private bool _warnedNoPlayer;

        /// <summary>现在是不是已经转过了（一次性机关，表现层要播动画就读它）。</summary>
        public bool IsUsed => _used;

        /// <summary>管腔（世界坐标）。玩家身体进入这里就算「走进管子里面」。</summary>
        public Bounds Cavity => _cavity;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            ConfigureBody();

            ApplyLength();
            Physics2D.SyncTransforms();   // bounds 是缓存的：改完缩放同步一次，管腔才推得对
            CaptureHome();

            _nextResolve = 0f;
            TryResolvePlayer();
        }

        /// <summary>重开一局 / 对象池取出时回到「没转过」的那一面，不能接着上一局的状态。</summary>
        private void OnEnable()
        {
            if (_ready) Restore();
        }

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，翻转也必须停（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true) return;
            if (_used) return;

            if (_playerCollider == null) TryResolvePlayer();

            if (!_turning)
            {
                if (!TryGetPlayerBounds(out Bounds body) || !_cavity.Intersects(body)) return;

                Begin();
                if (_used) return;      // 时长填 0：Begin 里已经瞬间转完
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，写 Time 会跟它撞名，
            // 所以这里全限定（与 MovingPlatform 同一处理）。
            float dt = UnityEngine.Time.fixedDeltaTime;
            _turned = Mathf.Min(_turned + dt / _turnSeconds, 1f);

            if (_turned >= 1f)
            {
                _turning = false;
                _used = true;
                RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, true);
            }

            Apply(_turned);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        private void Start()
        {
            // 读档恢复：这根管子已经翻过了 —— 直接摆到转完的位姿，不再播一遍。
            if (!RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) return;
            _used = true;
            _turned = 1f;
            Apply(1f);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "used");

        /// <summary>开始翻转：锁定支点与起始位姿。时长填 0 时直接瞬间到位。</summary>
        private void Begin()
        {
            _pivot = Pivot();
            _startPosition = transform.position;
            _startRotation = transform.rotation;
            _startOffset = _startPosition - (Vector3)_pivot;

            _turned = 0f;

            if (_turnSeconds <= 0f)
            {
                Apply(1f);
                _used = true;
                RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, true);
                return;
            }

            _turning = true;
        }

        /// <summary>把「转过多少」写成位姿：整体绕 <see cref="_pivot"/> 转过 <c>−角度 × 进度</c>。</summary>
        /// <remarks>角度取负 = 屏幕上顺时针（Unity 里 Z 正向是逆时针）。</remarks>
        private void Apply(float progress)
        {
            Quaternion delta = Quaternion.Euler(0f, 0f, -_angleDegrees * progress);

            _body.MovePosition((Vector2)((Vector3)_pivot + delta * _startOffset));
            _body.MoveRotation(_startRotation * delta);
        }

        /// <summary>支点 = sprite 的中心（管子整根的视觉中心）；没有 sprite 就退到两壁并集的中心。</summary>
        private Vector2 Pivot()
        {
            var sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite != null) return sprite.bounds.center;

            if (_ready) return _cavity.center;
            return transform.position;
        }

        /// <summary>把 <see cref="_length"/> 应用到两根壁与整根视觉上 —— 只沿管子方向伸缩。</summary>
        /// <remarks>约定：壁 / 视觉的 sprite 是 1×1 单位，<c>localScale.x</c> 就是长度
        /// （白盒生成器就是这么建的）。管腔高度、壁厚、壁的位置都不动。</remarks>
        private void ApplyLength()
        {
            float length = Mathf.Max(0.5f, _length);

            if (_wallTop != null) SetLocalLength(_wallTop.transform, length);
            if (_wallBottom != null) SetLocalLength(_wallBottom.transform, length);
            if (_art != null) SetLocalLength(_art, length);
        }

        private static void SetLocalLength(Transform target, float length)
        {
            Vector3 scale = target.localScale;
            if (Mathf.Approximately(scale.x, length)) return;

            scale.x = length;
            target.localScale = scale;
        }

        /// <summary>复位到没转过的样子（位姿 + 一次性标记）。</summary>
        private void Restore()
        {
            _used = false;
            _turning = false;
            _turned = 0f;

            if (_body == null) return;
            _body.position = _homePosition;
            // ⚠️ Rigidbody2D.rotation 是 float（Z 轴角度），不是 Quaternion。
            _body.rotation = _homeRotation.eulerAngles.z;
        }

        /// <summary>抓初始位姿，并按上下两根壁推出管腔。</summary>
        private void CaptureHome()
        {
            _homePosition = transform.position;
            _homeRotation = transform.rotation;

            if (!TryGetCavity(out Bounds cavity, out string problem))
            {
                _ready = false;
                Debug.LogError("[Tube] " + problem + "用菜单 Tools/规则游戏/地图组件/⑤ 生成，" +
                               "或手动把上下两根**实心**碰撞体拖进 Inspector。", this);
                return;
            }

            _cavity = cavity;
            _ready = true;
        }

        /// <summary>管腔 = 两壁之间的空隙（世界坐标）。</summary>
        /// <remarks>两壁必须是**实心**碰撞体：它们是挡人的墙，不是判定范围。
        /// 判定区天生由它们推导，所以这里要把三种摆错的情况认出来并点名。</remarks>
        private bool TryGetCavity(out Bounds cavity, out string problem)
        {
            cavity = default;
            problem = null;

            if (_wallTop == null || _wallBottom == null)
            {
                problem = "上下壁没有指定 —— 水管既挡不住人，也没有可以走进去的管腔。";
                return false;
            }

            if (_wallTop.isTrigger || _wallBottom.isTrigger)
            {
                problem = "上下壁是 Trigger —— 它俩必须是**实心**碰撞体（墙），否则玩家会直接穿过去。";
                return false;
            }

            Bounds top = _wallTop.bounds;
            Bounds bottom = _wallBottom.bounds;

            if (top.min.y <= bottom.max.y)
            {
                problem = "上壁没有在下壁上方（或者两壁叠在一起）—— 夹不出管腔。";
                return false;
            }

            var min = new Vector3(Mathf.Min(top.min.x, bottom.min.x), bottom.max.y, 0f);
            var max = new Vector3(Mathf.Max(top.max.x, bottom.max.x), top.min.y, 0f);
            cavity = new Bounds((min + max) * 0.5f, max - min);
            return true;
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
        /// 关卡流程可能比水管晚实例化玩家。</summary>
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
                var controller = FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
                if (controller != null) _player = controller.transform;
            }

            if (_player != null) _playerCollider = _player.GetComponent<Collider2D>();

            if (_playerCollider == null && !_warnedNoPlayer)
            {
                _warnedNoPlayer = true;
                Debug.LogWarning("[Tube] 没找到玩家（场景里没有 PlayerController），这根水管不会被触发。" +
                                 "玩家若是运行时才实例化的，本组件会自己补找。", this);
            }
        }

        private void ConfigureBody()
        {
            // Kinematic：位姿完全由代码决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;

            // ⚠️ 不能 FreezeRotation —— MoveRotation 会被它吃掉，管子就转不动了。

            // 定步长物理 + 高帧率渲染：不开插值翻转会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上上下两根实心壁。</summary>
        public void SetWalls(Collider2D top, Collider2D bottom)
        {
            _wallTop = top;
            _wallBottom = bottom;
        }

        /// <summary>只给编辑器工具用：接上整根管子的视觉（跟着长度伸缩）。</summary>
        public void SetArt(Transform art)
        {
            _art = art;
            ApplyLength();
        }

        /// <summary>只给编辑器工具用：接上白盒标签的引用。</summary>
        public void SetWhitebox(TextMesh label)
        {
            _label = label;
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float angleDegrees)
        {
            string arrow = angleDegrees >= 0f ? "↻" : "↺";
            return "水管 " + arrow + Mathf.Abs(angleDegrees).ToString("0.#") + "°";
        }

        private void OnValidate()
        {
            if (_length < 0.5f) _length = 0.5f;

            ApplyLength();
            Physics2D.SyncTransforms();
            RefreshLabel();
        }

        /// <summary>把翻转角度写进白盒标签。**只在文字真的变了才写** —— 否则每次脚本重编译
        /// 都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_angleDegrees);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            // 两根壁：实心、挡人。
            Gizmos.color = new Color(0.72f, 0.76f, 0.82f, 0.9f);
            if (_wallTop != null) Gizmos.DrawWireCube(_wallTop.bounds.center, _wallTop.bounds.size);
            if (_wallBottom != null) Gizmos.DrawWireCube(_wallBottom.bounds.center, _wallBottom.bounds.size);

            // 管腔：玩家走进来就触发。
            if (TryGetCavity(out Bounds cavity, out _))
            {
                Gizmos.color = new Color(0.42f, 1f, 0.55f, 0.6f);
                Gizmos.DrawWireCube(cavity.center, cavity.size);
            }

            // 支点（sprite 中心）—— 翻转围绕的就是这个点。
            var sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite == null) return;

            Gizmos.color = new Color(1f, 0.9f, 0.25f, 1f);
            Gizmos.DrawWireSphere(sprite.bounds.center, 0.12f);
        }
    }
}
