using UnityEngine;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Editor.Infrastructure
{
    /// <summary>
    /// 时间管理器契约验证：倍率合成、来源去重、暂停、时间机制开关。
    /// </summary>
    /// <remarks>
    /// <para>核心断言是策划案 §3.1 那条红线：<b>暂停绝不碰 <c>Time.timeScale</c></b>。</para>
    /// </remarks>
    public static class TimeManagerVerifier
    {
        public static string Run()
        {
            var h = new RuleGame.Editor.Rules.RuleVerifyHarness();
            TimeManager.Initialize();
            TimeManager tm = TimeManager.Instance;
            if (tm == null) return "✗ TimeManager 初始化失败";

            int scaleEvents = 0;
            int pauseEvents = 0;
            int mechanicsEvents = 0;
            tm.OnTimeScaleChanged += _ => scaleEvents++;
            tm.OnPausedChanged += _ => pauseEvents++;
            tm.OnTimeMechanicsChanged += _ => mechanicsEvents++;

            // ==============================================================
            //  ① 初始化：干净状态
            // ==============================================================
            h.Section("1 初始化");

            h.Check("初始倍率为 1", tm.TimeScale == 1f, tm.TimeScale.ToString());
            h.Check("引擎 timeScale 被重置为 1", Time.timeScale == 1f, Time.timeScale.ToString());
            h.Check("默认不暂停", !tm.IsPaused, tm.IsPaused.ToString());
            h.Check("时间机制默认可用", tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());

            // ==============================================================
            //  ② 倍率合成：按来源去重 + 乘法合成
            // ==============================================================
            h.Section("2 倍率合成");

            tm.PushScale(TimeSource.Rule, 0.5f);
            h.Check("推送 Rule=0.5 后倍率 0.5", tm.TimeScale == 0.5f, tm.TimeScale.ToString());
            h.Check("引擎 timeScale 同步", Time.timeScale == 0.5f, Time.timeScale.ToString());
            h.Check("OnTimeScaleChanged 触发一次", scaleEvents == 1, $"events={scaleEvents}");

            tm.PushScale(TimeSource.Rule, 0.25f);
            h.Check("同来源重复推送是覆盖不是累乘（0.25 而非 0.125）",
                    tm.TimeScale == 0.25f, tm.TimeScale.ToString());

            tm.PushScale(TimeSource.UI, 0.5f);
            h.Check("多来源乘法合成（0.25 × 0.5 = 0.125）",
                    tm.TimeScale == 0.125f, tm.TimeScale.ToString());

            tm.RemoveScale(TimeSource.Rule);
            h.Check("移除 Rule 后回到 0.5", tm.TimeScale == 0.5f, tm.TimeScale.ToString());

            tm.RemoveScale(TimeSource.Rule);
            h.Check("移除不存在的来源不报错也不变值", tm.TimeScale == 0.5f, tm.TimeScale.ToString());

            tm.RemoveScale(TimeSource.UI);
            h.Check("全部移除后回到 1", tm.TimeScale == 1f, tm.TimeScale.ToString());

            tm.PushScale(TimeSource.Rule, -1f);
            h.Check("负倍率被拒绝", tm.TimeScale == 1f, tm.TimeScale.ToString());
            tm.RemoveScale(TimeSource.Rule);
            h.Check("被拒绝的来源确实没进表（移除它无效果）", tm.TimeScale == 1f, tm.TimeScale.ToString());

            // ==============================================================
            //  ③ 暂停：停物理，不碰 timeScale（策划案 §3.1 红线）
            // ==============================================================
            h.Section("3 暂停");

            pauseEvents = 0;
            tm.SetPaused(true);
            h.Check("IsPaused 为 true", tm.IsPaused, tm.IsPaused.ToString());
            h.Check("物理模拟切到 Script（停步进）",
                    Physics2D.simulationMode == SimulationMode2D.Script,
                    Physics2D.simulationMode.ToString());
            h.Check("timeScale 保持 1（绝不为 0）", Time.timeScale == 1f, Time.timeScale.ToString());
            h.Check("OnPausedChanged 触发一次", pauseEvents == 1, $"events={pauseEvents}");
            h.Check("暂停期间 DeltaTime 为 0", tm.DeltaTime == 0f, tm.DeltaTime.ToString());

            tm.SetPaused(true);
            h.Check("重复设置同一状态不重复广播", pauseEvents == 1, $"events={pauseEvents}");

            tm.SetPaused(false);
            h.Check("恢复后物理模拟切回 FixedUpdate",
                    Physics2D.simulationMode == SimulationMode2D.FixedUpdate,
                    Physics2D.simulationMode.ToString());
            h.Check("恢复后 DeltaTime 与 Time.deltaTime 一致",
                    tm.DeltaTime == Time.deltaTime,
                    $"tm={tm.DeltaTime}, engine={Time.deltaTime}");

            // ==============================================================
            //  ④ 时间机制开关：只切开关，不改倍率
            // ==============================================================
            h.Section("4 时间机制开关");

            mechanicsEvents = 0;
            tm.SetTimeMechanicsEnabled(false);
            h.Check("开关置 false", !tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());
            h.Check("倍率不受影响", tm.TimeScale == 1f, tm.TimeScale.ToString());
            h.Check("引擎 timeScale 不受影响", Time.timeScale == 1f, Time.timeScale.ToString());
            h.Check("OnTimeMechanicsChanged 触发一次", mechanicsEvents == 1, $"events={mechanicsEvents}");

            tm.SetTimeMechanicsEnabled(false);
            h.Check("重复设置同一状态不重复广播", mechanicsEvents == 1, $"events={mechanicsEvents}");

            tm.SetTimeMechanicsEnabled(true);
            h.Check("开关恢复 true", tm.TimeMechanicsEnabled, tm.TimeMechanicsEnabled.ToString());

            // ==============================================================
            //  清理
            // ==============================================================
            tm.SetPaused(false);
            TimeManager.Reset();
            Time.timeScale = 1f;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;

            return h.Report("时间管理器验证");
        }
    }
}
