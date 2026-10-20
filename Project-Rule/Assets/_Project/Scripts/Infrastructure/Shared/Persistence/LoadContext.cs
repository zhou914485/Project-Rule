namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 本次启动的读档上下文：存档里记的「玩家该站在哪个点」以及「这是不是续档」。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么放在基础设施层</b>：写入方是 <c>PersistenceDriver</c>（Persistence 层），
    /// 读取方是 <c>WorldStreamer</c> / <c>GameRoot</c>（Gameplay 层）。两边都依赖 Infrastructure，
    /// 彼此不能互认 —— 与 <see cref="LevelFlowHub"/> 同一条做法。</para>
    /// <para><b>只存编号，不存坐标</b>（约束 C5）：位置由场景里的 <c>PlayerSpawnPoint</c> 提供。
    /// 编号对应的点所在分区还没加载出来时，流式加载会退回默认落点（见 WorldStreamer）。</para>
    /// </remarks>
    public static class LoadContext
    {
        /// <summary>本次是「继续游戏」而不是新开一局。</summary>
        public static bool ContinueGame { get; private set; }

        /// <summary>存档点编号；没有存档时为 null。</summary>
        public static string RespawnId { get; private set; }

        /// <summary>存档点所在的分区场景路径；续档要先把它加载出来。没有存档时为 null。</summary>
        public static string RespawnScene { get; private set; }

        /// <summary>由存档驱动在 <c>Apply</c> 之后写入。</summary>
        public static void SetFromSave(SaveData data)
        {
            ContinueGame = data != null;
            RespawnId = data?.RespawnId;
            RespawnScene = data?.RespawnScene;
        }

        /// <summary>落点已经用掉：之后的开局落人不再尝试它。</summary>
        public static void ConsumeRespawnId()
        {
            RespawnId = null;
            RespawnScene = null;
        }

        /// <summary>场景卸载 / 测试之间清理。</summary>
        public static void Reset()
        {
            ContinueGame = false;
            RespawnId = null;
            RespawnScene = null;
        }
    }
}
