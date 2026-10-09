using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Development.P4
{
    /// <summary>
    /// 规则矩阵的 UI 预览记录。非重力的费用和时长是演示参数；重力以 P2 RuleDef 为准。
    /// 字符串 ID 必须与 UXML 词条按钮 name 一致。
    /// </summary>
    [Serializable]
    public sealed class DemoRulePreview
    {
        public string AttributeId;
        public string RelationId;
        public string Title;
        [TextArea] public string Description;
        [Min(0f)] public float Cost = 10f;
        public float Duration = 30f;
        public bool PendingDesign; // 未定稿仍允许测试 UI，但在预览里明确提示。
    }

    [Serializable]
    // JsonUtility 需要对象根节点，用包装类读取 JSON 中的 Previews 数组。
    public sealed class DemoRuleCatalog
    {
        public List<DemoRulePreview> Previews;
    }
}
