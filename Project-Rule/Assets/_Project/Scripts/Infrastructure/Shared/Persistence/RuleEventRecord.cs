using System;

namespace RuleGame.Infrastructure.Persistence
{
    /// <summary>
    /// 一条规则激活历史记录（接口文档 §4.4）。
    /// </summary>
    /// <remarks>
    /// <para>只记录「什么时候、在哪一关、哪条规则被激活过」，<b>供 NPC 回顾式台词引用</b>
    /// （策划案 §十、台词系统接入文档）。不记录规则参数、不记录实体、不记录物理状态。</para>
    /// <para><see cref="RuleId"/> 形如 <c>"gravity.revert"</c>，与 <c>RuleDef.Id</c> 同一格式。</para>
    /// </remarks>
    [Serializable]
    public class RuleEventRecord
    {
        /// <summary>规则 Id（属性.关系，小写），与 <c>RuleDef.Id</c> 一致。</summary>
        public string RuleId;

        /// <summary>激活时所在的关卡号。</summary>
        public int LevelIndex;

        /// <summary>激活时刻（<c>Time.time</c>，秒）。仅用于台词 / 调试，不参与游戏逻辑。</summary>
        public float Time;
    }
}
