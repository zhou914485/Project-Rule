using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 危险瓦片场：挂在**逻辑 Tilemap** 上，让画在里面的「危险格」对碰到的实体造成伤害。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用触发器回调</b>：玩家是 Kinematic 刚体 + <c>Rigidbody2D.Slide</c> 解算位移，
    /// 默认不与静态碰撞体产生接触 / 触发回调（要开 <c>useFullKinematicContacts</c> 才有）。
    /// 这里改用**显式重叠查询**：与刚体类型无关，也不要求刺格带碰撞体。</para>
    ///
    /// <para><b>危险格为什么不带碰撞体</b>：刺是「碰到就受伤」，不是墙。
    /// 瓦片资产的 <c>Collider Type</c> 设成 <c>None</c>，玩家不会被挡住，只被判定。</para>
    ///
    /// <para><b>判定的是受击箱</b>（<c>Hurtbox</c> 层）：身体碰撞体压到刺上不算数，
    /// 和挥砍 / 弹体用的是同一套几何 —— 受击箱调小，能擦着刺边走过去。</para>
    ///
    /// <para><b>伤害走唯一出口</b>：<see cref="CombatSystem.ApplyDamage"/> ——
    /// 伤害缩放 / 反转 / 因果 / 挂账都由 <c>DamageProcessor</c> 处理，这里只传**基础伤害**。
    /// 不要自己算最终值（会绕过伤害规则）。</para>
    ///
    /// <para><b>击退方向</b>：<c>EntityDamageable</c> 按「施加方位置」推方向，
    /// 所以每次结算前把 <see cref="_impulse"/> 挪到命中的那一格 —— 方向才是局部正确的。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Tilemap))]
    public sealed class HazardField : MonoBehaviour
    {
        /// <summary>一次重叠查询最多拿到的碰撞体数。</summary>
        private const int MaxHits = 16;

        // ==================================================================
        //  配置
        // ==================================================================

        /// <summary>一种危险瓦片的定义。同一张 Tilemap 可以混多种危险。</summary>
        [System.Serializable]
        public struct Hazard
        {
            [Tooltip("危险瓦片资产（Assets > Create > 2D > Tiles > Tile）。")]
            public TileBase Tile;

            [Tooltip("基础伤害。0 = 只击退不掉血。")]
            [Min(0f)] public float Damage;

            [Tooltip("击退强度。0 = 不击退。")]
            [Min(0f)] public float Knockback;

            [Tooltip("自带美术：瓦片本身就是要显示的画面（齿轮这类），铺装器不会在上面盖区域套件的刺。")]
            public bool SelfArt;
        }

        [Header("危险瓦片")]
        [Tooltip("哪些瓦片算危险，以及各自的伤害 / 击退。")]
        [SerializeField] private Hazard[] _hazards = new Hazard[0];

        [Header("节奏")]
        [Tooltip("扫描间隔（秒）。越小越不容易漏掉高速穿过的那一帧。")]
        [SerializeField, Min(0.01f)] private float _scanInterval = 0.05f;

        [Tooltip("同一个目标两次受伤的最小间隔（秒）。目标自带无敌帧时以无敌帧为准。")]
        [SerializeField, Min(0f)] private float _repeatInterval = 0.5f;

        [Header("引用")]
        [Tooltip("伤害出口。留空则在运行时自动找场景里的 CombatSystem。")]
        [SerializeField] private CombatSystem _combat;

        // ==================================================================
        //  运行时
        // ==================================================================

        private readonly Collider2D[] _hits = new Collider2D[MaxHits];
        private readonly HashSet<IDamageable> _hitThisScan = new HashSet<IDamageable>();
        private readonly Dictionary<IDamageable, float> _nextHit = new Dictionary<IDamageable, float>();
        private readonly List<IDamageable> _stale = new List<IDamageable>();

        private Tilemap _tilemap;
        private Transform _impulse;
        private float _nextScan;
        private bool _warnedNoCombat;

        private void Awake()
        {
            _tilemap = GetComponent<Tilemap>();

            // 只在结算的那一瞬间用来表达「伤害从哪一格发出」。
            var marker = new GameObject("HazardImpulse");
            marker.transform.SetParent(transform, false);
            _impulse = marker.transform;
        }

        private void OnDisable() => _nextHit.Clear();

        private void FixedUpdate()
        {
            if (_hazards == null || _hazards.Length == 0) return;

            // 暂停：物理已停步进，判定也必须停（单一真相是 TimeManager）。
            if (TimeManager.Instance?.IsPaused == true) return;

            _nextScan -= UnityEngine.Time.fixedDeltaTime;
            if (_nextScan > 0f) return;
            _nextScan = _scanInterval;

            CombatSystem combat = ResolveCombat();
            if (combat == null) return;

            Bounds bounds = FieldBounds();
            if (bounds.size.x <= 0f || bounds.size.y <= 0f) return;

            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);
            int count = Physics2D.OverlapBox(bounds.center, bounds.size, 0f, filter, _hits);
            if (count <= 0) return;

            PruneCooldowns();

            _hitThisScan.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                // 只认受击箱（身体碰撞体不算）。⚠️ 这里**不套 CombatSystem 的阵营准入** ——
                // 刺格对谁都扎：玩家、敌人、中立道具一视同仁。
                IDamageable target = Hurtbox.ResolveOwner(hit);
                if (target == null) continue;

                if (!TryFindHazard(hit.bounds, out Hazard hazard, out Vector3Int cell)) continue;

                // 一次扫描里，一个目标只结算一次（多碰撞体不重复打）。
                if (!_hitThisScan.Add(target)) continue;

                Apply(combat, target, hazard, cell);
            }
        }

        /// <summary>
        /// 这一格是不是危险瓦片。铺装器用它跳过刺格 —— 不给刺铺墙皮，也不把刺当成实心几何。
        /// </summary>
        public bool IsHazardCell(Vector3Int cell)
        {
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();
            if (_tilemap == null || _hazards == null) return false;
            return TryMatch(_tilemap.GetTile(cell), out _);
        }

        /// <summary>
        /// 这一格的危险瓦片是否**自带美术**（齿轮这类）。铺装器对这类格子不铺刺 ——
        /// 盖上去会挡住瓦片自己的贴图。
        /// </summary>
        public bool IsSelfArtCell(Vector3Int cell)
        {
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();
            if (_tilemap == null || _hazards == null) return false;
            return TryMatch(_tilemap.GetTile(cell), out Hazard hazard) && hazard.SelfArt;
        }

        // ==================================================================
        //  内部
        // ==================================================================

        private void Apply(CombatSystem combat, IDamageable target, Hazard hazard, Vector3Int cell)
        {
            if (_nextHit.TryGetValue(target, out float readyAt) && UnityEngine.Time.time < readyAt) return;

            _impulse.position = _tilemap.GetCellCenterWorld(cell);
            int applied = combat.ApplyDamage(_impulse, target, hazard.Damage, hazard.Knockback);

            if (applied > 0 && _repeatInterval > 0f)
                _nextHit[target] = UnityEngine.Time.time + _repeatInterval;
        }

        private bool TryFindHazard(Bounds bounds, out Hazard hazard, out Vector3Int cell)
        {
            hazard = default;
            cell = default;
            Vector3Int min = _tilemap.WorldToCell(bounds.min);
            Vector3Int max = _tilemap.WorldToCell(bounds.max);

            // 夹到 tilemap 自己的范围：危险格只可能存在于这张图里，
            // 顺便挡住「超大触发器碰撞体」把循环撑成整张地图。
            BoundsInt cells = _tilemap.cellBounds;
            min = new Vector3Int(Mathf.Max(min.x, cells.xMin), Mathf.Max(min.y, cells.yMin), 0);
            max = new Vector3Int(Mathf.Min(max.x, cells.xMax), Mathf.Min(max.y, cells.yMax), 0);
            if (min.x > max.x || min.y > max.y) return false;

            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (!TryMatch(_tilemap.GetTile(c), out hazard)) continue;
                    cell = c;
                    return true;
                }
            }

            cell = default;
            return false;
        }

        private bool TryMatch(TileBase tile, out Hazard hazard)
        {
            if (tile != null)
            {
                for (int i = 0; i < _hazards.Length; i++)
                {
                    if (_hazards[i].Tile != tile) continue;
                    hazard = _hazards[i];
                    return true;
                }
            }

            hazard = default;
            return false;
        }

        private Bounds FieldBounds()
        {
            BoundsInt cells = _tilemap.cellBounds;
            Vector3 min = _tilemap.CellToWorld(cells.min);
            Vector3 max = _tilemap.CellToWorld(cells.max);
            return new Bounds((min + max) * 0.5f,
                              new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), 0f));
        }

        private CombatSystem ResolveCombat()
        {
            if (_combat != null) return _combat;

            _combat = FindAnyObjectByType<CombatSystem>();
            if (_combat == null && !_warnedNoCombat)
            {
                _warnedNoCombat = true;
                Debug.LogWarning("[HazardField] 场景里没有 CombatSystem，危险瓦片打不出伤害。", this);
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
    }
}
