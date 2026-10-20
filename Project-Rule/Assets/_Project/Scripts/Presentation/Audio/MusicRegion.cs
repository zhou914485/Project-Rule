using RuleGame.Presentation.SceneComponents;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    /// <summary>
    /// 只有音乐、没有背景的区域；和 <see cref="BackdropZone"/> 共用同一套矩形解析。
    /// </summary>
    /// <remarks>
    /// <para><b>什么时候用它</b>：试听场景、临时的 BOSS 房、或者一块还没配背景的区域。
    /// 正式关卡里如果同一块区域已经摆了 <c>BackdropZone</c>，直接把音乐填在那个组件上，
    /// 不要再叠一个 MusicRegion。</para>
    /// <para><b>不再自己维护登记表</b>：矩形、Priority、迟滞和同优先级比较都在
    /// <see cref="AreaZone"/>；这里只负责把 <c>_profile</c> 报给音乐侧。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MusicRegion : AreaZone
    {
        [SerializeField] private MusicProfile _profile;

        public MusicProfile Profile => _profile;
        public override MusicProfile Music => _profile;
    }
}
