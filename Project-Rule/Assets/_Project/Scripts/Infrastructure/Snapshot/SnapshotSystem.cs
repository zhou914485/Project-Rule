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
    /// <para><b>采集范围 = 对象注册表</b>：遍历 <see cref="IWorldRegistry.All"/>，
    /// 不做 <c>FindObjectsOfType</c>（对象注册表的存在的意义）。</para>
    /// <para><b>恢复顺序</b>：对象激活状态 → 规则表 / 属性管理器 → 实体物理与动画 → 触发
    /// <see cref="OnRestored"/>。先恢复激活状态，保证后面写刚体 / 状态的对象都处于「应该在的那一侧」；
    /// 规则 / 属性在实体前写回，实体恢复后的下一帧读到的就是快照时刻的世界。</para>
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
        public static void Initialize(IRuleSystem rules, IWorldRegistry registry)
            => _instance = new SnapshotSystem(rules, registry);

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
        private readonly IWorldRegistry _registry;

        private readonly List<Snapshot> _buffer = new List<Snapshot>();
        private int _nextSeq;

        public event Action OnRestored;

        public SnapshotSystem(IRuleSystem rules, IWorldRegistry registry)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        // ==================================================================
        //  单点快照
        // ==================================================================

        public Snapshot Capture()
        {
            // 全限定：RuleGame.Infrastructure 下的 Time 命名空间会遮蔽 UnityEngine.Time。
            var snap = new Snapshot { CapturedAt = UnityEngine.Time.time };

            IReadOnlyList<IRuleAffected> entities = _registry.All;
            for (int i = 0; i < entities.Count; i++)
            {
                IRuleAffected entity = entities[i];
                Rigidbody2D body = entity.Body;
                if (body == null) continue;         // 已销毁：注册表会清理，这里再兜一层

                var es = new EntityState
                {
                    Entity   = entity,
                    Position = body.position,
                    Velocity = body.linearVelocity, // Unity 6 API（旧 velocity 已弃用）
                    Rotation = body.rotation,
                };

                if (TryReadAnimator(body, out int hash, out float normalized))
                {
                    es.AnimatorStateHash      = hash;
                    es.AnimatorNormalizedTime = normalized;
                }

                if (body.TryGetComponent<IEntityStatus>(out IEntityStatus status))
                    es.Status = status.ToData();

                snap.Entities.Add(es);
                snap.ObjectStates.Add(new ObjectActiveState
                {
                    GameObject = body.gameObject,
                    ActiveSelf = body.gameObject.activeSelf,
                });
            }

            snap.RuleSlots  = _rules.Table.SnapshotSlots();
            snap.Attributes = _rules.Attributes.Values;
            return snap;
        }

        public void Restore(Snapshot snap)
        {
            if (snap == null) return;

            // ① 对象激活状态：先把「该复活的」「该睡下的」都摆到位（架构 D15）。
            for (int i = 0; i < snap.ObjectStates.Count; i++)
            {
                ObjectActiveState os = snap.ObjectStates[i];
                if (os.GameObject == null) continue;        // 已被销毁的对象无法恢复
                if (os.GameObject.activeSelf != os.ActiveSelf)
                    os.GameObject.SetActive(os.ActiveSelf);
            }

            // ② 规则与属性。直接覆盖槽位，不经过 Request —— 按契约不触发规则系统事件，
            //    需要收尾的模块听 OnRestored。剩余时间一起回退：快照的语义就是「世界回到那一刻」。
            if (snap.RuleSlots != null) _rules.Table.RestoreSlots(snap.RuleSlots);
            _rules.Attributes.RestoreValues(snap.Attributes);

            // ③ 实体：物理量 + 动画 + 实体状态。
            for (int i = 0; i < snap.Entities.Count; i++)
            {
                EntityState es = snap.Entities[i];
                IRuleAffected entity = es.Entity;
                if (entity == null || entity.Body == null) continue;    // 已销毁：跳过

                Rigidbody2D body = entity.Body;
                body.position      = es.Position;
                body.linearVelocity = es.Velocity;
                body.rotation      = es.Rotation;

                if (es.AnimatorStateHash != 0 &&
                    body.TryGetComponent<Animator>(out Animator animator) &&
                    animator.runtimeAnimatorController != null)
                {
                    animator.Play(es.AnimatorStateHash, 0, es.AnimatorNormalizedTime);
                }

                if (es.Status != null && body.TryGetComponent<IEntityStatus>(out IEntityStatus status))
                    status.ApplyData(es.Status);
            }

            // ④ 全部写完才广播（先写状态，再广播 —— 与规则系统同一条纪律）。
            OnRestored?.Invoke();
        }

        /// <summary>
        /// 读动画状态；拿不到就返回 false，快照里留 0，恢复时跳过。
        /// </summary>
        /// <remarks>
        /// 动画是「锦上添花」：实体位置恢复后即使动画停在原地，下一帧也会被状态机接管。
        /// 所以这里宁可少记也不让快照采集抛异常。
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
    }
}
