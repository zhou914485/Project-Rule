using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// Boss 战控制器：**召唤 Boss、关三扇门、开战、打赢开门**。
    /// </summary>
    /// <remarks>
    /// <para><b>它不认识按钮</b>：实现 <see cref="ISwitchTarget"/>，由 <c>CitySwitch.FollowMotor</c> 拖过来驱动；
    /// 以后想改成「走进房间触发」也只需要多一个入口，不用动 <c>TrapFloor</c>。</para>
    ///
    /// <para><b>Boss 必须场景预摆 + 开局 inactive</b>：运行时 <c>Instantiate</c> 出来的敌人通常要设
    /// <c>RuntimeSpawned = true</c>，而 <c>EnemyActor.DiscardWhenMissing => RuntimeSpawned</c> ——
    /// <c>SnapshotSystem.Restore</c> 第 ⓪ 步会把「名册里有、快照里没有、可弃置」的对象
    /// <c>SetActive(false)</c>，而且**之后再也不会打开**。预摆 inactive 时 <c>Awake</c> 没跑、
    /// <c>RuleAffectedBody.Body == null</c>，快照采集会直接跳过它，等于天然免疫。</para>
    ///
    /// <para><b>门的语义统一</b>：侧门与顶部闸门都是 <c>CityGate</c>（<c>CityPathMotor</c>），
    /// <c>SetOn(false)</c> = 关、<c>SetOn(true)</c> = 开。门的 <c>EndOffset</c> 要摆成「打开方向」。</para>
    ///
    /// <para><b>顶部闸门不能早关</b>：<c>CityPathMotor</c> 的 <c>CarryRiders</c> 会把站在门里的东西带走，
    /// 所以要等玩家越过 <see cref="_arenaTopY"/> 再关，否则把人夹在洞口。</para>
    ///
    /// <para><b>玩家死了什么都不重置</b>：血量 / 位置 / 阶段全保留；复活点在场地内，
    /// 开战瞬间强制激活一次，防止「掉在半路死了，复活到外面」。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BossEncounter : MonoBehaviour, ISwitchTarget
    {
        /// <summary>Boss 战的阶段。</summary>
        public enum Phase
        {
            /// <summary>还没开战。</summary>
            Dormant,
            /// <summary>地板正在塌，等召唤。</summary>
            Collapsing,
            /// <summary>Boss 已出场，战斗进行中。</summary>
            Fighting,
            /// <summary>Boss 已死，门全开。</summary>
            Cleared
        }

        [Header("机关")]
        [Tooltip("开战时先让它塌。留空 = 只召唤，不塌地板。")]
        [SerializeField] private TrapFloor _trapFloor;

        [Header("Boss")]
        [Tooltip("场景里预摆的 Boss。**开局要把它的根物体关掉**，开战时由本组件激活。")]
        [SerializeField] private EnemyActor _boss;

        [Header("封口")]
        [Tooltip("两侧的门（CityGate / CityPathMotor）。开战关、打赢开。\n" +
                 "拖组件或拖 GameObject 都行 —— 会自己 GetComponent 兜底。")]
        [SerializeField] private MonoBehaviour[] _sideDoors = new MonoBehaviour[0];

        [Tooltip("洞口上方的门，与侧门同一套语义（SetOn(false) = 关）。\n" +
                 "⚠️ 等玩家落到场地内才关，否则会把人夹在洞口。")]
        [SerializeField] private MonoBehaviour _topGate;

        [Tooltip("场地顶线（世界 Y）。玩家低于这条线 = 已经在场地内。")]
        [SerializeField] private float _arenaTopY;

        [Header("复活")]
        [Tooltip("场地内的复活点。开战瞬间强制激活一次。")]
        [SerializeField] private PlayerSpawnPoint _arenaSpawn;

        [Header("时机")]
        [Tooltip("塌陷开始后多久召唤 Boss（秒）。≈ 玩家从洞口落到场地底的时间。")]
        [SerializeField, Min(0f)] private float _summonDelay = 0.6f;

        [Header("事件")]
        [Tooltip("开战瞬间（地板开始塌）。召唤演出 / 音效 / 镜头震挂这里。")]
        [SerializeField] private UnityEvent _onBegan = new UnityEvent();

        [Tooltip("Boss 被打死。**火车启用挂这里** —— 只会发一次。")]
        [SerializeField] private UnityEvent _onCleared = new UnityEvent();

        private float _elapsed;
        private bool _topClosed;
        private bool _spawnActivated;
        private PlayerController _player;

        /// <summary>当前阶段。</summary>
        public Phase Current { get; private set; } = Phase.Dormant;

        /// <summary>Boss 已死。</summary>
        public bool IsCleared => Current == Phase.Cleared;

        /// <summary>阶段变化（表现层 / 测试用）。</summary>
        public event System.Action<Phase> PhaseChanged;

        /// <summary>Boss 被打死（运行时击杀发一次）。结尾演出的挂点。</summary>
        /// <remarks>读档时 <c>Start</c> 直接把阶段拉成 <c>Cleared</c>，走的是另一条路、**不发**这个事件 ——
        /// 所以结尾动画不会在读档时重播。</remarks>
        public static event System.Action<BossEncounter> Cleared;

        // ==================================================================
        //  对外
        // ==================================================================

        /// <summary>开战。幂等：重复调用（例如开关又扳了一次）没有副作用。</summary>
        public void Begin()
        {
            if (Current != Phase.Dormant) return;

            Current = Phase.Collapsing;
            _elapsed = 0f;
            _player = FindAnyObjectByType<PlayerController>();

            if (_trapFloor != null) _trapFloor.Collapse();
            else Debug.LogWarning("[BossEncounter] 没有接 TrapFloor —— 地板不会塌。", this);

            // 玩家还在上面，关侧门没有夹人风险；顶部闸门要等他下去。
            SetGates(_sideDoors, false);

            TryActivateSpawn();

            PhaseChanged?.Invoke(Current);
            _onBegan.Invoke();
        }

        /// <summary><see cref="ISwitchTarget"/>：开关打开 = 开战。关掉不做任何事。</summary>
        public void SetSwitchState(bool on)
        {
            if (on) Begin();
        }

        // ==================================================================
        //  推进
        // ==================================================================

        private void FixedUpdate()
        {
            if (Current == Phase.Dormant || Current == Phase.Cleared) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            _elapsed += Time.fixedDeltaTime;

            if (!_spawnActivated) TryActivateSpawn();

            if (Current == Phase.Collapsing && _elapsed >= _summonDelay) Summon();

            if (Current != Phase.Fighting) return;

            if (!_topClosed) TryCloseTopGate();

            if (_boss != null && _boss.Defeated) Clear();
        }

        private void Summon()
        {
            if (_boss == null)
            {
                Debug.LogError("[BossEncounter] 没有接 Boss —— 只塌地板，不召唤。", this);
                Current = Phase.Fighting;
                PhaseChanged?.Invoke(Current);
                return;
            }

            // 防御：运行时生成的敌人若被标成 RuntimeSpawned，回溯会把它们永久 SetActive(false)。
            _boss.RuntimeSpawned = false;
            _boss.gameObject.SetActive(true);

            Current = Phase.Fighting;
            PhaseChanged?.Invoke(Current);

            // 召唤特效 + 音效：Presentation 的 EnemyVfxPresenter / EnemySfxPresenter 都在听这个事件。
            _boss.EmitFeedback(EnemyFeedbackKind.Summon, _boss.transform.position, Vector2.up);
        }

        /// <summary>玩家已经落到场地顶线以下 → 关顶部闸门。</summary>
        private void TryCloseTopGate()
        {
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null) return;

            if (_player.transform.position.y >= _arenaTopY) return;

            SetGate(_topGate, false);
            _topClosed = true;
        }

        private void Start()
        {
            // 读档恢复：Boss 在存档里已经通关 —— 直接把阶段拉到 Cleared，别再重新开战。
            if (!RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) return;
            Current = Phase.Cleared;
            PhaseChanged?.Invoke(Current);
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "cleared");

        private void Clear()
        {
            if (Current == Phase.Cleared) return;

            Current = Phase.Cleared;
            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, true);   // Boss 通关是永久进度

            SetGates(_sideDoors, true);
            SetGate(_topGate, true);

            PhaseChanged?.Invoke(Current);
            Cleared?.Invoke(this);
            _onCleared.Invoke();
        }

        /// <summary>
        /// 开战瞬间把场地内的复活点写进玩家。
        /// </summary>
        /// <remarks>玩家是关卡流程里才实例化的，找不到就下一帧再试 —— 只做一次。</remarks>
        private void TryActivateSpawn()
        {
            if (_arenaSpawn == null) { _spawnActivated = true; return; }
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null) return;

            _arenaSpawn.Activate(_player);
            _spawnActivated = true;
        }

        // ==================================================================
        //  门
        // ==================================================================

        private void SetGates(MonoBehaviour[] gates, bool on)
        {
            if (gates == null) return;
            for (int i = 0; i < gates.Length; i++) SetGate(gates[i], on);
        }

        private void SetGate(MonoBehaviour target, bool on)
        {
            if (target == null) return;

            // 与 CitySwitch 同一套解析：拖过来的可能是同物体上的另一个组件。
            CityPathMotor motor = target as CityPathMotor ?? target.GetComponent<CityPathMotor>();
            if (motor != null)
            {
                motor.SetOn(on);
                return;
            }

            ISwitchTarget switchTarget = target as ISwitchTarget ?? target.GetComponent<ISwitchTarget>();
            if (switchTarget != null)
            {
                switchTarget.SetSwitchState(on);
                return;
            }

            Debug.LogWarning(
                $"[BossEncounter] {target.name} 既不是 CityPathMotor 也没实现 ISwitchTarget —— 驱动不了。", this);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>Inspector 右键：检查最常见的几处接错。</summary>
        [ContextMenu("检查接线")]
        private void CheckWiring()
        {
            if (_boss == null) Debug.LogError("[BossEncounter] 没接 Boss。", this);
            else
            {
                if (_boss.gameObject.activeSelf)
                    Debug.LogWarning("[BossEncounter] Boss 开局是激活的 —— 请在 Inspector 里关掉它的根物体。", this);
                if (_boss.RuntimeSpawned)
                    Debug.LogError("[BossEncounter] Boss 的 RuntimeSpawned 勾上了 —— 时间回溯会把它永久关掉。", this);
                if (!_boss.UseSpawnRelativeArena)
                    Debug.LogWarning(
                        "[BossEncounter] Boss 没勾 UseSpawnRelativeArena —— ArenaX / ArenaY 是世界坐标，" +
                        "房间不在原点的话它会跑出场地。", this);
            }

            if (_trapFloor == null) Debug.LogWarning("[BossEncounter] 没接 TrapFloor。", this);
            if (_sideDoors == null || _sideDoors.Length == 0) Debug.LogWarning("[BossEncounter] 没接侧门。", this);
            if (_topGate == null) Debug.LogWarning("[BossEncounter] 没接顶部闸门 —— 玩家能蹬墙爬出去。", this);
            if (_arenaSpawn == null) Debug.LogWarning("[BossEncounter] 没接场地内的复活点。", this);

            for (int i = 0; i < (_sideDoors?.Length ?? 0); i++)
            {
                MonoBehaviour door = _sideDoors[i];
                if (door == null) continue;
                if (door is CityPathMotor || door.GetComponent<CityPathMotor>() != null) continue;
                if (door is ISwitchTarget || door.GetComponent<ISwitchTarget>() != null) continue;
                Debug.LogError($"[BossEncounter] 侧门 {door.name} 驱动不了。", this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Vector3 centre = transform.position;
            Gizmos.DrawLine(new Vector3(centre.x - 15f, _arenaTopY, 0f),
                            new Vector3(centre.x + 15f, _arenaTopY, 0f));

            Gizmos.color = new Color(0.3f, 0.9f, 0.5f, 0.9f);
            if (_boss != null) Gizmos.DrawWireSphere(_boss.transform.position, 0.6f);
        }
    }
}
