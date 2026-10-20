var sb = new System.Text.StringBuilder();
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (!a.GetName().Name.StartsWith("RuleGame")) continue;
    var t = a.GetType("RuleGame.Development.P3.AttackTestDummy");
    sb.Append(a.GetName().Name).Append(" -> ").Append(t == null ? "无" : "有").AppendLine();
}
var go = GameObject.Find("Dummy");
sb.Append("Dummy = ").Append(go == null ? "找不到" : go.name);
return sb.ToString();
