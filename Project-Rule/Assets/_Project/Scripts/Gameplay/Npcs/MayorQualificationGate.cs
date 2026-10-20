using System;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Persistence;
using UnityEngine;
using UnityEngine.Events;

namespace RuleGame.Gameplay.Npcs
{
    /// <summary>
    /// 村长资格门：**只有镇长在「规则 + 能力全解锁」后读完 Talk2，才打开这扇门。**
    /// </summary>
    /// <remarks>
    /// <para><b>配法</b>：挂在门 / 闸门上，<c>_gate</c> 留空会自动取同物体的
    /// <see cref="CityPathMotor"/>；如果门不是 CityPathMotor（例如一块纯碰撞体），
    /// 把 <c>_blocker</c> 拖进来，开门时只关碰撞体。</para>
    ///
    /// <para><b>不依赖场景引用</b>：镇长发 <see cref="NpcActor.MayorQualified"/> 静态事件，
    /// 门订阅它；镇长和门不在同一个分区也能工作。</para>
    ///
    /// <para><b>读档恢复</b>：资格键写在 <see cref="WorldStateStore"/>（<c>npc.mayor.qualified</c>），
    /// 门在 <c>Start</c> 里读；从盘续档时门已经开着，不会把玩家堵在魔王城外面。</para>
    ///
    /// <para><b>二次保险</b>：<see cref="Open"/> 会再查一次
    /// <see cref="PlayerProgress.HasAllUnlocks"/>。即使白盒里把镇长的
    /// <c>_abilityReady</c> 手动打开，门也不会提前开。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MayorQualificationGate : MonoBehaviour
    {
        [Header("门")]
        [Tooltip("要打开的门。留空 = 取同物体上的 CityPathMotor。")]
        [SerializeField] private CityPathMotor _gate;

        [Tooltip("没有 CityPathMotor 的门：开门时只关这个碰撞体。")]
        [SerializeField] private Collider2D _blocker;

        [Tooltip("勾上 = 资格发放时自动把门开到 EndOffset；取消 = 只解锁，等别的机关来开。")]
        [SerializeField] private bool _openOnQualified = true;

        [Header("事件")]
        [Tooltip("开门瞬间（音效 / 镜头 / 提示）。")]
        [SerializeField] private UnityEvent _onOpened = new UnityEvent();

        /// <summary>这扇门已经开过了。</summary>
        public bool IsOpen { get; private set; }

        private bool _warnedNoTarget;

        private void Awake()
        {
            if (_gate == null) _gate = GetComponent<CityPathMotor>();
        }

        private void OnEnable()
        {
            NpcActor.MayorQualified += HandleMayorQualified;
        }

        private void OnDisable()
        {
            NpcActor.MayorQualified -= HandleMayorQualified;
        }

        private void Start()
        {
            if (WorldStateStore.GetBool(NpcActor.MayorQualifiedKey))
            {
                OpenFromSave();
                return;
            }

            if (_gate == null && _blocker == null && !_warnedNoTarget)
            {
                _warnedNoTarget = true;
                Debug.LogWarning("[MayorQualificationGate] 既没有 CityPathMotor 也没有 Collider2D，" +
                                 "资格发放后这扇门不会有任何物理变化。", this);
            }
        }

        /// <summary>镇长资格发放（静态事件）。</summary>
        private void HandleMayorQualified() => Open();

        /// <summary>给 UnityEvent 用的别名；和 <see cref="Open"/> 等价。</summary>
        public void Unlock() => Open();

        /// <summary>
        /// 开门。会再检查一次玩家是否真的集齐规则与能力；条件不满足时保持关闭。
        /// </summary>
        public void Open()
        {
            if (IsOpen) return;

            PlayerProgress progress = PlayerProgress.Find();
            if (progress != null && !progress.HasAllUnlocks) return;

            OpenInternal();
        }

        /// <summary>读档恢复：资格键已经存在，不再要求玩家重新对话一次。</summary>
        private void OpenFromSave()
        {
            if (IsOpen) return;
            OpenInternal();
        }

        private void OpenInternal()
        {
            IsOpen = true;

            if (_openOnQualified && _gate != null) _gate.SetOn(true);
            if (_blocker != null) _blocker.enabled = false;

            WorldStateStore.SetBool(NpcActor.MayorQualifiedKey, true);
            _onOpened.Invoke();
        }
    }
}
