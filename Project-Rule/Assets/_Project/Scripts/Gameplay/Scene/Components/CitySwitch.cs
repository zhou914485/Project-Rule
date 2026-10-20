using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>摇杆可反复切换，按钮可单次锁定；通过 UnityEvent 连门、电梯或炸弹，不按物体名字找对象。</summary>
    /// <remarks>
    /// <para>三条触发路径：**玩家交互**（<c>CityInteractor</c> → <see cref="Interact"/>）、
    /// **重物压到**（<see cref="IContactSwitch"/>，巨石 / 铁箱 / 弹珠在触发回调里调）、
    /// **玩家走进触发区**（<c>_pressByPlayerProximity</c>，默认关，给「踩上去」的按钮用）。
    /// 后两条都是**加出来的**，不改第一条 —— 不打开就完全不影响现有按钮。</para>
    /// </remarks>
    public sealed class CitySwitch : CityInteractable, IContactSwitch
    {
        public bool Toggle = true;
        public bool IsOn;

        [Tooltip("接正式美术后勾上：刷新状态时不再改 SpriteRenderer 颜色（改由贴图/帧序列表达）。")]
        public bool KeepArtColor;

        [Header("拉杆表现")]
        [Tooltip("会跟着开关扳动的把手（子物体）。**留空 = 只换颜色、不转** —— 按钮类不填。\n" +
                 "⚠️ 绕它自己的轴心转：预制品里指向 Visual，轴心在根部才像拉杆（中心轴心看起来像跷跷板）。")]
        [SerializeField] private Transform _handle;

        [Tooltip("关状态的角度（绕 Z，度）。")]
        [SerializeField] private float _offAngle = 25f;

        [Tooltip("开状态的角度（绕 Z，度）。")]
        [SerializeField] private float _onAngle = -35f;

        [Tooltip("扳动速度（度 / 秒）。小于等于 0 = 直接到位。")]
        [SerializeField] private float _handleDegreesPerSecond = 540f;

        [Tooltip("这个开关**驱动谁**：把带 CityPathMotor（门 / 电梯）或 GravityBoulder（巨石）的 **GameObject** 拖过来即可 ——\n" +
                 "拖哪个组件都能解析到目标（Unity 拖 GameObject 时只会赋第一个 MonoBehaviour，这里做了兜底）。\n" +
                 "门/电梯直接改目标端；其它走 ISwitchTarget.SetSwitchState。")]
        public MonoBehaviour FollowMotor;

        /// <summary>FollowMotor 当成路径机（门 / 电梯）看。</summary>
        /// <remarks>⚠️ 字段类型是 <c>MonoBehaviour</c>：往这种字段拖 GameObject 时，Unity 赋值的是它身上
        /// **第一个** MonoBehaviour。CityMechanism 的 <c>[RequireComponent(typeof(Rigidbody2D), typeof(RuleAffectedBody))]</c>
        /// 让 <c>RuleAffectedBody</c> 永远排在 <c>CityPathMotor</c> 前面，于是拖出来的引用指向 RuleAffectedBody
        /// （Level_Building_01 的两扇门就是这么坏的）。所以先 <c>as</c>，再退一步到**同一个物体**上找 ——
        /// 拖的是哪个组件都能解析对。</remarks>
        private CityPathMotor Motor =>
            FollowMotor as CityPathMotor ?? (FollowMotor != null ? FollowMotor.GetComponent<CityPathMotor>() : null);

        /// <summary>FollowMotor 当成「被开关驱动的东西」看（巨石等）。</summary>
        private ISwitchTarget Target =>
            FollowMotor as ISwitchTarget ?? (FollowMotor != null ? FollowMotor.GetComponent<ISwitchTarget>() : null);

        [Tooltip("玩家**走进触发区**就算按下（不用交互键）。默认关 —— 现有按钮仍然只认 CityInteractor。\n" +
                 "给「踩上去 / 摸一下」的按钮用，例如巨石断剑机关的解锁按钮。\n" +
                 "⚠️ 判定走 OverlapCollider 轮询，不是触发回调（玩家的触发回调不可靠）。")]
        public bool PressByPlayerProximity;
        public UnityEvent<bool> OnChanged = new UnityEvent<bool>();
        public override bool Available => Toggle || !IsOn;
        public override bool Interact(PlayerController player)
        {
            if (!Available || Paused) return false;
            bool on = Motor != null ? !Motor.TargetEnd : !IsOn;
            SetOn(on);
            return true;
        }
        // ------------------------------------------------------------------
        //  IContactSwitch：被重物压到（不是玩家按的）
        // ------------------------------------------------------------------

        /// <summary>被巨石 / 铁箱 / 弹珠压到：直接打开。</summary>
        public void PressByContact()
        {
            if (Paused) return;
            SetOn(true);
        }

        /// <summary>
        /// 压着的东西走了。
        /// </summary>
        /// <remarks>⚠️ <b>单次锁定的按钮（<c>Toggle = false</c>）忽略它</b> ——
        /// 按下去就锁住，压着的东西走开也不该自己弹回来（与 <see cref="Available"/> 的语义一致）。
        /// 摇杆（<c>Toggle = true</c>）才跟着离开复位。</remarks>
        public void ReleaseByContact()
        {
            if (Paused || !Toggle) return;
            SetOn(false);
        }

        // ------------------------------------------------------------------
        //  IContactSwitch：被打中（攻击盒 / 投射物）
        // ------------------------------------------------------------------

        /// <summary>
        /// 被攻击盒或投射物**打中**：切换型翻转，单次按钮按下即锁死。
        /// </summary>
        /// <remarks>
        /// <para>翻转规则与 <see cref="Interact"/> 完全一致（都看 <c>Motor.TargetEnd</c>）——
        /// 「砍一下拉杆」和「按 E 扳拉杆」对电梯是同一个结果。</para>
        /// <para>⚠️ 与 <see cref="PressByContact"/> 是两条语义：那条是「压住 → 离开」的保持型
        /// （石头 / 敌人 / 巨石当压板用），这条是一次性边沿，没有「离开」。</para>
        /// </remarks>
        /// <param name="sourcePoint">从哪儿打来的。两态开关用不到它 —— 多档拉杆才按位置选档。</param>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (Paused || !Available) return;

            if (!Toggle)
            {
                SetOn(true);                        // 按钮：打中就锁死，与按 E 一致
                return;
            }

            SetOn(Motor != null ? !Motor.TargetEnd : !IsOn);
        }

        public void SetOn(bool on)
        {
            if (Motor != null) Motor.SetOn(on);
            else if (Target != null) Target.SetSwitchState(on);
            if (IsOn == on) return;
            IsOn = on; RefreshVisual(); OnChanged.Invoke(IsOn);
            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, IsOn);
        }

        private void Start()
        {
            // 读档恢复：这个开关在存档里是按下的，打开场景就要保持按下（连带把它驱动的门也带上）。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.TryGet(SaveStateKey, out string saved)) SetOn(saved == "1");

            RefreshVisual();
            WarnIfTargetUnsupported();
        }

        /// <summary>存档用的稳定键（场景名 + 层级路径 + 槽位）。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "on");

        /// <summary>FollowMotor 拖了既不认路径机、也不实现 <see cref="ISwitchTarget"/> 的东西 —— 报一次。</summary>
        private void WarnIfTargetUnsupported()
        {
            if (FollowMotor == null || Motor != null || Target != null) return;
            if (_warnedTarget) return;

            _warnedTarget = true;
            Debug.LogWarning(
                $"[CitySwitch] {name} 的 FollowMotor 拖的是 {FollowMotor.GetType().Name}，" +
                "它既不是 CityPathMotor 也没实现 ISwitchTarget —— 这个开关驱动不了它。", this);
        }

        private bool _warnedTarget;

        /// <summary>编辑期就报错：FollowMotor 拖了什么都不是的东西 —— 不用等到 Play 才发现。</summary>
        /// <remarks>判据与 <see cref="WarnIfTargetUnsupported"/> 完全一致，只是时机提前到 Inspector 里。</remarks>
        private void OnValidate()
        {
            if (Application.isPlaying) return;
            if (FollowMotor == null || Motor != null || Target != null) return;
            Debug.LogError(
                $"[CitySwitch] {name} 的 FollowMotor 拖的是 {FollowMotor.GetType().Name}，" +
                "它既不是 CityPathMotor 也没实现 ISwitchTarget —— 这个开关驱动不了它。", this);
        }

        /// <summary>探测缓存（只在 PressByPlayerProximity 打开时用）。</summary>
        private readonly Collider2D[] _probe = new Collider2D[8];

        /// <summary>
        /// 玩家走进触发区就按下。
        /// </summary>
        /// <remarks>⚠️ 用 <c>OverlapCollider</c> 轮询而不是 <c>OnTriggerEnter2D</c>：
        /// 玩家是 Kinematic + <c>Slide</c>（<c>useAttachedTriggers = false</c>），触发回调不可靠
        /// —— 与 <c>ImpactButton</c> / <c>SwordInStone</c> 同一条做法。</remarks>
        private void FixedUpdate()
        {
            if (!PressByPlayerProximity || IsOn || Shape == null) return;
            if (Paused) return;

            var filter = new ContactFilter2D { useTriggers = true };
            int count = Shape.Overlap(filter, _probe);

            for (int i = 0; i < count; i++)
            {
                if (!PlayerLookup.IsPlayer(_probe[i])) continue;
                SetOn(true);
                return;
            }
        }
        private void LateUpdate()
        {
            if (Motor != null && IsOn != Motor.TargetEnd) { IsOn = Motor.TargetEnd; RefreshVisual(); }
            AnimateHandle();
        }

        /// <summary>把把手从当前角度扳向 <see cref="IsOn"/> 对应的角度。没接把手就什么都不做。</summary>
        private void AnimateHandle()
        {
            if (_handle == null) return;

            float target = IsOn ? _onAngle : _offAngle;
            Vector3 euler = _handle.localEulerAngles;

            // localEulerAngles 会把角度折回 0~360，MoveTowardsAngle 负责沿最短的弧走。
            float z = _handleDegreesPerSecond > 0f
                ? Mathf.MoveTowardsAngle(euler.z, target, _handleDegreesPerSecond * Time.deltaTime)
                : target;

            if (Mathf.Approximately(z, euler.z)) return;
            _handle.localRotation = Quaternion.Euler(euler.x, euler.y, z);
        }

        private void RefreshVisual()
        {
            if (!KeepArtColor && Visuals.Length > 0)
                Visuals[0].color = IsOn ? new Color(.3f, .75f, .5f) : new Color(.85f, .6f, .2f);
        }
        protected override object CaptureMechanismState() => IsOn;
        protected override void RestoreMechanismState(object state)
        {
            IsOn = (bool)state; RefreshVisual();
            // 不重发联动事件：目标自己的快照已经恢复，重发会把门/电梯再次启动。
        }
    }
}
