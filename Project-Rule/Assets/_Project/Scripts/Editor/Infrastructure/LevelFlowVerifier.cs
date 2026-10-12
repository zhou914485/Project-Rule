using System.Collections.Generic;
using RuleGame.Infrastructure.LevelFlow;

namespace RuleGame.Editor.Infrastructure
{
    /// <summary>
    /// 关卡流程验证：状态机与事件的分工（纯 C#，不依赖引擎）。
    /// </summary>
    public static class LevelFlowVerifier
    {
        public static string Run()
        {
            var h = new RuleGame.Editor.Rules.RuleVerifyHarness();
            var flow = new LevelFlow();

            var starts = new List<int>();
            var completes = new List<int>();
            var fails = new List<int>();
            var restarts = new List<int>();
            var nexts = new List<int>();
            flow.OnLevelStart += l => starts.Add(l);
            flow.OnLevelComplete += l => completes.Add(l);
            flow.OnLevelFailed += l => fails.Add(l);
            flow.OnRestartRequested += l => restarts.Add(l);
            flow.OnNextLevelRequested += l => nexts.Add(l);

            // ==============================================================
            //  ① 初始状态
            // ==============================================================
            h.Section("1 初始状态");

            h.Check("默认从第 1 关开始", flow.CurrentLevel == 1, flow.CurrentLevel.ToString());
            h.Check("默认无通关记录", flow.ClearedLevels.Count == 0, flow.ClearedLevels.Count.ToString());

            // ==============================================================
            //  ② 开始一关
            // ==============================================================
            h.Section("2 开始一关");

            flow.StartLevel(2);
            h.Check("CurrentLevel 写入", flow.CurrentLevel == 2, flow.CurrentLevel.ToString());
            h.Check("OnLevelStart(2) 触发",
                    starts.Count == 1 && starts[0] == 2, $"starts=[{string.Join(",", starts)}]");

            // ==============================================================
            //  ③ 到达终点：登记通关 + 发事件
            // ==============================================================
            h.Section("3 到达终点");

            flow.NotifyGoalReached();
            h.Check("OnLevelComplete(2) 触发",
                    completes.Count == 1 && completes[0] == 2, $"completes=[{string.Join(",", completes)}]");
            h.Check("通关记录登记了第 2 关",
                    flow.ClearedLevels.Count == 1 && flow.ClearedLevels[0] == 2,
                    $"cleared=[{string.Join(",", flow.ClearedLevels)}]");

            flow.NotifyGoalReached();
            h.Check("同关重复触终点：通关标记去重",
                    flow.ClearedLevels.Count == 1, flow.ClearedLevels.Count.ToString());
            h.Check("同关重复触终点：事件不去重（结算 UI 每次都要收到）",
                    completes.Count == 2, $"completes={completes.Count}");

            // ==============================================================
            //  ④ 请求：只发事件，不写状态
            // ==============================================================
            h.Section("4 重开与下一关请求");

            flow.RequestRestart();
            h.Check("OnRestartRequested(2) 触发",
                    restarts.Count == 1 && restarts[0] == 2, $"restarts=[{string.Join(",", restarts)}]");
            h.Check("重开请求不改当前关卡号", flow.CurrentLevel == 2, flow.CurrentLevel.ToString());

            flow.RequestNextLevel();
            h.Check("OnNextLevelRequested(2) 触发",
                    nexts.Count == 1 && nexts[0] == 2, $"nexts=[{string.Join(",", nexts)}]");
            h.Check("下一关请求不推进关卡号（等场景加载完调 StartLevel）",
                    flow.CurrentLevel == 2, flow.CurrentLevel.ToString());

            flow.NotifyLevelFailed();
            h.Check("OnLevelFailed(2) 触发",
                    fails.Count == 1 && fails[0] == 2, $"fails=[{string.Join(",", fails)}]");

            // ==============================================================
            //  ⑤ 载入进度：只写状态，不发事件
            // ==============================================================
            h.Section("5 载入进度");

            int eventsBefore = starts.Count + completes.Count + fails.Count
                             + restarts.Count + nexts.Count;
            flow.SetProgress(4, new[] { 1, 2, 3, 3 });
            h.Check("CurrentLevel 恢复为 4", flow.CurrentLevel == 4, flow.CurrentLevel.ToString());
            h.Check("通关记录恢复且去重（传入 [1,2,3,3] 得 [1,2,3]）",
                    flow.ClearedLevels.Count == 3
                    && flow.ClearedLevels[0] == 1 && flow.ClearedLevels[1] == 2 && flow.ClearedLevels[2] == 3,
                    $"cleared=[{string.Join(",", flow.ClearedLevels)}]");
            h.Check("载入进度不触发任何事件", starts.Count + completes.Count + fails.Count
                                             + restarts.Count + nexts.Count == eventsBefore,
                    "进度恢复不是「又开了一关」");

            return h.Report("关卡流程验证");
        }
    }
}
