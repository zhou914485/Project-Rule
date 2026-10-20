using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 门锁：**Boss 被打败之前这扇门打不开；Boss 倒下的那一刻它自己开。**
    /// </summary>
    /// <remarks>
    /// <para><b>配法</b>：挂在**门**（带 <see cref="CityPathMotor"/> 的物体）上，
    /// 把这一关的 <see cref="BossEncounter"/> 拖进 <c>_encounter</c>，其余留空即可。
    /// 门默认停在关位（<c>TargetEnd = false</c>），开的方向由 <see cref="CityPathMotor.EndOffset"/> 决定。</para>
    ///
    /// <para><b>为什么不直接用 <see cref="BossEncounter"/> 的 <c>_sideDoors</c></b>：那个列表是
    /// 「开战封口」语义（开战时关、打赢开），和出口门混在一起会让 Inspector 上分不清哪扇门管什么；
    /// 而且 <see cref="BossEncounter"/> 读档恢复时只把阶段拉回 <c>Cleared</c>、不碰门 ——
    /// 出口门由本组件自己盯着 <see cref="BossEncounter.IsCleared"/>，读档回来门直接就是开的。</para>
    ///
    /// <para><b>锁是硬的</b>：锁着的时候就算还有别的开关连在这扇门上，
    /// <c>_holdClosedWhileLocked</c> 也会把门按住 —— 「Boss 没死别想出去」不靠别人别乱接。</para>
    ///
    /// <para><b>状态跟着 Boss 走</b>：Boss 通关由 <see cref="BossEncounter"/> 写进世界状态仓库，
    /// 本组件只读它的阶段，不另存一份键 —— 两处记同一个进度迟早会对不上。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CityPathMotor))]
    public sealed class BossGateLock : MonoBehaviour
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("绑定")]
        [Tooltip("这扇门等哪个 Boss 战通关。拖场景里的 BossEncounter 组件。")]
        [SerializeField] private BossEncounter _encounter;

        [Tooltip("要开的门。留空 = 本物体上的 CityPathMotor（门自己）。")]
        [SerializeField] private CityPathMotor _gate;

        [Tooltip("锁着的时候把门按住：万一还有别的开关连在这扇门上，也别让它提前开。")]
        [SerializeField] private bool _holdClosedWhileLocked = true;

        [Header("通关后")]
        [Tooltip("Boss 倒下的瞬间把门打开。取消 = 只解锁，等别的机关来开。")]
        [SerializeField] private bool _openOnUnlock = true;

        [Header("解锁时")]
        [Tooltip("解锁瞬间要接的事情（音效 / 镜头 / 提示）。把接收方拖进来即可。")]
        [SerializeField] private UnityEvent _onUnlocked = new UnityEvent();

        // ==================================================================
        //  事件 / 只读状态
        // ==================================================================

        /// <summary>解锁瞬间（边沿）。这时起门才能开。</summary>
        public event Action OnUnlocked;

        /// <summary>现在解锁没有（= 门能不能开）。</summary>
        public bool IsUnlocked { get; private set; }

        /// <summary>这扇门在等哪场 Boss 战（只读，调试 / 验证用）。</summary>
        public BossEncounter Encounter => _encounter;

        /// <summary>这扇门（只读，调试 / 验证用）。</summary>
        public CityPathMotor Gate => _gate;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_gate == null) _gate = GetComponent<CityPathMotor>();
        }

        private void OnEnable()
        {
            if (_encounter != null) _encounter.PhaseChanged += HandlePhaseChanged;
        }

        private void OnDisable()
        {
            if (_encounter != null) _encounter.PhaseChanged -= HandlePhaseChanged;
        }

        private void Start()
        {
            if (_encounter == null)
            {
                Debug.LogWarning($"[BossGateLock] {name} 没绑 BossEncounter —— 这扇门永远不会解锁。", this);
                return;
            }

            // 读档恢复：Boss 在档里已经通关，门直接开，不能再锁一次。
            // （BossEncounter 的 Start 也会补发一次 PhaseChanged，两条路都覆盖执行顺序。）
            if (_encounter.IsCleared) Unlock();
        }

        /// <summary>
        /// 锁着的时候把门按住。
        /// </summary>
        /// <remarks>⚠️ 放在 <c>LateUpdate</c>：<see cref="CityPathMotor"/> 在 <c>FixedUpdate</c> 里走位移，
        /// 晚一步把目标端扳回关位，门就不会真的滑出去。解锁之后这段不再执行。</remarks>
        private void LateUpdate()
        {
            if (IsUnlocked || !_holdClosedWhileLocked) return;
            if (_gate != null && _gate.TargetEnd) _gate.SetOn(false);
        }

        // ==================================================================
        //  解锁
        // ==================================================================

        private void HandlePhaseChanged(BossEncounter.Phase phase)
        {
            if (phase == BossEncounter.Phase.Cleared) Unlock();
        }

        /// <summary>
        /// 解锁：从此这扇门可以打开（默认还会立刻替玩家把它拉开）。
        /// </summary>
        /// <remarks>Boss 倒下时由 <see cref="BossEncounter"/> 的 <c>PhaseChanged</c> 调过来；
        /// 重复调用是幂等的。也可以被别的机关 / UnityEvent 调。</remarks>
        public void Unlock()
        {
            if (IsUnlocked) return;

            IsUnlocked = true;

            if (_openOnUnlock && _gate != null) _gate.SetOn(true);

            OnUnlocked?.Invoke();
            _onUnlocked.Invoke();
        }

        /// <summary>给 <c>UnityEvent&lt;bool&gt;</c> 用：传 true = 解锁。传 false 不会重新锁上。</summary>
        public void SetUnlocked(bool unlocked)
        {
            if (unlocked) Unlock();
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        [ContextMenu("检查接线")]
        private void CheckWiring()
        {
            if (_encounter == null) Debug.LogError("[BossGateLock] 没绑 BossEncounter —— 门永远不会开。", this);
            if (_gate == null) Debug.LogError("[BossGateLock] 没有 CityPathMotor —— 驱动不了门。", this);
            else if (_gate.EndOffset.sqrMagnitude < 0.001f)
                Debug.LogWarning("[BossGateLock] 门的 EndOffset 是 0 —— 开和关在同一个位置。", this);
        }
    }
}
