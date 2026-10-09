using System;
using System.Collections.Generic;
using RuleGame.Infrastructure.Rules;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RuleGame.Gameplay.Dialogue
{
    /// <summary>
    /// L2 校验：Yarn 编译器负责语法；此处补充项目的规则 ID、场景引用、显式变量和兜底约束。
    /// 只检查约定，不执行表达式；条件解释完全交给 Yarn。
    /// </summary>
    public static class DialogueContentValidator
    {
        private static readonly Regex RuleCall = new Regex("\\b(?:rule_active|rule_seen)\\s*\\(\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\s*\\)");
        private static readonly Regex FlagCall = new Regex("\\bscene_flag\\s*\\(\\s*\"([^\"]+)\"\\s*\\)");
        private static readonly Regex Variable = new Regex(@"\$[A-Za-z_]\w*");
        private static readonly Regex Declaration = new Regex(@"<<declare\s+(\$[A-Za-z_]\w*)\b");
        private static readonly Regex LineId = new Regex(@"#line:([^\s]+)");

        public static List<string> Validate(DialogueContent content, DialogueWorldContext context = null)
        {
            var errors = new List<string>();
            if (content == null) { errors.Add("未配置 DialogueContent。"); return errors; }
            if (content.Project == null || content.Project.compiledYarnProgram == null || content.Project.compiledYarnProgram.Length == 0)
                errors.Add("Yarn Project 未成功编译。请先修复 .yarn 导入错误。");
            if (content.Scripts == null || content.Scripts.Length == 0) errors.Add("未配置外置 .yarn 台词文件。");
            if (context != null && !context.IsHistoryReferenceValid) errors.Add("历史组件必须实现 IDialogueHistorySource，或留空。");
            if (context != null) errors.AddRange(context.ValidateSceneBindings());

            var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
            var lineIds = new HashSet<string>(StringComparer.Ordinal);
            var declared = new HashSet<string>();
            var referenced = new HashSet<string>();
            foreach (TextAsset script in content.Scripts ?? Array.Empty<TextAsset>())
            {
                if (script == null) { errors.Add("台词文件引用丢失。"); continue; }
                // 去掉整行注释，避免示例、解释和废弃台词误报为正在使用的引用。
                string source = Regex.Replace(script.text, @"(?m)^\s*//[^\r\n]*", "");
                if (Regex.IsMatch(source, @"(?m)^\s*->"))
                    errors.Add(script.name + " 包含玩家选项；首版 bark 显示器只接收台词，请先接入 Yarn 的选项显示组件。");
                foreach (Match match in Declaration.Matches(source)) declared.Add(match.Groups[1].Value);
                foreach (Match match in Variable.Matches(source)) referenced.Add(match.Value);
                foreach (Match match in LineId.Matches(source))
                    if (!lineIds.Add(match.Groups[1].Value)) errors.Add("重复台词 id：" + match.Groups[1].Value);
                foreach (Match match in RuleCall.Matches(source))
                    if (!DialogueWorldContext.TryRuleIds(match.Groups[1].Value, match.Groups[2].Value, out _, out _))
                        errors.Add("未知规则 ID：" + match.Value);
                foreach (Match match in Regex.Matches(source, "\\brule_present\\s*\\(\\s*\"([^\"]+)\"\\s*\\)"))
                    if (!Enum.TryParse(match.Groups[1].Value, true, out AttributeId attribute)
                        || !Enum.IsDefined(typeof(AttributeId), attribute)) errors.Add("未知属性 ID：" + match.Value);
                if (context != null)
                    foreach (Match match in FlagCall.Matches(source))
                        if (!context.HasSceneFlag(match.Groups[1].Value)) errors.Add("未绑定场景状态：" + match.Groups[1].Value);
                foreach (Match match in Regex.Matches(source, @"(?ms)^title:\s*([^\r\n]+)\r?\n(.*?)^===[ \t]*\r?$"))
                {
                    string name = match.Groups[1].Value.Trim();
                    // 项目首版使用唯一入口节点 + line groups，避免同名节点和作者 ID 混淆。
                    if (nodes.ContainsKey(name)) errors.Add("重复节点 id：" + name);
                    else nodes.Add(name, match.Groups[2].Value);
                }
            }
            foreach (string variable in referenced)
                if (!declared.Contains(variable)) errors.Add("变量需要显式 declare 初始值：" + variable);

            var speakerIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueSpeaker speaker in content.Speakers ?? Array.Empty<DialogueSpeaker>())
            {
                if (speaker == null || string.IsNullOrWhiteSpace(speaker.Id) || !speakerIds.Add(speaker.Id))
                { errors.Add("NPC id 为空或重复。"); continue; }
                if (string.IsNullOrWhiteSpace(speaker.EntryNode) || !nodes.TryGetValue(speaker.EntryNode, out string entry))
                    errors.Add(speaker.Id + " 的入口节点不存在。");
                else if (!Regex.IsMatch(entry, @"(?m)^\s*=>\s*[^\r\n<]+#line:[^\r\n]+$"))
                    errors.Add(speaker.Id + " 的 line group 缺少无条件兜底台词。");
                if (string.IsNullOrWhiteSpace(speaker.FallbackNode) || !nodes.TryGetValue(speaker.FallbackNode, out string fallback))
                    errors.Add(speaker.Id + " 的兜底节点不存在。");
                else if (Regex.IsMatch(fallback, @"\bwhen:|<<|^\s*(?:->|=>)", RegexOptions.Multiline)
                    || !LineId.IsMatch(fallback))
                    errors.Add(speaker.Id + " 的兜底节点必须包含无条件正文。");
            }
            if (speakerIds.Count == 0) errors.Add("至少配置一个 NPC 内容入口。");
            return errors;
        }
    }
}
