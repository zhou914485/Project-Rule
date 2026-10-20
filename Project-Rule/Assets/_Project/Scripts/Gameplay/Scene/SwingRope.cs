using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 危险跳绳：一条**碰到就受伤**的旋转曲线，绳身绕着两个端点的连线做跳绳运动。
    /// </summary>
    /// <remarks>
    /// <para><b>形状</b>：两端点固定在 <see cref="_span"/> 的两头（局部 X 轴），
    /// 中间按抛物线向「下」垂<see cref="_sag"/>米 —— 这个垂度就是 Inspector 上的
    /// 「曲线到中轴的长度」。跳绳时整条绳子绕这条中轴旋转，侧视投影恰好是
    /// <c>垂度 × cos(相位)</c>：相位 0° 垂在下方、90° 拉成直线、180° 拱到上方。
    /// 相位按 <see cref="_spinSpeed"/> 度/秒推进。</para>
    ///
    /// <para><b>判定和尖刺完全同一条路</b>：只查<b>受击箱</b>（<see cref="Hurtbox.Mask"/>，
    /// <c>useTriggers = true</c>），伤害交给 <see cref="CombatSystem.ApplyDamage"/> 传**基础值**，
    /// 缩放 / 反转 / 挂账全由 <c>DamageProcessor</c> 处理。和 <see cref="HazardField"/> 一样
    /// **不套阵营准入** —— 刺对谁都扎，绳子对谁都抽。</para>
    ///
    /// <para><b>绳子不是墙</b>：没有任何碰撞体，玩家不用「跳过碰撞」而是「跳过判定」——
    /// 贴着绳子擦过去不会掉血，受击箱压到才会。</para>
    ///
    /// <para><b>为什么要扫掠</b>：绳子转得快时，两个物理帧之间的绳身会跳过一段距离。
    /// 每帧除当前曲线外，还对**上一帧采样点到本帧采样点**的轨迹做一次胶囊查询，
    /// 高速甩过的绳子不会从人身上「穿」过去。</para>
    ///
    /// <para><b>视觉</b>：沿曲线摆一串细长方块（段），不依赖 LineRenderer / 材质资产 ——
    /// 白盒方块在 URP 2D 下必定可见。绳子的「近 / 远」通过颜色明暗表达：
    /// 旋转到面前的一半更亮、转到背后的一半更暗，于是正转 / 反转也看得出来。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑱ 危险跳绳（旋转曲线陷阱）</c> 生成
    /// <c>Prefabs/SwingRope.prefab</c>，关卡里直接拖。改根物体的旋转可以倾斜整根绳。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class SwingRope : MonoBehaviour
    {
        /// <summary>一次重叠查询最多拿到的碰撞体数。</summary>
        private const int MaxHits = 16;

        /// <summary>段的视觉长度重叠量（米），避免曲率处出现细缝。</summary>
        private const float SegmentOverlap = 0.01f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("形状")]
        [Tooltip("两个端点之间的距离（米）。局部单位，随根物体缩放。")]
        [SerializeField, Min(0.1f)] private float _span = 6f;

        [Tooltip("曲线中点离中轴（两端点连线）的距离，也就是「垂度」。0 = 一条直线。")]
        [SerializeField, Min(0f)] private float _sag = 2f;

        [Tooltip("绳子的视觉粗细（米）。")]
        [SerializeField, Min(0.01f)] private float _thickness = 0.09f;

        [Header("旋转")]
        [Tooltip("跳绳的转速（度 / 秒）。负值 = 反向；视觉上靠近 / 远明暗区分。")]
        [SerializeField] private float _spinSpeed = 180f;

        [Tooltip("起始相位（度）。0 = 绳子垂在下方；180 = 拱在上方；90 = 拉成直线。")]
        [SerializeField] private float _startPhase = 0f;

        [Header("伤害（与尖刺同一条路）")]
        [Tooltip("基础伤害。0 = 只击退不掉血。")]
        [SerializeField, Min(0f)] private float _damage = 10f;

        [Tooltip("击退强度。0 = 不击退。")]
        [SerializeField, Min(0f)] private float _knockback = 6f;

        [Tooltip("同一个目标两次受伤的最小间隔（秒）。目标自带无敌帧时以无敌帧为准。")]
        [SerializeField, Min(0f)] private float _repeatInterval = 0.5f;

        [Tooltip("判定半径（世界单位，从绳子中心线往外量）。比视觉粗细大一点，留出手感余量。")]
        [SerializeField, Min(0.01f)] private float _hitRadius = 0.18f;

        [Tooltip("伤害出口。留空则在运行时自动找场景里的 CombatSystem。")]
        [SerializeField] private CombatSystem _combat;

        [Header("视觉")]
        [Tooltip("段用的精灵。留空则运行时用 1×1 白纹理自动生成。")]
        [SerializeField] private Sprite _segmentSprite;

        [Tooltip("绳子的基础颜色。")]
        [SerializeField] private Color _color = new Color(0.17f, 0.14f, 0.12f, 1f);

        [Tooltip("转到背面时的亮度比例。1 = 不做远近明暗；0.5 = 背面的段暗一半。")]
        [SerializeField, Range(0.1f, 1f)] private float _farShade = 0.55f;

        [Tooltip("沿曲线切几段。段数越多越圆滑，判定查询也越多。")]
        [SerializeField, Range(4, 48)] private int _segmentCount = 16;

        [Tooltip("段所在排序层内的顺序。建议略高于角色，绳子挡在人前面。")]
        [SerializeField] private int _sortingOrder = 4;

        [Header("端点装饰（可选）")]
        [Tooltip("左端点。只做视觉，位置由「两端点距离」自动摆。")]
        [SerializeField] private Transform _anchorA;

        [Tooltip("右端点。只做视觉，位置由「两端点距离」自动摆。")]
        [SerializeField] private Transform _anchorB;

        // ==================================================================
        //  运行时
        // ==================================================================

        private readonly List<SpriteRenderer> _segments = new List<SpriteRenderer>();
        private readonly Collider2D[] _hits = new Collider2D[MaxHits];
        private readonly HashSet<IDamageable> _hitThisScan = new HashSet<IDamageable>();
        private readonly Dictionary<IDamageable, float> _nextHit = new Dictionary<IDamageable, float>();
        private readonly List<IDamageable> _stale = new List<IDamageable>();

        private Transform _impulse;
        private Vector3[] _current = new Vector3[0];
        private Vector3[] _previous = new Vector3[0];
        private bool _hasPrevious;
        private float _phase;
        private Sprite _fallbackSprite;
        private bool _warnedNoCombat;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>当前相位（度）。0 = 垂下、180 = 上拱。</summary>
        public float Phase => _phase;

        /// <summary>当前采样到的世界坐标点（段数 + 1 个）。</summary>
        public Vector3[] CurrentPoints => _current;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            // 只在结算的那一瞬间用来表达「伤害从绳子的哪一点发出」。
            var marker = new GameObject("SwingRopeImpulse");
            marker.transform.SetParent(transform, false);
            _impulse = marker.transform;

            EnsureSegments();
            UpdateVisual();
        }

        private void OnEnable()
        {
            _phase = _startPhase;
            _hasPrevious = false;

            EnsureSegments();
            UpdateVisual();
        }

        private void OnDisable()
        {
            _nextHit.Clear();
            _hasPrevious = false;
        }

        private void OnDestroy()
        {
            if (_fallbackSprite == null) return;

            if (Application.isPlaying) Destroy(_fallbackSprite);
            else DestroyImmediate(_fallbackSprite);
            _fallbackSprite = null;
        }

        private void FixedUpdate()
        {
            // 暂停：物理已停步进，绳子的旋转与判定也必须停（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            _phase = Mathf.Repeat(_phase + _spinSpeed * dt, 360f);

            UpdateVisual();
            Scan();
        }

        // ==================================================================
        //  几何（纯函数，编辑器自检直接调）
        // ==================================================================

        /// <summary>
        /// 曲线上的点（**局部坐标**）。<paramref name="t"/> 从 0（左端）到 1（右端），
        /// <paramref name="cosPhase"/> 是当前相位的余弦 —— 垂度按它伸缩，负值就拱到中轴上方。
        /// </summary>
        public static Vector3 PointOnCurve(float t, float span, float sag, float cosPhase)
        {
            float shape = 4f * t * (1f - t);          // 两端 0、中点 1 的抛物线
            float x = -span * 0.5f + span * t;
            return new Vector3(x, -sag * shape * cosPhase, 0f);
        }

        // ==================================================================
        //  判定
        // ==================================================================

        private void Scan()
        {
            int n = _segments.Count;
            if (n < 2) return;

            CombatSystem combat = ResolveCombat();
            if (combat == null) return;

            SampleCurve(n);

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);

            PruneCooldowns();
            _hitThisScan.Clear();

            // 当前曲线：段本身是连续的，胶囊首尾相接，没有缝。
            for (int i = 0; i < n; i++)
            {
                Vector2 a = _current[i];
                Vector2 b = _current[i + 1];
                ScanCapsule(combat, filter, a, b, (a + b) * 0.5f);
            }

            // 运动扫掠：绳子转得快时，上一帧到本帧之间的轨迹也要算。
            // 位移小于判定半径一半时扫描没有意义，直接跳过（大多数转速下省掉一半查询）。
            if (_hasPrevious)
            {
                float sweep = 0f;
                for (int i = 0; i <= n; i++)
                {
                    sweep = Mathf.Max(sweep, Vector3.Distance(_previous[i], _current[i]));
                }

                if (sweep > _hitRadius * 0.5f)
                {
                    for (int i = 0; i <= n; i++)
                    {
                        Vector2 a = _previous[i];
                        Vector2 b = _current[i];
                        if ((a - b).sqrMagnitude <= 0.0001f) continue;
                        ScanCapsule(combat, filter, a, b, (a + b) * 0.5f);
                    }
                }
            }

            System.Array.Copy(_current, _previous, _current.Length);
            _hasPrevious = true;
        }

        private void ScanCapsule(CombatSystem combat, ContactFilter2D filter,
                                 Vector2 a, Vector2 b, Vector2 mid)
        {
            // 2D 没有「两点 + 半径」的胶囊查询，用的是「中心 + 包围盒尺寸 + 朝向 + 角度」。
            // 水平胶囊的 size.x 是整个包围盒长度（线段长 + 两端半圆），size.y 是直径。
            int count;
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length <= 0.0001f)
            {
                count = Physics2D.OverlapCircle(a, _hitRadius, filter, _hits);
            }
            else
            {
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                var size = new Vector2(length + _hitRadius * 2f, _hitRadius * 2f);
                count = Physics2D.OverlapCapsule(
                    mid, size, CapsuleDirection2D.Horizontal, angle, filter, _hits);
            }

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                // 只认受击箱（身体碰撞体不算），与尖刺 / 挥砍同一套几何。
                IDamageable target = Hurtbox.ResolveOwner(hit);
                if (target == null) continue;

                // 一次扫描里，一个目标只结算一次（多段同时压到不重复打）。
                if (!_hitThisScan.Add(target)) continue;

                Apply(combat, target, mid);
            }
        }

        private void Apply(CombatSystem combat, IDamageable target, Vector2 point)
        {
            if (_nextHit.TryGetValue(target, out float readyAt) && UnityEngine.Time.time < readyAt) return;

            // 击退方向由承受方按「施加方位置」推 —— 所以把标记挪到命中的那一点。
            _impulse.position = point;
            int applied = combat.ApplyDamage(_impulse, target, _damage, _knockback);

            if (applied > 0 && _repeatInterval > 0f)
                _nextHit[target] = UnityEngine.Time.time + _repeatInterval;
        }

        private void SampleCurve(int n)
        {
            int points = n + 1;
            if (_current.Length != points) _current = new Vector3[points];
            if (_previous.Length != points)
            {
                _previous = new Vector3[points];
                _hasPrevious = false;
            }

            float cos = Mathf.Cos(_phase * Mathf.Deg2Rad);
            for (int i = 0; i <= n; i++)
            {
                _current[i] = transform.TransformPoint(PointOnCurve(i / (float)n, _span, _sag, cos));
            }
        }

        private CombatSystem ResolveCombat()
        {
            if (_combat != null) return _combat;

            _combat = FindAnyObjectByType<CombatSystem>();
            if (_combat == null && !_warnedNoCombat)
            {
                _warnedNoCombat = true;
                Debug.LogWarning("[SwingRope] 场景里没有 CombatSystem，危险跳绳打不出伤害。", this);
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
        //  视觉
        // ==================================================================

        private void EnsureSegments()
        {
            int wanted = Mathf.Clamp(_segmentCount, 4, 48);

            bool valid = _segments.Count == wanted;
            if (valid)
            {
                for (int i = 0; i < _segments.Count; i++)
                {
                    if (_segments[i] != null) continue;
                    valid = false;
                    break;
                }
            }
            if (valid) return;

            ClearSegments();

            Sprite sprite = ResolveSegmentSprite();
            if (sprite == null) return;

            for (int i = 0; i < wanted; i++)
            {
                var go = new GameObject("RopeSegment_" + i.ToString("00"));
                go.transform.SetParent(transform, false);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = _color;
                renderer.sortingOrder = _sortingOrder;
                _segments.Add(renderer);
            }
        }

        private void ClearSegments()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                if (_segments[i] == null) continue;
                Destroy(_segments[i].gameObject);
            }
            _segments.Clear();
        }

        /// <summary>
        /// 摆段：每段是一块拉长的方块，位置与朝向按曲线切线走。
        /// 同时把「近 / 远」写进颜色 —— 转到背面的部分更暗。
        /// </summary>
        private void UpdateVisual()
        {
            EnsureSegments();

            int n = _segments.Count;
            if (n < 2) return;

            float phaseRad = _phase * Mathf.Deg2Rad;
            float cos = Mathf.Cos(phaseRad);
            float sin = Mathf.Sin(phaseRad);

            Color dim = new Color(_color.r * _farShade, _color.g * _farShade, _color.b * _farShade, _color.a);

            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n;
                float t1 = (i + 1) / (float)n;

                Vector3 p0 = PointOnCurve(t0, _span, _sag, cos);
                Vector3 p1 = PointOnCurve(t1, _span, _sag, cos);
                Vector3 delta = p1 - p0;

                SpriteRenderer segment = _segments[i];
                segment.transform.localPosition = (p0 + p1) * 0.5f;
                segment.transform.localRotation =
                    Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                segment.transform.localScale = new Vector3(delta.magnitude + SegmentOverlap, _thickness, 1f);

                // 中点处沿「深度方向」的位移：负值 = 转到背面。
                float shape = 4f * (t0 + t1) * 0.5f * (1f - (t0 + t1) * 0.5f);
                float far = Mathf.Clamp01(-shape * sin);
                segment.color = Color.Lerp(_color, dim, far);
            }

            UpdateAnchors();
        }

        private void UpdateAnchors()
        {
            float half = _span * 0.5f;
            if (_anchorA != null) _anchorA.localPosition = new Vector3(-half, 0f, 0f);
            if (_anchorB != null) _anchorB.localPosition = new Vector3(half, 0f, 0f);
        }

        private Sprite ResolveSegmentSprite()
        {
            if (_segmentSprite != null) return _segmentSprite;

            if (_fallbackSprite == null)
            {
                Texture2D texture = Texture2D.whiteTexture;
                _fallbackSprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    1f);
                _fallbackSprite.name = "SwingRopeSegment";
            }
            return _fallbackSprite;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给生成工具用：接上端点装饰与段用的精灵。</summary>
        public void Configure(Transform anchorA, Transform anchorB, Sprite segmentSprite)
        {
            _anchorA = anchorA;
            _anchorB = anchorB;
            if (segmentSprite != null) _segmentSprite = segmentSprite;

            UpdateAnchors();
        }

        private void OnDrawGizmos()
        {
            float cos = Mathf.Cos((Application.isPlaying ? _phase : _startPhase) * Mathf.Deg2Rad);

            const int samples = 32;
            Gizmos.color = new Color(0.95f, 0.35f, 0.2f, 0.9f);
            Vector3 previous = transform.TransformPoint(PointOnCurve(0f, _span, _sag, cos));
            for (int i = 1; i <= samples; i++)
            {
                Vector3 next = transform.TransformPoint(PointOnCurve(i / (float)samples, _span, _sag, cos));
                Gizmos.DrawLine(previous, next);
                previous = next;
            }

            // 两个端点。
            Gizmos.color = new Color(1f, 0.82f, 0.3f, 1f);
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(-_span * 0.5f, 0f, 0f)), 0.16f);
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(_span * 0.5f, 0f, 0f)), 0.16f);
        }

        private void OnDrawGizmosSelected()
        {
            // 判定带：绳子中心线上下各 _hitRadius 之间的区域都会打中受击箱。
            float cos = Mathf.Cos((Application.isPlaying ? _phase : _startPhase) * Mathf.Deg2Rad);
            Vector3 up = transform.up;

            const int samples = 16;
            Gizmos.color = new Color(1f, 0.25f, 0.2f, 0.45f);

            Vector3 first = transform.TransformPoint(PointOnCurve(0f, _span, _sag, cos));
            Vector3 previousA = first + up * _hitRadius;
            Vector3 previousB = first - up * _hitRadius;

            for (int i = 1; i <= samples; i++)
            {
                Vector3 point = transform.TransformPoint(PointOnCurve(i / (float)samples, _span, _sag, cos));
                Vector3 a = point + up * _hitRadius;
                Vector3 b = point - up * _hitRadius;
                Gizmos.DrawLine(previousA, a);
                Gizmos.DrawLine(previousB, b);
                previousA = a;
                previousB = b;
            }
        }
    }
}
