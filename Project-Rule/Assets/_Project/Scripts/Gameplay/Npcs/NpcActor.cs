using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Persistence;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.Events;

namespace RuleGame.Gameplay.Npcs
{
    /// <summary>需求表的交互状态编排；交易、资格与战斗通过事件交给各自权威系统。</summary>
    [RequireComponent(typeof(DialogueNpc), typeof(Rigidbody2D))]
    public sealed class NpcActor : MonoBehaviour, ISnapshotParticipant, IContactSwitch
    {
        /// <summary>镇长资格发放的全局存档键；门 / 剧情都用它跨分区判断。</summary>
        public const string MayorQualifiedKey = "npc.mayor.qualified";

        /// <summary>镇长资格发放（每次 Play 只应有一次；读档恢复不发）。</summary>
        public static event Action MayorQualified;

        [SerializeField] private NpcDefinition _definition;
        [SerializeField] private DialogueHost _host;
        [SerializeField] private SpriteRenderer _view;
        [Header("由关卡真实事件更新条件；留空视为未满足")]
        [SerializeField, Tooltip("仅白盒/测试手动覆盖；正式场景留空。镇长资格默认直接读 PlayerProgress。")]
        private DialogueSceneFlag _abilityReady;
        [SerializeField] private DialogueSceneFlag _buttonPowered;
        [SerializeField] private DialogueSceneFlag _gearReleased;
        [SerializeField] private DialogueSceneFlag _hasPass;
        [SerializeField] private DialogueSceneFlag _townSoldOut;
        [SerializeField] private Collider2D _passageBlocker;
        [SerializeField, Tooltip("守卫自动检测的玩家；其他角色仍由交互键调用 TryInteract。")]
        private Transform _proximityActor;
        [Header("接线到既有系统，不在 NPC 内结算商品或生成 Boss")]
        [SerializeField] private UnityEvent _onTradeRequested = new UnityEvent();
        [SerializeField] private UnityEvent _onQualificationGranted = new UnityEvent();
        [SerializeField] private UnityEvent _onBossRequested = new UnityEvent();
        [SerializeField] private UnityEvent _onAttackRequested = new UnityEvent();
        [SerializeField] private UnityEvent _onPassageGranted = new UnityEvent();
        private DialogueNpc _dialogue;
        private Rigidbody2D _body;
        private DialogueHost _subscribedHost;
        private bool _speaking;
        private bool _introduced;
        // 外部进度事件不随物理回溯重复发放；落盘由进度/存档系统负责。
        private bool _delivered;
        private float _timer;
        private float _nextSpeakAt;
        private float _homeX;
        private float _direction = -1f;
        private bool _wasInRange;
        private bool _warnedNoHost;
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];
        public NpcDefinition Definition => _definition;
        public NpcState State { get; private set; }
        public bool DiscardWhenMissing => false;
        public bool IsSpeaking => _speaking;

        private void Awake()
        {
            _dialogue = GetComponent<DialogueNpc>();
            _body = GetComponent<Rigidbody2D>();
            _homeX = _body.position.x;
            _timer = _definition != null ? _definition.IdleDuration : 2f;
        }

        /// <summary>当前启用的全部 NPC。分区加载 / 卸载时自动增删，供运行时交互入口查找。</summary>
        public static IReadOnlyCollection<NpcActor> All => AllActors;

        private static readonly HashSet<NpcActor> AllActors = new HashSet<NpcActor>();

