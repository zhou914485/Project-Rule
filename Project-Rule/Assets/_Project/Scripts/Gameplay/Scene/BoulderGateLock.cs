using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Persistence;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 门锁：**这块巨石碎开之前，这扇门打不开；碎开的那一刻它自己开。**
    /// </summary>
    /// <remarks>
    /// <para><b>配法</b>：挂在**门**（带 <see cref="CityPathMotor"/> 的物体）上，
    /// 把巨石（<see cref="GravityBoulder"/>）拖进 <c>_boulder</c>，其余留空即可。</para>
    ///
    /// <para><b>为什么不直接把门拖到天花板按钮的 FollowMotor 上</b>：那个按钮是巨石**上升途中**
    /// 被顶到的 —— 那一刻石头还顶在天花板上、离碎开还有一整段。门要是挂在按钮上，
    /// 玩家能提前钻进碎石区，甚至被落下来的石头砸中。「碎掉才开」必须等
    /// <see cref="GravityBoulder.OnShattered"/> 这一下。</para>
    ///
    /// <para><b>锁是硬的</b>：锁着的时候就算还有别的开关连在这扇门上（旧配法没拆干净），
    /// <c>_holdClosedWhileLocked</c> 也会把门按住 —— 「碎掉才能开」不靠别人别乱接。</para>
    ///
    /// <para><b>解锁状态进存档</b>（<see cref="WorldStateStore"/>）：巨石是「地形」、不进存档，
    /// 读档回来它会重新变完整。门要是跟着石头一起重置，已经拿过剑的玩家就会被一扇
    /// 永远打不开的门堵在外面 —— 所以门自己记住「已经开过了」。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CityPathMotor))]
    public sealed class BoulderGateLock : MonoBehaviour
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("绑定")]
        [Tooltip("这扇门等哪块巨石碎。拖 BoulderSwordMechanism 下的 Boulder（GravityBoulder 组件）。")]
        [SerializeField] private GravityBoulder _boulder;

        [Tooltip("要开的门。留空 = 本物体上的 CityPathMotor（门自己）。")]
        [SerializeField] private CityPathMotor _gate;

        [Tooltip("锁着的时候把门按住：万一还有别的开关连在这扇门上，也别让它提前开。")]
        [SerializeField] private bool _holdClosedWhileLocked = true;

        [Header("解锁后")]
        [Tooltip("巨石碎开的瞬间把门打开。取消 = 只解锁，等别的机关来开。")]
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

        /// <summary>这本机关在等的那块巨石（只读，调试 / 验证用）。</summary>
        public GravityBoulder Boulder => _boulder;

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
            if (_boulder != null) _boulder.OnShattered += Unlock;
        }

        private void OnDisable()
        {
            if (_boulder != null) _boulder.OnShattered -= Unlock;
        }

        private void Start()
        {
            if (_boulder == null)
            {
                if (!_warnedNoBoulder)
                {
                    _warnedNoBoulder = true;
                    Debug.LogWarning($"[BoulderGateLock] {name} 没绑巨石 —— 这扇门永远不会解锁。", this);
                }

                return;
            }

            // 读档恢复：存档里已经解锁过 —— 门直接开，不能再等石头碎一次。
            if (WorldStateStore.GetBool(SaveStateKey) || _boulder.CurrentState == GravityBoulder.State.Shattered)
                Unlock();
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

        /// <summary>
        /// 解锁：从此这扇门可以打开（默认还会立刻替玩家把它拉开）。
        /// </summary>
        /// <remarks>巨石碎开的瞬间由 <see cref="GravityBoulder.OnShattered"/> 调过来；重复调用是幂等的。
        /// 也可以被别的机关 / UnityEvent 调。</remarks>
        public void Unlock()
        {
            if (IsUnlocked) return;

            IsUnlocked = true;

            if (_openOnUnlock && _gate != null) _gate.SetOn(true);

            // 巨石不进存档、门要进：否则拿过剑的玩家读档回来会被一扇永远打不开的门堵住。
            WorldStateStore.SetBool(SaveStateKey, true);

            OnUnlocked?.Invoke();
            _onUnlocked.Invoke();
        }

        /// <summary>给 <c>UnityEvent&lt;bool&gt;</c> 用：传 true = 解锁。传 false 不会重新锁上。</summary>
        public void SetUnlocked(bool unlocked)
        {
            if (unlocked) Unlock();
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => WorldStateStore.KeyOf(this, "unlocked");

        private bool _warnedNoBoulder;
    }
}
