using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 激光发射块：沿**水平轨道**往返移动，并从自己身上射出一条固定长度的激光（**默认朝下**）。
    /// </summary>
    /// <remarks>
    /// <para><b>移动</b>与平移平台（<see cref="MovingPlatform"/>）同一条路线：
    /// Kinematic 刚体 + 代码积分，起点 / 终点各是一个**世界 X 坐标**，Y 永远是摆放位置的高度。
    /// 到达端点立刻折返，匀速、没有缓动。暂停时冻结；速度用
    /// <c>Time.fixedDeltaTime</c>，所以「时间 × 变慢」会让它跟着变慢。</para>
    ///
    /// <para><b>激光方向</b>是四向之一（<see cref="BeamSide"/>，默认 <c>Down</c>）：
    /// 光束从块对应那一侧的**表面**射出，判定盒、视觉、gizmo 共用同一个方向与起点 ——
    /// 换成朝下就是一台悬在头顶、横向扫过的激光发射器。</para>
    ///
    /// <para><b>伤害判定与「刺」（<see cref="HazardField"/>）完全同一套</b>：
    /// ① 只查 <c>Hurtbox</c> 层（<c>useTriggers = true</c>），身体碰撞体压上来不算；
    /// ② **不套 <c>CombatSystem.CanHit</c> 的阵营准入** —— 激光对玩家 / 敌人 / 中立道具一视同仁；
    /// ③ 伤害走 <see cref="CombatSystem.ApplyDamage"/> 这唯一出口，这里只传**基础伤害**，
    /// 缩放 / 反转 / 因果 / 挂账都交给 <c>DamageProcessor</c>；
    /// ④ 同一个目标按 <see cref="_repeatInterval"/> 冷却，无敌帧仍由承受方自己决定。</para>
    ///
    /// <para><b>激光会被地形截断</b>：长度先取配置值，再被 <see cref="_stopLayers"/>
    /// （默认 Ground 层）的射线打短。视觉与判定用的是**同一个**长度，不会出现
    /// 「看着被墙挡住、实际隔着墙烧人」。</para>
    ///
    /// <para><b>击退方向</b>：<c>EntityDamageable</c> 按「施加方位置」推方向，
    /// 所以每次结算前把 <c>LaserImpulse</c> 挪到**激光上离目标最近的一点** ——
    /// 与刺「挪到命中的那一格」是同一个意思。</para>
    ///
    /// <para><b>站在上面的人会被带走</b>：本类实现 <see cref="IMovingSurface"/>，
    /// 承载方（<c>ContactSensor</c> → <c>MotionMover</c>）每个物理帧读 <see cref="Velocity"/>。</para>
    ///
    /// <para><b>可以被开关驱动</b>（<see cref="ISwitchTarget"/>）：把本组件拖到
    /// <c>CityButton</c> / <c>CityLever</c> 的 <c>FollowMotor</c> 上即可。开关只控制**激光发射**，
    /// 块照常巡逻（像一台待机的扫描器）。实际状态 = <see cref="_startsOn"/> XOR 开关状态
    /// （与虚实平台同一套），所以「开局开火 + 按钮开局关」的常见配法要按一下才生效。</para>
    ///
    /// <para>⚠️ 块本身是 Ground 层的实心碰撞体，能挡路、能站；激光只是判定与视觉，
    /// 不产生碰撞。⚠️ 不参与时间回溯（与其余地图组件一致）。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑳ 激光发射块（水平移动 + 激光）</c> 一键生成，
    /// 再用 Inspector 上的「起点 / 终点 = 当前位置」定轨道。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class LaserBlock : MonoBehaviour, IMovingSurface, ISwitchTarget
    {
        /// <summary>一次重叠查询最多拿到的碰撞体数。</summary>
        private const int MaxHits = 32;

        /// <summary>激光从块表面再往外挪一点，免得射线 / 判定盒吃进自己的碰撞体。</summary>
        private const float Skin = 0.02f;

        /// <summary>激光朝哪一侧射出。</summary>
        public enum BeamSide
        {
            /// <summary>朝下（默认）。</summary>
            Down = 0,

            /// <summary>朝上。</summary>
            Up = 1,

            /// <summary>向右。</summary>
            Right = 2,

            /// <summary>向左。</summary>
            Left = 3,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("轨道（世界 X 坐标）")]
        [Tooltip("起点 X。进入播放时块会被搬到这里；Y 永远是摆放位置的高度。")]
        [SerializeField] private float _startX = 0f;

        [Tooltip("终点 X。和起点相同 = 不会动的块。")]
        [SerializeField] private float _endX = 4f;

        [Tooltip("移动速度（单位 / 秒）。到端点立刻折返。")]
        [SerializeField, Min(0f)] private float _speed = 2f;

        [Header("激光")]
        [Tooltip("激光朝哪一侧射出。**默认朝下**（悬在头顶的扫描激光）。")]
        [SerializeField] private BeamSide _side = BeamSide.Down;

        [Tooltip("激光长度（单位）。碰到阻挡层会被截短。")]
        [SerializeField, Min(0.1f)] private float _length = 8f;

        [Tooltip("激光粗细（单位）。伤害判定盒与视觉同宽。")]
        [SerializeField, Min(0.02f)] private float _width = 0.14f;

        [Header("伤害（与刺同一套）")]
        [Tooltip("基础伤害。0 = 只击退不掉血。⚠️ 填基础值，不要自己先乘倍率（会绕过伤害规则）。")]
        [SerializeField, Min(0f)] private float _damage = 1f;

        [Tooltip("击退强度。0 = 不击退。")]
        [SerializeField, Min(0f)] private float _knockback = 6f;

        [Tooltip("同一个目标两次受伤的最小间隔（秒）。目标自带无敌帧时以无敌帧为准。")]
        [SerializeField, Min(0f)] private float _repeatInterval = 0.5f;

        [Tooltip("伤害出口。留空则在运行时自动找场景里的 CombatSystem。")]
        [SerializeField] private CombatSystem _combat;

        [Header("开关驱动（可选）")]
        [Tooltip("开局激光是否发射。\n" +
                 "接开关后**实际状态 = 本值 XOR 开关状态**（与虚实平台同一套）：按钮开局是「关」，" +
                 "所以第一次按下正好翻到另一态 —— 不会一进关就被按钮的初始状态改掉。\n" +
                 "配法：把本组件拖到 CityButton / CityLever 的 FollowMotor 上。")]
        [SerializeField] private bool _startsOn = true;

        [Header("阻挡")]
        [Tooltip("挡住激光的层（默认 Ground）。激光碰到这一层的实心碰撞体就被截断。")]
        [SerializeField] private LayerMask _stopLayers;

        [Header("白盒")]
        [Tooltip("激光束的视觉节点（1×1 白盒方块，运行时按长度拉伸）。留空则只做伤害、不显示。")]
        [SerializeField] private Transform _beam;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private BoxCollider2D _box;
        private Transform _impulse;
        private float _halfWidth = 0.4f;
        private float _halfHeight = 0.4f;

        /// <summary>摆放位置的高度：整条轨道都在这一行上。</summary>
        private float _baseY;

        /// <summary>当前 X，始终夹在 <see cref="_minX"/> ~ <see cref="_maxX"/>。</summary>
        private float _x;

        private float _minX;
        private float _maxX;

        /// <summary>下一步往哪头走：+1 去终点，−1 回起点。</summary>
        private float _forward = 1f;

        private readonly Collider2D[] _hits = new Collider2D[MaxHits];
        private readonly HashSet<IDamageable> _hitThisStep = new HashSet<IDamageable>();
        private readonly Dictionary<IDamageable, float> _nextHit = new Dictionary<IDamageable, float>();
        private readonly List<IDamageable> _stale = new List<IDamageable>();

        private bool _warnedNoCombat;

        /// <summary>激光现在发不发射 = <see cref="_startsOn"/> XOR 开关状态。关着时块照常移动。</summary>
        private bool _active = true;

        /// <summary>本物理帧的移动速度（单位 / 秒）。站在块上的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        public Vector2 Velocity { get; private set; }

        /// <summary>起点（世界 X）。</summary>
        public float StartX => _startX;

        /// <summary>终点（世界 X）。</summary>
        public float EndX => _endX;

        /// <summary>移动速度（单位 / 秒）。</summary>
        public float Speed => _speed;

        /// <summary>配置的激光长度（还没被墙截短）。</summary>
        public float BeamLength => _length;

        /// <summary>激光现在是否发射（开关驱动后的实际状态）。</summary>
        public bool IsOn => _active;

        /// <summary>发射方向的单位向量。</summary>
        public Vector2 BeamDirection
        {
            get
            {
                switch (_side)
                {
                    case BeamSide.Up:    return Vector2.up;
                    case BeamSide.Right: return Vector2.right;
                    case BeamSide.Left:  return Vector2.left;
                    default:             return Vector2.down;
                }
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            EnsureRefs();

            if (_beam == null)
                Debug.LogWarning("[LaserBlock] 没有接激光束视觉（Beam）—— 伤害与开关照常，但屏幕上什么都看不到。" +
                                 "用菜单 Tools/规则游戏/地图组件/⑳ 生成的块自带它。", this);

            // 只在结算的那一瞬间用来表达「伤害从激光上的哪一点发出」。
            var marker = new GameObject("LaserImpulse");
            marker.transform.SetParent(transform, false);
            _impulse = marker.transform;

            ConfigureBody();
            Rebuild();
        }

        private void OnEnable()
        {
            // 重开一局 / 对象池取出时也要回到起点，不能接着上次的进度走。
            EnsureRefs();
            ConfigureBody();
            Rebuild();
            ApplyActiveState(_startsOn);
        }

        private void OnDisable() => _nextHit.Clear();

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，判定也必须停（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // ⚠️ 本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            //    所以这里必须全限定。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            StepMotion(dt);

            // 关着的时候块照常巡逻（待机的发射器），只是不发射、不判定。
            if (!_active) return;

            float length = ResolveBeamLength();
            UpdateBeamVisual(length);
            DamageAlong(length);
        }

        // ==================================================================
        //  移动
        // ==================================================================

        private void StepMotion(float dt)
        {
            if (_maxX - _minX <= 0f || _speed <= 0f)
            {
                Velocity = Vector2.zero;
                return;
            }

            float before = _x;
            _x += _speed * dt * _forward;

            // ⚠️ 两个端点都夹住：速度为 0 时 _x 恒等于端点，不夹会每帧折返一次。
            if (_x >= _maxX)
            {
                _x = _maxX;
                _forward = -1f;
            }
            else if (_x <= _minX)
            {
                _x = _minX;
                _forward = 1f;
            }

            _body.MovePosition(new Vector2(_x, _baseY));
            Velocity = new Vector2((_x - before) / dt, 0f);
        }

        /// <summary>按配置把内部状态摆回起点，并立刻落到位。</summary>
        private void Rebuild()
        {
            // Y 永远是「摆放位置的高度」：整条轨道都在这一行上。
            _baseY = transform.position.y;

            _minX = Mathf.Min(_startX, _endX);
            _maxX = Mathf.Max(_startX, _endX);
            _x = Mathf.Clamp(_startX, _minX, _maxX);

            // 起点在右边（往左走的那条轨道），第一步就应该往左。
            _forward = _maxX > _minX && _startX > _endX ? -1f : 1f;

            Velocity = Vector2.zero;
            if (_body != null) _body.position = new Vector2(_x, _baseY);
        }

        // ==================================================================
        //  激光
        // ==================================================================

        /// <summary>
        /// 块现在所在的 X。运行时读内部账本 —— <c>MovePosition</c> 要到物理步末尾才写回
        /// <c>transform</c>，直接读 transform 会让激光比块晚一帧。
        /// </summary>
        private float CurrentX => Application.isPlaying ? _x : transform.position.x;

        private float CurrentY => Application.isPlaying ? _baseY : transform.position.y;

        /// <summary>块中心（世界坐标）。</summary>
        private Vector2 BlockCenter => new Vector2(CurrentX, CurrentY);

        /// <summary>激光起点：块中心沿发射方向挪出「该方向的半尺寸 + 一点皮」。</summary>
        private Vector2 BeamOrigin =>
            BlockCenter + BeamDirection * (HalfExtent(BeamDirection) + Skin);

        /// <summary>激光方向的旋转角（度）。判定盒与视觉共用，保证两者永远对齐。</summary>
        private float BeamAngle => Mathf.Atan2(BeamDirection.y, BeamDirection.x) * Mathf.Rad2Deg;

        /// <summary>块在某个方向上的半尺寸（水平用半宽，竖直用半高）。</summary>
        private float HalfExtent(Vector2 direction)
            => Mathf.Abs(direction.x) > 0.5f ? _halfWidth : _halfHeight;

        /// <summary>实际激光长度：配置长度被阻挡层截短后的值。</summary>
        private float ResolveBeamLength()
        {
            if (_length <= 0f) return 0f;
            if (_stopLayers == 0) return _length;

            RaycastHit2D wall = Physics2D.Raycast(BeamOrigin, BeamDirection, _length, _stopLayers);
            return wall.collider != null ? Mathf.Max(0f, wall.distance) : _length;
        }

        /// <summary>把激光束视觉拉伸到 <paramref name="length"/>。</summary>
        /// <remarks>节点按**激光方向**旋转、位置取世界坐标 —— 块被旋转过也不会让光束歪掉。
        /// 长度靠 localScale.x，所以根物体的 scale 必须保持 1（生成工具就是这么搭的）。</remarks>
        private void UpdateBeamVisual(float length)
        {
            if (_beam == null) return;

            _beam.rotation = Quaternion.Euler(0f, 0f, BeamAngle);
            _beam.position = (Vector3)(BeamOrigin + BeamDirection * (length * 0.5f));
            _beam.localScale = new Vector3(Mathf.Max(0.0001f, length), _width, 1f);
        }

        /// <summary>
        /// 沿激光做一次受击箱重叠查询并结算。与 <see cref="HazardField"/> 的
        /// <c>FixedUpdate</c> 一一对应，只是几何从「瓦片格」换成「一条粗线」。
        /// </summary>
        private void DamageAlong(float length)
        {
            if (_damage <= 0f && _knockback <= 0f) return;
            if (length <= 0f) return;

            CombatSystem combat = ResolveCombat();
            if (combat == null) return;

            Vector2 origin = BeamOrigin;
            Vector2 direction = BeamDirection;

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);

            int count = Physics2D.OverlapBox(origin + direction * (length * 0.5f),
                                             new Vector2(length, _width), BeamAngle, filter, _hits);
            if (count <= 0) return;

            PruneCooldowns();

            _hitThisStep.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                // 只认受击箱（身体碰撞体不算）。⚠️ 这里**不套 CombatSystem 的阵营准入** ——
                // 激光和刺一样，对谁都烧：玩家、敌人、中立道具一视同仁。
                IDamageable target = Hurtbox.ResolveOwner(hit);
                if (target == null) continue;

                // 一次扫描里，一个目标只结算一次（多碰撞体不重复打）。
                if (!_hitThisStep.Add(target)) continue;

                if (_nextHit.TryGetValue(target, out float readyAt) && UnityEngine.Time.time < readyAt) continue;

                // 击退方向从激光上离目标最近的一点发出。
                float along = Mathf.Clamp(Vector2.Dot((Vector2)hit.bounds.center - origin, direction), 0f, length);
                _impulse.position = (Vector3)(origin + direction * along) + new Vector3(0f, 0f, -0.5f);

                int applied = combat.ApplyDamage(_impulse, target, _damage, _knockback);
                if (applied > 0 && _repeatInterval > 0f)
                    _nextHit[target] = UnityEngine.Time.time + _repeatInterval;
            }
        }

        // ==================================================================
        //  开关驱动
        // ==================================================================

        /// <summary>
        /// 开关被扳到「开」/「关」位：实际发射状态 = <see cref="_startsOn"/> XOR <paramref name="on"/>。
        /// </summary>
        /// <remarks>
        /// <para>用异或而不是「on = 发射」，是为了让「块开局开火、按钮开局是关」这个最常见配法
        /// 第一次按下才改变状态 —— 与 <c>PhasePlatform</c> 同一个理由。</para>
        /// <para>⚠️ <c>CitySwitch.SetOn</c> 每次调用都会转发过来（哪怕状态没变），
        /// 所以这里只按「目标状态」套用，重复请求是幂等的。</para>
        /// </remarks>
        public void SetSwitchState(bool on) => ApplyActiveState(_startsOn ^ on);

        /// <summary>套用发射状态：同步激光束的显示 / 隐藏。</summary>
        private void ApplyActiveState(bool active)
        {
            bool changed = _active != active;
            _active = active;

            if (_beam != null && _beam.gameObject.activeSelf != active)
                _beam.gameObject.SetActive(active);

            // 开关是低频事件，每次真的变了打一条 —— 排查「按钮接了但没反应」时，Console 里有没有这行就是分水岭。
            if (changed) Debug.Log($"[LaserBlock] {name}：激光{(active ? "开" : "关")}", this);
        }

        /// <summary>Inspector 右键「试一下开关」—— 不接按钮也能验证视觉与判定。</summary>
        [ContextMenu("试一下开关")]
        private void ToggleFromContextMenu() => ApplyActiveState(!_active);

        private CombatSystem ResolveCombat()
        {
            if (_combat != null) return _combat;

            _combat = FindAnyObjectByType<CombatSystem>();
            if (_combat == null && !_warnedNoCombat)
            {
                _warnedNoCombat = true;
                Debug.LogWarning("[LaserBlock] 场景里没有 CombatSystem，激光打不出伤害。", this);
            }
            return _combat;
        }

        /// <summary>清掉已销毁目标与已过期记录的冷却项，避免字典无限增长。</summary>
        private void PruneCooldowns()
        {
            if (_nextHit.Count <= 8) return;

            float now = UnityEngine.Time.time;
            _stale.Clear();
            foreach (var kv in _nextHit)
            {
                bool dead = kv.Key == null || (kv.Key is Object o && o == null);
                if (dead || now >= kv.Value) _stale.Add(kv.Key);
            }
            for (int i = 0; i < _stale.Count; i++) _nextHit.Remove(_stale[i]);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        private void EnsureRefs()
        {
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_box == null) _box = GetComponent<BoxCollider2D>();
            RefreshHalfExtents();
        }

        private void RefreshHalfExtents()
        {
            // size 是局部空间的，会被 localScale 再乘一次 —— 和碰撞箱实际世界尺寸一致。
            _halfWidth  = _box != null ? Mathf.Abs(_box.size.x * 0.5f * transform.lossyScale.x) : 0.4f;
            _halfHeight = _box != null ? Mathf.Abs(_box.size.y * 0.5f * transform.lossyScale.y) : 0.4f;
        }

        private void ConfigureBody()
        {
            // Kinematic：位移由代码决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值块会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：不动内部账本，直接改两个端点。</summary>
        public void SetPath(float startX, float endX)
        {
            _startX = startX;
            _endX = endX;
            _minX = Mathf.Min(startX, endX);
            _maxX = Mathf.Max(startX, endX);

            if (!Application.isPlaying)
            {
                EnsureRefs();
                UpdateBeamVisual(_length);
            }
        }

        /// <summary>只给生成工具用：接上激光束的视觉节点。</summary>
        public void SetWhitebox(Transform beam)
        {
            _beam = beam;
            if (!Application.isPlaying)
            {
                EnsureRefs();
                UpdateBeamVisual(_length);
            }
        }

        /// <summary>只给生成工具用：指定截断激光的层。</summary>
        public void SetStopLayers(LayerMask layers) => _stopLayers = layers;

        /// <summary>新建时（Reset）：默认只被 Ground 层挡住。</summary>
        private void Reset()
        {
            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0) _stopLayers = 1 << ground;
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            _minX = Mathf.Min(_startX, _endX);
            _maxX = Mathf.Max(_startX, _endX);

            EnsureRefs();
            UpdateBeamVisual(_length);
        }

        private void OnDrawGizmos()
        {
            if (_box == null) _box = GetComponent<BoxCollider2D>();
            RefreshHalfExtents();

            float y = transform.position.y;

            // 轨道
            Vector3 a = new Vector3(_startX, y, 0f);
            Vector3 b = new Vector3(_endX, y, 0f);
            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(a, 0.15f);

            Gizmos.color = new Color(1f, 0.5f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(b, 0.15f);

            // 激光预览（配置长度；被墙截断是运行时的事）
            Vector3 origin = BeamOrigin;
            Vector3 end = origin + (Vector3)(BeamDirection * _length);

            Gizmos.color = new Color(1f, 0.3f, 0.25f, 0.85f);
            Gizmos.DrawLine(origin, end);

            Gizmos.color = new Color(1f, 0.3f, 0.25f, 0.25f);
            Gizmos.matrix = Matrix4x4.TRS((origin + end) * 0.5f,
                                          Quaternion.Euler(0f, 0f, BeamAngle), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_length, _width, 0f));
            Gizmos.matrix = Matrix4x4.identity;

            // 播放中把「块现在走到哪了」标出来。
            if (!Application.isPlaying) return;
            Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
            Gizmos.DrawWireSphere(new Vector3(_x, y, 0f), 0.18f);
        }
    }
}
