using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Dialogue
{
    /// <summary>
    /// P1 存档系统的可选接入边界。只回答已经发生过的事，不推测未来规则。
    /// 未接入或历史尚未加载时返回 false，不提供虚构的历史实现。
    /// </summary>
    public interface IDialogueHistorySource
    {
        bool IsReady { get; }
        bool TryHasActivated(AttributeId attribute, RelationId relation, out bool activated);
    }
}
