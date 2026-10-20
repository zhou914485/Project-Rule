var sb = new System.Text.StringBuilder();
var devs = UnityEngine.InputSystem.InputSystem.devices;
sb.Append("设备总数: ").Append(devs.Count).AppendLine();
for (int i = 0; i < devs.Count; i++)
{
    var d = devs[i];
    sb.Append("  ").Append(d.GetType().Name).Append(" name=").Append(d.name).Append(" added=").Append(d.added);
    var pad = d as UnityEngine.InputSystem.Gamepad;
    if (pad != null) sb.Append(" leftStick=").Append(pad.leftStick.ReadValue());
    sb.AppendLine();
}
return sb.ToString();
