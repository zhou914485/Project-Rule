using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 一个动态实体在快照时刻的状态。
    /// </summary>
    /// <remarks>
    /// <para><see cref="Entity"/> 持的是接口引用，恢复时用它找回刚体写回状态。
    /// 实体若已被销毁（没走「禁用」路线的漏网之鱼），恢复时跳过 —— 快照不负责复活已被
    /// <c>Destroy</c> 的对象，只负责恢复「禁用」的对象。</para>
    /// <para>动画状态用「状态 hash + 归一化时间」表达：恢复时 <c>Animator.Play(hash, 0, t)</c>，
    /// 不试图倒放动画帧 —— 够用且成本为零。</para>
    /// </remarks>
    public struct EntityState
    {
        /// <summary>实体引用（来自对象注册表）。</summary>
        public IRuleAffected Entity;

        /// <summary>刚体位置（<c>Rigidbody2D.position</c>，非 Transform）。</summary>
        public Vector2 Position;

        /// <summary>刚体速度（<c>Rigidbody2D.linearVelocity</c>，Unity 6 API）。</summary>
        public Vector2 Velocity;

        /// <summary>刚体旋转角（度）。</summary>
        public float Rotation;

        /// <summary>动画状态 fullPathHash。实体没有可用的 Animator 时为 0（恢复时跳过）。</summary>
        public int AnimatorStateHash;

        /// <summary>动画状态的归一化时间。</summary>
        public float AnimatorNormalizedTime;

        /// <summary>实体状态数据（能量 / 生命 / 持有物）。实体没有 <see cref="IEntityStatus"/> 时为 null。</summary>
        public EntityStatusData Status;
    }
}
