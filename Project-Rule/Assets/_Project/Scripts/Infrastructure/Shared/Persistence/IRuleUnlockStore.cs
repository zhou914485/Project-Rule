using System.Collections.Generic;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 已解锁词条（属性 / 关系）的读写边界。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>v3.0 §4.4 未定义本接口</b>：<see cref="SaveData"/> 里有
    /// <c>UnlockedAttributes</c> / <c>UnlockedRelations</c> 两个字段，但玩家解锁系统
    /// 排期在存档之后，P1 没有任何运行时对象持有这份数据。存档系统不能凭空造一个
    /// 解锁管理器（那是 Gameplay 层的事），所以只定义<b>它读谁、写谁</b>的接缝：
    /// 未来玩家侧的解锁组件实现本接口并在引导处注入即可。<b>待团队确认后并入冻结文档。</b></para>
    /// <para>字符串统一用枚举名（<c>AttributeId</c> / <c>RelationId</c> 的 <c>ToString()</c>），
    /// 不用中文显示名 —— 显示名可改，枚举名是序列化契约。</para>
    /// <para>位置在 Infrastructure/Shared：Gameplay 层的解锁组件（允许依赖 Infrastructure
    /// 但不允许依赖 Persistence）要能实现它。</para>
    /// </remarks>
    public interface IRuleUnlockStore
    {
        /// <summary>当前已解锁的属性词条枚举名。</summary>
        IReadOnlyList<string> UnlockedAttributes { get; }

        /// <summary>当前已解锁的关系词条枚举名。</summary>
        IReadOnlyList<string> UnlockedRelations { get; }

        /// <summary>读档后恢复解锁集合（整体覆盖）。</summary>
        void RestoreUnlocks(IEnumerable<string> attributes, IEnumerable<string> relations);
    }
}
