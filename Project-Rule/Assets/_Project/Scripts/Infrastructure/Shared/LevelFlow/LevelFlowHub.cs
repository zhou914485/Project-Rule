namespace RuleGame.Infrastructure.LevelFlow
{
    /// <summary>
    /// 全局关卡流程入口：由常驻宿主（<c>GameRoot</c>）写入，存档驱动读取。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：真正的 <see cref="ILevelFlow"/> 在 <c>GameRoot</c>（Gameplay 层），
    /// 而存档驱动在 Persistence 层 —— 后者只能依赖 Core + Infrastructure，看不见 Gameplay。
    /// 没有这个入口，存档就会自己 <c>new</c> 一个流程，永远不会收到真实的关卡事件（进度存不下来、
    /// 自动存档也不触发）。</para>
    /// <para>与 <c>TimeManager.Initialize()</c> 同一种做法：基础设施层放一个静态当前实例，
    /// 由知道全局的那个场景组件写入。</para>
    /// </remarks>
    public static class LevelFlowHub
    {
        /// <summary>当前关卡流程；场景还没起来时为 null。</summary>
        public static ILevelFlow Current { get; private set; }

        public static void Set(ILevelFlow flow) => Current = flow;

        /// <summary>场景卸载 / 测试之间清理。</summary>
        public static void Reset() => Current = null;
    }
}
