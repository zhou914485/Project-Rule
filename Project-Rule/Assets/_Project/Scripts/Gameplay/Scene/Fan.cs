using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 风扇：朝一个方向吹出**一条风道** —— 风场的实体载体，与 <see cref="WindZone"/> 姊妹。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="WindZone"/> 的分工</b>：WindZone 是「这里有一块区域」——
    /// 区域自己就是一切，力沿**世界 y** 从下往上衰减；风扇是「从这里朝那边吹」——
    /// 风道从扇体出发沿**风向**延伸，力沿**离出风口的距离**衰减。
    /// 贴地朝上 = 上升气流，贴墙朝外 = 横向风道。二者的受力规则完全一样
    /// （报力、由实体自己除以质量），只是摆的姿势不同。</para>
    ///
    /// <para><b>范围与风力都在这一个组件上</b>：<see cref="_length"/> × <see cref="_width"/>
    /// 是风道尺寸，<see cref="_force"/> 是出风口风力、<see cref="_forceGradient"/> 是每米的衰减量（N/m）。
    /// 两者都画在 Gizmo 里 —— 选中风扇就能看见风吹到哪、每一段多强；
    /// 朝上吹时还会画出 ×削弱 / 正常 / ×增强 三条**平衡高度线**（<see cref="_showEquilibriumLines"/> 控制开关）。
    /// 平衡点离出风口的距离 <c>y(g) = (F₀ − m·g) ÷ 梯度</c>，相邻重力档的间距 <c>Δy = m·Δg ÷ 梯度</c>。</para>
    ///
    /// <para><b>为什么不组合一个 WindZone 子物体</b>：那样范围与风力要么存两份
    /// （风扇一份、风场一份，谁说了算不好办），要么用户得对着两个对象调。
    /// 这里把「风道从哪吹到哪」的几何直接长在自己身上：一个对象、一份参数，
    /// 而且 <see cref="IWindField"/> 本来就是为「不认识的场景组件照样吹得动」留的口子。</para>
    ///
    /// <para><b>碰撞体是「查询入口」，必须覆盖整条风道</b>：WindSensor 是拿玩家碰撞体中心
    /// <c>OverlapPoint</c> 查 Wind 层，命中后走接口调到 <see cref="ForceAt"/>。
    /// 所以本组件在 <c>OnValidate</c> / <c>OnEnable</c> 里把风道尺寸写进自己的 BoxCollider2D
    /// —— 那不是「碰撞」，只是让查询命中得到；真正的受力判定在矩形取样里。
    /// 碰撞体是 trigger：不挡人（与风场同一约定）。</para>
    ///
    /// <para>⚠️ <b>必须落 Wind 层</b>：玩家的 <c>WindSensor</c> 只查这个层（<c>_windLayer</c>），
    /// 放错层风扇白放、且没有任何报错。生成工具会自动设好。</para>
    ///
    /// <para><b>只看风、不看风扇</b>：关掉「白盒 → 显示本体」（<see cref="_showBody"/>）只隐藏两个渲染器
    /// （白盒方块 + 标签），**判定与碰撞体一个都不动** —— 风照常吹。
    /// 想运行时切换用 <see cref="SetBodyVisible"/>。
    /// ⚠️ 风道本身只有**编辑器 Gizmo**，没有运行期可视化 —— 关掉本体后，play 里是完全看不见的
    /// （要做「看得见的风」得另接粒子 / 半透明面片）。</para>
    ///
    /// <para>⚠️ <b>不要旋转风扇</b>：风向由 <see cref="_direction"/> 决定，风道与碰撞体都按它算；
    /// 旋转对象只会让碰撞体与风道错位。</para>
    ///
    /// <para>⚠️ <b>风道下方要有地形</b>：风场是判定不是墙 —— 被托出风道边缘的人会直接掉下去
    /// （上升气流的经典事故，见 <c>Doc/玩家动作设计.md</c> §9.7）。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/② 风扇（吹出风场）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class Fan : MonoBehaviour, IWindField
    {
        /// <summary>朝哪吹。风道从扇体出发沿这个方向延伸。</summary>
        public enum BlowDirection
        {
            /// <summary>朝右吹。</summary>
            Right = 0,

            /// <summary>朝上吹（默认，上升气流）。</summary>
            Up = 1,

            /// <summary>朝左吹。</summary>
            Left = 2,

            /// <summary>朝下吹。</summary>
            Down = 3,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("风向与范围")]
        [Tooltip("朝哪吹。风道从扇体出发沿这个方向延伸「风道长度」。\n" +
                 "⚠️ 改这个字段而不是旋转对象 —— 风道与碰撞体都按它算，旋转会让两者错位。")]
        [SerializeField] private BlowDirection _direction = BlowDirection.Up;

        [Tooltip("风道长度：从扇体起沿风向吹多远（世界单位）。0 = 不吹。")]
        [SerializeField, Min(0f)] private float _length = 10f;

        [Tooltip("风道宽度：垂直于风向的那一维（世界单位），以扇体为中轴左右各一半。")]
        [SerializeField, Min(0.05f)] private float _width = 4f;

        [Header("风力（牛顿）")]
        [Tooltip("出风口（近端）的风力大小，沿风向吹。150 是风场的实测基准 —— " +
                 "质量 1 的人会被托起来，质量规则一改浮空高度当场变化。")]
        [SerializeField, Min(0f)] private float _force = 150f;

        [Tooltip("力的梯度：每远离出风口 1 米，风力减少多少牛顿（N/m）。0 = 恒定力。\n" +
                 "它决定「不同重力档在风道里的落点间距」：间距 Δy = m·Δg ÷ 梯度（与梯度成反比）——\n" +
                 "想让人在不同重力下分得更开，就调小它；想挤在一起，就调大它。\n" +
                 "⚠️ 平衡点要在风道内：出风口风力必须大于 m·g（正常重力）。")]
        [SerializeField] private float _forceGradient = 15f;

        [Header("预览")]
        [Tooltip("在 Scene 视图画出「各重力档的平衡高度线」（×削弱 / 正常 / ×增强），\n" +
                 "按场景里的玩家的质量与重力倍率计算。纯编辑器辅助，不参与运行时判定。")]
        [SerializeField] private bool _showEquilibriumLines = true;

        [Header("阻尼")]
        [Tooltip("风道内的空气阻力（每秒速度衰减率），水平与竖直**都收**。0 = 带着动能冲过平衡点、被弹出风道" +
                 "（实测出场速度 18+）；2 左右就会收敛到平衡位置。它就是风的「耗散」。")]
        [SerializeField, Min(0f)] private float _drag = 2f;

        [Tooltip("竖直阻尼（每秒速度衰减率）。**只收竖直方向**（沿当前重力轴）的速度，横向不动；与空气阻力叠加。\n" +
                 "默认 8 ≈ 临界阻尼（150N / 10m / 质量 1 时临界值约 7.7）：被吹起来后一路上升、不上下弹。\n" +
                 "想更「黏」就调大，想更有弹性就调小。")]
        [SerializeField, Min(0f)] private float _verticalDamping = 8f;

        [Header("白盒")]
        [Tooltip("**显示本体**（白盒方块 + 文字标签）。关掉后场景里只剩风道 —— 适合正式美术接进来、\n" +
                 "或想让风吹向看不出从哪来时用。判定 / 碰撞体完全不受影响，风照常吹。")]
        [SerializeField] private bool _showBody = true;

        [Tooltip("本体白盒方块。留空时自动找子物体上的 SpriteRenderer。**接正式美术时按需清空。**")]
        [SerializeField] private SpriteRenderer _bodyVisual;

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private BoxCollider2D _area;

        /// <summary>标签的渲染器缓存（TextMesh 自带一个 MeshRenderer）。</summary>
        private MeshRenderer _labelRenderer;

        /// <summary>出风口（风扇自己）的世界坐标。风道从这里朝外延伸。</summary>
        public Vector2 Origin => transform.position;

        /// <summary>风向（单位向量）。</summary>
        public Vector2 DirectionVector
        {
            get
            {
                switch (_direction)
                {
                    case BlowDirection.Right: return Vector2.right;
                    case BlowDirection.Up:    return Vector2.up;
                    case BlowDirection.Left:  return Vector2.left;
                    default:                  return Vector2.down;
                }
            }
        }

        /// <summary>风道长度（沿风向）。</summary>
        public float Length => _length;

        /// <summary>风道宽度（垂直于风向）。</summary>
        public float Width => _width;

        /// <summary>出风口的风力（牛顿）。</summary>
        public float Force => _force;

        /// <summary>力的梯度（N/m）：每远离出风口 1 米，风力减少多少牛顿。</summary>
        public float ForceGradient => _forceGradient;

        /// <summary>风道内的空气阻力（每秒速度衰减率）。</summary>
        public float Drag => _drag;

        /// <summary>竖直阻尼（每秒速度衰减率）。只作用于竖直方向。</summary>
        public float VerticalDamping => _verticalDamping;

        /// <summary>当前风向。</summary>
        public BlowDirection Direction => _direction;

        /// <summary>本体（白盒方块 + 标签）当前是否显示。</summary>
        public bool BodyVisible => _showBody;

        // ==================================================================
        //  IWindField
        // ==================================================================

        /// <inheritdoc />
        /// <remarks>点不在风道内、或本组件被关掉时返回 <see cref="Vector2.zero"/>。
        /// 关掉组件即可让风扇停用 —— 调用方不需要额外判断（与 <see cref="WindZone"/> 同约定）。</remarks>
        public Vector2 ForceAt(Vector2 point)
        {
            if (!TrySample(point, out float t)) return Vector2.zero;

            return DirectionVector * MagnitudeAtDistance(t * _length);
        }

        /// <summary>离出风口 <paramref name="distance"/> 米处的风力大小（牛顿）：按梯度递减，最低到 0。</summary>
        private float MagnitudeAtDistance(float distance)
            => Mathf.Max(_force - _forceGradient * distance, 0f);

        /// <inheritdoc />
        public float DragAt(Vector2 point) => TrySample(point, out float _) ? _drag : 0f;

        /// <inheritdoc />
        public float VerticalDampingAt(Vector2 point) => TrySample(point, out float _) ? _verticalDamping : 0f;

        /// <summary>
        /// 点是否落在风道内；在的话给出「离出风口多远」的归一化位置（0 = 出风口，1 = 远端）。
        /// </summary>
        /// <remarks>把点投影到风向轴上：沿风向的分量决定远近、垂直分量决定宽度。
        /// 与碰撞体无关 —— 碰撞体只是查询入口，这里才是真正的形状。</remarks>
        private bool TrySample(Vector2 point, out float along01)
        {
            along01 = 0f;

            if (!isActiveAndEnabled) return false;
            if (_length <= 0f) return false;

            Vector2 dir  = DirectionVector;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            Vector2 offset = point - Origin;
            float along  = Vector2.Dot(offset, dir);
            float across = Vector2.Dot(offset, perp);

            if (along < 0f || along > _length) return false;
            if (Mathf.Abs(across) > _width * 0.5f) return false;

            along01 = along / _length;
            return true;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void OnEnable()
        {
            SyncArea();
            ApplyBodyVisibility();
        }

        private void OnValidate()
        {
            if (_length < 0f) _length = 0f;
            if (_width < 0.05f) _width = 0.05f;

            SyncArea();
            RefreshLabel();
            ApplyBodyVisibility();
        }

        /// <summary>给脚本 / 机关用：运行时切本体的显示状态（风照常吹）。</summary>
        public void SetBodyVisible(bool visible)
        {
            _showBody = visible;
            ApplyBodyVisibility();
        }

        /// <summary>把 <see cref="_showBody"/> 落到两个渲染器上。</summary>
        /// <remarks>
        /// <para>只关渲染器，**不动碰撞体** —— 碰撞体是 <c>WindSensor</c> 的查询入口，
        /// 关掉风扇就失效了；也不禁用子物体，避免以后往本体上挂表现组件时被连带断掉。</para>
        /// <para>引用留空时按子物体自动补齐：老场景里的风扇没有序列化这两个引用，
        /// 补齐之后开关立刻可用。</para>
        /// </remarks>
        private void ApplyBodyVisibility()
        {
            ResolveVisuals();

            if (_bodyVisual != null) _bodyVisual.enabled = _showBody;
            if (_labelRenderer != null) _labelRenderer.enabled = _showBody;
        }

        /// <summary>补齐白盒引用。编辑器里改了层级也会重新找。</summary>
        private void ResolveVisuals()
        {
            if (_bodyVisual == null) _bodyVisual = GetComponentInChildren<SpriteRenderer>(true);

            if (_label != null && (_labelRenderer == null || _labelRenderer.gameObject != _label.gameObject))
            {
                _labelRenderer = _label.GetComponent<MeshRenderer>();
            }
        }

        /// <summary>把风道尺寸写进碰撞体 —— 让 WindSensor 的 OverlapPoint 查得到。</summary>
        /// <remarks>风道从扇体出发**朝外**延伸：碰撞体中心在风道中点上（半径 = 长度的一半），
        /// 沿风向的那一维是长度、垂直那一维是宽度。</remarks>
        private void SyncArea()
        {
            if (_area == null) _area = GetComponent<BoxCollider2D>();
            if (_area == null) return;

            Vector2 dir = DirectionVector;

            _area.size = Mathf.Abs(dir.x) > 0.5f
                       ? new Vector2(_length, _width)
                       : new Vector2(_width, _length);
            _area.offset = dir * (_length * 0.5f);
            _area.isTrigger = true;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒方块与标签的引用。</summary>
        public void SetWhitebox(SpriteRenderer body, TextMesh label)
        {
            _bodyVisual = body;
            _label      = label;
            RefreshLabel();
            ApplyBodyVisibility();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float force, BlowDirection direction)
        {
            string way;
            switch (direction)
            {
                case BlowDirection.Right: way = "右"; break;
                case BlowDirection.Up:    way = "上"; break;
                case BlowDirection.Left:  way = "左"; break;
                default:                  way = "下"; break;
            }

            return "风扇 " + way + " " + force.ToString("0.#") + "N";
        }

        /// <summary>把当前风力 / 风向写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声（与弹簧平台同一处理）。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_force, _direction);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            Vector2 dir  = DirectionVector;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 origin = Origin;
            Vector2 halfW  = perp * (_width * 0.5f);
            Vector2 far    = origin + dir * _length;

            // 风道轮廓（真实形状，与受力判定一致）。
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.85f);
            Vector2 cornerA = origin - halfW;
            Vector2 cornerB = origin + halfW;
            Vector2 cornerC = far + halfW;
            Vector2 cornerD = far - halfW;
            Gizmos.DrawLine(cornerA, cornerB);
            Gizmos.DrawLine(cornerB, cornerC);
            Gizmos.DrawLine(cornerC, cornerD);
            Gizmos.DrawLine(cornerD, cornerA);

            // 三排箭头：近 / 中 / 远，长度按该处的力 —— 一眼看出「越远越弱」。
            DrawArrowRow(origin + dir * (_length * 0.12f), perp, _width, dir,
                         MagnitudeAtDistance(_length * 0.12f));
            DrawArrowRow(origin + dir * (_length * 0.5f), perp, _width, dir,
                         MagnitudeAtDistance(_length * 0.5f));
            DrawArrowRow(origin + dir * (_length * 0.88f), perp, _width, dir,
                         MagnitudeAtDistance(_length * 0.88f));

            // 扇体。
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            Gizmos.DrawWireCube(origin, new Vector3(0.7f, 0.7f, 0f));

            // 各重力档的平衡高度线（预览开关）。
            if (_showEquilibriumLines) DrawEquilibriumLines(origin, dir, perp);
        }

        /// <summary>画出「各重力档的平衡高度线」—— 风力 = 该档重力时离出风口的距离。</summary>
        /// <remarks>
        /// <para>只对**竖直向上**的风道有意义（横向风不会在竖直方向形成平衡点），
        /// 且梯度必须非 0（恒定力没有平衡点）。</para>
        /// <para>纯编辑器预览：用场景里玩家实体的质量与重力倍率算；场景里没有玩家就按 m = 1、倍率 = 1。</para>
        /// </remarks>
        private void DrawEquilibriumLines(Vector2 origin, Vector2 dir, Vector2 perp)
        {
            if (_direction != BlowDirection.Up) return;
            if (Mathf.Abs(_forceGradient) < 0.0001f) return;

#if UNITY_EDITOR
            float mass = 1f;
            float gravityScale = 1f;

            var player = UnityEngine.Object.FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
            if (player != null)
            {
                var body = player.GetComponent<Rigidbody2D>();
                if (body != null) mass = body.mass;
                gravityScale = player.GravityScale;
            }

            DrawTierLine(origin, dir, perp, "×削弱", 0.3f, new Color(0.55f, 0.75f, 1f, 0.95f), mass, gravityScale);
            DrawTierLine(origin, dir, perp, "正常",   1f,   new Color(0.55f, 1f, 0.6f, 0.95f), mass, gravityScale);
            DrawTierLine(origin, dir, perp, "×增强", 2f,   new Color(1f, 0.62f, 0.35f, 0.95f), mass, gravityScale);
#endif
        }

#if UNITY_EDITOR
        /// <summary>某一个重力档的平衡高度线。档位倍率与 <c>GravityRule</c> 的取值保持一致。</summary>
        private void DrawTierLine(Vector2 origin, Vector2 dir, Vector2 perp, string label,
                                  float gravityTier, Color color, float mass, float gravityScale)
        {
            float force = mass * 9.81f * gravityTier * gravityScale;   // 平衡所需的力（牛顿）
            float distance = (_force - force) / _forceGradient;
            if (distance < 0f || distance > _length) return;

            Vector2 center = origin + dir * distance;
            Vector2 halfW = perp * (_width * 0.5f);

            Gizmos.color = color;
            Gizmos.DrawLine(center - halfW, center + halfW);
            UnityEditor.Handles.Label(center - halfW + Vector2.up * 0.12f,
                                      label + "  y=" + center.y.ToString("0.00"));
        }
#endif

        /// <summary>沿风道横截面画一排箭头示意风力（100 牛顿 ≈ 1 个单位长度）。</summary>
        private static void DrawArrowRow(Vector2 center, Vector2 perp, float width, Vector2 dir, float force)
        {
            const float NewtonsPerUnit = 100f;

            float length = Mathf.Clamp(force / NewtonsPerUnit, 0f, 2f);
            if (length <= 0.01f) return;

            Gizmos.color = new Color(0.9f, 0.95f, 0.4f, 0.9f);

            Vector2 head = -dir * (length * 0.28f);
            Vector2 wing = perp * (length * 0.12f);

            const int arrows = 4;
            for (int i = 0; i < arrows; i++)
            {
                float u = arrows == 1 ? 0.5f : i / (float)(arrows - 1);

                // 箭头在宽度方向上一字排开，两端各留 10% 的边。
                Vector2 basePoint = center + perp * (width * 0.8f * (u - 0.5f));
                Vector2 tip = basePoint + dir * length;

                Gizmos.DrawLine(basePoint, tip);
                Gizmos.DrawLine(tip, tip + head + wing);
                Gizmos.DrawLine(tip, tip + head - wing);
            }
        }
    }
}
