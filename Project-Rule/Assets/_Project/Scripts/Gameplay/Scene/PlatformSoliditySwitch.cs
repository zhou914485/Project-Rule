using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 全局「虚实平台」开关：每次被**攻击**（攻击盒 / 纸飞机）打中，切换
    /// <see cref="PlatformSolidityState.AIsSolid"/> 的 1/0。
    /// </summary>
    /// <remarks>
    /// <para><b>1 / 0</b>：1 = A 实体、B 虚化；0 = A 虚化、B 实体。平台侧由
    /// <see cref="PlatformSolidityMember"/> 监听。</para>
    ///
    /// <para><b>触发区</b>：本物体上的 Trigger 碰撞体（生成工具会放一个盖住胸口高度的）。
    /// 玩家的攻击盒 / 纸飞机打中 Trigger 就调到 <see cref="ImpactByContact"/> —— 与拉杆 / 按钮
    /// 同一个入口，**不进伤害管线**，所以「伤害 × 失效」不会把开关一起关掉。</para>
    ///
    /// <para><b>开局值</b>：<see cref="_startAIsSolid"/> 在 Awake 写一次。场景里建议只放一个
    /// 控制组件 —— 多个会互相覆盖开局值。</para>
    ///
    /// <para><b>白盒</b>：<see cref="_label"/> / <see cref="_visual"/> 跟着状态换；
    /// 接正式美术时清空它们即可，玩法判定不读。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlatformSoliditySwitch : MonoBehaviour, IContactSwitch
    {
        private static readonly Color AColor = new Color(0.3f, 0.8f, 0.95f, 1f);
        private static readonly Color BColor = new Color(0.95f, 0.55f, 0.2f, 1f);

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("全局参数")]
        [Tooltip("进入 Play 时写一次全局参数：true = 1（A 实体 / B 虚化），false = 0（相反）。\n" +
                 "场景里建议只放一个控制组件；多个会互相覆盖开局值。")]
        [SerializeField] private bool _startAIsSolid = true;

        [Header("白盒")]
        [Tooltip("显示当前状态的白盒标签。接正式美术时清空。")]
        [SerializeField] private TextMesh _label;

        [Tooltip("状态指示灯：A 实体时青色，B 实体时橙色。留空 = 不换色。")]
        [SerializeField] private SpriteRenderer _visual;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>当前全局值：true = 1，false = 0。</summary>
        public bool AIsSolid => PlatformSolidityState.AIsSolid;

        /// <summary>开局值（Inspector 显示用）。</summary>
        public bool StartAIsSolid => _startAIsSolid;

        /// <summary>当前状态的中文描述。</summary>
        public string CurrentStateName =>
            PlatformSolidityState.AIsSolid ? "A 实体 / B 虚化" : "A 虚化 / B 实体";

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            // 只写一次：之后由攻击 / 调试按钮切换，不要在 OnEnable 里重置。
            PlatformSolidityState.Set(_startAIsSolid);
        }

        private void OnEnable()
        {
            PlatformSolidityState.Changed += HandleChanged;
            Refresh();
        }

        private void OnDisable()
        {
            PlatformSolidityState.Changed -= HandleChanged;
        }

        private void HandleChanged(bool aIsSolid) => Refresh();

        // ==================================================================
        //  对外入口
        // ==================================================================

        /// <summary>切换全局参数（1 ↔ 0）。</summary>
        public void Toggle()
        {
            PlatformSolidityState.Toggle();
        }

        /// <summary>把全局参数设成指定值。重复设同一个值是幂等的。</summary>
        public void Set(bool aIsSolid)
        {
            PlatformSolidityState.Set(aIsSolid);
        }

        /// <summary>刷新白盒标签 / 指示灯。</summary>
        public void Refresh()
        {
            if (_label != null)
            {
                string wanted = CurrentStateName;
                if (_label.text != wanted) _label.text = wanted;
            }

            if (_visual != null)
            {
                _visual.color = PlatformSolidityState.AIsSolid ? AColor : BColor;
            }
        }

        // ==================================================================
        //  IContactSwitch：打一下就切
        // ==================================================================

        /// <summary>
        /// 被非玩家的东西压到。**本开关只认攻击**：石头 / 铁箱压上去不切换，
        /// 所以这里什么都不做。
        /// </summary>
        public void PressByContact()
        {
        }

        /// <summary>压着的东西离开。理由同 <see cref="PressByContact"/>。</summary>
        public void ReleaseByContact()
        {
        }

        /// <summary>
        /// 被**攻击盒 / 纸飞机**打中：切换全局 1/0。
        /// </summary>
        /// <param name="sourcePoint">从哪儿打来的。本开关不用它。</param>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!Application.isPlaying) return;

            Toggle();
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：接上白盒标签 / 指示灯与开局值。</summary>
        public void Configure(TextMesh label, SpriteRenderer visual, bool startAIsSolid)
        {
            _label = label;
            _visual = visual;
            _startAIsSolid = startAIsSolid;
            Refresh();
        }

        [ContextMenu("调试/切换 A/B（Play）")]
        private void DebugToggle()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[PlatformSoliditySwitch] 编辑模式只改全局值，平台不会立刻刷新；进 Play 再切换。", this);
                return;
            }

            Toggle();
        }

        private void OnDrawGizmosSelected()
        {
            // Trigger 本身在 Scene 视图里可见；这里在头顶再标一个状态点。
            Gizmos.color = PlatformSolidityState.AIsSolid ? AColor : BColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.2f, 0.18f);
        }
    }
}
