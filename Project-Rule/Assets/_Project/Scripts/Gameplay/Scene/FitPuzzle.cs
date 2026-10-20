using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 拼装机关：**所有凹位都入位**之后，驱动一个目标（升门 / 开灯 / 按开关）。
    /// </summary>
    /// <remarks>
    /// <para>它只做三件事：① 订阅每个 <see cref="FitSocket"/> 的入位事件；
    /// ② 全部入位时把 <c>_onSolvedTarget</c>（实现 <see cref="ISwitchTarget"/>）打开；
    /// ③ 有 <c>_gate</c> 的话把它整体抬起来 <c>_gateRise</c> 米。</para>
    ///
    /// <para><b>为什么把「升门」写在这里</b>：闸门本身没有新机制 —— 它就是一块石头。
    /// 抬它不需要 <c>MovingPlatform</c> 那套路径系统（那套是给滑板车 / 电梯用的）。
    /// 想要更复杂的开门动作，把 <c>_gateRise</c> 设成 0，改用 <c>_onSolvedTarget</c> 驱动现成组件。</para>
    ///
    /// <para>灯是可选的：<c>_lamps</c> 与 <c>_sockets</c> 一一对应，谁入位谁亮。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class FitPuzzle : MonoBehaviour
    {
        [Header("要凑齐的凹位")]
        [Tooltip("要凑齐的凹位。**留空也可以** —— 只要勾了下面的自动收集，开局会自己找场景里所有 FitSocket。")]
        [SerializeField] private FitSocket[] _sockets = new FitSocket[0];

        [Tooltip("_sockets 留空时，开局自动收集场景里**所有** FitSocket（懒人接线：拖进来就能用）。")]
        [SerializeField] private bool _autoCollectSockets = true;

        [Header("解开之后")]
        [Tooltip("整体抬起来的闸门（可留空）。")]
        [SerializeField] private Transform _gate;

        [Tooltip("闸门抬起多少米（正数 = 往上）。")]
        [SerializeField] private float _gateRise = 2f;

        [Tooltip("闸门抬起速度（米/秒）。")]
        [SerializeField, Min(0.01f)] private float _gateSpeed = 1.2f;

        [Tooltip("**当按钮用**：拖一个 CitySwitch 过来，两块都合体时它会像被压到一样按下（PressByContact）。")]
        [SerializeField] private MonoBehaviour _button;

        [Tooltip("解锁时驱动的目标（实现 ISwitchTarget，可留空）。")]
        [SerializeField] private MonoBehaviour _onSolvedTarget;

        [Tooltip("解锁时触发一次（要不要接东西都行）。")]
        public UnityEvent OnSolved = new UnityEvent();

        [Header("指示灯（可选，与凹位一一对应）")]
        [SerializeField] private GameObject[] _lamps = new GameObject[0];

        [Tooltip("解锁时打一条 Console。")]
        [SerializeField] private bool _logEvents = true;

        private bool _solved;
        private bool _subscribed;
        private Vector3 _gateClosed;

        /// <summary>已经解锁。</summary>
        public bool IsSolved => _solved;

        /// <summary>已合体的凹位数量。</summary>
        public int SeatedCount
        {
            get
            {
                if (_sockets == null) return 0;

                int count = 0;
                for (int i = 0; i < _sockets.Length; i++)
                {
                    if (_sockets[i] != null && _sockets[i].IsSeated) count++;
                }

                return count;
            }
        }

        /// <summary>需要凑齐几个凹位（就是数组长度，通常 2）。</summary>
        public int RequiredCount => _sockets != null ? _sockets.Length : 0;

        private void OnEnable()
        {
            // 第一次激活时 _sockets 可能还是空的（等 Start 去自动收集），所以这里只在已经有数组时订阅。
            if (_sockets != null && _sockets.Length > 0) Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            Unsubscribe();
            if (_sockets == null) return;

            for (int i = 0; i < _sockets.Length; i++)
            {
                if (_sockets[i] != null) _sockets[i].SeatedEvent += HandleSeated;
            }

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _sockets == null)
            {
                _subscribed = false;
                return;
            }

            for (int i = 0; i < _sockets.Length; i++)
            {
                if (_sockets[i] != null) _sockets[i].SeatedEvent -= HandleSeated;
            }

            _subscribed = false;
        }

        /// <summary>数组留空时把场景里的凹位全收进来 —— 让「拖个预制体进来」就能用。</summary>
        private void CollectSocketsIfEmpty()
        {
            if (!_autoCollectSockets || (_sockets != null && _sockets.Length > 0)) return;

            _sockets = FindObjectsByType<FitSocket>(FindObjectsSortMode.None);
            if (_logEvents && _sockets.Length > 0)
            {
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < _sockets.Length; i++)
                {
                    if (i > 0) names.Append("、");
                    names.Append(_sockets[i].name);
                }

                Debug.Log($"[拼装缺口] _sockets 是空的 —— 自动收到 {_sockets.Length} 个凹位：{names}", this);
            }
        }

        private void Start()
        {
            if (_gate != null) _gateClosed = _gate.position;

            CollectSocketsIfEmpty();
            Subscribe();
            ValidateWiring();

            // 读档恢复：拼图在存档里已经解开。只标记状态、**不重放按钮** ——
            // 它驱动的门 / 电梯自己也有存档状态，重放会对已经开着的目标再推一次。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) _solved = true;

            // 场景可能是「已经拼好」的状态存下来的：开局先对一次。
            Evaluate();
        }

        /// <summary>开局体检：接错线时**当场报出来**，别等玩到一半才发现门不开。</summary>
        private void ValidateWiring()
        {
            if (_sockets == null || _sockets.Length < 2)
            {
                Debug.LogWarning($"[拼装缺口] 这个机关只接了 {RequiredCount} 个凹位 —— " +
                                 "「两个都合体才开门」至少要接 2 个。", this);
            }

            if (_sockets != null)
            {
                for (int i = 0; i < _sockets.Length; i++)
                {
                    if (_sockets[i] == null)
                    {
                        Debug.LogWarning($"[拼装缺口] _sockets[{i}] 是空的 —— 这个机关永远不会解开。", this);
                    }
                }
            }

            if (_gate == null && _button == null && _onSolvedTarget == null)
            {
                Debug.LogWarning("[拼装缺口] 解锁之后什么都没接（闸门 / 按钮 / 目标全空）—— " +
                                 "两个都合体了也不会有任何反应。", this);
            }
        }

        private void Update()
        {
            if (!_solved || _gate == null) return;

            Vector3 target = _gateClosed + Vector3.up * _gateRise;
            _gate.position = Vector3.MoveTowards(_gate.position, target, _gateSpeed * Time.deltaTime);
        }

        private void HandleSeated(FitSocket socket)
        {
            int index = _sockets != null ? Array.IndexOf(_sockets, socket) : -1;
            if (index >= 0 && _lamps != null && index < _lamps.Length && _lamps[index] != null)
            {
                _lamps[index].SetActive(true);
            }

            if (_logEvents)
            {
                Debug.Log($"[拼装缺口] 已合体 {SeatedCount}/{RequiredCount} —— " +
                          (SeatedCount >= RequiredCount ? "够了，触发。" : "还差一个。"), this);
            }

            Evaluate();
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "solved");

        private void Evaluate()
        {
            if (_solved || _sockets == null || _sockets.Length == 0) return;

            foreach (FitSocket socket in _sockets)
            {
                if (socket == null || !socket.IsSeated) return;
            }

            _solved = true;
            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, true);

            if (_logEvents) Debug.Log("[拼装缺口] 两个都合体了 —— 发事件（按一次按钮）。", this);

            // 三条出口，按需要接：像按钮一样按下 CitySwitch / 直接驱动 ISwitchTarget（门 / 电梯）/ 纯 UnityEvent。
            // 同一个物体同时挂 CitySwitch 和 ISwitchTarget 也只会让目标收到一次「开」。
            IContactSwitch contact = Resolve<IContactSwitch>(_button);
            if (contact != null) contact.PressByContact();

            ISwitchTarget switchTarget = Resolve<ISwitchTarget>(_button) ?? Resolve<ISwitchTarget>(_onSolvedTarget);
            if (switchTarget != null) switchTarget.SetSwitchState(true);

            if (switchTarget == null)
            {
                string who = _onSolvedTarget != null ? _onSolvedTarget.name : (_button != null ? _button.name : "（空）");
                Debug.LogWarning("[拼装缺口] 解开了，但没找到能驱动的东西：" + who +
                                 "。把**门**拖到 _onSolvedTarget —— 拖整只 GameObject 也行，" +
                                 "这里会在它身上找 CityPathMotor / ISwitchTarget。", this);
            }

            OnSolved.Invoke();
        }

        /// <summary>
        /// 从「拖进来的那个 MonoBehaviour」解析出接口。
        /// </summary>
        /// <remarks>
        /// ⚠️ **必须带 GetComponent 兜底**：字段类型是 <c>MonoBehaviour</c> 时，把整只 GameObject 拖进来，
        /// Unity 赋的是它身上**第一个** MonoBehaviour —— 门上有 <c>RuleAffectedBody</c>（RequireComponent 保证排第一），
        /// 于是引用指向了它，<c>as ISwitchTarget</c> 直接判空、门纹丝不动。
        /// <c>CitySwitch</c> 早就在踩这个坑（文档里写着 Level_Building_01 的两扇门就是这么坏的）。
        /// </remarks>
        private static T Resolve<T>(MonoBehaviour source) where T : class
        {
            if (source == null) return null;

            return source as T ?? source.GetComponent<T>();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
        }

        // ==================================================================
        //  供关卡工具配置
        // ==================================================================

        /// <summary>接线（生成工具用）：凹位 + 闸门 + 灯。</summary>
        public void Configure(FitSocket[] sockets, Transform gate, GameObject[] lamps, float rise)
        {
            _sockets = sockets ?? new FitSocket[0];
            _gate = gate;
            _lamps = lamps ?? new GameObject[0];
            _gateRise = rise;
        }
    }
}
