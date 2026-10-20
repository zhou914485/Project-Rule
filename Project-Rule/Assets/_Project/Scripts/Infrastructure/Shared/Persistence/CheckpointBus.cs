using System;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 存档点请求总线：场景里的存档点广播「我这儿该存一次」，存档系统订阅后落盘。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么放基础设施层</b>：存档点在 <c>RuleGame.Gameplay</c>，存档驱动在
    /// <c>RuleGame.Persistence</c>；Gameplay 不能依赖 Persistence（分层铁律），所以中间加一条
    /// 只带字符串的总线 —— 与 <c>IRuleSystem</c> 的事件协作是同一条做法。</para>
    /// <para>参数是存档点编号（<c>PlayerSpawnPoint.Id</c>），落盘时原样存进 <c>SaveData.RespawnId</c>。</para>
    /// </remarks>
    public static class CheckpointBus
    {
        /// <summary>存档点被激活（玩家走到 / 开局落人）。参数是存档点编号。</summary>
        public static event Action<string> Requested;

        /// <summary>由 <c>PlayerSpawnPoint</c> 调用。</summary>
        public static void Request(string spawnPointId)
        {
            if (string.IsNullOrEmpty(spawnPointId)) return;
            Requested?.Invoke(spawnPointId);
        }

        /// <summary>测试 / 重载时清空订阅，避免旧实例残留。</summary>
        public static void Reset() => Requested = null;
    }
}
