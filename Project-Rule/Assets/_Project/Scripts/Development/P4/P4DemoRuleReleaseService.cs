using System.Collections.Generic;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using UnityEngine;

namespace RuleGame.Development.P4
{
    /// <summary>
    /// P3 释放服务落地前的临时边界，仅用于重力演示，不新增团队共享接口。
    /// 检查能量与冷却，向 P2 请求；成功才返回应扣费用并开始冷却。
    /// </summary>
    public sealed class P4DemoRuleReleaseService
    {
        private readonly IRuleSystem _rules;
        private readonly IRuleCatalog _catalog;
        private readonly Dictionary<RelationId, float> _cooldowns = new();
        private float _clock;

        public P4DemoRuleReleaseService(IRuleSystem rules, IRuleCatalog catalog)
        {
            _rules = rules;
            _catalog = catalog;
        }

        public bool CanRelease(RelationId relation, float energy, out string reason)
        {
            if (_catalog.Get(AttributeId.Gravity, relation) == null)
            {
                reason = "这条重力规则尚未配置。";
                return false;
            }
            if (_cooldowns.TryGetValue(relation, out float until) && until > _clock)
            {
                reason = $"冷却还剩 {Mathf.CeilToInt(until - _clock)} 秒。";
                return false;
            }
            float cost = _rules.GetCost(AttributeId.Gravity, relation).Energy;
            if (energy < cost)
            {
                reason = $"还差 {Mathf.CeilToInt(cost - energy)} 能量，按 R 重置。";
                return false;
            }
            reason = "已接入世界：所有受重力影响的动态刚体一起生效。";
            return true;
        }

        public bool TryRelease(RelationId relation, float energy, out float spent, out string reason)
        {
            // 默认不扣费，调用方必须先检查返回值；拒绝请求时 spent 保持为零。
            spent = 0f;
            if (!CanRelease(relation, energy, out reason))
            {
                return false;
            }
            RuleCost cost = _rules.GetCost(AttributeId.Gravity, relation);
            RuleRequestResult result = _rules.Request(AttributeId.Gravity, relation, RuleSource.Player);
            if (!result.Succeeded)
            {
                reason = result.Status == RuleRequestStatus.NoResource
                    ? "这条规则缺少所需资源，没有扣费。"
                    : "规则系统尚未实现这个组合，没有扣费。";
                return false;
            }
            spent = cost.Energy;
            if (cost.Cooldown > 0f)
            {
                _cooldowns[relation] = _clock + cost.Cooldown;
            }
            return true;
        }

        // 由演示控制器在世界未暂停时调用，冷却与规则寿命遵循同一暂停边界。
        public void Tick(float deltaTime) => _clock += deltaTime;
        public void Reset() => _cooldowns.Clear();
    }
}
