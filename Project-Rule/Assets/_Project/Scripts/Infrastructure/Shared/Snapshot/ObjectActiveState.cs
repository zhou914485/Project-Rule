using UnityEngine;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 一个 GameObject 在快照时刻的激活状态。
    /// </summary>
    /// <remarks>
    /// <para>恢复的是 <c>activeSelf</c>（自身开关），不是 <c>activeInHierarchy</c>
    /// （层级继承的结果）—— 父物体自己被禁用不该被子物体的快照「意外打开」。</para>
    /// <para>⚠️ 依赖架构 D15：<b>对象一律「禁用」而非销毁</b>。被打死的敌人、被捡走的道具
    /// 都必须以禁用的形式留在场景里，快照恢复时才能 <c>SetActive</c> 复活。
    /// 这条如果后期才改，所有销毁代码都要返工。</para>
    /// </remarks>
    public struct ObjectActiveState
    {
        /// <summary>对象引用。已被销毁的对象在恢复时跳过。</summary>
        public GameObject GameObject;

        /// <summary>快照时刻的 <c>activeSelf</c>。</summary>
        public bool ActiveSelf;
    }
}
