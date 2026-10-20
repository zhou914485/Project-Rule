using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;

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
    /// <item>开局：<see cref="WorldStreamer"/> 挑一个点调 <see cref="Arrive"/>（落人 + 记成复活点）；</item>
    /// <item>途中：玩家走进 <see cref="_activateRadius"/> 自动 <see cref="Activate"/>，刷新复活点；</item>
    /// <item>死亡：<c>PlayerController</c> 自己回到记住的位置 —— 本组件不参与复活。</item>
    /// </list>
    /// <para><b>为什么用距离轮询，不用触发回调</b>：玩家是 Kinematic + <c>Slide</c>
    /// （<c>useAttachedTriggers = false</c>），触发回调不可靠 —— 与
    /// <c>CitySwitch.PressByPlayerProximity</c> 同一条做法。判定范围是<b>点位的球形半径</b>，
    /// 不要求场景里配碰撞体，所以一个空物体就能当存档点。</para>
    /// <para><b>不写盘</b>：只广播 <see cref="SaveRequested"/>（参数是 <see cref="Id"/>），
    /// 磁盘交给以后的 <c>ISaveSystem</c>。玩家侧的落点是内存字段 —— 约束 C5 不序列化物理状态，
    /// 落盘时只需要存 <c>RespawnId</c> 这种小数据，位置由场景里的点提供。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnPoint : MonoBehaviour
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

        [Tooltip("玩家走进这个半径就把它记成复活点。0 = 只能靠开局或手动激活。")]
        [SerializeField, Min(0f)] private float _activateRadius = 1.2f;

        [Tooltip("已经是当前点、再次手动调用 Activate 时是否重新广播一次。" +
                 "自动轮询不会重复触发：同一个点只在它成为当前点的那一次广播。")]
        [SerializeField] private bool _reactivate;

        [Header("事件")]
        [Tooltip("激活瞬间的表现钩子（音效 / 特效 / 提示）。")]
        [SerializeField] private UnityEvent _onActivated = new UnityEvent();

        [Tooltip("请求写盘；参数是落点编号。落盘系统落地后在这里接线，现在只是广播。")]
        [SerializeField] private UnityEvent<string> _saveRequested = new UnityEvent<string>();

        /// <summary>判定间隔（秒）。存档点很少，没必要每帧算距离。</summary>
        private const float PollInterval = 0.15f;

        private PlayerController _player;
        private float _nextPoll;
        private bool _inside;

        /// <summary>落点编号；留空按默认点处理。</summary>
        public string Id => string.IsNullOrWhiteSpace(_id) ? DefaultId : _id;

        /// <summary>出生朝向：+1 右，−1 左。目前只作为标记数据（与合并前一致）。</summary>
        public int Facing => _facing >= 0 ? 1 : -1;

        /// <summary>自动激活半径（米）。</summary>
        public float ActivateRadius => _activateRadius;

        /// <summary>本次运行里是否已经被激活过。运行时不序列化 —— 重开世界就该重新开始。</summary>
        public bool Activated { get; private set; }

        /// <summary>激活瞬间（表现层 / 测试用）。</summary>
        public event Action<PlayerSpawnPoint> OnActivated;

        // ==================================================================
        //  开局兜底
        // ==================================================================

        private void Start()
        {
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

            // 开局落人**不写盘**：读档刚恢复的存档点（比如 checkpoint-05）不能被开局的
            // start 点覆盖掉，否则每次启动都会把进度洗回第一关。
            Activate(player, requestSave: false);
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
        /// 玩家在哪、死回哪，只有一份真相。
        /// </remarks>
        public bool Activate(PlayerController player) => Activate(player, requestSave: true);

        /// <param name="requestSave">是否广播「该存盘了」。开局落人传 false（见 <see cref="Arrive"/>）。</param>
        public bool Activate(PlayerController player, bool requestSave)
        {
            if (player == null) return false;

            // 判断「这个点是否已经是玩家当前的落点」——不是看本组件自己的 Activated。
            // 这样来回经过 A → B → A 时，每次换点都会重新广播，而站在同一个点上不会刷屏。
            bool wasCurrent = player.HasRespawn && player.RespawnId == Id;

            Activated = true;
            player.SetRespawn(Id, transform.position);

            // 已经是当前点：默认不重播；_reactivate 只保留给手动 Activate。
            if (wasCurrent && !_reactivate) return false;

            _onActivated?.Invoke();
            OnActivated?.Invoke(this);

            if (requestSave)
            {
                _saveRequested?.Invoke(Id);

                // 落盘：走基础设施层的总线，存档驱动订阅后写盘（Gameplay 不认识 Persistence）。
                // 存档点同时是补给点 —— 落盘时会把玩家回满（见 SaveSystem.SaveCheckpoint）。
                RuleGame.Infrastructure.Persistence.CheckpointBus.Request(Id, gameObject.scene.path);
            }
            return true;
        }

        /// <summary>搭建工具用：写编号。运行时不要调（编号是场景数据）。</summary>
        public void SetId(string id) => _id = id;

        // ==================================================================
        //  途中自动激活
        // ==================================================================

        private void Update()
        {
            if (_activateRadius <= 0f) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            _nextPoll -= Time.deltaTime;
            if (_nextPoll > 0f) return;
            _nextPoll = PollInterval;

            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null) return;

            Vector2 delta = (Vector2)_player.transform.position - (Vector2)transform.position;
            bool inside = delta.sqrMagnitude <= _activateRadius * _activateRadius;

            // 只处理「进入半径」这一条边沿；离开后才允许下一次。
            // 防止两个半径重叠时 A/B 每个轮询互相抢成 ping-pong。
            if (!inside)
            {
                _inside = false;
                return;
            }
            if (_inside) return;
            _inside = true;

            // 已经是当前落点：不换点、不广播、不播过场。
            if (_player.HasRespawn && _player.RespawnId == Id) return;

            Activate(_player);
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

            if (_activateRadius <= 0f) return;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, _activateRadius);
        }
    }
}
