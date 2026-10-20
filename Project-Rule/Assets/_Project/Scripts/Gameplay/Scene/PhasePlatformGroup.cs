using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 一个按钮同时管的一组 <see cref="PhasePlatform"/>。
    /// </summary>
    /// <remarks>
    /// <para><c>CitySwitch.FollowMotor</c> 只能拖一个对象，而「按一下按钮、好几块虚实平台一起翻」
    /// 是常见摆法：把平台们接到这个组上，再把**组**拖给按钮。</para>
    ///
    /// <para>单块平台也可以不用组 —— <see cref="PhasePlatform"/> 自己就实现了
    /// <see cref="ISwitchTarget"/>，直接拖到 <c>FollowMotor</c> 上一样能切。</para>
    ///
    /// <para><b>按钮是点按式</b>（<c>CitySwitch.Toggle = false</c>）：组把每次按下原样转发给
    /// 每块平台，平台**自己翻转**（不看 <c>on</c>）。转发之后组会把接在自己身上的按钮复位成
    /// 「未按下」（<c>PhaseButtonRelease</c>），所以每块平台上的按钮**任何时候都能再按**，
    /// 不会出现「按了另一个按钮没反应」。</para>
    ///
    /// <para>切换延迟住在每块平台自己的 <see cref="PhasePlatform"/> 上（<c>_switchDelay</c>，默认 0.5 秒）：
    /// 一次按键在同一帧转发给全部平台，倒计时同时开始、同时到点，看上去就是整组一起切。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PhasePlatformGroup : MonoBehaviour, ISwitchTarget
    {
        [Tooltip("这个按钮管哪些平台。留空不会报错，但那些平台就不会动。\n" +
                 "Inspector 上的「从子物体收集平台」可以一键填。")]
        [SerializeField] private PhasePlatform[] _platforms = new PhasePlatform[0];

        /// <summary>同一帧里多路来源（多个按钮 / 近战 + 纸飞机）只转发一次。</summary>
        private int _lastSwitchFrame = -1;

        /// <summary>这次按下之后要把接在本组上的按钮复位成「未按下」。</summary>
        private bool _releaseButtons;

        /// <summary>接在本组上的按钮缓存（点按式复位用）。</summary>
        private CitySwitch[] _buttons;

        /// <summary>接了几块平台。</summary>
        public int Count => _platforms != null ? _platforms.Length : 0;

        /// <summary>
        /// 一次按下：转发给组里每一块平台（平台自己翻转），然后把按钮复位成「未按下」。
        /// </summary>
        public void SetSwitchState(bool on)
        {
            if (Time.frameCount == _lastSwitchFrame) return;   // 同一帧只算一次按下
            _lastSwitchFrame = Time.frameCount;

            if (_platforms != null)
            {
                for (int i = 0; i < _platforms.Length; i++)
                {
                    if (_platforms[i] != null) _platforms[i].SetSwitchState(on);
                }
            }

            _releaseButtons = true;
        }

        /// <summary>点按式按钮的复位：按完立刻变回「未按下」，下一次按才是完整的一次按下。</summary>
        private void LateUpdate()
        {
            if (!_releaseButtons) return;
            _releaseButtons = false;
            PhaseButtonRelease.ReleaseAll(this, ref _buttons);
        }

        /// <summary>只给编辑器工具用：填平台列表。</summary>
        public void SetPlatforms(PhasePlatform[] platforms)
        {
            _platforms = platforms ?? new PhasePlatform[0];
        }
    }
}
