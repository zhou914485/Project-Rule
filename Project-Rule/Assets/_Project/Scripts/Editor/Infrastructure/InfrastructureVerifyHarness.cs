using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Infrastructure
{
    /// <summary>
    /// 基础设施层验证的公共骨架与统一入口。
    /// </summary>
    /// <remarks>
    /// <para>断言骨架复用 <see cref="RuleGame.Editor.Rules.RuleVerifyHarness"/>；
    /// 本类只负责把三个验证器串起来。</para>
    /// <para>三个验证器都按<b>编辑模式与 Play 模式均可运行</b>设计：
    /// 不依赖 <c>Awake</c> / <c>OnEnable</c> / 物理步进。</para>
    /// </remarks>
    public static class InfrastructureVerifyHarness
    {
        [MenuItem("RuleGame/P1/运行基础设施验证")]
        public static void RunFromMenu()
        {
            string report = RunAll();
            Debug.Log(report);
        }

        /// <summary>跑全部基础设施验证，返回完整报告。</summary>
        public static string RunAll()
            => TimeManagerVerifier.Run()
             + "\n\n" + LevelFlowVerifier.Run()
             + "\n\n" + SnapshotSystemVerifier.Run();
    }
}
