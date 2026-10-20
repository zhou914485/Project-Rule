using System;
using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 全局「虚实平台」参数：<c>true</c>（1）= A 有实体、B 虚化；<c>false</c>（0）= 相反。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是静态类</b>：A / B 平台分散在场景里，控制开关可能只有一个。把参数放在
    /// 全局静态类里，平台不需要互相找引用，也不需要场景里再摆一个「管理器」。</para>
    ///
    /// <para><b>谁读它</b>：<see cref="PlatformSolidityMember"/> 在 OnEnable 与
    /// <see cref="Changed"/> 时读；A 角色取 <see cref="AIsSolid"/>，B 角色取反。</para>
    ///
    /// <para><b>谁写它</b>：<see cref="PlatformSoliditySwitch"/> 被攻击时调
    /// <see cref="Toggle"/>；代码也可以直接 <see cref="Set"/>。</para>
    ///
    /// <para><b>重开一局</b>：<see cref="RuntimeInitializeOnLoadMethodAttribute"/> 在进 Play 前
    /// 把值重置成 <see cref="DefaultAIsSolid"/>、清空订阅者，避免上一局的静态状态漏到下一局。</para>
    /// </remarks>
    public static class PlatformSolidityState
    {
        /// <summary>进 Play 时的默认值：1 = A 实体 / B 虚化。</summary>
        public const bool DefaultAIsSolid = true;

        /// <summary>1 = A 有实体、B 虚化；0 = A 虚化、B 有实体。</summary>
        public static bool AIsSolid { get; private set; } = DefaultAIsSolid;

        /// <summary>B 角色现在有没有实体（恒为 <see cref="AIsSolid"/> 的反）。</summary>
        public static bool BIsSolid => !AIsSolid;

        /// <summary>值变化时广播。参数是新的 <see cref="AIsSolid"/>。</summary>
        /// <remarks>只在值真的变了才发（重复 Set 同一个值是幂等的）。</remarks>
        public static event Action<bool> Changed;

        /// <summary>把全局参数设成 1（A 实体）或 0（B 实体）。值没变就不发事件。</summary>
        public static void Set(bool aIsSolid)
        {
            if (AIsSolid == aIsSolid) return;

            AIsSolid = aIsSolid;
            Changed?.Invoke(AIsSolid);
        }

        /// <summary>在 1 / 0 之间切换。</summary>
        public static void Toggle() => Set(!AIsSolid);

        /// <summary>进 Play 前重置，避免编辑器里上一局的静态值 / 订阅者残留。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            AIsSolid = DefaultAIsSolid;
            Changed = null;
        }
    }
}
