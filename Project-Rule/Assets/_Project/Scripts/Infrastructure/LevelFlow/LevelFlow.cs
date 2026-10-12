using System;
using System.Collections.Generic;

namespace RuleGame.Infrastructure.LevelFlow
{
    /// <summary>
    /// <see cref="ILevelFlow"/> 的默认实现：纯 C# 状态机，不继承 MonoBehaviour。
    /// </summary>
    /// <remarks>
    /// <para><b>不做单例</b>：它的持有者是场景系统 / 存档系统（尚未落地），
    /// 谁需要谁 new 一个，方便按「整局进度」或「单关流程」各自组装，也方便单元测试。</para>
    /// <para><b>状态与事件的分工</b>：<see cref="NotifyGoalReached"/> 是唯一既写状态
    /// （登记通关）又发事件的请求入口；<see cref="RequestRestart"/> /
    /// <see cref="RequestNextLevel"/> 只发事件不写状态 —— 它们表达的是「请求」，
    /// 世界真的重载 / 切换完成后由 <see cref="StartLevel"/> 收口。这样事件订阅者
    /// （场景系统）失败或延迟时，状态机不会先飘走。</para>
    /// </remarks>
    public sealed class LevelFlow : ILevelFlow
    {
        /// <summary>关卡号从 1 起。</summary>
        private const int FirstLevel = 1;

        private readonly List<int> _cleared = new List<int>();
        private IReadOnlyList<int> _clearedView;

        public int CurrentLevel { get; private set; } = FirstLevel;

        /// <remarks>返回复用的只读包装，避免每次访问都分配。</remarks>
        public IReadOnlyList<int> ClearedLevels => _clearedView ?? (_clearedView = _cleared.AsReadOnly());

        public LevelFlow() { }

        // ==================================================================
        //  请求
        // ==================================================================

        public void RequestRestart() => OnRestartRequested?.Invoke(CurrentLevel);

        public void RequestNextLevel() => OnNextLevelRequested?.Invoke(CurrentLevel);

        public void NotifyGoalReached()
        {
            // 通关标记去重：同一关反复触终点只登记一次。
            // 事件不去重 —— 结算 UI 每次都需要收到。
            if (!_cleared.Contains(CurrentLevel)) _cleared.Add(CurrentLevel);

            OnLevelComplete?.Invoke(CurrentLevel);
        }

        public void NotifyLevelFailed() => OnLevelFailed?.Invoke(CurrentLevel);

        // ==================================================================
        //  状态写入
        // ==================================================================

        public void StartLevel(int level)
        {
            CurrentLevel = level;
            OnLevelStart?.Invoke(level);
        }

        public void SetProgress(int currentLevel, IEnumerable<int> clearedLevels)
        {
            CurrentLevel = currentLevel;

            _cleared.Clear();
            if (clearedLevels != null)
            {
                foreach (int level in clearedLevels)
                {
                    if (!_cleared.Contains(level)) _cleared.Add(level);
                }
            }
        }

        // ==================================================================
        //  事件
        // ==================================================================

        public event Action<int> OnLevelStart;
        public event Action<int> OnLevelComplete;
        public event Action<int> OnLevelFailed;
        public event Action<int> OnRestartRequested;
        public event Action<int> OnNextLevelRequested;
    }
}
