var t = System.Type.GetType("RuleGame.Core.Rules.RuleSystem, RuleGame.Core");
var sb = new System.Text.StringBuilder();
if (t == null) return "找不到 RuleSystem 类型";
var instProp = t.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
var inst = instProp == null ? null : instProp.GetValue(null);
sb.Append("RuleSystem.Instance = ").Append(inst == null ? "null（没初始化）" : "ok").AppendLine();
if (inst != null)
{
    var attrsProp = t.GetProperty("Attributes");
    var attrs = attrsProp.GetValue(inst);
    var valuesProp = attrs.GetType().GetProperty("Values");
    var v = valuesProp.GetValue(attrs);
    foreach (var f in v.GetType().GetFields())
        sb.Append("  ").Append(f.Name).Append(" = ").Append(f.GetValue(v)).AppendLine();
}
var pc = GameObject.Find("Player").GetComponent("PlayerController");
var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var mi = pc.GetType().GetField("_moveInput", F);
var fa = pc.GetType().GetField("_facing", F);
sb.Append("_moveInput=").Append(mi.GetValue(pc)).Append("  _facing=").Append(fa.GetValue(pc));
return sb.ToString();
