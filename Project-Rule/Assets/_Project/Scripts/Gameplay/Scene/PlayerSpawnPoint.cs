using System;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.Events;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 落点 —— <b>出生点与存档点合并后的唯一组件</b>。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么合并</b>（2026-10-18 定）：出生点与存档点描述的是同一件事 ——
    /// 「玩家该站在哪」。分成两个组件时，「开局落在哪」和「死了回哪」各有一套写法；
    /// 而且检查点只能靠<c>世界快照</c>记位置，玩家一走出加载半径、分区卸载，快照里的实体就
    /// 被销毁，复活只恢复半个世界。合并后只有<b>一份数据</b>：玩家自己记住最近一次激活的落点
    /// （<see cref="PlayerController.RespawnPosition"/>），跟世界快照彻底脱钩。</para>
    /// <para><b>它做三件事</b>：</para>
    /// <list type="number">
    /// <item>开局：<see cref="WorldStreamer"/> 挑一个点调 <see cref="Arrive"/>（落人 + 记成复活点，
    /// 不播表现、不写盘）；</item>
    /// <item>途中：玩家用攻击盒 / 飞刀打中它（<see cref="IContactSwitch.ImpactByContact"/>）——
    /// 第一次播表现并写盘，之后只重播表现；</item>
    /// <item>死亡：<c>PlayerController</c> 自己回到记住的位置 —— 本组件不参与复活。</item>
    /// </list>
    /// <para><b>为什么改成攻击触发</b>（2026-10-20）：与按钮 / 宝箱走同一条
    /// <see cref="IContactSwitch"/> 路径，不再靠距离轮询自动存档。已经存过的点再被打中
    /// <b>只播表现</b>（<see cref="_onActivated"/> / <see cref="OnActivated"/>），
    /// 不重复写盘、也不改玩家当前复活点 —— 否则从后面的进度走回来碰一下，档就被洗回旧点了。</para>
    /// <para><b>「存过」记在世界状态仓库</b>：<see cref="WorldStateStore"/> 的 <c>saved</c> 槽位随
    /// <c>SaveData.WorldStates</c> 落盘，跨分区卸载 / 读档都还在；新游戏 <c>Apply(null)</c> 会清空
    /// 仓库，所以重开一局会重新开始。</para>
    /// <para><b>为什么判定走碰撞体而不是距离</b>：攻击盒 / 飞刀只认碰撞体（<c>OverlapBox</c> /
    /// 扫掠 + <c>useTriggers</c>），所以想被攻击的落点必须挂一个 Trigger 碰撞体。
    /// 玩家是 Kinematic + <c>Slide</c>（<c>useAttachedTriggers = false</c>），触发回调不可靠，
    /// 因此这里也不监听 <c>OnTriggerEnter2D</c>。</para>
    /// <para><b>不写盘</b>：只广播 <see cref="SaveRequested"/>（参数是 <see cref="Id"/>），
    /// 磁盘交给 <c>ISaveSystem</c>。玩家侧的落点是内存字段 —— 约束 C5 不序列化物理状态，
    /// 落盘时只需要存 <c>RespawnId</c> 这种小数据，位置由场景里的点提供。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnPoint : MonoBehaviour, IContactSwitch
    {
        /// <summary>关卡入口用的默认落点编号。</summary>
        public const string DefaultId = "start";

        [Header("落点")]
        [Tooltip("落点编号。入口固定用 start（全世界只能有一个）；其余用 checkpoint-NN，不能重名。")]
        [SerializeField] private string _id = DefaultId;

        [Tooltip("出生朝向：1 朝右，−1 朝左。")]
        [SerializeField] private int _facing = 1;

        [Tooltip("没有常驻宿主时（直接打开关卡场景调试）自己把玩家搬过来，并记成复活点")]
        [SerializeField] private bool _applyWhenStandalone = true;

        [Tooltip("被攻击盒 / 飞刀打中才存档。关掉 = 只能由脚本 Activate（开局落人 / 车站到达）。")]
        [SerializeField] private bool _activateByAttack = true;

        [Tooltip("已经是当前点、再次手动调用 Activate 时是否重新广播一次。" +
                 "攻击路径不受它限制：打中未存过的点一定会播表现并存档。")]
        [SerializeField] private bool _reactivate;

        [Header("事件")]
        [Tooltip("激活瞬间的表现钩子（秋千过场 / 音效 / 特效）。攻击命中时每次都会触发；" +
                 "已经存过的点也照样重播。")]
        [SerializeField] private UnityEvent _onActivated = new UnityEvent();

        [Tooltip("请求写盘；参数是落点编号。落盘系统落地后在这里接线，现在只是广播。")]
        [SerializeField] private UnityEvent<string> _saveRequested = new UnityEvent<string>();

        private PlayerController _player;
        private bool _saved;

        /// <summary>落点编号；留空按默认点处理。</summary>
        public string Id => string.IsNullOrWhiteSpace(_id) ? DefaultId : _id;

        /// <summary>出生朝向：+1 右，−1 左。目前只作为标记数据（与合并前一致）。</summary>
        public int Facing => _facing >= 0 ? 1 : -1;

        /// <summary>本次运行里是否已经被激活过。运行时不序列化 —— 重开世界就该重新开始。</summary>
        public bool Activated { get; private set; }

        /// <summary>
        /// 这个点是否已经存过档（跨读档有效）。
        /// </summary>
        /// <remarks>读 <see cref="WorldStateStore"/>；<see cref="_saved"/> 只是没有存档驱动时的
        /// 本次运行兜底，避免同一个点连续攻击时重复请求写盘。</remarks>
        public bool Saved => _saved || WorldStateStore.GetBool(SaveStateKey);

        /// <summary>激活瞬间（表现层 / 测试用）。</summary>
        public event Action<PlayerSpawnPoint> OnActivated;

        // ==================================================================
        //  开局兜底
        // ==================================================================

        private void Start()
        {
            SyncSaved();

            // 有常驻宿主：开局落人交给 WorldStreamer（它要等分区加载完）。
            // 兜底只认默认点，避免一个场景里多个点互相抢。
            if (!_applyWhenStandalone) return;
            if (GameRoot.Instance != null) return;
            if (Id != DefaultId) return;

            Arrive();
        }

        // ==================================================================
        //  落人与记录
        // ==================================================================

        /// <summary>
        /// 把玩家搬到这里，并把它记成复活点。由 <see cref="WorldStreamer"/> 与单场景兜底调用。
        /// </summary>
        /// <returns>落人成功返回 true；找不到玩家返回 false（调用方可以稍后重试）。</returns>
        public bool Arrive()
        {
            PlayerController player = Apply();
            if (player == null) return false;

            // 开局落人**不播表现、不写盘**：读档刚恢复的存档点（比如 checkpoint-05）不能被开局的
            // start 点覆盖掉，也不该在读档时弹一次秋千过场。
            ActivateInternal(player, requestSave: false, playFeedback: false, force: false);
            return true;
        }

        /// <summary>只把玩家搬到这里，<b>不动</b>复活点记录。</summary>
        /// <returns>被搬动的玩家；找不到时返回 null。</returns>
        public PlayerController Apply()
        {
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null)
            {
                Debug.LogWarning("[PlayerSpawnPoint] 场景里没有 PlayerController，落点没有生效。", this);
                return null;
            }

            _player.TeleportTo(transform.position);
            return _player;
        }

        /// <summary>
        /// 把这个点记成玩家的复活点。
        /// </summary>
        /// <remarks>
        /// 位置写进玩家自己的状态（<see cref="PlayerController.SetRespawn"/>），本组件不留副本 ——
        /// 玩家在哪、死回哪，只有一份真相。车站到达也走这里（它会写盘）。
        /// </remarks>
        public bool Activate(PlayerController player) => Activate(player, requestSave: true);

        /// <param name="requestSave">是否广播「该存盘了」。开局落人传 false（见 <see cref="Arrive"/>）。</param>
        public bool Activate(PlayerController player, bool requestSave)
            => ActivateInternal(player, requestSave, playFeedback: true, force: false);

        /// <summary>
        /// 记复活点 + 按需播表现 / 写盘。
        /// </summary>
        /// <param name="force">true = 忽略「已经是当前点」的提前返回（攻击路径用）。</param>
        private bool ActivateInternal(PlayerController player, bool requestSave, bool playFeedback, bool force)
        {
            if (player == null) return false;

            // 判断「这个点是否已经是玩家当前的落点」——不是看本组件自己的 Activated。
            // 这样来回经过 A → B → A 时，每次换点都会重新广播，而站在同一个点上不会刷屏。
            bool wasCurrent = player.HasRespawn && player.RespawnId == Id;
            bool changed = force || !wasCurrent || _reactivate;

            Activated = true;
            player.SetRespawn(Id, transform.position);

            if (playFeedback && changed) RaiseActivated();
            if (requestSave && changed) RequestSave();

            return changed;
        }

        // ==================================================================
        //  IContactSwitch：被攻击盒 / 投射物打中
        // ==================================================================

        /// <summary>
        /// 被攻击盒 / 飞刀打中：第一次存档，已经存过的点只重播表现。
        /// </summary>
        /// <remarks>
        /// <para>与按钮 / 宝箱同一条路径，<b>不进伤害管线</b>（落点不是 <c>IDamageable</c>）。</para>
        /// <para>已经存过时<b>不改玩家当前复活点</b>：玩家可能已经从更后面的存档点走过来，
        /// 碰一下旧点就悄悄改复活位置，会和盘里的 <c>RespawnId</c> 对不上。</para>
        /// </remarks>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!_activateByAttack) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            // 入口点只负责开局落人；攻击它不该把档洗回开头。
            if (Id == DefaultId) return;

            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null) return;

            SyncSaved();
            if (Saved)
            {
                Activated = true;
                RaiseActivated();
                return;
            }

            // 第一次：记复活点 + 播表现 + 写盘。force 绕过「玩家已经站在这个点上」的提前返回。
            ActivateInternal(_player, requestSave: true, playFeedback: true, force: true);
        }

        /// <summary>落点不是压板：石头 / 敌人压上来不会激活。</summary>
        public void PressByContact() { }

        /// <summary>落点不是压板：压着的东西走开也不需要复位。</summary>
        public void ReleaseByContact() { }

        /// <summary>搭建工具用：写编号。运行时不要调（编号是场景数据）。</summary>
        public void SetId(string id) => _id = id;

        // ==================================================================
        //  存盘 / 表现
        // ==================================================================

        /// <summary>存档用的稳定键（场景名 + 层级路径 + 槽位）。</summary>
        private string SaveStateKey => WorldStateStore.KeyOf(this, "saved");

        /// <summary>把「这个点存过了」写进世界状态仓库；随下一次存档一起落盘。</summary>
        private void RequestSave()
        {
            _saved = true;
            WorldStateStore.SetBool(SaveStateKey, true);

            _saveRequested?.Invoke(Id);

            // 落盘：走基础设施层的总线，存档驱动订阅后写盘（Gameplay 不认识 Persistence）。
            // 存档点同时是补给点 —— 落盘时会把玩家回满（见 SaveSystem.SaveCheckpoint）。
            CheckpointBus.Request(Id, gameObject.scene.path);
        }

        /// <summary>读档后同步一次「这个点存过没有」。</summary>
        private void SyncSaved()
        {
            if (_saved) return;
            _saved = WorldStateStore.GetBool(SaveStateKey);
        }

        /// <summary>播一次激活表现（UnityEvent + C# 事件）。</summary>
        private void RaiseActivated()
        {
            _onActivated?.Invoke();
            OnActivated?.Invoke(this);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <remarks>
        /// 用 OnDrawGizmosSelected 而不是 OnDrawGizmos：
        /// 后者在 Game 视图开启 Gizmos 时也会画出来，跑起来能看见一个绿圈。
        /// </remarks>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + new Vector3(Facing * 0.6f, 0f, 0f));
        }
    }
}
