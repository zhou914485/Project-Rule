using System.IO;
using System.Linq;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Editor.Rules;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.LevelFlow;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Rules;
using RuleGame.Persistence;

namespace RuleGame.Editor.Persistence
{
    /// <summary>
    /// 存档系统契约验证。
    /// </summary>
    /// <remarks>
    /// 核心断言：① 小数据往返不丢字段；② 重开关卡后规则是<b>真的重新生效</b>
    /// （引擎属性值恢复，不只是槽位有值）；③ 读档重放不污染历史、不触发自动存档；
    /// ④ 坏档 / 坏 Id 不炸；⑤ 文件存储原子写入链路可用。
    /// </remarks>
    public static class SaveSystemVerifier
    {
        public static string Run()
        {
            var h = new RuleVerifyHarness();

            RuleDef[] defs = RuleVerifyHarness.MakeDefs(AttributeId.Mass, 10f);
            var catalog = new RuleVerifyHarness.TestCatalog(defs);

            // ==============================================================
            //  ① 无档启动
            // ==============================================================
            h.Section("1 无档启动");

            var storage1 = new SaveTestDoubles.MemoryStorage();
            var rules1 = new RuleSystem(catalog);
            var flow1 = new LevelFlow();
            // 带上玩家与解锁词条：否则存出来的档里这两块是空的，第 ④ 节无从验证恢复。
            var player1 = new SaveTestDoubles.FakePlayerStatus(42f, 77f);
            var unlocks1 = new SaveTestDoubles.MemoryUnlockStore(
                new[] { "Gravity", "Mass" }, new[] { "Revert", "Enhance" });
            var save1 = new SaveSystem(rules1, flow1, storage1, player1, unlocks1);

            h.Check("无档时 Load 返回 null", save1.Load() == null, "应为 null");
            h.Check("未 Apply 前历史未就绪", !save1.IsReady, save1.IsReady.ToString());
            h.Check("未就绪时 TryHasActivated 返回 false",
                    !save1.TryHasActivated(AttributeId.Mass, RelationId.Enhance, out bool beforeReady),
                    "IsReady=false 时应回答不了");

            save1.Apply(null);   // = 新游戏
            h.Check("Apply(null) 后历史就绪", save1.IsReady, save1.IsReady.ToString());
            h.Check("新游戏历史为空（查 mass.enhance = false）",
                    save1.TryHasActivated(AttributeId.Mass, RelationId.Enhance, out bool emptyHist)
                    && !emptyHist,
                    $"ret={emptyHist}");

            // ==============================================================
            //  ② 采集：激活规则 + 通关
            // ==============================================================
            h.Section("2 采集");

            RuleRequestResult req = rules1.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            h.Check("前置：质量增强请求成功", req.Succeeded, req.Status.ToString());
            h.Check("采集到激活规则 mass.enhance",
                    save1.Capture().ActiveRuleIds.Contains("mass.enhance"),
                    $"[{string.Join(",", save1.Capture().ActiveRuleIds)}]");

            flow1.NotifyGoalReached();   // 通关第 1 关（会触发一次自动存档）
            SaveData captured = save1.Capture();
            h.Check("当前关卡为 1", captured.CurrentLevel == 1, captured.CurrentLevel.ToString());
            h.Check("通关标记含 1", captured.ClearedLevels.Contains(1),
                    $"[{string.Join(",", captured.ClearedLevels)}]");
            h.Check("激活历史记录了 1 条",
                    captured.History.Count == 1
                    && captured.History[0].RuleId == "mass.enhance"
                    && captured.History[0].LevelIndex == 1,
                    $"count={captured.History.Count}");
            h.Check("历史查询口能查到激活过",
                    save1.TryHasActivated(AttributeId.Mass, RelationId.Enhance, out bool hasIt) && hasIt,
                    $"ret={hasIt}");
            h.Check("历史查询口对没发生过的规则回答 false",
                    save1.TryHasActivated(AttributeId.Gravity, RelationId.Revert, out bool notHappened)
                    && !notHappened,
                    $"ret={notHappened}");

            // ==============================================================
            //  ③ 写盘 JSON 形状
            // ==============================================================
            h.Section("3 写盘");

            save1.Save();
            string json = storage1.Read();
            h.Check("存档已写入", storage1.Exists() && !string.IsNullOrEmpty(json), "storage 应有内容");
            h.Check("JSON 含规则 Id", json != null && json.Contains("mass.enhance"), "");
            h.Check("JSON 含版本号", json != null && json.Contains("\"Version\""), "");

            // ==============================================================
            //  ④ 跨实例恢复（重开关卡模拟）：全新规则系统 + 全新存档系统读同一份档
            // ==============================================================
            h.Section("4 跨实例恢复");

            var rules2 = new RuleSystem(catalog);
            var flow2 = new LevelFlow();
            var player2 = new SaveTestDoubles.FakePlayerStatus(42f, 77f);
            var unlocks2 = new SaveTestDoubles.MemoryUnlockStore(
                new[] { "Gravity", "Mass" }, new[] { "Revert", "Enhance" });
            var save2 = new SaveSystem(rules2, flow2, storage1, player2, unlocks2);

            // 第二局先玩出点东西，确认读档会盖掉它们
            player2.ApplyData(new EntityStatusData { Energy = 5f, Health = 5f });
            rules2.Request(AttributeId.Mass, RelationId.Weaken, RuleSource.Player);

            int writesDuringApply = storage1.WriteCount;
            SaveData loaded = save2.Load();
            h.Check("读到存档", loaded != null, "null");
            int writesBeforeApply = storage1.WriteCount;
            save2.Apply(loaded);

            h.Check("规则槽位恢复（Mass/Enhance 激活）",
                    rules2.Table.IsActive(AttributeId.Mass, RelationId.Enhance),
                    "槽位应有规则");
            h.Check("规则效果真的重新执行了（MassScale ≠ 1，不是只写槽位）",
                    !Mathf.Approximately(rules2.Attributes.Values.MassScale, 1f),
                    rules2.Attributes.Values.MassScale.ToString());
            h.Check("恢复前的 Weaken 被清掉（世界先回初态再重放）",
                    !rules2.Table.IsActive(AttributeId.Mass, RelationId.Weaken),
                    "旧规则不应残留");
            h.Check("关卡进度恢复", flow2.CurrentLevel == 1, flow2.CurrentLevel.ToString());
            h.Check("通关标记恢复", flow2.ClearedLevels.Contains(1),
                    $"[{string.Join(",", flow2.ClearedLevels)}]");
            h.Check("玩家能量恢复为 42", player2.Energy == 42f, player2.Energy.ToString());
            h.Check("玩家生命恢复为 77", player2.Health == 77f, player2.Health.ToString());
            h.Check("已解锁属性恢复", unlocks2.UnlockedAttributes.Count == 2
                    && unlocks2.UnlockedAttributes.Contains("Gravity"),
                    $"[{string.Join(",", unlocks2.UnlockedAttributes)}]");
            h.Check("已解锁关系恢复", unlocks2.UnlockedRelations.Count == 2
                    && unlocks2.UnlockedRelations.Contains("Enhance"),
                    $"[{string.Join(",", unlocks2.UnlockedRelations)}]");
            h.Check("历史随档恢复（1 条）",
                    save2.TryHasActivated(AttributeId.Mass, RelationId.Enhance, out bool histAfter)
                    && histAfter,
                    $"ret={histAfter}");
            h.Check("重放不污染历史（仍为 1 条，不翻倍）",
                    save2.Capture().History.Count == 1,
                    $"count={save2.Capture().History.Count}");
            h.Check("Apply 期间不触发自动存档（写盘 0 次）",
                    storage1.WriteCount == writesBeforeApply,
                    $"before={writesBeforeApply} after={storage1.WriteCount}");

            // ==============================================================
            //  ⑤ 自动存档时机：关卡开头 / 结尾
            // ==============================================================
            h.Section("5 自动存档");

            var storage3 = new SaveTestDoubles.MemoryStorage();
            var rules3 = new RuleSystem(catalog);
            var flow3 = new LevelFlow();
            var save3 = new SaveSystem(rules3, flow3, storage3);
            save3.Apply(null);

            int w0 = storage3.WriteCount;
            flow3.StartLevel(3);
            h.Check("关卡开头自动存档一次", storage3.WriteCount == w0 + 1,
                    $"writes={storage3.WriteCount}");

            rules3.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            flow3.NotifyGoalReached();
            h.Check("关卡结尾再自动存档一次", storage3.WriteCount == w0 + 2,
                    $"writes={storage3.WriteCount}");

            SaveData autoData = save3.Load();
            h.Check("自动存档内容正确（关卡=3）", autoData != null && autoData.CurrentLevel == 3,
                    autoData == null ? "null" : autoData.CurrentLevel.ToString());
            h.Check("自动存档含本关激活的规则",
                    autoData != null && autoData.ActiveRuleIds.Contains("mass.enhance"), "");

            // ==============================================================
            //  ⑥ 坏档 / 坏 Id 容错
            // ==============================================================
            h.Section("6 容错");

            var badStorage = new SaveTestDoubles.MemoryStorage();
            badStorage.Write("{{{这不是 JSON");
            var badRules = new RuleSystem(catalog);
            var badFlow = new LevelFlow();
            var badSave = new SaveSystem(badRules, badFlow, badStorage);
            SaveData badResult = null;
            bool loadThrew = false;
            try { badResult = badSave.Load(); } catch { loadThrew = true; }
            h.Check("损坏 JSON 不抛异常", !loadThrew, "抛了");
            h.Check("损坏 JSON 返回 null", badResult == null, "应 null");

            // 坏 Id 混在好 Id 里：坏的跳过，好的照常恢复
            var mixedStorage = new SaveTestDoubles.MemoryStorage();
            var mixedData = new SaveData
            {
                ActiveRuleIds = { "totally.notarule", "mass.enhance", null },
            };
            mixedStorage.Write(JsonUtility.ToJson(mixedData));
            var mixedRules = new RuleSystem(catalog);
            var mixedFlow = new LevelFlow();
            var mixedSave = new SaveSystem(mixedRules, mixedFlow, mixedStorage);
            bool applyThrew = false;
            try { mixedSave.Apply(mixedSave.Load()); } catch { applyThrew = true; }
            h.Check("坏 Id / null Id 不抛异常", !applyThrew, "抛了");
            h.Check("好 Id 仍正常恢复",
                    mixedRules.Table.IsActive(AttributeId.Mass, RelationId.Enhance),
                    "mass.enhance 应生效");

            // ==============================================================
            //  ⑦ 文件存储（真实磁盘，临时目录）
            // ==============================================================
            h.Section("7 文件存储");

            string dir = Path.Combine(Application.temporaryCachePath,
                                     "SaveVerify_" + System.Guid.NewGuid().ToString("N"));
            var fileStorage = new FileSaveStorage(dir, "verify.json");
            h.Check("初始不存在", !fileStorage.Exists(), "");
            h.Check("读不存在的档返回 null", fileStorage.Read() == null, "");

            fileStorage.Write("{\"hello\":\"存档\"}");
            h.Check("写入后存在", fileStorage.Exists(), "");
            h.Check("读回内容一致", fileStorage.Read() == "{\"hello\":\"存档\"}",
                    fileStorage.Read() ?? "(null)");

            fileStorage.Clear();
            h.Check("清除后不存在", !fileStorage.Exists(), "");
            try { Directory.Delete(dir, true); } catch { /* 临时目录清不掉就算了 */ }

            // ==============================================================
            //  ⑧ Dispose 后不再自动存档
            // ==============================================================
            h.Section("8 解绑");

            var storage8 = new SaveTestDoubles.MemoryStorage();
            var rules8 = new RuleSystem(catalog);
            var flow8 = new LevelFlow();
            var save8 = new SaveSystem(rules8, flow8, storage8);
            save8.Apply(null);
            save8.Dispose();

            int writes8 = storage8.WriteCount;
            flow8.NotifyGoalReached();
            rules8.Request(AttributeId.Mass, RelationId.Enhance, RuleSource.Player);
            h.Check("Dispose 后关卡事件不再写盘", storage8.WriteCount == writes8,
                    $"before={writes8} after={storage8.WriteCount}");

            // ==============================================================
            //  ⑨ 世界状态 + 存档点（2026-10-20：机关 / 敌人状态 + 回满）
            // ==============================================================
            h.Section("9 世界状态与存档点");

            const string doorKey = "castle/Door[0]#broken";
            const string switchKey = "castle/Switch[3]#on";

            // 新游戏必须把上一局留下的机关状态清掉，否则「门还是碎的」会跟着进新档。
            WorldStateStore.Clear();
            WorldStateStore.SetBool(doorKey, true);
            var storage9 = new SaveTestDoubles.MemoryStorage();
            var rules9 = new RuleSystem(catalog);
            var flow9 = new LevelFlow();
            var player9 = new SaveTestDoubles.FakePlayerStatus(12f, 20f);
            var save9 = new SaveSystem(rules9, flow9, storage9, player9);

            save9.Apply(null);
            h.Check("新游戏清空世界状态", !WorldStateStore.TryGet(doorKey, out _),
                    $"count={WorldStateStore.Count}");

            // 机关改变状态 → 写进仓库 → 采集时整体带走。
            WorldStateStore.SetBool(doorKey, true);
            WorldStateStore.Set(switchKey, "0");
            SaveData shot = save9.Capture();
            h.Check("世界状态进入采集",
                    shot.WorldStates.Any(r => r.Key == doorKey && r.Value == "1")
                    && shot.WorldStates.Any(r => r.Key == switchKey && r.Value == "0"),
                    $"count={shot.WorldStates.Count}");

            // 存档点：编号落档 + 玩家回满。
            save9.SaveCheckpoint("checkpoint-03");
            h.Check("存档点编号落档", save9.LatestRespawnId == "checkpoint-03", save9.LatestRespawnId ?? "null");
            h.Check("存档点回满能量", Mathf.Approximately(player9.Energy, player9.EnergyMax),
                    $"{player9.Energy}/{player9.EnergyMax}");
            h.Check("存档点回满生命", Mathf.Approximately(player9.Health, player9.HealthMax),
                    $"{player9.Health}/{player9.HealthMax}");

            // 跨实例恢复：新系统读同一个盘，世界状态与玩家状态都要回来。
            WorldStateStore.Clear();     // 模拟重启：内存表是空的
            var player9b = new SaveTestDoubles.FakePlayerStatus(1f, 1f);
            var rules9b = new RuleSystem(catalog);
            var flow9b = new LevelFlow();
            var save9b = new SaveSystem(rules9b, flow9b, storage9, player9b);
            SaveData loaded9 = save9b.Load();
            save9b.Apply(loaded9);

            h.Check("世界状态随档恢复",
                    WorldStateStore.GetBool(doorKey) && WorldStateStore.TryGet(switchKey, out string sw9) && sw9 == "0",
                    $"count={WorldStateStore.Count}");
            h.Check("存档点编号随档恢复", save9b.LatestRespawnId == "checkpoint-03", save9b.LatestRespawnId ?? "null");
            h.Check("玩家数值随档回满（不是旧值）",
                    Mathf.Approximately(player9b.Energy, player9b.EnergyMax)
                    && Mathf.Approximately(player9b.Health, player9b.HealthMax),
                    $"{player9b.Energy}/{player9b.Health}");

            WorldStateStore.Clear();

            // ==============================================================
            //  清理
            // ==============================================================
            foreach (RuleDef d in defs) Object.DestroyImmediate(d);

            return h.Report("存档系统验证");
        }
    }
}
