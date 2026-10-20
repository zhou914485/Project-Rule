using System;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 一条「世界状态」：可交互场景组件 / 敌人的状态快照（键值对）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是字符串而不是强类型</b>：每个组件要知道的状态各不相同（开关按下 / 门碎掉 /
    /// 剑被拔出 / 敌人已死），而这一层<b>不认识任何具体组件</b>（分层铁律）。组件自己决定怎么写，
    /// 存档只负责搬运字符串。</para>
    /// <para>⚠️ <b>绝不在这里放 Transform / Rigidbody / 物理状态</b>（约束 C5）：位置由场景里的点提供，
    /// 状态只记「这个机关现在是什么样」。</para>
    /// </remarks>
    [Serializable]
    public class WorldStateRecord
    {
        /// <summary>稳定键：场景名 + 层级路径 + 槽位，见 <see cref="WorldStateStore.KeyOf"/>。</summary>
        public string Key;

        /// <summary>组件自己解释的载荷（目前都是 "0" / "1" 或短字符串）。</summary>
        public string Value;
    }
}
