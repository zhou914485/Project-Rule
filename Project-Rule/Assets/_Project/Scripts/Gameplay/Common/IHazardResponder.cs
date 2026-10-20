using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 危险格（刺）的承受方回调：由 <c>HazardField</c> 在伤害结算之后、同一物理帧内调用。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么和 <c>IDamageable</c> 分开</b>：伤害照常走战斗链路，本接口只表达
    /// 「碰到刺了」这件事，让承受方自己决定要不要做额外处理 —— 目前只有玩家实现
    /// （回到安全落点），敌人碰到刺照样只掉血。</para>
    /// <para><b>只在 <c>HazardField</c> 里调用</b>：敌人攻击、爆炸、接触伤害都不会走到这里。
    /// 以后别的陷阱要同一行为，实现本接口即可，不必认识玩家或场景组件。</para>
    /// <para>⚠️ 调用时机在 <c>ApplyDamage</c> <b>之后</b>：此时伤害 / 无敌帧 / 击退都已经结算，
    /// 实现方可以安全地清掉击退、改写位置。</para>
    /// </remarks>
    public interface IHazardResponder
    {
        /// <summary>
        /// 碰到了危险格。
        /// </summary>
        /// <param name="hazardPoint">命中的刺格中心（世界坐标），供表现层定位。</param>
        /// <returns><c>true</c> = 承受方已经接管处理（例如把玩家搬回安全点）。</returns>
        bool OnHazardContact(Vector2 hazardPoint);
    }
}
