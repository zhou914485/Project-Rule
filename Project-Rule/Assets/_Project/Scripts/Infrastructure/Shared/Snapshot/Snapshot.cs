using System.Collections.Generic;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 一张世界快照。
    /// </summary>
    /// <remarks>
    /// <para><b>不记录</b>粒子、特效 —— 恢复时直接重启，视觉上无感（接口文档 §3.2）。</para>
    /// <para>⚠️ <b>绝不包含 Transform / Rigidbody 的序列化</b> —— 快照只在运行时内存里活
    /// （约束 C5），持久化走存档系统的「已解锁 + 激活历史」小数据。</para>
    /// </remarks>
    public class Snapshot
    {
        /// <summary>采集时刻（<c>Time.time</c>）。仅用于调试展示，不参与恢复。</summary>
        public float CapturedAt;

        /// <summary>动态实体的状态（位置 / 速度 / 旋转 / 动画 / 实体状态）。</summary>
        public List<EntityState> Entities = new List<EntityState>();

        /// <summary>对象的激活状态。恢复的前提是对象被「禁用」而非销毁（架构 D15）。</summary>
        public List<ObjectActiveState> ObjectStates = new List<ObjectActiveState>();

        /// <summary>规则维护表的 6 个属性槽位（含剩余时间 —— 恢复时连它一起回退）。</summary>
        public ActiveRule?[] RuleSlots;

        /// <summary>属性管理器的值（摩擦 / 质量 / 伤害 / 因果等无引擎载体的属性）。</summary>
        public AttributeValues Attributes;

        /// <summary>
        /// 环形缓冲序号。
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>v3.0 §3.2 未列出该字段</b>：<see cref="ISnapshotSystem.CaptureToBuffer"/> 返回的
        /// 「索引」必须不受缓冲裁剪影响（环形缓冲挤掉最老的一张后，列表下标会整体前移），
        /// 所以用单调递增的序号做身份标识。<see cref="ISnapshotSystem.DiscardNewerThan"/> 按它比较。
        /// <b>待团队确认后并入冻结文档。</b>
        /// </remarks>
        public int Seq;
    }
}
