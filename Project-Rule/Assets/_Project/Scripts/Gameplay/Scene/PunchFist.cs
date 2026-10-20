using System.Collections.Generic;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 拳击手套：**开关一响就弹出去**，沿出拳方向把碰到的实体**持续推飞**。
    /// </summary>
    /// <remarks>
    /// <para><b>它做三件事</b>：① 被开关驱动（<see cref="ISwitchTarget"/>，把本物体拖到
    /// <c>CitySwitch.FollowMotor</c> 上）；② 手套子物体**弹出 → 停一下 → 收回**；
    /// ③ 弹出与停留期间，**出拳走廊**（从手套静止位置扫到当前位置的整条带子）里的实体每物理帧吃一份**加速度**。</para>
    ///
    /// <para><b>为什么是加速度、不是一次冲量</b>：加速度 × 接触时长 = 最终速度。
    /// 顶在拳头上不动会被**越推越快**，擦一下只是被碰一下 —— 力度由「接触了多久」决定。
    /// 与铁箱 / 石头那种「交一次力、物体自己除以质量」的推法是同一套分工的两端：
    /// 那边是重物障碍，这边是**打飞**。</para>
    ///
    /// <para><b>推的是谁</b>：父链上有 <see cref="IKnockbackReceiver"/> 的实体（玩家 / 敌人）
    /// 走 <c>AddImpulse(加速度 × dt)</c>，并入它们自己的击退速度（<c>MotionMover</c> 的
    /// <c>_externalVelocity</c>，按 <c>KnockbackDecay</c> 衰减）；Dynamic 刚体（铁箱 / 石头 / 弹珠）
    /// 直接加到 <c>linearVelocity</c> 上，并夹在 <see cref="_maxLaunchSpeed"/> 以内。
    /// 地形、Trigger 区、按钮一概不动 —— 它们既没有接收方，也不是 Dynamic。</para>
    ///
    /// <para><b>出拳方向 = 本物体的局部方向</b>（<see cref="_punchDirection"/>，默认向右）。
    /// 根节点转多少，拳头就朝哪边打、人就被往哪边推：贴着右边的墙朝左挂，就把根节点转 180°。</para>
    ///
    /// <para><b>只在出拳 / 停留阶段推人</b>：收回途中判定关掉，免得把还贴着的手套又拽回来。</para>
    ///
    /// <para>⚠️ <b>开关的两种接法</b>：拉杆（<c>CityLever</c>，<c>Toggle = true</c>）每打开一次出一拳，
    /// 关掉再打开再出一拳；按钮（<c>CityButton</c>，按下即锁死）只出一次 —— 要反复打就用拉杆。</para>
    ///
    /// <para>⚠️ <b>一次只出一拳</b>：出拳 / 停留 / 收回途中再按开关**忽略**（见 <see cref="Punch"/>）——
    /// 不做「连打叠加速度」，否则两下就能把人打到天上。</para>
    ///
    /// <para>⚠️ <b>判定盒不挡路</b>：手套只有一张贴图、没有碰撞体，玩家 / 敌人 / 纸飞机都能穿过去，
    /// 只吃加速度。要让它当墙用，那是 <c>SpringBackBody</c>（回弹块）的活。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑲ 拳击手套（开关驱动出拳）</c> 生成
    /// <c>Prefabs/PunchFist.prefab</c>；关卡里拖一个，旁边放 <c>CityButton</c> / <c>CityLever</c>，
    /// 把拳头拖到开关的 <c>FollowMotor</c> 上。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PunchFist : MonoBehaviour, ISwitchTarget
    {
        /// <summary>弹簧被压到的最短长度：0 会让子物体缩到看不见、还会除出无穷大。</summary>
        private const float MinSpringLength = 0.02f;

        /// <summary>出拳的四个阶段。只有 Extend / Hold 推人。</summary>
        private enum Phase { Idle, Extend, Hold, Retract }

        [Header("出拳")]
        [Tooltip("拳头往哪个方向弹（**本物体的局部方向**，默认向右）。根节点转多少，拳头就朝哪边打 —— " +
                 "关卡里把它摆到墙边、让手套朝着要打的方向，或者直接转根节点。")]
        [SerializeField] private Vector2 _punchDirection = Vector2.right;

        [Tooltip("弹出距离（世界单位，1 ≈ 玩家身高）。从静止位置往前推这么远。")]
        [SerializeField, Min(0.01f)] private float _punchDistance = 2f;

        [Tooltip("弹出用时（秒）。越小越快、越像「打出去」。默认 0.08 已经很干脆。")]
        [SerializeField, Min(0.001f)] private float _extendDuration = 0.08f;

        [Tooltip("弹到最远之后停多久（秒）。**这期间判定一直开着** —— 顶在拳头上的人会持续加速。")]
        [SerializeField, Min(0f)] private float _holdDuration = 0.25f;

        [Tooltip("收回用时（秒）。收回途中**不再推人**。")]
        [SerializeField, Min(0.001f)] private float _retractDuration = 0.35f;

        [Tooltip("弹出时的过冲：0 = 直接到位；1 = 冲出一点再弹回来（默认，读起来有「弹」的手感）。")]
        [SerializeField, Range(0f, 2f)] private float _overshoot = 1f;

        [Header("推力")]
        [Tooltip("推飞碰到的实体用的**加速度**（m/s²）。接触期间每物理帧累加，所以「被顶住多久」决定打多远。\n" +
                 "玩家的击退速度按 KnockbackDecay（30/秒）衰减，稳态速度 ≈ 加速度 ÷ 30：\n" +
                 "默认 500 → 约 16 m/s（和跳跃 16.5 一个量级），足够把人在竖井里打飞。")]
        [SerializeField, Min(0f)] private float _acceleration = 500f;

        [Tooltip("**动态物体**（铁箱 / 石头 / 弹珠）被这一拳推到的速度上限（m/s）。0 = 不封顶。\n" +
                 "玩家 / 敌人有自己的击退衰减，不受这一栏管。")]
        [SerializeField, Min(0f)] private float _maxLaunchSpeed = 18f;

        [Tooltip("判定盒大小（世界单位）。**出拳走廊** = 从手套静止位置扫到当前手套位置的整条带子，\n" +
                 "判定盒是它的粗细 —— 站在拳臂中间的人也会被打到，不只有拳尖那一小块。")]
        [SerializeField] private Vector2 _strikeSize = new Vector2(1.1f, 1.1f);

        [Header("表现")]
        [Tooltip("会弹出去的子物体（手套）。留空 = 不动的拳头，只会推人。")]
        [SerializeField] private Transform _head;

        [Tooltip("弹簧子物体（会被拉长）。留空 = 不拉。\n" +
                 "长边沿局部 +Y（竖图）或 +X（横图）都行 —— 运行时自动转到出拳方向，再沿长边拉伸。")]
        [SerializeField] private Transform _spring;

        [Header("调试")]
        [Tooltip("在 Console 里打印每一次出拳、打到了谁；一拳什么都没碰到会报一次警告。\n" +
                 "**调手感时开着**（能直接看出是没接线、方向反了、还是人不在判定里），调完关掉。")]
        [SerializeField] private bool _logHits = true;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>手套静止时的局部位置（Awake 记一次，之后只在这个位置上加偏移）。</summary>
        private Vector3 _restLocalPosition;

        /// <summary>弹簧的初始局部缩放：长度那一轴每帧重算，另一轴（粗细）沿用它。</summary>
        private Vector3 _springBaseScale = Vector3.one;

        /// <summary>弹簧贴图的长边是否沿局部 Y（竖图）。竖图要先转到出拳方向再拉长。</summary>
        private bool _springAlongY;

        /// <summary>弹簧贴图长边在 scale = 1 时的长度（世界单位）—— 决定「拉多长要乘多少」。</summary>
        private float _springUnitLength = 1f;

        private bool _initialized;

        private Phase _phase = Phase.Idle;
        private float _elapsed;

        /// <summary>当前阶段的进度：出拳 / 收回是 0~1，停留恒为 1。</summary>
        private float _progress;

        /// <summary>开关当前状态 —— 只在**上升沿**出拳，开着的时候不反复打。</summary>
        private bool _switchOn;

        /// <summary>判定盒查询缓存（每帧复用，不分配）。</summary>
        private readonly Collider2D[] _hits = new Collider2D[16];

        /// <summary>这一物理帧已经推过的接收方 / 刚体（同一实体常有两个碰撞体：身体 + 受击箱）。</summary>
        private readonly HashSet<IKnockbackReceiver> _receivers = new HashSet<IKnockbackReceiver>();
        private readonly HashSet<Rigidbody2D> _bodies = new HashSet<Rigidbody2D>();

        /// <summary>这一拳已经打过日志的对象（同一拳只报一次；一拳什么都没碰到时用它判断）。</summary>
        /// <remarks>用 <see cref="Object"/> 本身当键：Unity 重写过 Equals / GetHashCode，
        /// 而 <c>GetInstanceID()</c> 在 Unity 6.6 已废弃（编译报错）。</remarks>
        private readonly HashSet<Object> _announced = new HashSet<Object>();

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>现在是不是「判定开着」的阶段（出拳 / 停留）。</summary>
        public bool IsStriking => _phase == Phase.Extend || _phase == Phase.Hold;

        /// <summary>出拳方向（世界方向，单位向量）。</summary>
        public Vector2 PunchDirectionWorld => transform.TransformDirection(PunchDirectionLocal);

        /// <summary>出拳方向（局部方向，单位向量）。</summary>
        public Vector2 PunchDirectionLocal
        {
            get
            {
                Vector2 direction = _punchDirection;
                return direction.sqrMagnitude < 0.0001f ? Vector2.right : direction.normalized;
            }
        }

        /// <summary>判定盒中心（世界坐标）：手套当前所在的位置。</summary>
        public Vector2 StrikeCenter
        {
            get
            {
                if (_head != null) return _head.position;
                return (Vector2)transform.position + PunchDirectionWorld * _punchDistance;
            }
        }

        // ==================================================================
        //  ISwitchTarget：开关驱动
        // ==================================================================

        /// <summary>
        /// 开关打开 / 关闭。**只有打开的那一下出拳**；关掉不回拳、也不打断正在打的那一拳。
        /// </summary>
        /// <remarks>⚠️ <c>CitySwitch.SetOn</c> 每次调用都会转发过来（哪怕状态没变），
        /// 所以这里必须自己看**上升沿**，不能一收到 <c>true</c> 就打。</remarks>
        public void SetSwitchState(bool on)
        {
            bool rising = on && !_switchOn;
            _switchOn = on;

            if (rising) Punch();
        }

        /// <summary>打一拳。**正在出拳 / 停留 / 收回时忽略**（不叠加速度）。</summary>
        public void Punch()
        {
            if (_phase != Phase.Idle) return;

            _phase = Phase.Extend;
            _elapsed = 0f;
            _progress = 0f;
            _announced.Clear();

            if (_logHits)
                Debug.Log($"[PunchFist] {name} 出拳：方向 {PunchDirectionWorld}，弹出 {_punchDistance} 单位，" +
                          $"加速度 {_acceleration} m/s²", this);
        }

        /// <summary>Inspector 右键菜单「试打一拳」—— 不用接开关也能验证手感 / 判定。</summary>
        [ContextMenu("试打一拳")]
        private void PunchFromContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[PunchFist] 先进入 Play 模式再试打。", this);
                return;
            }

            Punch();
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_head != null) _restLocalPosition = _head.localPosition;

            if (_spring != null)
            {
                _springBaseScale = _spring.localScale;

                var renderer = _spring.GetComponent<SpriteRenderer>();
                if (renderer != null && renderer.sprite != null)
                {
                    Vector2 spriteSize = renderer.sprite.bounds.size;
                    _springAlongY = spriteSize.y >= spriteSize.x;
                    _springUnitLength = Mathf.Max(0.0001f, _springAlongY ? spriteSize.y : spriteSize.x);
                }

                // 竖图（CityHammer_Spring 就是竖着的）：把长边转到出拳方向上，后面按 Y 拉伸才顺。
                if (_springAlongY)
                    _spring.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3)PunchDirectionLocal);
            }

            _initialized = true;
            ApplyVisual();
        }

        private void OnDisable()
        {
            // 重新启用（对象池 / 重开一局）时不能停在「打出去」的那一帧。
            _phase = Phase.Idle;
            _elapsed = 0f;
            _progress = 0f;
            _switchOn = false;

            ApplyVisual();
        }

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，动画与判定也一起冻住（与 SpringBackBody 同一条）。
            if (RuleGame.Infrastructure.Time.TimeManager.Instance?.IsPaused == true) return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;

            Step(dt);
            ApplyVisual();

            if (IsStriking) Push(dt);
        }

        // ==================================================================
        //  动画状态机
        // ==================================================================

        /// <summary>推进一步：出拳 → 停留 → 收回 → 待机。</summary>
        private void Step(float dt)
        {
            switch (_phase)
            {
                case Phase.Extend:
                    _elapsed += dt;
                    _progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.0001f, _extendDuration));
                    if (_progress >= 1f)
                    {
                        _phase = Phase.Hold;
                        _elapsed = 0f;
                        _progress = 1f;
                    }
                    break;

                case Phase.Hold:
                    _elapsed += dt;
                    if (_elapsed >= _holdDuration)
                    {
                        if (_logHits && _announced.Count == 0)
                            Debug.LogWarning($"[PunchFist] {name} 这一拳**没碰到任何东西**：" +
                                             $"走廊从 {transform.TransformPoint(_restLocalPosition)} 扫到 {StrikeCenter}，" +
                                             "检查出拳方向（局部 +X）、弹出距离，以及目标是不是真的站在拳前。", this);

                        _phase = Phase.Retract;
                        _elapsed = 0f;
                    }
                    break;

                case Phase.Retract:
                    _elapsed += dt;
                    _progress = 1f - Mathf.Clamp01(_elapsed / Mathf.Max(0.0001f, _retractDuration));
                    if (_progress <= 0f)
                    {
                        _phase = Phase.Idle;
                        _elapsed = 0f;
                        _progress = 0f;
                    }
                    break;
            }
        }

        /// <summary>手套现在伸到了百分之几（1 = 最远）。出拳带过冲，可以略大于 1。</summary>
        private float HeadFactor
        {
            get
            {
                switch (_phase)
                {
                    case Phase.Extend: return EaseOutBack(_progress, _overshoot);
                    case Phase.Hold: return 1f;
                    case Phase.Retract: return Mathf.SmoothStep(0f, 1f, _progress);
                    default: return 0f;
                }
            }
        }

        /// <summary>把「伸到百分之几」落到手套与弹簧上。</summary>
        private void ApplyVisual()
        {
            if (!_initialized || _head == null) return;

            Vector3 headLocal = _restLocalPosition
                + (Vector3)(PunchDirectionLocal * (_punchDistance * HeadFactor));

            if (_head.localPosition != headLocal) _head.localPosition = headLocal;

            if (_spring == null) return;

            // 弹簧从底座原点拉到手套中心：中点 + 长度都由手套位置推出来。
            float length = Mathf.Max(headLocal.magnitude, MinSpringLength);
            Vector3 springPosition = headLocal * 0.5f;

            // 沿贴图的**长边**拉伸：竖图（长边在 Y）拉 Y，横图拉 X。另一轴是粗细，保持原样。
            Vector3 springScale = _springBaseScale;
            if (_springAlongY) springScale.y = length / _springUnitLength;
            else springScale.x = length / _springUnitLength;

            if (_spring.localPosition != springPosition) _spring.localPosition = springPosition;
            if (_spring.localScale != springScale) _spring.localScale = springScale;
        }

        /// <summary>「啪」地弹到位、略微过冲再回来。纯函数，编辑器里也能直接验。</summary>
        private static float EaseOutBack(float t, float strength)
        {
            t = Mathf.Clamp01(t);
            if (strength <= 0f) return 1f - Mathf.Pow(1f - t, 3f);

            float c1 = 1.70158f * strength;
            float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ==================================================================
        //  推力
        // ==================================================================

        /// <summary>
        /// 出拳走廊里碰到的实体各吃一份**加速度 × dt** 的速度增量。
        /// </summary>
        /// <remarks>
        /// <para>走廊 = 手套静止位置 → 手套当前所在位置 的整条带子（粗细是 <see cref="_strikeSize"/>）。
        /// **不是只在拳尖拍一个小盒子**：拳臂扫过的地方都要算，站在拳臂中间的人不会漏。</para>
        /// <para>先认 <see cref="IKnockbackReceiver"/>（玩家 / 敌人），再退到 Dynamic 刚体
        /// （铁箱 / 石头 / 弹珠）。同一实体的身体碰撞体与受击箱会同时命中，按接收方 / 刚体去重。</para>
        /// <para>⚠️ 不配 LayerMask、带 <c>useTriggers</c>：受击箱是 Trigger、敌人的身体碰撞体在
        /// 受击箱外面，两种都要能碰到。地形命中后既没有接收方也不是 Dynamic，自然被忽略。</para>
        /// </remarks>
        private void Push(float dt)
        {
            Vector2 direction = PunchDirectionWorld;
            if (direction.sqrMagnitude < 0.0001f || _acceleration <= 0f) return;

            Vector2 rest = transform.TransformPoint(_restLocalPosition);
            Vector2 tip = StrikeCenter;
            Vector2 reach = tip - rest;

            var center = (rest + tip) * 0.5f;
            var size = new Vector2(
                Mathf.Abs(reach.x) + _strikeSize.x,
                Mathf.Abs(reach.y) + _strikeSize.y);

            var filter = new ContactFilter2D { useTriggers = true };
            int count = Physics2D.OverlapBox(center, size, 0f, filter, _hits);

            float speedStep = _acceleration * dt;

            _receivers.Clear();
            _bodies.Clear();

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

                IKnockbackReceiver receiver = hit.GetComponentInParent<IKnockbackReceiver>();
                if (receiver == null && hit.attachedRigidbody != null)
                    receiver = hit.attachedRigidbody.GetComponentInParent<IKnockbackReceiver>();

                if (receiver != null)
                {
                    if (_receivers.Add(receiver))
                    {
                        receiver.AddImpulse(direction * speedStep);
                        Announce(receiver as Object);
                    }
                    continue;
                }

                Rigidbody2D body = hit.attachedRigidbody;
                if (body == null || body.bodyType != RigidbodyType2D.Dynamic) continue;

                if (_bodies.Add(body))
                {
                    PushDynamic(body, direction, speedStep);
                    Announce(body);
                }
            }
        }

        /// <summary>这一拳第一次打到某个对象时打一条日志（每拳每个对象只报一次）。</summary>
        private void Announce(Object target)
        {
            if (!_logHits || target == null) return;
            if (!_announced.Add(target)) return;

            Debug.Log($"[PunchFist] {name} 打到 {target.name}：加速度 {_acceleration} m/s²，" +
                      $"方向 {PunchDirectionWorld}", target);
        }

        /// <summary>给 Dynamic 刚体加一份速度增量，并夹在 <see cref="_maxLaunchSpeed"/> 以内。</summary>
        private void PushDynamic(Rigidbody2D body, Vector2 direction, float speedStep)
        {
            Vector2 velocity = body.linearVelocity;
            float along = Vector2.Dot(velocity, direction);

            float step = speedStep;
            if (_maxLaunchSpeed > 0f)
            {
                step = Mathf.Min(step, _maxLaunchSpeed - along);
                if (step <= 0f) return;
            }

            body.linearVelocity = velocity + direction * step;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        private void OnValidate()
        {
            if (_strikeSize.x < 0.05f) _strikeSize.x = 0.05f;
            if (_strikeSize.y < 0.05f) _strikeSize.y = 0.05f;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 direction = PunchDirectionWorld;

            // 播放中：走廊 = 静止位置 → 手套当前位置。编辑中：手套还没动，按最大射程画。
            Vector3 rest = Application.isPlaying
                ? transform.TransformPoint(_restLocalPosition)
                : (Vector3)StrikeCenter;
            Vector3 tip = Application.isPlaying
                ? (Vector3)StrikeCenter
                : rest + direction * _punchDistance;

            Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.9f);
            Gizmos.DrawLine(rest, tip);
            Gizmos.DrawWireSphere(tip, 0.12f);

            Vector3 reach = tip - rest;
            var size = new Vector3(Mathf.Abs(reach.x) + _strikeSize.x, Mathf.Abs(reach.y) + _strikeSize.y, 0.1f);

            Gizmos.color = new Color(1f, 0.85f, 0.25f, 0.9f);
            Gizmos.DrawWireCube((rest + tip) * 0.5f, size);
        }
    }
}
