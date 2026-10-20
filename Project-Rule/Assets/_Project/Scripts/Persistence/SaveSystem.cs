using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Dialogue;
using RuleGame.Infrastructure.LevelFlow;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Persistence
{
    /// <summary>
    /// <see cref="ISaveSystem"/> 的默认实现：采集 / 恢复小数据 + JSON 读写 + 自动存档。
    /// </summary>
    /// <remarks>
    /// <para><b>纯 C# 类</b>（与 RuleSystem / SaveSystem 同层保持一致的风格），
    /// 由 <see cref="PersistenceDriver"/> 在场景启动时装配。</para>
    /// <para><b>事件协作，不反向调用</b>（架构 §3.3 的同一条纪律）：
    /// 订阅 <c>IRuleSystem.OnRuleActivated</c> 攒历史、订阅 <c>ILevelFlow</c>
    /// 的关卡开头 / 结尾事件自动存档。Gameplay 层不需要知道存档存在。</para>
    /// <para>本类同时实现 <see cref="IDialogueHistorySource"/> —— NPC 台词要查的「正式存档」
    /// 就是它，台词文档明确要求不要再攒第二份历史。</para>
    /// </remarks>
    public sealed class SaveSystem : ISaveSystem, IDialogueHistorySource
    {
        // ==================================================================
        //  依赖
        // ==================================================================

        private readonly IRuleSystem      _rules;
        private readonly ILevelFlow       _levelFlow;
        private readonly ISaveStorage     _storage;
        private readonly IEntityStatus    _player;       // 可空：P1 早期可能没接玩家
        private readonly IRuleUnlockStore _unlocks;      // 可空：玩家解锁系统排期更晚

        // ==================================================================
        //  状态
        // ==================================================================

        /// <summary>内存中的激活历史。磁盘里的历史在 <see cref="Apply"/> 时灌入。</summary>
        private readonly List<RuleEventRecord> _history = new List<RuleEventRecord>();

        /// <summary>
        /// 应用存档期间为 true：抑制两件事 —— 规则重放写入历史、关卡事件触发自动存档。
        /// </summary>
        /// <remarks>不抑制就会出现：一读档，重放的规则全被记成「这一关新激活的」，
        /// 历史瞬间翻倍；Apply 内部的状态流还可能把刚读到的档覆盖回盘。</remarks>
        private bool _applying;

        private bool _isReady;

        /// <summary>最近一次存档点的编号；没存过点是 null。</summary>
        private string _respawnId;

        // ==================================================================
        //  构造 / 生命周期
        // ==================================================================

        /// <param name="rules">规则系统（激活规则采集 / 重放 / 历史事件源）。</param>
        /// <param name="levelFlow">关卡流程（进度采集 / 自动存档时机）。</param>
        /// <param name="storage">存档存放处（生产用 <see cref="FileSaveStorage"/>）。</param>
        /// <param name="player">玩家状态；没有玩家实体的场景（主菜单 / 测试）传 null。</param>
        /// <param name="unlocks">玩家解锁词条存储；解锁系统落地前传 null（解锁列表存空）。</param>
        public SaveSystem(IRuleSystem rules, ILevelFlow levelFlow, ISaveStorage storage,
                          IEntityStatus player = null, IRuleUnlockStore unlocks = null)
        {
            _rules     = rules     ?? throw new ArgumentNullException(nameof(rules));
            _levelFlow = levelFlow ?? throw new ArgumentNullException(nameof(levelFlow));
            _storage   = storage   ?? throw new ArgumentNullException(nameof(storage));
            _player    = player;
            _unlocks   = unlocks;

            _rules.OnRuleActivated    += HandleRuleActivated;
            _levelFlow.OnLevelStart   += HandleLevelBoundary;
            _levelFlow.OnLevelComplete+= HandleLevelBoundary;
        }

        /// <summary>解绑全部事件。引导组件 OnDestroy 时调用，避免重编译后旧实例残留订阅。</summary>
        public void Dispose()
        {
            _rules.OnRuleActivated    -= HandleRuleActivated;
            _levelFlow.OnLevelStart   -= HandleLevelBoundary;
            _levelFlow.OnLevelComplete-= HandleLevelBoundary;
        }

        // ==================================================================
        //  ISaveSystem：采集
        // ==================================================================

        public SaveData Capture()
        {
            var data = new SaveData
            {
                ActiveRuleIds = _rules.ExportActiveIds(),
                CurrentLevel  = _levelFlow.CurrentLevel,
                ClearedLevels = new List<int>(_levelFlow.ClearedLevels),
                PlayerStatus  = _player != null ? _player.ToData() : null,
                RespawnId     = _respawnId,

                // 世界状态是「全量照片」：组件在改变时写进 WorldStateStore，
                // 卸载的分区也留在表里，所以这里整体导出即可，不需要枚举场景对象。
                WorldStates   = WorldStateStore.Export(),
            };

            // 历史 / 解锁做元素级拷贝：存档是那一刻的照片，不能与运行时列表共享引用。
            data.History.Capacity = _history.Count;
            for (int i = 0; i < _history.Count; i++)
            {
                RuleEventRecord r = _history[i];
                data.History.Add(new RuleEventRecord
                {
                    RuleId = r.RuleId,
                    LevelIndex = r.LevelIndex,
                    Time = r.Time,
                });
            }

            if (_unlocks != null)
            {
                data.UnlockedAttributes = new List<string>(_unlocks.UnlockedAttributes);
                data.UnlockedRelations  = new List<string>(_unlocks.UnlockedRelations);
            }

            return data;
        }

        // ==================================================================
        //  ISaveSystem：应用
        // ==================================================================

        public void Apply(SaveData data)
        {
            _applying = true;
            try
            {
                // null = 新游戏：世界保持设计初态，只把规则表清干净、历史置空。
                if (data == null)
                {
                    _rules.ClearAll();
                    _history.Clear();
                    _respawnId = null;

                    // 新游戏：世界回到设计初态 —— 清掉上一局留下的机关状态，
                    // 否则「门还是碎的 / 开关还是按下的」会跟着进新档。
                    WorldStateStore.Clear();
                    return;
                }

                // ⓪ 世界状态先灌回仓库：随后加载出来的分区会在 Start 里各自查表恢复。
                _respawnId = data.RespawnId;
                WorldStateStore.Import(data.WorldStates);

                // ① 进度（SetProgress 只写状态不发事件，不会在这一步触发自动存档）。
                _levelFlow.SetProgress(data.CurrentLevel, data.ClearedLevels);

                // ② 解锁词条。
                _unlocks?.RestoreUnlocks(data.UnlockedAttributes, data.UnlockedRelations);

                // ③ 玩家数值。
                if (_player != null && data.PlayerStatus != null)
                    _player.ApplyData(data.PlayerStatus);

                // ④ 历史直接换成档里的（之后重放规则时抑制追加，见 HandleRuleActivated）。
                _history.Clear();
                if (data.History != null) _history.AddRange(data.History);

                // ⑤ 跨关卡激活规则：世界已在设计初态（新场景 / ClearAll），逐条重新 Request，
                //    让重力 / 摩擦 / 质量等引擎效果真正生效。
                //    不能只 RestoreSlots —— 那只写槽位，不执行规则类的效果。
                _rules.ClearAll();
                if (data.ActiveRuleIds != null)
                {
                    for (int i = 0; i < data.ActiveRuleIds.Count; i++)
                        ReapplyRule(data.ActiveRuleIds[i]);
                }
            }
            finally
            {
                // 无论上面哪一步抛异常都要解除抑制，否则后续存档全哑火。
                _applying = false;
                _isReady = true;

                // 读档到此结束：之后组件的状态写入才允许落进仓库（否则 OnEnable 的默认值会冲掉存档）。
                WorldStateStore.MarkLoaded();
            }
        }

        /// <summary>
        /// 把一条规则 Id（如 <c>"mass.enhance"</c>）解析回枚举并重新请求生效。
        /// </summary>
        private void ReapplyRule(string ruleId)
        {
            if (!TryParseRuleId(ruleId, out AttributeId attr, out RelationId rel))
            {
                Debug.LogWarning($"[SaveSystem] 无法识别的规则 Id，已跳过：\"{ruleId}\"。");
                return;
            }

            // Request 会执行规则效果并广播事件，但不扣能量 —— 扣费在 RuleReleaseService，
            // 存档恢复走的是系统层入口，不是玩家释放。
            // LevelScript 来源：这是关卡 / 存档在说话，不是玩家或敌人的行为。
            RuleRequestResult result = _rules.Request(attr, rel, RuleSource.LevelScript);
            if (!result.Succeeded)
            {
                // catalog 缺该格位（如规则类尚未实现）时 Request 返回 Undefined。
                // 跳过比整档报废好，但必须留痕 —— 丢的是玩家进度。
                Debug.LogWarning($"[SaveSystem] 规则 {ruleId} 重新应用失败（{result.Status}），已跳过。");
            }
        }

        /// <summary>拆分 <c>"attribute.relation"</c> 并大小写不敏感地解析回枚举。</summary>
        private static bool TryParseRuleId(string ruleId, out AttributeId attr, out RelationId rel)
        {
            attr = default;
            rel = default;
            if (string.IsNullOrEmpty(ruleId)) return false;

            // 只按第一个点拆：枚举名本身不含点，Id 格式由 RuleDef.Id 固定。
            int dot = ruleId.IndexOf('.');
            if (dot <= 0 || dot == ruleId.Length - 1) return false;

            string attrPart = ruleId.Substring(0, dot);
            string relPart  = ruleId.Substring(dot + 1);

            return Enum.TryParse(attrPart, true, out attr)
                && Enum.TryParse(relPart,  true, out rel);
        }

        // ==================================================================
        //  ISaveSystem：读写磁盘
        // ==================================================================

        public void Save()
        {
            SaveData data = Capture();
            string json = JsonUtility.ToJson(data);
            _storage.Write(json);
        }

        /// <summary>
        /// 存档点存档：先把玩家<b>回满</b>，再按当前世界写一次盘。
        /// </summary>
        /// <remarks>
        /// <para>存档点同时是补给点（玩家走到存档点 = 喘口气），所以先回血回能量再采集 ——
        /// 采集发生在回满之后，写进档里的就是满状态，读档回来也是满的。</para>
        /// <para><see cref="WorldStateStore"/> 里已经是全量世界状态（含已卸载分区），
        /// 所以这里不需要再枚举场景对象。</para>
        /// </remarks>
        public void SaveCheckpoint(string respawnPointId)
        {
            if (!string.IsNullOrEmpty(respawnPointId)) _respawnId = respawnPointId;
            RefillPlayer();
            Save();
        }

        /// <summary>最近一次存档点编号；没存过点是 null。</summary>
        public string LatestRespawnId => _respawnId;

        /// <summary>
        /// 把能量 / 生命回满（并清冷却）。
        /// </summary>
        /// <remarks>先 ToData 再改两项再 ApplyData：<c>ApplyData</c> 会整体覆盖，
        /// 直接传一个新建的 data 会把玩家手里的东西清掉。</remarks>
        private void RefillPlayer()
        {
            if (_player == null) return;

            EntityStatusData full = _player.ToData();
            full.Energy = _player.EnergyMax;
            full.Health = _player.HealthMax;
            _player.ApplyData(full);
        }

        public SaveData Load()
        {
            string json = _storage.Read();
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                SaveData data = JsonUtility.FromJson<SaveData>(json);
                if (data == null) return null;

                // 版本是未来迁移的入口。当前只有版本 1：字段缺失 JsonUtility 给默认值，
                // 天然兼容「加字段」；不兼容的结构变更在这里按 data.Version 分流迁移。
                if (data.Version > SaveData.CurrentVersion)
                {
                    Debug.LogWarning($"[SaveSystem] 存档来自更新的版本（{data.Version} > " +
                                     $"{SaveData.CurrentVersion}），按当前版本尽力读取。");
                }
                return data;
            }
            catch (Exception e)
            {
                // 坏档不能让游戏启动失败 —— 当作新游戏，但必须喊出来。
                Debug.LogError($"[SaveSystem] 存档反序列化失败，按无档处理：{e.Message}");
                return null;
            }
        }

        // ==================================================================
        //  IDialogueHistorySource：NPC 回顾式台词的查询口
        // ==================================================================

        /// <summary>存档已加载 / 应用完成。未就绪时台词系统对历史问题一律返回「不知道」。</summary>
        public bool IsReady => _isReady;

        public bool TryHasActivated(AttributeId attribute, RelationId relation, out bool activated)
        {
            activated = false;
            if (!_isReady) return false;

            string id = MakeRuleId(attribute, relation);
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].RuleId == id) { activated = true; break; }
            }
            return true;
        }

        // ==================================================================
        //  事件处理
        // ==================================================================

        private void HandleRuleActivated(AttributeId attr, RelationId rel, RuleSource source)
        {
            // 读档重放规则也会触发本事件 —— 那些历史已经在档里了，不能再记一遍。
            if (_applying) return;

            _history.Add(new RuleEventRecord
            {
                RuleId = MakeRuleId(attr, rel),
                LevelIndex = _levelFlow.CurrentLevel,
                Time = UnityEngine.Time.time,
            });
        }

        private void HandleLevelBoundary(int level)
        {
            // 策划案 §十：每关开头 / 结尾自动存档。
            // Apply 内部的进度恢复不能触发存盘 —— 否则刚加载就把读到的数据写回去，
            // 既多余，也可能在重放完成前落盘成一个半成品状态。
            if (_applying) return;
            Save();
        }

        /// <summary>与 <c>RuleDef.Id</c> 同一格式：属性.关系，全小写。</summary>
        private static string MakeRuleId(AttributeId attr, RelationId rel)
            => $"{attr}.{rel}".ToLowerInvariant();
    }
}
