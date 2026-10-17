using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Persistence
{
    /// <summary>
    /// 存档验证的菜单入口。编辑模式与 Play 模式均可运行（纯 C# + 内存替身）。
    /// </summary>
    public static class PersistenceVerifyHarness
    {
        [MenuItem("RuleGame/P1/运行存档验证")]
        public static void RunFromMenu()
        {
            Debug.Log(SaveSystemVerifier.Run());
        }
    }
}
