using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// <see cref="ISnapshotSystem"/> 的默认实现。
    /// </summary>
    /// <remarks>
    /// <para><b>采集范围 = 快照名册</b>（<see cref="ISnapshotRoster"/>），<b>不是</b>活动实体注册表。
    /// 名册的成员资格是「生命周期」，所以采集时禁用的对象也在里面 —— 这是「世界回到那一刻」
    /// 对禁用对象也成立的前提。两个集合的分工见 <see cref="ISnapshotRoster"/>。</para>
    /// <para><b>恢复顺序</b>：回退未来 → 激活状态 → 规则表 / 属性管理器 → 实体物理与动画 →
    /// <b>重建规则效果</b> → 触发 <see cref="OnRestored"/>。</para>
    /// <para><b>纯 C# 类</b>（与 <c>RuleSystem</c> 同一套单例模式），由
    /// <c>RuleSystemDriver</c> 在 Awake 里初始化。</para>
    /// </remarks>
    public sealed class SnapshotSystem : ISnapshotSystem
    {
        // ==================================================================
        //  单例
        // ==================================================================

        private static SnapshotSystem _instance;

        /// <summary>全局单例。由 <c>RuleSystemDriver</c> 在 Awake 里初始化。</summary>
        public static SnapshotSystem Instance => _instance;

        /// <summary>初始化单例。重复调用会覆盖（重开关卡 / 切场景时有用）。</summary>
        public static void Initialize(IRuleSystem rules, ISnapshotRoster roster)
            => _instance = new SnapshotSystem(rules, roster);

        /// <summary>仅测试用：注入替身。</summary>
        public static void SetInstanceForTesting(SnapshotSystem instance) => _instance = instance;

        /// <summary>清空单例。切场景 / 退出时调用，让下次重新初始化。</summary>
        public static void Reset() => _instance = null;

        // ==================================================================
        //  字段
        // ==================================================================

        /// <summary>环形缓冲容量。反转回溯范围 5-10 秒、快照间隔 5 秒，3-4 张就够，留一倍余量。</summary>
        private const int BufferCapacity = 8;

        private readonly IRuleSystem _rules;
        private readonly ISnapshotRoster _roster;

        private readonly List<Snapshot> _buffer = new List<Snapshot>();
        private int _nextSeq;

        /// <summary>定点快照（时间恒定用）。存储归本系统，何时 Pin 由规则类决定。</summary>
        private Snapshot _pinned;
        private int _pinnedHandle;
        private int _nextPinHandle;

        /// <summary>
        /// 存档点快照（暂停菜单回档用）。**与时间恒定的定点槽分开** ——
        /// 共用一张会让「时间恒定生效时按 ESC 回档」把时间线的定点踩掉。
        /// </summary>
        private Snapshot _checkpoint;

        public event Action OnRestored;

        public SnapshotSystem(IRuleSystem rules, ISnapshotRoster roster)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        }

        // ==================================================================
        //  单点快照
        // ==================================================================

        public Snapshot Capture()
        {
            // 全限定：RuleGame.Infrastructure 下的 Time 命名空间会遮蔽 UnityEngine.Time。
            var snap = new Snapshot { CapturedAt = UnityEngine.Time.time };

            IReadOnlyList<IRuleAffected> targets = _roster.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                IRuleAffected entity = targets[i];
                Rigidbody2D body = entity.Body;
                if (body == null) continue;         // 已销毁：名册会清理，这里再兜一层

                GameObject go = body.gameObject;

                var es = new EntityState
                {
                    Entity     = entity,
                    Position   = body.position,
                    Velocity   = body.linearVelocity, // Unity 6 API（旧 velocity 已弃用）
                    Rotation   = body.rotation,
                    ActiveSelf = go.activeSelf,       // 含「采集时就是禁用」的对象
                };

                if (TryReadAnimator(body, out int hash, out float normalized))
                {
                    es.AnimatorStateHash      = hash;
                    es.AnimatorNormalizedTime = normalized;
                }

                if (body.TryGetComponent<IEntityStatus>(out IEntityStatus status))
                    es.Status = status.ToData();

                if (body.TryGetComponent<ISnapshotParticipant>(out ISnapshotParticipant module))
                {
                    es.HasModuleState = true;
                    es.ModuleState = module.CaptureSnapshotState();
                    if (es.ModuleState == null)
                    {
                        Debug.LogWarning(
                            $"[SnapshotSystem] {go.name} 的 ISnapshotParticipant.CaptureSnapshotState() " +
                            "返回了 null。契约要求返回非 null（无状态就给一个空结构体），否则恢复侧无法区分" +
                            "「没有模块」与「模块状态是 null」。", go);
                    }
                }

                snap.Entities.Add(es);
            }

            // 静态机关：没有 Rigidbody2D / IRuleAffected，走独立的显式注册表。
            SnapshotParticipantRegistry.PruneDestroyed();
            IReadOnlyList<ISnapshotParticipant> participants = SnapshotParticipantRegistry.All;
            for (int i = 0; i < participants.Count; i++)
            {
                ISnapshotParticipant participant = participants[i];
                if (participant == null) continue;

                // 同一对象已经有实体级模块状态时不重复采集。
                if (participant is Component component && component.GetComponent<IRuleAffected>() != null)
                    continue;

                snap.Participants.Add(new ParticipantState
                {
                    Participant = participant,
                    State = participant.CaptureSnapshotState(),
                });
            }

            snap.RuleSlots  = _rules.Table.SnapshotSlots();
            snap.Attributes = _rules.Attributes.Values;
            return snap;
        }

        public void Restore(Snapshot snap, RestoreMode mode = RestoreMode.Full)
        {
            if (snap == null) return;

            // ⓪ 回退后未来作废：名册里有、快照里没有、且主动声明 DiscardWhenMissing 的临时对象
            //    不能继续留在世界里。只处理主动声明的模块，静态关卡物体及已有调用方不受影响。
            var present = new HashSet<Rigidbody2D>();
            for (int i = 0; i < snap.Entities.Count; i++)
            {
                Rigidbody2D body = snap.Entities[i].Entity?.Body;
                if (body != null) present.Add(body);
            }

            IReadOnlyList<IRuleAffected> targets = _roster.Targets;
            var currentBodies = new List<Rigidbody2D>();
            for (int i = 0; i < targets.Count; i++)
            {
                Rigidbody2D body = targets[i].Body;
                if (body != null) currentBodies.Add(body);
            }
            for (int i = 0; i < currentBodies.Count; i++)
            {
                Rigidbody2D body = currentBodies[i];
                if (present.Contains(body)) continue;
                if (body.TryGetComponent<ISnapshotParticipant>(out ISnapshotParticipant module)
                    && module.DiscardWhenMissing) body.gameObject.SetActive(false);
            }

            // ① 激活状态：先把「该复活的」「该睡下的」都摆到位（架构 D15）。
            //    含「采集时就是禁用」的对象 —— 名册记住了它，这里才能把它关回去。
            for (int i = 0; i < snap.Entities.Count; i++)
            {
                EntityState es = snap.Entities[i];
                IRuleAffected entity = es.Entity;
                if (entity == null) continue;

                Rigidbody2D body = entity.Body;
                if (body == null) continue;         // 已被销毁的对象无法恢复

                GameObject go = body.gameObject;
                if (go.activeSelf != es.ActiveSelf) go.SetActive(es.ActiveSelf);
            }

            // ② 规则与属性。直接覆盖槽位，不经过 Request —— 按契约不触发规则系统事件，
            //    需要收尾的模块听 OnRestored。剩余时间一起回退：快照的语义就是「世界回到那一刻」。
            if (snap.RuleSlots != null) _rules.Table.RestoreSlots(snap.RuleSlots);
            _rules.Attributes.RestoreValues(snap.Attributes);

            // ③ 实体：物理量 + 动画 + 实体状态 + 模块状态。
            for (int i = 0; i < snap.Entities.Count; i++)
            {
                EntityState es = snap.Entities[i];
                IRuleAffected entity = es.Entity;
                if (entity == null) continue;

                Rigidbody2D body = entity.Body;
                if (body == null) continue;         // 已销毁：跳过

                body.position       = es.Position;
                body.linearVelocity = es.Velocity;
                body.rotation       = es.Rotation;

                if (es.AnimatorStateHash != 0 &&
                    body.TryGetComponent<Animator>(out Animator animator) &&
                    animator.runtimeAnimatorController != null)
                {
                    animator.Play(es.AnimatorStateHash, 0, es.AnimatorNormalizedTime);
                }

                if (es.Status != null && body.TryGetComponent<IEntityStatus>(out IEntityStatus status))
                {
                    if (mode == RestoreMode.KeepEnergy)
                    {
                        // 用实体**当前**的能量盖掉快照里那个，但不改动快照本身。
                        // ⚠️ EntityStatusData 是 class：不 Clone 的话改的就是快照，
                        // 而恒定的定点快照要反复用，第一次回溯就被污染了。
                        EntityStatusData data = es.Status.Clone();
                        data.Energy = status.Energy;
                        status.ApplyData(data);
                    }
                    else
                    {
                        status.ApplyData(es.Status);
                    }
                }

                if (es.HasModuleState &&
                    body.TryGetComponent<ISnapshotParticipant>(out ISnapshotParticipant module))
                {
                    module.RestoreSnapshotState(es.ModuleState);
                }
            }

            // ③′ 静态机关模块状态。它们不在实体名册里，单独恢复。
            if (snap.Participants != null)
            {
                for (int i = 0; i < snap.Participants.Count; i++)
                {
                    ParticipantState state = snap.Participants[i];
                    if (state.Participant is UnityEngine.Object obj && obj == null) continue;
                    state.Participant.RestoreSnapshotState(state.State);
                }
            }

            // ④ 重建规则效果。恢复按契约不触发任何规则事件（OnRulesChanged 也是 ✗），
            //    所以质量 / 摩擦 / 重力这些「值回去了但没落到世界上」的效果必须在这里补。
            //    ReapplyActiveEffects 走静默路径，不触发规则生命周期事件，也不广播 OnRulesChanged。
            _rules.ReapplyActiveEffects();

            // ⑤ 全部写完才广播（先写状态，再广播 —— 与规则系统同一条纪律）。
            OnRestored?.Invoke();
        }

        /// <summary>
        /// 读动画状态；拿不到就返回 false，快照里留 0，恢复时跳过。
        /// </summary>
        /// <remarks>
        /// 动画是「锦上添花」：实体位置恢复后即使动画停在原地，下一帧也会被状态机接管。
        /// 所以这里宁可少记也不让快照采集抛异常。禁用中的对象拿不到状态，属正常。
        /// </remarks>
        private static bool TryReadAnimator(Rigidbody2D body, out int hash, out float normalizedTime)
        {
            hash = 0;
            normalizedTime = 0f;

            if (!body.TryGetComponent<Animator>(out Animator animator)) return false;
            if (!animator.isActiveAndEnabled) return false;
            if (animator.runtimeAnimatorController == null) return false;
            if (!animator.isInitialized) return false;              // 首帧未初始化时读状态会抛异常

            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
            hash = info.fullPathHash;
            normalizedTime = info.normalizedTime;
            return true;
        }

        // ==================================================================
        //  环形缓冲（时间反转）
        // ==================================================================

        public int CaptureToBuffer()
        {
            Snapshot snap = Capture();
            snap.Seq = _nextSeq++;
            _buffer.Add(snap);

            if (_buffer.Count > BufferCapacity)
                _buffer.RemoveAt(0);                    // 挤掉最老的一张

            return snap.Seq;
        }

        public bool TryPeekSecondLast(out Snapshot snap)
        {
            snap = null;
            if (_buffer.Count < 2) return false;

            snap = _buffer[_buffer.Count - 2];          // 只读倒数第二张（接口文档 §3.2）
            return true;
        }

        public bool TryPeekOldest(out Snapshot snap)
        {
            snap = null;
            if (_buffer.Count == 0) return false;

            snap = _buffer[0];                          // 最旧的一张（开局第一帧垫的那张）
            return true;
        }

        public void DiscardNewerThan(int index)
        {
            // 倒序移除：Seq 单调递增，尾部一定是最新。
            for (int i = _buffer.Count - 1; i >= 0; i--)
            {
                if (_buffer[i].Seq <= index) break;     // 后面的更老，遇到就停
                _buffer.RemoveAt(i);
            }
        }

        public void ClearBuffer() => _buffer.Clear();

        // ==================================================================
        //  定点快照（时间恒定）
        // ==================================================================

        public int Pin()
        {
            _pinned = Capture();
            _pinnedHandle = ++_nextPinHandle;           // 句柄从 1 起，0 表示「没有」
            return _pinnedHandle;
        }

        public bool TryRestorePinned(int handle, RestoreMode mode = RestoreMode.Full)
        {
            if (_pinned == null) return false;
            if (handle == 0 || handle != _pinnedHandle) return false;

            Restore(_pinned, mode);
            return true;
        }

        public void Unpin(int handle)
        {
            if (handle == 0 || handle != _pinnedHandle) return;
            _pinned = null;
            _pinnedHandle = 0;
        }

        // ==================================================================
        //  存档点快照
        // ==================================================================

        public bool HasCheckpointSnapshot => _checkpoint != null;

        public bool CaptureCheckpointSnapshot()
        {
            _checkpoint = Capture();
            return true;
        }

        public bool TryRestoreCheckpointSnapshot(RestoreMode mode = RestoreMode.Full)
        {
            if (_checkpoint == null) return false;

            Restore(_checkpoint, mode);
            return true;
        }

        public void ClearCheckpointSnapshot() => _checkpoint = null;
    }
}
