using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 双因子滑轨：滑块沿一条**竖直导轨**上下走，一共**五档**，
    /// 档位 = **重力**与**质量**两个世界法则各自把滑块往上 / 往下推一格后叠加的结果。
    /// </summary>
    /// <remarks>
    /// <para><b>它是会动的支撑面这一族的成员</b>：都实现 <see cref="IMovingSurface"/>
    /// （站在上面的实体被带走），区别只在「目标位置由什么决定」——
    /// <c>MovingPlatform</c> 由**时间**（匀速往返），<c>SpringPlatform</c> 由**负载**（弹簧积分），
    /// <c>RailLift</c> 由**质量**（三档），本组件由**重力 + 质量叠加**（五档）。
    /// 要按时间往返的平台请用 <c>MovingPlatform</c>，本组件不会自己走。</para>
    ///
    /// <para><b>五档怎么算</b> —— 每个因子只投一票：<b>+1 = 往上</b>（变轻）、<b>−1 = 往下</b>（变重）、
    /// <b>0 = 不动</b>。档位 = <c>中档 + 重力票 + 质量票</c>：</para>
    /// <list type="bullet">
    /// <item><b>重力</b>：增强（×2）→ −1；削弱（×0.3）/ 失效（0）→ +1；反转（g 朝上）→ +1；恒定 / 无规则 → 0。</item>
    /// <item><b>质量</b>：增强（×3）→ −1；削弱（×0.3）/ 失效（×0.05）→ +1；
    /// 反转 → **逐台**按 <see cref="_baseMass"/> 跟参考质量 1 比：比 1 重 → +1、比 1 轻 → −1；恒定 / 无规则 → 0。</item>
    /// </list>
    /// <para>于是：</para>
    /// <list type="bullet">
    /// <item>只减重力 → **上一档**；只减质量 → **上一档**；</item>
    /// <item>**同时减**（比如「重力 × 削弱」+「质量 × 削弱」）→ **上两档**；</item>
    /// <item>两个同时加 → **下两档**；</item>
    /// <item>一加一减 → **互相抵消**，回到中档 —— 不需要任何特例代码，是加法自己算出来的。</item>
    /// </list>
    ///
    /// <para><b>两个因子的读取方式不一样，但都是「读世界，不订阅」</b>：</para>
    /// <list type="bullet">
    /// <item>重力有引擎全局载体（<c>GravityRule</c> 直接写 <c>Physics2D.gravity</c>），
    /// 所以直接读 <c>Physics2D.gravity</c> —— 规则系统不存在时也读得到；</item>
    /// <item>质量只能从 <c>IAttributeManager</c> 读（与 <c>RuleAffectedBody</c> / <c>RailLift</c> 同一口径），
    /// 规则系统没起来时按默认值（= 不动）。</item>
    /// </list>
    /// <para>两边都是每物理帧读一次（一个向量 + 一个结构体拷贝，代价可忽略），
    /// **不订阅 <c>OnRulesChanged</c>** —— 规则在滑轨**生成之前**就已生效时，
    /// 第一次读就拿到正确档位，不需要「先补算一次」这种补丁。</para>
    ///
    /// <para><b>与 <c>RailLift</c> 的分工</b>：<c>RailLift</c> 是**只看质量**的三档版（更简单、不依赖规则系统）；
    /// 本组件是**重力 × 质量叠加**的五档版，适合「两种规则一起拧」的谜题。
    /// 两者都是可站、可承载的竖直升降台，选一个用即可。</para>
    ///
    /// <para><b>层级：轨道是父，滑块是子。</b>⚠️ 这与 <c>MovingPlatform</c> / <c>SpringPlatform</c>
    /// （碰撞体 + 刚体 + 脚本全挂在根的同一个物体上）不同，是有意的 —— 本组件天然有**两个物体**：
    /// 静止的轨道（要接美术、要能按段拼长度）与会动的滑块。若把轨道也挂在滑块底下，
    /// 就得每帧反过来搬它才看着不动。所以：</para>
    /// <list type="bullet">
    /// <item><b>根</b> = 轨道：只挂本脚本 + 轨道美术，**不挂碰撞体 / 刚体**（它是景物，不该挡路）；</item>
    /// <item><b>子物体</b>（默认名 <c>Carriage</c>）= 滑块：<c>Rigidbody2D</c>（Kinematic）
    /// + <c>Collider2D</c>（**Ground 层**，玩家只认这个层）+ 美术。</item>
    /// </list>
    /// <para>承载照常成立：玩家侧 <c>ContactSensor</c> 用 <c>GetComponentInParent&lt;IMovingSurface&gt;()</c>
    /// 找承载面，从滑块碰撞体往上一步就是本组件。</para>
    ///
    /// <para><b>档位写在本地坐标</b>（相对轨道原点的 y）：迁移 / 复制这一段时只要拖根物体，
    /// 五档跟着走，不会像世界坐标那样留在原地。⚠️ 代价是**不要缩放根物体** ——
    /// 父级缩放会被子物体继承，滑块的碰撞箱与位移都会跟着变形；
    /// 白盒轨道美术请缩放 <c>Rail</c> 子物体（见 <see cref="SyncRailVisual"/>）。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时物理已停步进（<c>Physics2D.simulationMode = Script</c>），
    /// 滑轨也必须停 —— 读 <c>TimeManager.IsPaused</c> 早退（单一真相是 TimeManager）；
    /// 换档用 <c>Time.fixedDeltaTime</c>，随 <c>Time.timeScale</c> 缩放 —— 世界变慢，滑块也跟着变慢。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑦b 重力×质量滑轨（五档升降）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GravityLift : MonoBehaviour, IMovingSurface
    {
        /// <summary>滑块停在哪一档。数值就是档位序号，档位 = <c>Middle + 重力票 + 质量票</c>。</summary>
        public enum Tier
        {
            /// <summary>最底档：重力增强 **且** 质量增强。</summary>
            Lowest = 0,

            /// <summary>下一档：重力或质量**其中一个**增强。</summary>
            Low = 1,

            /// <summary>常态：无规则 / 恒定，或者一个增强一个减弱（互相抵消）。</summary>
            Middle = 2,

            /// <summary>上一档：重力或质量**其中一个**减弱。</summary>
            High = 3,

            /// <summary>最高档：重力减弱 **且** 质量减弱。</summary>
            Highest = 4,
        }

        /// <summary>档位总数。</summary>
        public const int TierCount = 5;

        /// <summary>中档的序号 —— 累加两票时的基准。</summary>
        private const int MiddleIndex = (int)Tier.Middle;

        /// <summary>比基准重力大 / 小的判定容差。重力规则的取值是 9.81 / 19.62 / 2.943 / 0，离得很开。</summary>
        private const float GravityEpsilon = 0.01f;

        /// <summary>「反转」的参考质量，与 <c>RuleAffectedBody</c> / <c>RailLift</c> 同一口径。</summary>
        private const float ReferenceMass = 1f;

        /// <summary>比基准质量大 / 小的判定容差。质量规则的取值都是 1 / 3 / 0.3 / 0.05，离得很开。</summary>
        private const float MassEpsilon = 1e-3f;

        /// <summary>白盒轨道柱两端各多留一点，免得看着像滑块悬在轨道外面。</summary>
        private const float RailMargin = 0.25f;

        /// <summary>白盒标签离轨道顶端的距离。</summary>
        private const float LabelGap = 0.5f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("档位高度（本地坐标 = 相对轨道原点的 y）")]
        [Tooltip("中档：**无规则时**滑块停在哪。摆场景时按常态摆，自动美术与碰撞都从这里对齐。\n" +
                 "用 Inspector 上的「中档 = 滑块当前位置」按钮填。")]
        [SerializeField] private float _midHeight = 0f;

        [Tooltip("**每一档**往上走多高。五档里最高档 = 中档 + 2 × 这个值。\n" +
                 "0 = 变轻了也不动。")]
        [SerializeField, Min(0f)] private float _riseHeight = 1f;

        [Tooltip("**每一档**往下沉多深。五档里最低档 = 中档 − 2 × 这个值。\n" +
                 "0 = 变重了也不动。上下可以不对称。")]
        [SerializeField, Min(0f)] private float _sinkDepth = 1f;

        [Header("换档")]
        [Tooltip("换档速度（单位 / 秒）。档位之间是**匀速走**过去，不是瞬移 ——\n" +
                 "瞬移带不动站在上面的人（承载是按速度给的）。想读起来像瞬间跳档就把它调大（例如 60）。")]
        [SerializeField, Min(0.01f)] private float _speed = 3f;

        [Header("判定基准")]
        [Tooltip("工程里的**默认重力**沿竖直向下的分量（当前 Project Settings 是 9.81）。\n" +
                 "判定「比正常重 / 比正常轻」都拿它做基准。\n" +
                 "⚠️ 改了 Project Settings → Physics 2D → Gravity，就要同步改这里\n" +
                 "（与 GravityRule.Normal 同一口径）。")]
        [SerializeField, Min(0.01f)] private float _referenceGravity = 9.81f;

        [Tooltip("本机构的**基准质量**：判定「重的变轻 / 轻的变重」时拿它跟参考质量 1 比。\n" +
                 "⚠️ 它只是判定的基准，不是滑块刚体的 mass（Kinematic 刚体的质量物理不参与）。\n" +
                 "两根 _baseMass 一个大于 1、一个小于 1 的滑轨，在「质量 × 反转」下会**互相对调**。")]
        [SerializeField, Min(0.01f)] private float _baseMass = 1f;

        [Header("引用")]
        [Tooltip("滑块。留空则自动找子物体上的第一个 Rigidbody2D。")]
        [SerializeField] private Transform _carriage;

        [Header("白盒")]
        [Tooltip("白盒轨道柱的美术。**会被沿 Y 轴拉伸到五档的全行程**：\n" +
                 "所以这张图要按「1 世界单位高」做（与白盒方块一致）。\n" +
                 "**接正式美术时把这个引用清空**，把美术作为轨道（根物体）的子物体摆好 —— 代码不碰它。")]
        [SerializeField] private Transform _railVisual;

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;

        /// <summary>滑块当前的本地 y。**自己记账，不信 transform** ——
        /// <c>MovePosition</c> 要到物理步结束才落到 transform 上，读它拿到的可能是上一步的值。</summary>
        private float _carriageY;

        /// <summary>当前档位（由重力 + 质量算出）。</summary>
        private Tier _tier = Tier.Middle;

        private bool _warnedNoCarriage;

        /// <summary>本物理帧滑块的速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 ——
        /// 承载方加进自己的速度后，走出的距离与滑块**逐帧相等**，不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>当前档位。</summary>
        public Tier CurrentTier => _tier;

        /// <summary>滑块当前的本地 y。</summary>
        public float CarriageY => _carriageY;

        /// <summary>中档高度（本地 y）。</summary>
        public float MidHeight => _midHeight;

        /// <summary>每一档往上走多高。</summary>
        public float RiseHeight => _riseHeight;

        /// <summary>每一档往下沉多深。</summary>
        public float SinkDepth => _sinkDepth;

        /// <summary>换档速度（单位 / 秒）。</summary>
        public float Speed => _speed;

        /// <summary>判定用的基准重力（竖直向下分量）。</summary>
        public float ReferenceGravity => _referenceGravity;

        /// <summary>判定用的基准质量。</summary>
        public float BaseMass => _baseMass;

        /// <summary>滑块。摆场景 / 编辑器工具用。</summary>
        public Transform Carriage => _carriage;

        // ==================================================================
        //  档位判定（纯函数：不读字段、不碰 Unity 对象，便于断言）
        // ==================================================================

        /// <summary>
        /// 按当前重力与质量算出滑块该在哪一档：两票相加，再加到中档上。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 参数**按值传**，不用 <c>in</c>：这点拷贝开销不值得让 MCP 的 <c>codedom</c>
        /// 编译器（只到 C# 5）测不了它。</para>
        /// <para>两个因子各投一票（+1 往上 / −1 往下 / 0 不动），所以**同时减弱会叠加两档**，
        /// 而一个增强一个减弱会**互相抵消**。这是加法自然给出的行为，没有为组合写任何特例。</para>
        /// </remarks>
        public static Tier ResolveTier(Vector2 gravity, float referenceGravity,
                                       AttributeValues mass, float baseMass)
        {
            int level = MiddleIndex + GravityVote(gravity, referenceGravity) + MassVote(mass, baseMass);
            return ClampTier(level);
        }

        /// <summary>
        /// 重力投的票：+1 = 往上（不被往下压）、−1 = 往下（压得更狠）、0 = 不动。
        /// </summary>
        /// <remarks>
        /// <para>只取竖直向下的分量（<c>−gravity.y</c>），水平分量忽略 ——
        /// 规则矩阵里没有横向重力，有也轮不到这台升降台表态。</para>
        /// <para><b>反转为什么算 +1</b>：<c>GravityRule</c> 把 <c>g</c> 翻成朝上，
        /// 世界不再把滑块往下压、而是往上推 —— 按「越不被往下压越靠上」这条唯一判据，
        /// 它自然算「减轻」，不需要为反转单开一个特例。</para>
        /// </remarks>
        public static int GravityVote(Vector2 gravity, float referenceGravity)
        {
            float down = -gravity.y;

            if (down < -GravityEpsilon) return 1;                            // 反转：g 朝上
            if (down > referenceGravity + GravityEpsilon) return -1;         // 增强：压得更狠
            if (down < referenceGravity - GravityEpsilon) return 1;          // 削弱 / 失效：变轻
            return 0;                                                        // 恒定 / 无规则
        }

        /// <summary>
        /// 质量投的票：+1 = 往上（变轻）、−1 = 往下（变重）、0 = 不动。
        /// </summary>
        /// <remarks>反转的判定依据是 <paramref name="baseMass"/> 与参考质量的大小关系，
        /// 不是当前有效质量 —— 反转的语义就是「以参考质量为中心取倒数」，见 <c>MassRule</c>。</remarks>
        public static int MassVote(AttributeValues mass, float baseMass)
        {
            if (mass.MassInverted)
            {
                if (baseMass > ReferenceMass + MassEpsilon) return 1;    // 重的变轻 → 浮
                if (baseMass < ReferenceMass - MassEpsilon) return -1;   // 轻的变重 → 沉
                return 0;                                                // 正好在参考质量上 → 不动
            }

            if (mass.MassScale > 1f + MassEpsilon) return -1;    // 增强 = 变重 = 沉
            if (mass.MassScale < 1f - MassEpsilon) return 1;     // 削弱 / 失效 = 变轻 = 浮
            return 0;
        }

        private static Tier ClampTier(int level)
        {
            if (level <= 0) return Tier.Lowest;
            if (level >= TierCount - 1) return Tier.Highest;
            return (Tier)level;
        }

        /// <summary>某一档对应的本地 y。中档 + 每档高度 × 偏离档数。</summary>
        public float HeightOf(Tier tier)
        {
            int offset = (int)tier - MiddleIndex;   // −2 … +2
            if (offset >= 0) return _midHeight + _riseHeight * offset;
            return _midHeight + _sinkDepth * offset;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake() => Rebuild();

        /// <summary>重开一局 / 对象池取出时回到当前档位，不能接着上一次的位置走。</summary>
        private void OnEnable() => Rebuild();

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，滑轨也必须停（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定（与 MovingPlatform / SpringPlatform / RailLift 同一处理）。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;
            if (!EnsureCarriage()) return;

            UpdateTier();
            Advance(dt);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>读一次世界的重力与质量，档位真的变了才刷新记账（顺带刷新白盒标签）。</summary>
        private void UpdateTier()
        {
            Tier wanted = ResolveTier(Physics2D.gravity, _referenceGravity, ReadMass(), _baseMass);
            if (wanted == _tier) return;

            _tier = wanted;
            RefreshLabel();
        }

        /// <summary>朝目标档位匀速走一步。</summary>
        private void Advance(float dt)
        {
            float target = HeightOf(_tier);
            float next = Mathf.MoveTowards(_carriageY, target, _speed * dt);

            // 到位时 MoveTowards 返回的就是目标本身，所以这里可以直接比相等
            // —— 用 Approximately 会把「最后那一小步」也吞掉，滑块就永远差一点点。
            if (next == _carriageY)
            {
                Velocity = Vector2.zero;
                return;
            }

            Vector2 previous = _body.position;
            Vector2 world = ToWorld(next);

            Velocity = (world - previous) / dt;
            _carriageY = next;
            _body.MovePosition(world);
        }

        /// <summary>把「档位点」摆回当前档位并清空速度。开局 / 对象池取出时调用。</summary>
        private void Rebuild()
        {
            if (!EnsureCarriage()) return;

            _tier = ResolveTier(Physics2D.gravity, _referenceGravity, ReadMass(), _baseMass);
            _carriageY = HeightOf(_tier);

            if (_body != null) _body.position = ToWorld(_carriageY);
            Velocity = Vector2.zero;

            SyncRailVisual();
            RefreshLabel();
        }

        /// <summary>当前世界的质量属性。规则系统没起来时按默认值（= 这一票不投）。</summary>
        private static AttributeValues ReadMass()
        {
            IAttributeManager attributes = RuleSystem.Instance?.Attributes;
            return attributes != null ? attributes.Values : AttributeValues.Default;
        }

        /// <summary>滑块本体。没在 Inspector 里指定时自动找子物体上的第一个刚体。</summary>
        private bool EnsureCarriage()
        {
            if (_carriage == null)
            {
                Rigidbody2D found = GetComponentInChildren<Rigidbody2D>();
                if (found != null) _carriage = found.transform;
            }

            if (_carriage == null)
            {
                if (!_warnedNoCarriage)
                {
                    _warnedNoCarriage = true;
                    Debug.LogError(
                        "[GravityLift] 没有滑块 —— 轨道上找不到 Rigidbody2D。用菜单 " +
                        "Tools/规则游戏/地图组件/⑦b 生成，或在「引用」栏指定滑块。", this);
                }
                return false;
            }

            if (_body == null)
            {
                _body = _carriage.GetComponent<Rigidbody2D>();
                if (_body == null)
                {
                    Debug.LogError("[GravityLift] 滑块上没有 Rigidbody2D，滑块不会移动。", this);
                    return false;
                }
                ConfigureBody();
            }

            return true;
        }

        /// <summary>档位点的本地坐标 → 世界坐标。</summary>
        /// <remarks>滑块的 x / z 一直是摆场景时的值，代码只改 y。</remarks>
        private Vector2 ToWorld(float localY)
        {
            Vector3 local = _carriage.localPosition;
            return transform.TransformPoint(new Vector3(local.x, localY, local.z));
        }

        private void ConfigureBody()
        {
            // Kinematic：位移完全由代码决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值换档会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>滑块当前的本地 y（摆完之后用「中档 = 滑块当前位置」按钮读它）。</summary>
        public float CarriageLocalY => _carriage != null ? _carriage.localPosition.y : _midHeight;

        private float CarriageLocalX => _carriage != null ? _carriage.localPosition.x : 0f;

        /// <summary>只给编辑器工具用：一次改三档。</summary>
        public void SetTiers(float midHeight, float riseHeight, float sinkDepth)
        {
            _midHeight = midHeight;
            _riseHeight = Mathf.Max(0f, riseHeight);
            _sinkDepth = Mathf.Max(0f, sinkDepth);
            SyncRailVisual();
        }

        /// <summary>只给编辑器工具用：指定滑块。</summary>
        public void SetCarriage(Transform carriage)
        {
            _carriage = carriage;
            _body = null;
        }

        /// <summary>只给编辑器工具用：把判定基准设成当前工程重力（竖直向下分量的绝对值）。</summary>
        /// <remarks>⚠️ 只在**没进播放**时用 —— 播放中读到的是已经乘以规则倍率的重力，
        /// 拿它当基准等于把「当前档位」定义成「中档」。</remarks>
        public void SetReferenceGravity(float magnitude)
        {
            _referenceGravity = Mathf.Max(0.01f, magnitude);
            RefreshLabel();
        }

        /// <summary>只给编辑器工具用：接上白盒轨道柱与标签的引用。</summary>
        public void SetWhitebox(Transform railVisual, TextMesh label)
        {
            _railVisual = railVisual;
            _label = label;
            SyncRailVisual();
            RefreshLabel();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float riseHeight, float sinkHeight)
        {
            return "重力×质量 五档 · 每档升" + riseHeight.ToString("0.#") + " 沉" + sinkHeight.ToString("0.#");
        }

        /// <summary>
        /// 把白盒轨道柱拉成「五档全行程」，并把它与标签摆到对应高度。
        /// </summary>
        /// <remarks>
        /// <para>柱子是 1 世界单位高的图（与白盒方块一致），所以缩放 y 直接就是高度
        /// —— 与 <c>SpringPlatform</c> 的弹簧柱同一套做法。</para>
        /// <para><b>只有在 <see cref="_railVisual"/> 挂上时才碰美术</b>：正式轨道是拼出来的
        /// （绿 / 红 / 紫分开画，红色段可无缝衔接延长），那种图的长度是画死的，不该被拉伸
        /// —— 接正式美术时清空这个引用即可。</para>
        /// <para>写在**根物体的局部坐标**里：轨道柱、标签都是根的子物体，根不动它们就不动。</para>
        /// </remarks>
        private void SyncRailVisual()
        {
            if (_railVisual == null) return;

            float x = CarriageLocalX;
            float span = (_riseHeight + _sinkDepth) * 2f;
            float bottom = _midHeight - _sinkDepth * 2f;
            float height = Mathf.Max(span + RailMargin * 2f, 0.5f);

            _railVisual.localPosition = new Vector3(x, bottom + span * 0.5f, 0f);

            Vector3 scale = _railVisual.localScale;
            scale.y = height;
            _railVisual.localScale = scale;

            if (_label != null)
            {
                _label.transform.localPosition =
                    new Vector3(x, _midHeight + _riseHeight * 2f + LabelGap, 0f);
            }
        }

        private void OnValidate()
        {
            if (_sinkDepth < 0f) _sinkDepth = 0f;
            if (_riseHeight < 0f) _riseHeight = 0f;
            if (_speed < 0.01f) _speed = 0.01f;
            if (_referenceGravity < 0.01f) _referenceGravity = 0.01f;
            if (_baseMass < 0.01f) _baseMass = 0.01f;

            if (!Application.isPlaying) SyncRailVisual();
            RefreshLabel();
        }

        /// <summary>把档位与行程写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_riseHeight, _sinkDepth);
            // 播放中额外报当前档位 —— 白盒测试时不用去猜它为什么不动。
            if (Application.isPlaying) wanted += " · " + TierName(_tier);

            if (_label.text != wanted) _label.text = wanted;
        }

        /// <summary>档位的中文名（白盒标签 / 调试用）。</summary>
        public static string TierName(Tier tier)
        {
            switch (tier)
            {
                case Tier.Lowest:  return "最底档";
                case Tier.Low:     return "低档";
                case Tier.High:    return "高档";
                case Tier.Highest: return "最高档";
                default:           return "中档";
            }
        }

        private void OnDrawGizmos()
        {
            float x = CarriageLocalX;
            Vector3 lowest  = transform.TransformPoint(new Vector3(x, _midHeight - _sinkDepth * 2f, 0f));
            Vector3 highest = transform.TransformPoint(new Vector3(x, _midHeight + _riseHeight * 2f, 0f));

            // 轨道：最底档 → 最高档。
            Gizmos.color = new Color(0.55f, 0.75f, 0.95f, 0.9f);
            Gizmos.DrawLine(lowest, highest);

            // 五档。颜色即语义：红 = 最底下、青 = 最上面、黄 = 常态。
            for (int i = 0; i < TierCount; i++)
            {
                Tier tier = (Tier)i;
                float t = i / (float)(TierCount - 1);           // 0 最底 … 1 最高
                Gizmos.color = Color.Lerp(
                    new Color(1f, 0.45f, 0.35f, 0.95f),
                    new Color(0.45f, 0.95f, 1f, 0.95f), t);
                if (tier == Tier.Middle) Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.95f);

                Gizmos.DrawWireSphere(
                    transform.TransformPoint(new Vector3(x, HeightOf(tier), 0f)), 0.12f);
            }

            // 播放中：滑块现在在哪一档。
            if (!Application.isPlaying) return;

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(x, _carriageY, 0f)), 0.22f);
        }
    }
}
