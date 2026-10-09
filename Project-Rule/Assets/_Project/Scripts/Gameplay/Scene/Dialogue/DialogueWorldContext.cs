using System;
using System.Collections.Generic;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using UnityEngine;

namespace RuleGame.Gameplay.Dialogue
{
    /// <summary>
    /// 只读世界状态桥接。当前规则直接读 P2；物件状态读物件自身；历史留给 P1。
    /// 不订阅激活事件累积一份“当前规则”，避免过期、覆盖或读档后台词穿帮。
    /// </summary>
    public sealed class DialogueWorldContext : MonoBehaviour
    {
        [SerializeField, Tooltip("可留空。未来填入实现 IDialogueHistorySource 的存档适配组件。")]
        private MonoBehaviour _historySource;
        [SerializeField] private DialogueSceneFlag[] _sceneFlags = Array.Empty<DialogueSceneFlag>();
        private IRuleSystem _boundRules;
        private IDialogueHistorySource History => _historySource as IDialogueHistorySource;
        private IRuleSystem Rules => _boundRules ?? RuleSystem.Instance;

        /// <summary>测试或正式组合根可显式注入；传 null 后恢复读取现有 P2 单例。</summary>
        public void BindRules(IRuleSystem rules) => _boundRules = rules;
        public bool WorldReady() => Rules != null;
        public bool HistoryReady() => History != null && History.IsReady;

        /// <summary>供历史回顾判断该属性目前是否仍有规则，不把历史句优先于其他当前状态。</summary>
        public bool RulePresent(string attribute)
        {
            return Rules != null && Enum.TryParse(attribute, true, out AttributeId attr)
                && Enum.IsDefined(typeof(AttributeId), attr) && Rules.Table.GetSlot(attr).HasValue;
        }

        public bool RuleActive(string attribute, string relation)
        {
            if (Rules == null || !TryRuleIds(attribute, relation, out AttributeId attr, out RelationId rel))
                return false;
            ActiveRule? slot = Rules.Table.GetSlot(attr);
            return slot.HasValue && slot.Value.Relation == rel;
        }

        public bool RuleSeen(string attribute, string relation)
        {
            return HistoryReady() && TryRuleIds(attribute, relation, out AttributeId attr, out RelationId rel)
                && History.TryHasActivated(attr, rel, out bool activated) && activated;
        }

        public bool SceneFlag(string key)
        {
            foreach (DialogueSceneFlag flag in _sceneFlags)
                if (flag != null && flag.Key == key) return flag.Value;
            return false;
        }

        public bool HasSceneFlag(string key)
        {
            foreach (DialogueSceneFlag flag in _sceneFlags)
                if (flag != null && flag.Key == key) return true;
            return false;
        }

        public bool IsHistoryReferenceValid => _historySource == null || History != null;

        public List<string> ValidateSceneBindings()
        {
            var errors = new List<string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueSceneFlag flag in _sceneFlags)
            {
                if (flag == null) { errors.Add("场景状态组件引用丢失；不用时请移除数组元素。"); continue; }
                if (string.IsNullOrWhiteSpace(flag.Key) || !keys.Add(flag.Key))
                    errors.Add("场景状态 key 为空或重复：" + flag.Key);
            }
            return errors;
        }

        public static bool TryRuleIds(string attribute, string relation, out AttributeId attr, out RelationId rel)
        {
            bool validAttribute = Enum.TryParse(attribute, true, out attr) && Enum.IsDefined(typeof(AttributeId), attr);
            bool validRelation = Enum.TryParse(relation, true, out rel) && Enum.IsDefined(typeof(RelationId), rel);
            return validAttribute && validRelation;
        }
    }
}
