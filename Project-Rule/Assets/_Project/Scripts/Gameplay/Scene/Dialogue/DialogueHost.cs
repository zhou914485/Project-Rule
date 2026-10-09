using System;
using UnityEngine;
using UnityEngine.Events;
using Yarn.Unity;

namespace RuleGame.Gameplay.Dialogue
{
    /// <summary>
    /// Yarn 的项目组合根。P1/P3 可订阅开始/结束事件处理暂停与输入，不在这里代写队友系统。
    /// 每个 Host 独占一个 Runner；共享 Host 的 NPC 同时只播一段，不打断正在阅读的台词。
    /// </summary>
    [RequireComponent(typeof(DialogueRunner), typeof(DialogueWorldContext))]
    public sealed class DialogueHost : MonoBehaviour
    {
        [SerializeField] private DialogueContent _content;
        [SerializeField] private UnityEvent _onStarted = new UnityEvent();
        [SerializeField] private UnityEvent _onFinished = new UnityEvent();
        private DialogueRunner _runner;
        private DialogueWorldContext _context;
        private bool _ready;
        private bool _starting;
        private bool _announcedPlaying;
        public DialogueContent Content => _content;
        public DialogueRunner Runner => _runner;
        public bool IsPlaying => _starting || (_runner != null && _runner.IsDialogueRunning);
        public bool IsReady => _ready;
        public event Action<bool> PlayingChanged;

        private void Awake()
        {
            _runner = GetComponent<DialogueRunner>();
            _context = GetComponent<DialogueWorldContext>();
            _runner.autoStart = false;
            if (_content != null && _content.Project != null) _runner.SetProject(_content.Project);
            // 直接注册实例函数，使多个场景/Host 各自读取自己的物件与历史适配器。
            _runner.AddFunction<bool>("world_ready", _context.WorldReady);
            _runner.AddFunction<string, string, bool>("rule_active", _context.RuleActive);
            _runner.AddFunction<string, bool>("rule_present", _context.RulePresent);
            _runner.AddFunction<bool>("history_ready", _context.HistoryReady);
            _runner.AddFunction<string, string, bool>("rule_seen", _context.RuleSeen);
            _runner.AddFunction<string, bool>("scene_flag", _context.SceneFlag);
        }

        private void OnEnable()
        {
            _runner.onDialogueStart ??= new UnityEvent();
            _runner.onDialogueComplete ??= new UnityEvent();
            _runner.onDialogueStart.AddListener(HandleStarted);
            _runner.onDialogueComplete.AddListener(HandleFinished);
            _ready = false;
        }

        private void Start() => ValidateContent();

        /// <summary>内容或物件引用调整后可重新校验，错误时拒绝播放而不吞掉诊断。</summary>
        public bool ValidateContent()
        {
            var errors = DialogueContentValidator.Validate(_content, _context);
            _ready = errors.Count == 0;
            foreach (string error in errors) Debug.LogError("[Dialogue] " + error, this);
            return _ready;
        }

        public bool TrySpeak(string speakerId)
        {
            if (!isActiveAndEnabled || IsPlaying || (!_ready && !ValidateContent())) return false;
            DialogueSpeaker speaker = _content.FindSpeaker(speakerId);
            if (speaker == null) { Debug.LogWarning("[Dialogue] 未知 NPC id：" + speakerId, this); return false; }
            _starting = true;
            // 规则系统尚未初始化时直接播独立兜底，不把“未加载”冒充“世界恢复正常”。
            BeginAsync(_context.WorldReady() ? speaker.EntryNode : speaker.FallbackNode).Forget();
            return true;
        }

        private async YarnTask BeginAsync(string node)
        {
            try { await _runner.StartDialogue(node); }
            catch (Exception error) { Debug.LogException(error, this); HandleFinished(); }
            finally { _starting = false; }
        }

        public void StopDialogue()
        {
            if (_runner != null && _runner.IsDialogueRunning) _runner.Stop().Forget();
        }

        private void HandleStarted()
        {
            // 先选择当前状态最具体的短句；不要让“少重复”策略优先选到泛用兜底。
            _runner.Dialogue.ContentSaliencyStrategy = new Yarn.Saliency.BestSaliencyStrategy();
            _starting = false;
            _announcedPlaying = true;
            PlayingChanged?.Invoke(true);
            _onStarted.Invoke();
        }

        private void HandleFinished()
        {
            _starting = false;
            // 禁用时先释放输入，随后到达的 Yarn 完成回调不能再释放一次。
            if (!_announcedPlaying) return;
            _announcedPlaying = false;
            PlayingChanged?.Invoke(false);
            _onFinished.Invoke();
        }

        private void OnDisable()
        {
            StopDialogue();
            _runner.onDialogueStart.RemoveListener(HandleStarted);
            _runner.onDialogueComplete.RemoveListener(HandleFinished);
            // 异步停止可能稍后完成；先释放正式输入/暂停订阅者，不留下锁定状态。
            HandleFinished();
            _ready = false;
        }
    }
}
