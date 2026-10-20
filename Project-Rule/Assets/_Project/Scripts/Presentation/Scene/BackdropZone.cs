using RuleGame.Presentation.Audio;
using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 一块「进入就换背景」的世界矩形；同一块矩形也能带一首区域音乐。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不是两个组件</b>：美术按关卡几何摆一次区域，背景和音乐就应该同时生效。
    /// 这里直接带 <c>_music</c>，<c>MusicDirector</c> 读的是同一个矩形 ——
    /// 不再需要在同一位置再摆一个 <c>MusicRegion</c>、把尺寸和 Priority 抄一遍。</para>
    /// <para><b>不带背景、只带音乐</b>：把 <c>_profile</c> 留空、<c>_music</c> 填上即可；
    /// <c>BackdropDirector</c> 会跳过没有贴图的区域，音乐仍然生效。</para>
    /// <para><b>为什么不加碰撞体</b>：判定是「玩家位置落在本地矩形里」，每帧算一次，
    /// 不依赖物理触发 —— 玩家是 Kinematic + Slide，触发回调本来就不可靠。</para>
    /// <para><b>它能随分区一起卸载</b>：场景卸载时 <c>OnDisable</c> 把自己摘出登记表，
    /// 解不到区域就回到默认背景 / 默认音乐。</para>
    /// <para><b>不要放进 <c>Grid/Art</c></b>：铺装器（Ctrl+Shift+G）会清空那个容器。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BackdropZone : AreaZone
    {
        [Tooltip("进入这个矩形用哪张背景。留空 = 只当音乐区域，不换背景。")]
        [SerializeField] private BackdropProfile _profile;

        [Tooltip("进入这个矩形切哪首背景音乐。留空 = 不参与音乐归属（继续找下层区域 / 回落默认曲）。")]
        [SerializeField] private MusicProfile _music;

        public BackdropProfile Profile => _profile;
        public override MusicProfile Music => _music;

        /// <summary>挑出该用哪块背景区域：重叠取 Priority 高的，同 Priority 取面积小的。</summary>
        public static BackdropZone Resolve(Vector3 point, BackdropZone previous, float hysteresis)
        {
            return AreaZone.Resolve(point, previous, hysteresis,
                zone => zone.Profile != null && zone.Profile.Sprite != null);
        }
    }
}
