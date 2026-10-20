using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Gameplay.Travel
{
    /// <summary>
    /// 一个**火车站**：纯布景 + 一个可被攻击的触发区，自己不移动、不带刚体。
    /// </summary>
    /// <remarks>
    /// <para><b>它不是 <c>CityMechanism</c></b>：机关基类会强制塞一个 <c>Rigidbody2D</c> 和
    /// <c>RuleAffectedBody</c>，还会进世界快照 —— 布景不需要这三样，进去了只是白白多一份状态。
    /// 所以本组件是**纯 MonoBehaviour**，物体上不该挂刚体，也不该有实心碰撞体。</para>
    /// <para><b>怎么被使用</b>：碰撞体是 Trigger，交给 <see cref="IContactSwitch"/> 那条现成的路 ——
    /// 玩家挥砍（<c>PlayerController.PressSwitches</c>）或纸飞机撞上来，都会调
    /// <see cref="ImpactByContact"/>，把面板叫出来。压住型的两条（Press / Release）是空实现：
    /// 车站不是压板。</para>
    /// <para><b>复活点</b>：同一物体（或子物体）上挂 <see cref="PlayerSpawnPoint"/>，
    /// 落人之后由 <see cref="NotifyArrived"/> 显式激活 —— 不依赖「攻击存档点」那条路径，
    /// 传送完就一定是存档点。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrainStation : MonoBehaviour, IContactSwitch
    {
        [Header("站点")]
        [Tooltip("站点编号。必须与 TrainStationCatalog 里的 Id 一致；跨场景传送靠它找站。")]
        [SerializeField] private string _id = "station-00";

        [Tooltip("列表里显示的名字。留空则显示编号。")]
        [SerializeField] private string _displayName = "车站";

        [Tooltip("列表排序，小的在前。")]
        [SerializeField, Min(0)] private int _order;

        [Header("落点")]
        [Tooltip("玩家传送过来站在哪。留空则用本物体的位置。")]
        [SerializeField] private Transform _ridePoint;

        [Tooltip("到达后要激活的复活点。留空则自动在子物体里找。")]
        [SerializeField] private PlayerSpawnPoint _spawn;

        [Header("提示")]
        [Tooltip("白盒提示文字（正式版接 HUD）。留空不显示。")]
        [SerializeField] private string _prompt = "砍一下叫车";

        [Header("解锁")]
        [Tooltip("开局就解锁。只给出生站 / 剧情已经开放的站勾。")]
        [SerializeField] private bool _unlockedAtStart;

        [Tooltip("玩家走进这个半径就算「到过」并解锁。0 = 只能靠 Unlock()（按钮 / 剧情）打开。")]
        [SerializeField, Min(0f)] private float _unlockRadius = 2.5f;

        [Tooltip("没解锁时压暗的美术。留空自动找子物体里的 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer[] _visuals;

        [Tooltip("解锁瞬间的表现钩子（音效 / 特效 / 提示）。")]
        [SerializeField] private UnityEvent _onUnlocked = new UnityEvent();

        public string Id => string.IsNullOrWhiteSpace(_id) ? name : _id;

        public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? Id : _displayName;

        public int Order => _order;

        public string Prompt => _prompt;

        public PlayerSpawnPoint Spawn => _spawn;

        /// <summary>玩家落点（世界坐标）。</summary>
        public Vector2 RidePosition => _ridePoint != null ? _ridePoint.position : transform.position;

        /// <summary>落人完成（表现层 / 测试用）。</summary>
        public event Action<PlayerController> Arrived;

        /// <summary>本站在本局里被解锁（表现层 / 存档用）。</summary>
        public event Action<TrainStation> Unlocked;

        /// <summary>玩家解锁了本站吗。解锁记录住在玩家身上（<see cref="TrainPass"/>）。</summary>
        /// <remarks>惰性补认车票：车票可能是别的站或传送服务刚创建的，不能因为这里是 null 就把「已解锁」看成「还锁着」。</remarks>
        public bool IsUnlocked
        {
            get
            {
                ResolvePass();
                return _pass != null && _pass.IsUnlocked(Id);
            }
        }

        /// <summary>开局就开放。</summary>
        public bool UnlockedAtStart => _unlockedAtStart;

        /// <summary>走近解锁半径（0 = 只能外部解锁）。</summary>
        public float UnlockRadius => _unlockRadius;

        /// <summary>没解锁时压暗用的颜色。</summary>
        private static readonly Color LockedTint = new Color(0.36f, 0.38f, 0.46f, 0.82f);

        private const float PollInterval = 0.2f;

        private TrainPass _pass;
        private PlayerController _player;
        private float _nextPoll;

        /// <summary>美术的原始颜色。压暗是「原色 × 系数」，不能把作者调过的颜色洗掉。</summary>
        private Color[] _baseColors;

        private void Reset()
        {
            _id = name;
            _spawn = GetComponentInChildren<PlayerSpawnPoint>(true);
        }

        private void Awake()
        {
            if (_spawn == null) _spawn = GetComponentInChildren<PlayerSpawnPoint>(true);
        }

        private void Start()
        {
            _player = FindAnyObjectByType<PlayerController>();
            _pass = TrainPass.Find();

            // 开局就开放的站：直接写进玩家的车票，之后走同一条「已解锁」判定。
            if (_unlockedAtStart) Unlock(_player);

            RefreshVisual();
        }

        /// <summary>
        /// 到过即解锁：玩家走进 <see cref="_unlockRadius"/> 就把本站写进车票。
        /// </summary>
        /// <remarks>
        /// 用距离轮询而不是触发回调 —— 玩家是 Kinematic + Slide（<c>useAttachedTriggers = false</c>），
        /// 与 <c>PlayerSpawnPoint</c> / <c>CitySwitch.PressByPlayerProximity</c> 同一条做法。
        /// </remarks>
        private void Update()
        {
            if (_unlockRadius <= 0f) return;

            if (IsUnlocked) return;

            if (UnityEngine.Time.unscaledTime < _nextPoll) return;
            _nextPoll = UnityEngine.Time.unscaledTime + PollInterval;

            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null) return;

            Vector2 delta = (Vector2)_player.transform.position - (Vector2)transform.position;
            if (delta.sqrMagnitude > _unlockRadius * _unlockRadius) return;

            Unlock(_player);
        }

        private void OnEnable() => TrainNetwork.Register(this);

        private void OnDisable() => TrainNetwork.Unregister(this);

        // ------------------------------------------------------------------
        //  IContactSwitch：被打中就开门（攻击盒 / 投射物共用这一个入口）
        // ------------------------------------------------------------------

        /// <summary>被攻击盒或投射物打中：叫出线路面板。同一次挥砍的重复命中由调用方去重。</summary>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            // 没启用的车站**完全没反应**：不叫面板、不提示、不发光。
            // 「能在这里上车去别处」也算使用 —— 所以这条路必须堵死，不能只在目的地那一侧拦。
            if (!IsUnlocked) return;

            TrainTravelService.Instance?.RequestOpen(this);
        }

        /// <summary>车站不是压板。</summary>
        public void PressByContact() { }

        /// <summary>车站不是压板。</summary>
        public void ReleaseByContact() { }

        // ------------------------------------------------------------------
        //  落人
        // ------------------------------------------------------------------

        /// <summary>传送落人之后调用：解锁本站（到达即解锁）并把本站记成复活点。</summary>
        public void NotifyArrived(PlayerController player)
        {
            if (_spawn == null) _spawn = GetComponentInChildren<PlayerSpawnPoint>(true);
            if (player == null) return;

            Unlock(player);
            if (_spawn != null) _spawn.Activate(player);
            Arrived?.Invoke(player);
        }

        // ------------------------------------------------------------------
        //  解锁
        // ------------------------------------------------------------------

        /// <summary>
        /// 解锁本站。<b>返回 false = 之前就解锁过</b>（或者现在没有玩家）。
        /// </summary>
        /// <remarks>
        /// 三条解锁途径共用它：开局开放（<see cref="_unlockedAtStart"/>）、走近（<see cref="_unlockRadius"/>）、
        /// 以及外部调用（按钮 / Boss 清场 / 剧情）。想做成「按开关才开」的站，把半径设 0，
        /// 再把开关的 <c>OnChanged</c> 接到这个方法上即可。
        /// </remarks>
        public bool Unlock(PlayerController player)
        {
            if (player == null) player = _player != null ? _player : FindAnyObjectByType<PlayerController>();

            _pass = TrainPass.Ensure(player);
            if (_pass == null)
            {
                RefreshVisual();
                return false;
            }

            bool first = _pass.Unlock(Id);
            RefreshVisual();
            if (!first) return false;

            _onUnlocked?.Invoke();
            Unlocked?.Invoke(this);
            return true;
        }

        /// <summary>
        /// 无参解锁入口：给 <c>UnityEvent</c>（Boss 清场 / 开关 / 剧情）挂。
        /// </summary>
        /// <remarks>玩家由 <see cref="Unlock"/> 自己找；想做成「打死 Boss 才开」的站，把
        /// <c>_unlockRadius</c> 设 0，再把 Boss 的 <c>OnCleared</c> 接到这里即可。</remarks>
        public void UnlockStation() => Unlock(null);

        /// <summary>按玩家的车票刷新「锁定 / 解锁」外观：没启用就压暗。</summary>
        private void RefreshVisual()
        {
            ResolvePass();
            EnsureVisuals();

            bool unlocked = IsUnlocked;
            for (int i = 0; i < _visuals.Length; i++)
            {
                if (_visuals[i] == null) continue;

                Color baseColor = _baseColors != null && i < _baseColors.Length ? _baseColors[i] : Color.white;
                _visuals[i].color = unlocked
                    ? baseColor
                    : new Color(baseColor.r * LockedTint.r, baseColor.g * LockedTint.g,
                                baseColor.b * LockedTint.b, baseColor.a * LockedTint.a);
            }
        }

        private void ResolvePass()
        {
            if (_pass == null) _pass = TrainPass.Find();
        }

        /// <summary>找美术并记下原始颜色。只在第一次（或美术数量变了）时记，避免把压暗后的颜色当成原色。</summary>
        private void EnsureVisuals()
        {
            if (_visuals != null && _visuals.Length > 0 && _baseColors != null && _baseColors.Length == _visuals.Length) return;

            _visuals = GetComponentsInChildren<SpriteRenderer>(true);
            _baseColors = new Color[_visuals.Length];
            for (int i = 0; i < _visuals.Length; i++)
            {
                _baseColors[i] = _visuals[i] != null ? _visuals[i].color : Color.white;
            }
        }

        /// <summary>搭建工具用：写编号 / 名字 / 排序。运行时不要调。</summary>
        public void Configure(string id, string displayName, int order, Transform ridePoint, PlayerSpawnPoint spawn, bool unlockedAtStart = false)
        {
            _id = id;
            _displayName = displayName;
            _order = order;
            _ridePoint = ridePoint;
            _spawn = spawn;
            _unlockedAtStart = unlockedAtStart;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.55f, 0.45f, 0.7f, 0.8f);
            Vector3 ride = RidePosition;
            Gizmos.DrawWireSphere(ride, 0.25f);
            Gizmos.DrawLine(transform.position, ride);
            UnityEditor.Handles.Label(ride + Vector3.up * 0.4f, DisplayName + " [" + Id + "]");
        }
#endif
    }
}
