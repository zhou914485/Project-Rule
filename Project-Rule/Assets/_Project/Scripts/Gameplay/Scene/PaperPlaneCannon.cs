using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Persistence;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 纸飞机炮筒：每隔一段时间从炮口射出一架 <see cref="ThrownKnife"/>
    /// （默认预制体 <c>Assets/_Project/Prefabs/PaperPlaneKnife.prefab</c>），速度可在 Inspector 里调。
    /// </summary>
    /// <remarks>
    /// <para><b>速度既是飞行速度，也是伤害</b>：<c>ThrownKnife</c> 按 5.2 公式
    /// <c>D_raw = 5 × 质量 × 速度档位</c> 现算伤害，默认档位是 8 / 12 / 16 / 20。
    /// 所以把 <see cref="_speed"/> 从 16 调到 20，纸飞机飞得更快、伤害也从 2 档变 3 档；
    /// 调到 8 以下就是 0 档（只推不伤）。</para>
    ///
    /// <para><b>一直发射</b>：<see cref="_interval"/> 秒一发；<see cref="_maxAlive"/> 封顶，
    /// 到上限就跳过这一发（下一发继续尝试），防止刷屏。停火用 <see cref="_firing"/> 或
    /// Inspector 的「停火」按钮；<see cref="FireOnce"/> 可以手动补一发。</para>
    ///
    /// <para><b>炮口与方向</b>：出生点 = <see cref="_muzzle"/> 的位置，方向 = 它的 +X
    /// （生成工具会放一个红色小方块）。旋转炮筒（或 Muzzle 子物体）就能瞄准；没有炮口时
    /// 退回「根物体位置 + <see cref="_muzzleOffset"/>」和根物体的 +X。</para>
    ///
    /// <para><b>归属 / 阵营</b>：实现 <see cref="IFactionMember"/>，默认 <c>Enemy</c> ——
    /// 纸飞机只打**不同阵营**的受击箱（也就是只打玩家），不会误伤同类。
    /// 炮筒本身没有 <c>IDamageable</c>，打不坏；阵营只用于筛投射物的目标。
    /// ⚠️ 设成 <c>Player</c> 会变成玩家阵营的炮（只打敌人），但炮筒的碰撞体会被
    /// <c>PlayerLookup</c> 当成玩家 —— 别把它压在玩家专属开关上。</para>
    ///
    /// <para><b>谁打的</b>：<c>Launch(this, ...)</c> 把炮筒当投掷者，所以 <c>ThrownKnife</c>
    /// 会跳过炮筒自己的所有碰撞体 —— 纸飞机不会一出膛就撞在炮筒上。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时 <c>TimeManager.IsPaused</c> 早退，不发射、不推进计时；
    /// 时间变慢时 <c>Time.fixedDeltaTime</c> 变小，发射间隔跟着变慢（与其余机关一致）。</para>
    ///
    /// <para>⚠️ 原型：不做对象池、不接世界快照；每次发射 <c>Instantiate</c>，
    /// 由 <c>ThrownKnife</c> 的寿命（预制体上 6 秒）自己消失。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PaperPlaneCannon : MonoBehaviour, IFactionMember, ISnapshotParticipant
    {
        /// <summary>发射间隔下限，避免一帧连发几十架。</summary>
        private const float MinInterval = 0.05f;

        /// <summary>Scene 视图里瞄准线的长度。</summary>
        private const float GizmoLength = 3f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("发射物")]
        [Tooltip("拖 Assets/_Project/Prefabs/PaperPlaneKnife.prefab。\n" +
                 "生成工具会自动接上；留空不会发射，并在 Console 报错。")]
        [SerializeField] private ThrownKnife _projectilePrefab;

        [Tooltip("伤害出口。留空 = 运行时自动找场景里的 CombatSystem。")]
        [SerializeField] private CombatSystem _combat;

        [Header("炮口与方向")]
        [Tooltip("炮口：出生点 = 它的位置，方向 = 它的 +X（红色小方块）。\n" +
                 "旋转炮筒（或这个子物体）就能瞄准；留空 = 根物体 +X。")]
        [SerializeField] private Transform _muzzle;

        [Tooltip("没有指定炮口时，从根物体沿发射方向偏移多少作为出生点（世界单位）。")]
        [SerializeField, Min(0f)] private float _muzzleOffset = 0.8f;

        [Tooltip("随机散布角（度，发射方向左右各一半）。0 = 直线。")]
        [SerializeField, Range(0f, 180f)] private float _spreadDegrees = 0f;

        [Header("发射")]
        [Tooltip("纸飞机出手速度（米/秒）。\n" +
                 "⚠️ 它同时决定 5.2 伤害档位：≤8→0、≤12→1、≤16→2、≤20→3（默认表），\n" +
                 "原始伤害 = 5 × 质量 × 档位；再快仍按 3 档。")]
        [SerializeField, Min(0f)] private float _speed = 16f;

        [Tooltip("命中击退。玩家投掷默认 4。")]
        [SerializeField, Min(0f)] private float _knockback = 4f;

        [Tooltip("每隔多久发一架（秒）。0.05 以下按 0.05 算。")]
        [SerializeField, Min(MinInterval)] private float _interval = 1f;

        [Tooltip("进入 Play / 重新启用后，第一发等多久（秒）。")]
        [SerializeField, Min(0f)] private float _startDelay = 0.5f;

        [Tooltip("最多同时存在几架纸飞机。到上限就跳过这一发（下一发继续尝试），防止刷屏。")]
        [SerializeField, Min(1)] private int _maxAlive = 24;

        [Header("阵营")]
        [Tooltip("纸飞机只打**不同阵营**的受击箱：\n" +
                 "· Enemy（默认）= 只打玩家\n" +
                 "· Player = 只打敌人（玩家阵营炮）\n" +
                 "炮筒本身没有 IDamageable，打不坏；阵营只用来筛纸飞机的目标。\n" +
                 "⚠️ 设成 Player 后，炮筒的碰撞体会被 PlayerLookup 当成玩家 —— 别让它压到玩家专属开关上。")]
        [SerializeField] private Faction _faction = Faction.Enemy;

        [Header("调试")]
        [Tooltip("取消 = 停火：炮筒还在，但不自动发射（在飞的纸飞机继续飞）。")]
        [SerializeField] private bool _firing = true;

        [Tooltip("每次发射打一行 Log。")]
        [SerializeField] private bool _logSpawn = false;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>本炮筒发射出去、还在飞的纸飞机。</summary>
        private readonly List<ThrownKnife> _alive = new List<ThrownKnife>();

        /// <summary>距离下一发还有多久（秒）。</summary>
        private float _nextFire;

        private bool _warnedPrefab;
        private bool _warnedCombat;

        // ==================================================================
        //  只读状态（调试用）
        // ==================================================================

        /// <summary>纸飞机只打不同阵营；默认 Enemy = 只打玩家。</summary>
        public Faction Faction => _faction;

        /// <summary>出手速度（米/秒）。同时决定 5.2 伤害档位。</summary>
        public float Speed => _speed;

        /// <summary>发射间隔（秒）。</summary>
        public float Interval => _interval;

        /// <summary>同时存在的上限。</summary>
        public int MaxAlive => _maxAlive;

        /// <summary>是否自动开火。</summary>
        public bool IsFiring => _firing;

        /// <summary>纸飞机预制体（Inspector 显示用）。</summary>
        public ThrownKnife ProjectilePrefab => _projectilePrefab;

        /// <summary>有没有找到伤害出口。</summary>
        public bool HasCombat => _combat != null;

        /// <summary>现在还在飞的纸飞机数量（会顺手清掉已销毁的引用）。</summary>
        public int AliveCount
        {
            get
            {
                PruneAlive();
                return _alive.Count;
            }
        }

        /// <summary>距离下一发还有多久（秒）。停火时没有意义。</summary>
        public float NextFireIn => Mathf.Max(0f, _nextFire);

        /// <summary>按预制体默认档位表算出来的速度档位（0~3）。</summary>
        public int DefaultSpeedScale => ThrownKnife.SpeedScaleFor(_speed);

        /// <summary>预制体上的基础质量（伤害公式里的「质量」）。</summary>
        public float ProjectileBaseMass
        {
            get
            {
                if (_projectilePrefab == null) return 0f;

                Rigidbody2D body = _projectilePrefab.GetComponent<Rigidbody2D>();
                return body != null ? body.mass : 0f;
            }
        }

        /// <summary>发射方向（炮口的 +X；没有炮口时是根物体的 +X）。</summary>
        public Vector2 AimDirection
        {
            get
            {
                Vector2 direction = _muzzle != null ? (Vector2)_muzzle.right : (Vector2)transform.right;
                return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            }
        }

        /// <summary>炮口位置（世界坐标）。</summary>
        public Vector2 MuzzlePosition =>
            _muzzle != null
                ? (Vector2)_muzzle.position
                : (Vector2)transform.position + AimDirection * _muzzleOffset;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private struct CannonSnapshot
        {
            public bool Firing;
            public float NextFire;
        }

        /// <inheritdoc />
        public bool DiscardWhenMissing => false;

        /// <inheritdoc />
        public object CaptureSnapshotState() => new CannonSnapshot
        {
            Firing = _firing,
            NextFire = _nextFire,
        };

        /// <inheritdoc />
        public void RestoreSnapshotState(object snapshot)
        {
            if (!(snapshot is CannonSnapshot state)) return;
            _firing = state.Firing;
            _nextFire = state.NextFire;
        }

        private void Awake()
        {
            if (Application.isPlaying) SnapshotParticipantRegistry.Register(this);
            ResolveCombat();
            _nextFire = _startDelay;

            if (_projectilePrefab == null) WarnMissingPrefab();
        }

        private void OnDestroy()
        {
            SnapshotParticipantRegistry.Unregister(this);
        }

        private void OnEnable()
        {
            // 重开一局 / 对象池取出 / 分区重新加载：重新等一个起手延迟。
            _nextFire = _startDelay;
            PruneAlive();
            ResolveCombat();
        }

        private void OnDisable()
        {
            // 炮筒关了，在飞的纸飞机不归它管了 —— 退订并清空账本，避免悬挂引用。
            for (int i = 0; i < _alive.Count; i++)
            {
                ThrownKnife knife = _alive[i];
                if (knife != null) knife.OnSpent -= HandleKnifeSpent;
            }

            _alive.Clear();
        }

        private void Start()
        {
            if (!Application.isPlaying) return;
            if (WorldStateStore.TryGet(SaveStateKey, out string saved)) _firing = saved == "1";
        }

        private void FixedUpdate()
        {
            // 暂停：物理停步进、规则时钟停；发射计时也冻结。
            if (TimeManager.Instance?.IsPaused == true) return;

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            _nextFire -= dt;
            if (!_firing || _nextFire > 0f) return;

            SpawnOne();
            _nextFire = Mathf.Max(MinInterval, _interval);
        }

        // ==================================================================
        //  发射
        // ==================================================================

        /// <summary>
        /// 手动补一发（忽略间隔与停火，但仍受预制体 / 上限 / 暂停约束）。
        /// </summary>
        /// <remarks>编辑模式不会真的发射 —— 那会在场景里留下不会飞的纸飞机。</remarks>
        public void FireOnce()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[PaperPlaneCannon] 编辑模式不会真的发射；进 Play 再用。", this);
                return;
            }

            SpawnOne();
        }

        /// <summary>开火 / 停火。重新开火时下一物理帧立刻来一发。</summary>
        public void SetFiring(bool firing)
        {
            _firing = firing;
            WorldStateStore.SetBool(SaveStateKey, firing);
            if (firing) _nextFire = 0f;
        }

        /// <summary>
        /// 射出一架。没预制体 / 到上限 / 暂停 / 组件未启用时什么都不做。
        /// </summary>
        private void SpawnOne()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            if (_projectilePrefab == null)
            {
                WarnMissingPrefab();
                return;
            }

            PruneAlive();
            if (_alive.Count >= Mathf.Max(1, _maxAlive)) return;

            ResolveCombat();
            if (_combat == null) WarnMissingCombat();

            Vector2 direction = AimDirection;
            if (_spreadDegrees > 0.001f)
            {
                float angle = Random.Range(-_spreadDegrees * 0.5f, _spreadDegrees * 0.5f);
                Vector3 rotated = Quaternion.Euler(0f, 0f, angle) * (Vector3)direction;
                direction = new Vector2(rotated.x, rotated.y).normalized;
            }

            Vector2 origin = _muzzle != null
                ? (Vector2)_muzzle.position
                : (Vector2)transform.position + direction * _muzzleOffset;

            ThrownKnife knife = Instantiate(_projectilePrefab, (Vector3)origin, Quaternion.identity);
            knife.Launch(this, _combat, direction, _speed, _knockback);
            knife.OnSpent += HandleKnifeSpent;
            _alive.Add(knife);

            if (_logSpawn)
            {
                Debug.Log($"[PaperPlaneCannon] 发射：速度 {_speed:0.##}，方向 {direction}，位置 {origin}。", this);
            }
        }

        private void ResolveCombat()
        {
            if (_combat != null) return;

            _combat = FindAnyObjectByType<CombatSystem>();
        }

        private void WarnMissingPrefab()
        {
            if (_warnedPrefab) return;

            _warnedPrefab = true;
            Debug.LogError("[PaperPlaneCannon] 没有指定纸飞机预制体 —— 炮筒不会发射。" +
                           "把 Assets/_Project/Prefabs/PaperPlaneKnife.prefab 拖到「发射物」里。", this);
        }

        private void WarnMissingCombat()
        {
            if (_warnedCombat) return;

            _warnedCombat = true;
            Debug.LogWarning("[PaperPlaneCannon] 场景里没有 CombatSystem —— 纸飞机会飞，但不会造成伤害。" +
                             "把 CombatSystem 拖到「发射物 / 伤害出口」里，或确保它存在于场景中。", this);
        }

        // ==================================================================
        //  在飞账本
        // ==================================================================

        private void HandleKnifeSpent(ThrownKnife knife)
        {
            knife.OnSpent -= HandleKnifeSpent;
            _alive.Remove(knife);
        }

        /// <summary>清掉已经被销毁 / 场景卸载带走的纸飞机引用。</summary>
        private void PruneAlive()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                if (_alive[i] == null) _alive.RemoveAt(i);
            }
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：一次接好预制体、炮口、伤害出口与基础参数。</summary>
        public void Configure(ThrownKnife projectile, Transform muzzle, CombatSystem combat,
                              float speed, float interval, float startDelay, int maxAlive)
        {
            _projectilePrefab = projectile;
            _muzzle = muzzle;
            _combat = combat;
            _speed = Mathf.Max(0f, speed);
            _interval = Mathf.Max(MinInterval, interval);
            _startDelay = Mathf.Max(0f, startDelay);
            _maxAlive = Mathf.Max(1, maxAlive);
        }

        private void OnValidate()
        {
            _speed = Mathf.Max(0f, _speed);
            _knockback = Mathf.Max(0f, _knockback);
            _interval = Mathf.Max(MinInterval, _interval);
            _startDelay = Mathf.Max(0f, _startDelay);
            _maxAlive = Mathf.Max(1, _maxAlive);
            _muzzleOffset = Mathf.Max(0f, _muzzleOffset);
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 origin = MuzzlePosition;
            Vector2 direction = AimDirection;
            Vector2 tip = origin + direction * GizmoLength;
            var perp = new Vector2(-direction.y, direction.x);

            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.95f);
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawLine(tip, tip - direction * 0.3f + perp * 0.15f);
            Gizmos.DrawLine(tip, tip - direction * 0.3f - perp * 0.15f);
            Gizmos.DrawWireSphere(origin, 0.12f);
        }

        private string SaveStateKey => WorldStateStore.KeyOf(this, "firing");
    }
}
