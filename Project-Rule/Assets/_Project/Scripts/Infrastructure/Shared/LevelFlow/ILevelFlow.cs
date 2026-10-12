using System;
using System.Collections.Generic;

namespace RuleGame.Infrastructure.LevelFlow
{
    /// <summary>
    /// 关卡流程：纯状态机 + 事件，<b>零依赖</b>（架构 §3.3）。
    /// </summary>
    /// <remarks>
    /// <para><b>不直接调用场景系统、不直接调用存档系统</b> —— 所有跨系统协作走事件：
    /// 场景系统听 <see cref="OnRestartRequested"/> 去重载关卡，存档系统听
    /// <see cref="OnLevelComplete"/> 写通关记录。这样它可以脱离 Unity 单独测试。</para>
    /// </remarks>
    public interface ILevelFlow
    {
        /// <summary>当前关卡号。</summary>
        int CurrentLevel { get; }

        /// <summary>已通关的关卡号列表（只读）。</summary>
        IReadOnlyList<int> ClearedLevels { get; }

        // ------------------------------------------------------------------
        //  请求（内部走事件，不直接调用场景 / 存档）
        // ------------------------------------------------------------------

        /// <summary>请求重开当前关卡。只发 <see cref="OnRestartRequested"/>，自己不重置任何状态 —— 重开由场景系统执行，完成后回 <see cref="StartLevel"/>。</summary>
        void RequestRestart();

        /// <summary>请求进入下一关。只发 <see cref="OnNextLevelRequested"/>，不推进状态 —— 关卡号由 <see cref="StartLevel"/> 写。</summary>
        void RequestNextLevel();

        /// <summary>玩家到达终点（由场景触发器调用）。登记通关并发 <see cref="OnLevelComplete"/>。</summary>
        void NotifyGoalReached();

        // ------------------------------------------------------------------
        //  以下三个成员为 P1 补充（v3.0 §3.3 未列出，待团队确认后并入冻结文档）
        // ------------------------------------------------------------------

        /// <summary>开始一关（场景加载完成后由场景系统调用）：写 <see cref="CurrentLevel"/> 并发 <see cref="OnLevelStart"/>。</summary>
        void StartLevel(int level);

        /// <summary>宣告失败（由关卡逻辑调用），发 <see cref="OnLevelFailed"/>。</summary>
        void NotifyLevelFailed();

        /// <summary>
        /// 载入进度（存档恢复时调用）。
        /// </summary>
        /// <remarks>只写状态、不发事件 —— 进度恢复不是「又开了一关」，不该触发 OnLevelStart 的订阅者。</remarks>
        void SetProgress(int currentLevel, IEnumerable<int> clearedLevels);

        // ------------------------------------------------------------------
        //  事件
        // ------------------------------------------------------------------

        /// <summary>一关开始。订阅者：场景系统 · 存档 · UI。</summary>
        event Action<int> OnLevelStart;

        /// <summary>一关通关。订阅者：存档 · UI（结算）。</summary>
        event Action<int> OnLevelComplete;

        /// <summary>一关失败。订阅者：UI。</summary>
        event Action<int> OnLevelFailed;

        /// <summary>请求重开。订阅者：场景系统 · 快照系统。</summary>
        event Action<int> OnRestartRequested;

        /// <summary>请求下一关。订阅者：场景系统。</summary>
        event Action<int> OnNextLevelRequested;
    }
}
