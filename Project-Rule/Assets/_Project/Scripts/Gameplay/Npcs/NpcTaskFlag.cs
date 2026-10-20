using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using UnityEngine;

namespace RuleGame.Gameplay.Npcs
{
    /// <summary>
    /// 把机关的真实状态写进 NPC 的任务条件 <see cref="DialogueSceneFlag"/>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么每帧同步而不是只听一次事件</b>：读档恢复发生在各机关自己的
    /// <c>Start</c> 里（<c>WorldStateStore</c>），跨对象执行顺序不保证；轮询读到的是最终状态，
    /// 存档里也自然带回来，不需要第二份进度记录。</para>
    ///
    /// <para><b>条件是只读投影</b>：本组件不反向写机关、不碰规则表；
    /// 「任务完成」的判据就是机关现在的样子（齿轮在转 / 开关通电）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NpcTaskFlag : MonoBehaviour
    {
        [SerializeField, Tooltip("任务条件组件；齿轮人读它决定台词分支。")]
        private DialogueSceneFlag _flag;

        [Header("状态来源（二选一）")]
        [SerializeField, Tooltip("齿轮：转起来 = 完成。")]
        private RustyGear _gear;

        [SerializeField, Tooltip("开关：通电 = 完成。")]
        private CitySwitch _switch;

        private bool? _applied;

        private void OnEnable() => Sync(true);

        // 读档恢复在 Start 里，这里持续跟着机关走；值没变时不做任何事。
        private void Update() => Sync(false);

        /// <summary>立刻重新读一次来源（测试 / 编辑器改值后可用）。</summary>
        public void Sync() => Sync(true);

        private void Sync(bool force)
        {
            if (_flag == null) return;

            bool value = ReadState();
            if (!force && _applied == value) return;

            _applied = value;
            _flag.SetValue(value);
        }

        private bool ReadState()
        {
            if (_gear != null) return _gear.IsActivated;
            if (_switch != null) return _switch.IsOn;
            return false;
        }
    }
}
