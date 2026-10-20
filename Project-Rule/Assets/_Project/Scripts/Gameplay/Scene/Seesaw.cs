using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 跷跷板：**两端比质量** —— 重的那头沉下去，轻的那头被撬起来。
    /// </summary>
    /// <remarks>
    /// <para>只做一件事：每物理帧称一次左右两端的质量，谁重就往谁那边翻。
    /// 质量读的是 <c>Rigidbody2D.mass</c>，而「质量 × 反转」改的正是这个值 ——
    /// 规则一生效，两头的轻重互换，跷跷板**自己就翻过来了**，这里没有任何特判。</para>
    ///
    /// <para>板挂高摩擦材质（<c>SeesawMaterial2D</c>，摩擦 1、Combine = Maximum），
    /// 上面的石头 / 铁箱不会顺着斜面滑下去。</para>
    ///
    /// <para>⚠️ 板要在 <c>Ground</c> 层（层 8）：玩家的 Slide 与接地探测只认这个层。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑰ 跷跷板（两端比质量）</c> 生成
    /// <c>Prefabs/Seesaw.prefab</c>，关卡里直接拖。</para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class Seesaw : MonoBehaviour
    {
        /// <summary>没有 BoxCollider2D 时的兜底半长（米）。</summary>
        private const float FallbackHalfLength = 3f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("杠杆")]
        [Tooltip("会跟着转的那块板（本物体的子物体）。留空 = 用本物体自己。\n" +
                 "它必须带 Kinematic 刚体 + 实心碰撞体，并且住在 Ground 层。")]
        [SerializeField] private Transform _board;

        [Tooltip("翻到底时的倾角（度）。")]
        [SerializeField, Range(1f, 80f)] private float _maxAngle = 25f;

        [Tooltip("翻动的速度（度 / 秒）。90 ≈ 四分之一秒翻到底。")]
        [SerializeField, Min(1f)] private float _turnSpeed = 90f;

        [Header("称重")]
        [Tooltip("两端质量差小于它就算平衡（板回平）。")]
        [SerializeField, Min(0f)] private float _balanceEpsilon = 0.01f;

        [Tooltip("压在板上的哪些层参与称重。默认所有层（Trigger 永远不算）。")]
        [SerializeField] private LayerMask _loadMask = ~0;

        [Tooltip("贴板检测带的高度（米）：碰撞体伸进这一薄层就算「压在板上」。")]
        [SerializeField, Min(0.05f)] private float _probeHeight = 0.4f;

        [Header("白盒")]
        [Tooltip("在 Scene 视图里画检测带。")]
        [SerializeField] private bool _drawProbe = true;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>压在板上的一个刚体。质量取 <c>Rigidbody2D.mass</c>。</summary>
        private struct Load
        {
            public Vector2 Position;
            public float Mass;
            public PlayerController Player;
        }

        private Transform _boardTransform;
        private Rigidbody2D _boardBody;
        private Collider2D _boardSolid;

        /// <summary>当前倾角（度）。正值 = 逆时针 = **左端沉、右端抬起**。</summary>
        private float _angle;

        private float _leftMass;
        private float _rightMass;

        private readonly Collider2D[] _hits = new Collider2D[16];
        private readonly List<Load> _loads = new List<Load>(8);
        private readonly HashSet<Rigidbody2D> _seen = new HashSet<Rigidbody2D>();

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>支点（世界坐标）= 本物体位置。</summary>
        public Vector2 Pivot => transform.position;

        /// <summary>当前倾角（度）。正值 = 左端沉。</summary>
        public float Angle => _angle;

        /// <summary>左端压着的总质量（kg）。</summary>
        public float LeftMass => _leftMass;

        /// <summary>右端压着的总质量（kg）。</summary>
        public float RightMass => _rightMass;

        /// <summary>两端平衡（板回平）。</summary>
        public bool IsLevel => Mathf.Abs(_angle) < 0.001f;

        /// <summary>板轴（世界方向）。</summary>
        public Vector2 BoardAxis => AxisFor(_angle);

        /// <summary>板的上表面法线（世界方向）。</summary>
        public Vector2 BoardNormal => NormalFor(_angle);

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            ResolveBoard();
            _angle = 0f;
            SnapRotation();
        }

        private void OnEnable()
        {
            if (_boardBody == null) ResolveBoard();

            _angle = 0f;
            SnapRotation();
        }

        private void FixedUpdate()
        {
            // 暂停：物理已停步进（Physics2D.simulationMode = Script），姿态也冻结。
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            CollectMasses();

            float target = ResolveTargetAngle(_leftMass, _rightMass, _balanceEpsilon, _maxAngle);

            float previous = _angle;
            _angle = Mathf.MoveTowards(_angle, target, _turnSpeed * dt);

            ApplyRotation();
            CarryRiders(_angle - previous);
        }

        // ==================================================================
        //  规则（纯函数，编辑器自检直接调）
        // ==================================================================

        /// <summary>
        /// 两端质量 → 目标倾角。左边重就翻到 <c>+maxAngle</c>（左沉），右边重就翻到 <c>−maxAngle</c>，
        /// 差不多重就回平。
        /// </summary>
        public static float ResolveTargetAngle(float leftMass, float rightMass, float epsilon, float maxAngle)
        {
            if (leftMass > rightMass + epsilon) return maxAngle;
            if (rightMass > leftMass + epsilon) return -maxAngle;
            return 0f;
        }

        /// <summary>倾角（度）→ 板轴（世界方向）。</summary>
        public static Vector2 AxisFor(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        /// <summary>倾角（度）→ 板的上表面法线（世界方向）。</summary>
        public static Vector2 NormalFor(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
        }

        /// <summary>板绕支点转 <paramref name="deltaDeg"/> 度时，某个点要走的位移。</summary>
        public static Vector2 RotationDisplacement(Vector2 point, Vector2 pivot, float deltaDeg)
        {
            float rad = deltaDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            Vector2 r = point - pivot;
            var rotated = new Vector2(r.x * cos - r.y * sin, r.x * sin + r.y * cos);
            return rotated - r;
        }

        // ==================================================================
        //  称重
        // ==================================================================

        /// <summary>
        /// 把板面上那一薄层里的刚体称一遍，按位置分到左右两端。
        /// </summary>
        /// <remarks>
        /// 用 <c>OverlapBox</c> 不用射线：物体脚陷进板面几厘米时，射线从它内部出发会被直接跳过。
        /// 同一个刚体可能挂了好几个碰撞体，用 <see cref="_seen"/> 去重。
        /// </remarks>
        private void CollectMasses()
        {
            _leftMass = 0f;
            _rightMass = 0f;
            _loads.Clear();
            _seen.Clear();

            if (_boardSolid == null) return;

            Vector2 pivot = Pivot;
            Vector2 axis = BoardAxis;
            Vector2 normal = BoardNormal;

            float halfLength = HalfLength();
            float halfThickness = HalfThickness();

            Vector2 center = pivot + normal * (halfThickness + _probeHeight * 0.5f);
            var size = new Vector2(halfLength * 2f, _probeHeight);

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_loadMask);

            int count = Physics2D.OverlapBox(center, size, _angle, filter, _hits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                // 板自己（及它的子物体）不算载荷。
                if (hit.transform == _boardTransform || hit.transform.IsChildOf(_boardTransform)) continue;

                Rigidbody2D body = hit.attachedRigidbody;
                if (body == null || body == _boardBody) continue;

                if (!_seen.Add(body)) continue;

                Vector2 position = body.worldCenterOfMass;
                _loads.Add(new Load
                {
                    Position = position,
                    Mass = body.mass,
                    Player = body.GetComponentInParent<PlayerController>(),
                });

                // 支点哪一侧就算哪一端的质量。
                if (Vector2.Dot(position - pivot, axis) >= 0f) _rightMass += body.mass;
                else _leftMass += body.mass;
            }
        }

        /// <summary>
        /// 板转了多少角度，站在上面的玩家就跟着走多少位移。
        /// </summary>
        /// <remarks>
        /// 旋转半径取玩家在板面上的落点（质心沿板法线投影下去），不是质心本身 ——
        /// 拿质心当半径会让每转一度都多推他一段水平位移。位移交给玩家自己的承载入口，
        /// 与电梯 / 回弹块同一套机制，不会被转过来的板包进碰撞体里。
        /// </remarks>
        private void CarryRiders(float deltaDeg)
        {
            if (Mathf.Abs(deltaDeg) < 0.0001f) return;

            Vector2 pivot = Pivot;
            Vector2 normal = BoardNormal;

            for (int i = 0; i < _loads.Count; i++)
            {
                Load load = _loads[i];
                if (load.Player == null) continue;

                Vector2 point = load.Position - normal * Vector2.Dot(load.Position - pivot, normal);
                Vector2 displacement = RotationDisplacement(point, pivot, deltaDeg);
                if (displacement.sqrMagnitude <= 0f) continue;

                load.Player.CarryOnPlatform(displacement, _boardSolid);
            }
        }

        // ==================================================================
        //  内部
        // ==================================================================

        private void ResolveBoard()
        {
            _boardTransform = _board != null ? _board : transform;
            _boardBody = _boardTransform.GetComponent<Rigidbody2D>();
            _boardSolid = _boardTransform.GetComponent<Collider2D>();

            if (_boardBody == null)
            {
                Debug.LogWarning(
                    "[Seesaw] 板上没有 Rigidbody2D，跷跷板转不起来。" +
                    "用菜单「⑰ 跷跷板」生成的预制体结构是现成的。", this);
            }
        }

        /// <summary>板的一半长度（米）：取碰撞体的**世界**尺寸，关卡里把板拖大也照样准。</summary>
        private float HalfLength()
        {
            if (_boardSolid is BoxCollider2D box)
            {
                return Mathf.Abs(box.size.x * _boardTransform.lossyScale.x) * 0.5f;
            }

            return FallbackHalfLength;
        }

        /// <summary>板的一半厚度（米）。检测带从板的上表面往外量。</summary>
        private float HalfThickness()
        {
            if (_boardSolid is BoxCollider2D box)
            {
                return Mathf.Abs(box.size.y * _boardTransform.lossyScale.y) * 0.5f;
            }

            return 0.15f;
        }

        /// <summary>每物理帧提交转角。<c>MoveRotation</c> 要到物理步进时才生效 —— 与位移同步。</summary>
        private void ApplyRotation()
        {
            if (_boardBody == null) return;
            _boardBody.MoveRotation(_angle);
        }

        /// <summary>立刻把板摆到当前角度（初始化用，不走物理步进）。</summary>
        private void SnapRotation()
        {
            if (_boardBody == null) return;
            _boardBody.rotation = _angle;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        private void OnDrawGizmos()
        {
            if (!_drawProbe) return;

            Transform board = _board != null ? _board : transform;
            var solid = board.GetComponent<Collider2D>();

            float halfLength = solid is BoxCollider2D box
                ? Mathf.Abs(box.size.x * board.lossyScale.x) * 0.5f
                : FallbackHalfLength;
            float halfThickness = solid is BoxCollider2D b2
                ? Mathf.Abs(b2.size.y * board.lossyScale.y) * 0.5f
                : 0.15f;

            Vector2 pivot = transform.position;
            Vector2 axis = BoardAxis;
            Vector2 normal = BoardNormal;

            // 板本身。
            Gizmos.color = new Color(0.62f, 0.86f, 1f, 0.9f);
            DrawRotatedBox(pivot, halfLength * 2f, halfThickness * 2f, axis, normal);

            // 检测带：压在里面的刚体都会被称重。
            Gizmos.color = new Color(1f, 0.78f, 0.35f, 0.35f);
            DrawRotatedBox(
                pivot + normal * (halfThickness + _probeHeight * 0.5f),
                halfLength * 2f, _probeHeight, axis, normal);

            // 支点。
            Gizmos.color = new Color(0.35f, 1f, 0.7f, 0.95f);
            Gizmos.DrawWireSphere(pivot, 0.18f);

            if (!Application.isPlaying) return;

            // 播放中：标出重的那一端。
            if (Mathf.Abs(_leftMass - _rightMass) < 0.0001f) return;
            Gizmos.color = new Color(1f, 0.4f, 0.35f, 1f);
            Gizmos.DrawLine(pivot, pivot + axis * (_leftMass > _rightMass ? -1f : 1f) * halfLength * 0.85f);
        }

        private static void DrawRotatedBox(Vector2 center, float length, float thickness, Vector2 axis, Vector2 normal)
        {
            Vector2 halfAxis = axis * (length * 0.5f);
            Vector2 halfNormal = normal * (thickness * 0.5f);

            Vector2 a = center - halfAxis - halfNormal;
            Vector2 b = center + halfAxis - halfNormal;
            Vector2 c = center + halfAxis + halfNormal;
            Vector2 d = center - halfAxis + halfNormal;

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }

        /// <summary>只给编辑器工具用：接上会转的板。</summary>
        public void SetBoard(Transform board)
        {
            _board = board;
            ResolveBoard();
        }
    }
}