        // 工程开着「不重载域」的播放选项时，静态表会跨 Play 会话残留；进 Play 前先清空。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            AllActors.Clear();
            MayorQualified = null;
        }

        private void OnEnable()
        {
            AllActors.Add(this);
            Subscribe();
        }

        public void BindHost(DialogueHost host)
        {
            if (_speaking) _subscribedHost?.StopDialogue();
            Unsubscribe();
            _host = host;
            if (isActiveAndEnabled) Subscribe();
        }

        private DialogueHost ResolvedHost => _host != null ? _host : DialogueHost.Active;

        private void Subscribe()
        {
            DialogueHost host = ResolvedHost;
            if (host == null || _subscribedHost == host) return;
            Unsubscribe();
            _subscribedHost = host;
            _subscribedHost.ConversationFinished += FinishConversation;
        }

        private void Unsubscribe()
        {
            if (_subscribedHost != null) _subscribedHost.ConversationFinished -= FinishConversation;
            _subscribedHost = null;
        }

        public NpcState ResolveGreeting()
        {
            if (_definition == null) return NpcState.Idle;
            switch (_definition.Role)
            {
                case NpcRole.Mayor: return HasQualification() ? NpcState.Talk2 : NpcState.Talk1;
                case NpcRole.GearKeeper:
                    // 首次说明只在「什么都还没做」时播；玩家先通了电 / 松开齿轮再回来，
                    // 应该直接读当前状态，而不是又把任务说明念一遍（看起来像条件没生效）。
                    if (!_introduced && !Flag(_buttonPowered) && !Flag(_gearReleased)) return NpcState.Talk1;
                    return Flag(_buttonPowered) ? (Flag(_gearReleased) ? NpcState.Talk5 : NpcState.Talk3)
                        : (Flag(_gearReleased) ? NpcState.Talk4 : NpcState.Talk2);
                case NpcRole.Guard: return Flag(_hasPass) ? NpcState.Talk2 : NpcState.Talk1;
                case NpcRole.MysteryMerchant: return Flag(_townSoldOut) ? NpcState.Talk2 : NpcState.Talk1;
                default: return NpcState.Talk;
            }
        }

        public bool TryInteract(Transform player)
        {
            if (!isActiveAndEnabled || _definition == null || player == null) return false;
            if (ResolvedHost == null) { WarnNoHost(); return false; }
            if (TimeManager.Instance?.IsPaused == true || _speaking
                || State == NpcState.Sell || State == NpcState.PassKey || State == NpcState.Attack
                || Time.unscaledTime < _nextSpeakAt
                || ((Vector2)(player.position - transform.position)).sqrMagnitude > _definition.InteractionRadius * _definition.InteractionRadius)
                return false;
            return BeginConversation(ResolveGreeting(), player);
        }

        // ------------------------------------------------------------------
        //  IContactSwitch：攻击盒 / 投射物
        // ------------------------------------------------------------------

        /// <summary>NPC 不吃「压住」语义：石头 / 铁箱压过来不该开始对话。</summary>
        public void PressByContact() { }

        /// <summary>压着的东西离开。NPC 没有按住状态，忽略。</summary>
        public void ReleaseByContact() { }

        /// <summary>
        /// 被攻击盒 / 飞刀打中：与按钮、宝箱同一条 <see cref="IContactSwitch"/> 路径。
        /// 命中本身就是触发条件，不再用交互半径二次拒绝；冷却与状态闸门仍复用交谈规则。
        /// </summary>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!isActiveAndEnabled || _definition == null || _dialogue == null) return;
            if (ResolvedHost == null) { WarnNoHost(); return; }
            if (_speaking || TimeManager.Instance?.IsPaused == true
                || State == NpcState.Sell || State == NpcState.PassKey || State == NpcState.Attack
                || Time.unscaledTime < _nextSpeakAt) return;

            // player 传 null 走 DialogueNpc.TrySpeak：近战盒 / 飞刀扫掠已经完成了命中判定。
            BeginConversation(ResolveGreeting(), null);
        }

        private bool BeginConversation(NpcState state, Transform player = null)
        {
            Subscribe();
            _dialogue.Configure(ResolvedHost, _definition.SpeakerId(state), _definition.InteractionRadius, _definition.RepeatDelay);
            _speaking = true;
            bool accepted = state == NpcState.Bye ? _dialogue.TryContinueConversation()
                : player == null ? _dialogue.TrySpeak() : _dialogue.TryInteract(player);
            if (!accepted) { _speaking = false; return false; }
            State = state;
            _nextSpeakAt = Time.unscaledTime + _definition.RepeatDelay;
            RefreshView();
            return true;
        }

        private void FinishConversation(bool completed)
        {
            if (!_speaking) return;
            _speaking = false;
            NpcState previous = State;
            EnterIdle();
            if (!completed) return;
            switch (_definition.Role)
            {
                case NpcRole.Merchant:
                    if (previous == NpcState.Talk) OpenTrade();
                    break;
                case NpcRole.Mayor:
                    if (previous == NpcState.Talk2 && HasQualification() && !_delivered) BeginAction(NpcState.PassKey);
                    break;
                case NpcRole.GearKeeper:
                    _introduced = true;
                    if (previous == NpcState.Talk5 && Flag(_buttonPowered) && Flag(_gearReleased) && !_delivered)
                        BeginAction(NpcState.PassKey);
                    break;
                case NpcRole.Guard:
                    if (previous == NpcState.Talk2 && Flag(_hasPass) && !_delivered) BeginAction(NpcState.PassKey);
                    else if (previous == NpcState.Talk1 && !Flag(_hasPass)) BeginAction(NpcState.Attack);
                    break;
                case NpcRole.MysteryMerchant:
                    if (previous == NpcState.Talk2 && Flag(_townSoldOut)) OpenTrade();
                    break;
            }
        }

        private void OpenTrade()
        {
            State = NpcState.Sell;
            RefreshView();
            _onTradeRequested.Invoke();
        }

        /// <summary>交易 UI 关闭后调用；未能取得共享对话窗口时回到待机，不阻塞 NPC。</summary>
        public void CloseTrade()
        {
            if (State != NpcState.Sell) return;
            EnterIdle();
            BeginConversation(NpcState.Bye);
        }

        private void BeginAction(NpcState state)
        {
            State = state;
            _timer = _definition.ActionDuration;
            RefreshView();
            if (state == NpcState.Attack) _onAttackRequested.Invoke();
        }

        private void FixedUpdate()
        {
            if (_definition == null || TimeManager.Instance?.IsPaused == true) return;
            if (_definition.Role == NpcRole.Guard && _proximityActor != null)
            {
                bool inRange = ((Vector2)(_proximityActor.position - transform.position)).sqrMagnitude
                    <= _definition.InteractionRadius * _definition.InteractionRadius;
                if (!inRange) _wasInRange = false;
                else if (!_wasInRange && TryInteract(_proximityActor)) _wasInRange = true;
            }
            if (_speaking || State == NpcState.Sell) return;
            _timer -= Time.fixedDeltaTime;
            if (State == NpcState.PassKey && _timer <= 0f)
            {
                CompleteAction();
                return;
            }
            if (State == NpcState.Attack && _timer <= 0f) { EnterIdle(); return; }
            if (_definition.Role != NpcRole.Resident) return;
            if (State == NpcState.Idle && _timer <= 0f)
            {
                State = NpcState.Walk;
                _timer = 3f;
            }
            if (State != NpcState.Walk) return;
            bool inverted = RuleSystem.Instance != null && RuleSystem.Instance.Attributes.Values.FrictionInverted;
            float travelDirection = _direction * (inverted ? -1f : 1f);
            float offset = _body.position.x - _homeX;
            bool atEdge = Mathf.Abs(offset) >= _definition.PatrolRadius && Mathf.Sign(offset) == Mathf.Sign(travelDirection);
            var ground = new ContactFilter2D { useLayerMask = true, layerMask = 1 << 8, useTriggers = false };
            int supported = Physics2D.Raycast(_body.position + new Vector2(travelDirection * 0.4f, 0.15f), Vector2.down,
                ground, _groundHits, 0.5f);
            int wall = Physics2D.Raycast(_body.position + Vector2.up * 0.4f, Vector2.right * travelDirection,
                ground, _groundHits, 0.5f);
            if (atEdge || supported == 0 || wall > 0 || _timer <= 0f)
            {
                _direction = atEdge ? Mathf.Sign(_homeX - _body.position.x) * (inverted ? -1f : 1f) : -_direction;
                EnterIdle();
                return;
            }
            // 固定力而非强写速度；质量规则会影响加速，真实碰撞与重力由 Rigidbody2D 处理。
            float error = travelDirection * _definition.WalkSpeed - _body.linearVelocity.x;
            float force = Mathf.Clamp(error / 0.1f, -1f, 1f) * _definition.WalkForce;
            _body.AddForce(Vector2.right * force);
            if (_view != null) _view.flipX = travelDirection > 0f;
        }

        private void CompleteAction()
        {
            NpcRole role = _definition.Role;
            bool ready = role == NpcRole.Mayor ? HasQualification()
                : role == NpcRole.GearKeeper ? Flag(_buttonPowered) && Flag(_gearReleased) : Flag(_hasPass);
            EnterIdle();
            if (!ready || _delivered) return;
            _delivered = true;
            if (role == NpcRole.Mayor)
            {
                WorldStateStore.SetBool(MayorQualifiedKey, true);
                _onQualificationGranted.Invoke();
                MayorQualified?.Invoke();
            }
            else if (role == NpcRole.GearKeeper) _onBossRequested.Invoke();
            else if (role == NpcRole.Guard)
            {
                if (_passageBlocker != null) _passageBlocker.enabled = false;
                _onPassageGranted.Invoke();
            }
        }

        private void EnterIdle()
        {
            State = NpcState.Idle;
            _timer = _definition != null ? _definition.IdleDuration : 2f;
            RefreshView();
        }

        private void RefreshView()
        {
            if (_view == null || _definition == null) return;
            _view.sprite = (_speaking || State == NpcState.Sell) && _definition.TalkSprite != null
                ? _definition.TalkSprite : _definition.IdleSprite;
        }

        /// <summary>
        /// 镇长资格：直接读玩家当前的规则与能力解锁。
        /// <see cref="_abilityReady"/> 是白盒/测试的手动覆盖；正式场景留空即可。
        /// </summary>
        private bool HasQualification()
        {
            if (Flag(_abilityReady)) return true;
            PlayerProgress progress = PlayerProgress.Find();
            return progress != null && progress.HasAllUnlocks;
        }

        private void WarnNoHost()
        {
            if (_warnedNoHost) return;
            _warnedNoHost = true;
            Debug.LogWarning("[NpcActor] 场景里没有可用的 DialogueHost。" +
                             "正式流程请确认 LevelRuntime / GameRoot 里有 NpcDialogueSystem。", this);
        }

        private static bool Flag(DialogueSceneFlag flag) => flag != null && flag.Value;

        private void OnDisable()
        {
            AllActors.Remove(this);
            if (_speaking) _subscribedHost?.StopDialogue();
            _speaking = false;
            Unsubscribe();
            EnterIdle();
        }

        private struct Snapshot
        {
            public NpcState State;
            public bool Introduced;
            public float Timer, HomeX, Direction;
        }

        public object CaptureSnapshotState() => new Snapshot
        {
            State = _speaking || State == NpcState.Sell ? NpcState.Idle : State,
            Introduced = _introduced, Timer = _timer, HomeX = _homeX, Direction = _direction
        };

        public void RestoreSnapshotState(object state)
        {
            if (!(state is Snapshot snapshot)) return;
            if (_speaking) _subscribedHost?.StopDialogue();
            _speaking = false;
            State = snapshot.State;
            _introduced = snapshot.Introduced;
            _timer = snapshot.Timer;
            _homeX = snapshot.HomeX;
            _direction = snapshot.Direction;
            RefreshView();
        }
    }
}
