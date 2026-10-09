using System;
using System.Globalization;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using Yarn.Unity;

namespace RuleGame.UI.Dialogue
{
    /// <summary>
    /// Yarn 3 的 UI Toolkit 台词显示适配：固定结构在 UXML/USS，运行时只填内容。
    /// 使用真实字素边界显示中文/组合字符；关闭与禁用会取消等待，不留下异步悬挂。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DialogueToolkitPresenter : DialoguePresenterBase
    {
        [SerializeField] private DialogueRunner _runner;
        [SerializeField, Min(0f), Tooltip("0 表示立即显示整句。")]
        private float _charactersPerSecond = 28f;
        [SerializeField] private bool _autoAdvance;
        [SerializeField, Min(0.2f)] private float _readSeconds = 3f;
        private VisualElement _root;
        private Label _speaker;
        private Label _line;
        private Label _hint;
        private Button _next;
        private Button _close;
        private CancellationTokenSource _lifetime;
        private bool _typing;
        public bool IsTyping => _typing;
        public string DisplayedText => _line?.text;
        public string CurrentLineId { get; private set; }
        public bool IsVisible => _root != null && !_root.ClassListContains("hidden");

        private void OnEnable()
        {
            _lifetime = new CancellationTokenSource();
            VisualElement document = GetComponent<UIDocument>().rootVisualElement;
            _root = document.Q("dialogueRoot");
            _speaker = document.Q<Label>("dialogueSpeaker");
            _line = document.Q<Label>("dialogueLine");
            _hint = document.Q<Label>("dialogueHint");
            _next = document.Q<Button>("dialogueNext");
            _close = document.Q<Button>("dialogueClose");
            if (_root == null || _speaker == null || _line == null || _hint == null || _next == null || _close == null || _runner == null)
            {
                Debug.LogError("[Dialogue] 请配置 Dialogue.uxml、PanelSettings 与 Runner。", this);
                enabled = false;
                return;
            }
            _speaker.enableRichText = false;
            _line.enableRichText = false;
            _next.clicked += Advance;
            _close.clicked += Close;
            _root.RegisterCallback<KeyDownEvent>(HandleKey);
            Hide();
        }

        public void Advance()
        {
            if (_runner == null || !_runner.IsDialogueRunning || !IsVisible) return;
            if (_typing) _runner.RequestHurryUpLine();
            else _runner.RequestNextLine();
        }

        public void Close()
        {
            if (_runner != null && _runner.IsDialogueRunning) _runner.Stop().Forget();
        }

        private void HandleKey(KeyDownEvent evt)
        {
            // Enter/Space 使用 Button 自带的导航提交；只处理 Escape，避免同一按键推进两次。
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }

        public override YarnTask OnDialogueStartedAsync()
        {
            CurrentLineId = null;
            return YarnTask.CompletedTask;
        }

        public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
        {
            if (!isActiveAndEnabled || _lifetime == null) return;
            CancellationToken lifetime = _lifetime.Token;
            string text = line.TextWithoutCharacterName.Text;
            int[] characters = StringInfo.ParseCombiningCharacters(text);
            CurrentLineId = line.TextID;
            _speaker.text = line.CharacterName ?? "";
            _line.text = "";
            _root.RemoveFromClassList("hidden");
            _next.Focus();
            _typing = _charactersPerSecond > 0f && characters.Length > 0;
            _hint.text = _typing ? "点击继续，显示整句" : "Enter / 点击继续";
            float revealed = 0f;
            int visible = 0;
            while (_typing && visible < characters.Length && !token.IsHurryUpRequested && !lifetime.IsCancellationRequested)
            {
                revealed += Time.unscaledDeltaTime * _charactersPerSecond;
                int count = Mathf.Min(characters.Length, Mathf.FloorToInt(revealed));
                if (count != visible)
                {
                    visible = count;
                    _line.text = count == characters.Length ? text : text.Substring(0, characters[count]);
                }
                await YarnTask.Yield();
            }
            if (lifetime.IsCancellationRequested) return;
            _typing = false;
            _line.text = text;
            _hint.text = "Enter / 点击继续";
            float readUntil = Time.unscaledTime + _readSeconds;
            while (!token.IsNextContentRequested && !lifetime.IsCancellationRequested
                && (!_autoAdvance || Time.unscaledTime < readUntil))
                await YarnTask.Yield();
        }

        public override YarnTask OnDialogueCompleteAsync()
        {
            Hide();
            return YarnTask.CompletedTask;
        }

        private void Hide()
        {
            _root?.AddToClassList("hidden");
            _typing = false;
            CurrentLineId = null;
        }

        private void OnDisable()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            if (_next != null) _next.clicked -= Advance;
            if (_close != null) _close.clicked -= Close;
            _root?.UnregisterCallback<KeyDownEvent>(HandleKey);
            Close();
            Hide();
        }
    }
}
