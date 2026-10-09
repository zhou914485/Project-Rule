using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Infrastructure.Rules;
using RuleGame.UI.Dialogue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Dialogue
{
    /// <summary>
    /// 仅在可丢弃工程副本执行的 Play Mode 验证。检查 Yarn 实际选句、UI 推进与终止，
    /// 不改用户当前关卡。通过 SessionState 跨越 Play Mode 域重载。
    /// </summary>
    public static class DialogueValidation
    {
        private const string Pending = "RuleGame.Dialogue.Validation";
        private const string VisualOnly = "RuleGame.Dialogue.VisualValidation";
        private const string ConversationOnly = "RuleGame.Dialogue.ConversationValidation";
        private static DialogueHost _host;
        private static DialogueToolkitPresenter _view;
        private static RuleSystem _rules;
        private static Queue<Case> _cases;
        private static Case _current;
        private static RenderTexture _surface;
        private static DialogueNpc _npc;
        private static Transform _actor;
        private static int _stage, _started, _finished, _checked;
        private static int _lineIndex;
        private static string _advancedLine;
        private static double _deadline;
        private static bool _runtimeError, _searchError;
        private static string Output => Environment.GetEnvironmentVariable("DIALOGUE_VALIDATION_OUTPUT") ?? "Temp/DialogueValidation";

        /// <summary>仅样式变化时只验证一个真实台词画面，不重复运行全部行为案例。</summary>
        public static void CaptureOnly()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请只在临时工程副本执行。");
            SessionState.SetBool(VisualOnly, true);
            Run();
        }

        /// <summary>仅检查多句播放、未初始化时的多句兜底，以及中途关闭；不重复检查全部规则。</summary>
        public static void ValidateConversation()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请只在临时工程副本执行。");
            SessionState.SetBool(ConversationOnly, true);
            Run();
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请只在临时工程副本中以 batchmode 执行。");
            try
            {
                Directory.CreateDirectory(Output);
                DialogueSetup.Create();
                if (!SessionState.GetBool(VisualOnly, false)) ValidateAuthoring();
                EditorSceneManager.OpenScene(DialogueSetup.LabScenePath);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        private static void ValidateAuthoring()
        {
            DialogueContent content = AssetDatabase.LoadAssetAtPath<DialogueContent>(DialogueSetup.ContentPath);
            Require(DialogueContentValidator.Validate(content).Count == 0, "外置内容与兜底完整");
            string source = content.Scripts[0].text;
            RejectMutation(content, source.Replace("\"Gravity\"", "\"BadAttribute\""), "未知规则 ID");
            RejectMutation(content, source.Replace("world_ready()", "$undeclared"), "未声明变量");
            RejectMutation(content, source.Replace("#line:observer_generic", "#line:observer_revert"), "重复台词 ID");
            RejectMutation(content, Regex.Replace(source, @"(?m)^(=>[^\r\n]*)(#line:observer_generic)", "$1 <<if false>> $2"), "缺失无条件兜底");
        }

        private static void RejectMutation(DialogueContent content, string source, string behavior)
        {
            DialogueContent copy = UnityEngine.Object.Instantiate(content);
            TextAsset script = new TextAsset(source);
            try
            {
                var fields = new SerializedObject(copy);
                fields.FindProperty("_scripts").GetArrayElementAtIndex(0).objectReferenceValue = script;
                fields.ApplyModifiedPropertiesWithoutUndo();
                Require(DialogueContentValidator.Validate(copy).Count > 0, "拒绝" + behavior);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); UnityEngine.Object.DestroyImmediate(script); }
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            Application.logMessageReceived += LogError;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
            if (EditorApplication.isPlaying) Begin();
        }

        private static void Begin()
        {
            Application.runInBackground = true;
            _host = UnityEngine.Object.FindFirstObjectByType<DialogueHost>();
            _view = _host.GetComponent<DialogueToolkitPresenter>();
            _rules = RuleSystem.Instance;
            _host.PlayingChanged += playing => { if (playing) _started++; else _finished++; };
            _surface = new RenderTexture(1280, 720, 24);
            _surface.Create();
            UnityEngine.Object.FindFirstObjectByType<Camera>().targetTexture = _surface;
            _host.GetComponent<UIDocument>().panelSettings.targetTexture = _surface;
            _cases = new Queue<Case>();
            _cases.Enqueue(new Case("observer", "fallback", missingWorld: true));
            foreach (string id in new[] { "observer", "caretaker", "archivist" }) _cases.Enqueue(new Case(id, "generic"));
            foreach (RelationId relation in new[] { RelationId.Revert, RelationId.Nullify, RelationId.Constant, RelationId.Enhance, RelationId.Weaken })
                foreach (string id in new[] { "observer", "caretaker", "archivist" })
                    _cases.Enqueue(new Case(id, relation.ToString().ToLowerInvariant(), relation));
            _cases.Enqueue(new Case("observer", "generic", RelationId.Revert, expire: true));
            if (SessionState.GetBool(VisualOnly, false))
            {
                _cases.Clear();
                _cases.Enqueue(new Case("observer", "revert", RelationId.Revert));
            }
            if (SessionState.GetBool(ConversationOnly, false))
            {
                _cases.Clear();
                _cases.Enqueue(new Case("caretaker", "generic"));
                _cases.Enqueue(new Case("caretaker", "fallback", missingWorld: true));
                _cases.Enqueue(new Case("caretaker", "revert", RelationId.Revert, closeAfterLine: 3));
            }
            _deadline = EditorApplication.timeSinceStartup + 8;
            EditorApplication.update += Step;
        }

        private static void Step()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("台词验证阶段超时：" + _stage);
                switch (_stage)
                {
                    case 0:
                        if (!_host.IsReady) return;
                        Require(!_host.GetComponent<DialogueWorldContext>().HistoryReady(), "P1 历史未接入时不读取历史");
                        _stage = 1;
                        break;
                    case 1:
                        if (_cases.Count == 0)
                        {
                            if (SessionState.GetBool(ConversationOnly, false))
                            { Finish(!_runtimeError, "多句正常对话、无 P2 多句兜底均按六句顺序结束；第三句中途关闭通过。"); return; }
                            PrepareNpc(); _stage = 5; break;
                        }
                        _current = _cases.Dequeue();
                        _lineIndex = 0;
                        _rules.ClearAll();
                        RuleSystem.SetInstanceForTesting(_current.MissingWorld ? null : _rules);
                        if (_current.Relation.HasValue)
                        {
                            Require(_rules.Request(AttributeId.Gravity, _current.Relation.Value, RuleSource.LevelScript).Succeeded, "P2 测试状态写入");
                            if (_current.Expire) _rules.Tick(30f);
                        }
                        Require(_host.TrySpeak(_current.Speaker), "接受台词入口 " + _current.Speaker);
                        Require(!_host.TrySpeak(_current.Speaker), "阅读中拒绝并发启动");
                        _stage = 2;
                        break;
                    case 2:
                        if (_view.CurrentLineId == null) return;
                        string expected = _lineIndex == 0 ? _current.Speaker + "_" + _current.Suffix
                            : "caretaker_" + (_current.MissingWorld ? "fallback_" : "") + "chat_" + _lineIndex.ToString("00");
                        Require(_view.CurrentLineId == "line:" + expected, "Yarn 选句 " + expected + (_current.Expire ? "（到期）" : ""));
                        Require(_view.IsVisible, "台词可见");
                        if (_view.IsTyping) Submit("dialogueNext");
                        _stage = 3;
                        break;
                    case 3:
                        if (_view.IsTyping) return;
                        Require(!string.IsNullOrWhiteSpace(_view.DisplayedText), "逐字显示可跳到整句");
                        // 发布前预览按真实逐句推进记录，仅在显式指定预览输出时生成。
                        if (Environment.GetEnvironmentVariable("DIALOGUE_CAPTURE_CONVERSATION") == "1"
                            && _current.Speaker == "caretaker" && _current.Suffix == "generic")
                            Capture("conversation-" + _lineIndex.ToString("00") + ".png");
                        // 只保留一次截图，供检查中文字形与实际排版。
                        if (_current.Speaker == "observer" && _current.Suffix == "revert")
                        {
                            Capture();
                            if (SessionState.GetBool(VisualOnly, false)) { Finish(!_runtimeError, "台词焦点样式与视口检查完成。"); return; }
                        }
                        _advancedLine = _view.CurrentLineId;
                        Submit(_current.CloseAfterLine == _lineIndex + 1 ? "dialogueClose" : "dialogueNext");
                        _stage = 4;
                        break;
                    case 4:
                        if (_host.IsPlaying)
                        {
                            if (_view.CurrentLineId == _advancedLine || _view.CurrentLineId == null) return;
                            Require(_current.CloseAfterLine != _lineIndex + 1, "中途关闭不会再播下一句");
                            Require(_current.Speaker == "caretaker" && _lineIndex < 5, "一轮多句没有越界");
                            Require(_started == _finished + 1, "句间保持同一轮开始通知，不提前结束");
                            _lineIndex++;
                            _stage = 2;
                            break;
                        }
                        Require(_lineIndex + 1 == (_current.CloseAfterLine > 0 ? _current.CloseAfterLine : _current.Speaker == "caretaker" ? 6 : 1),
                            "指定句数后才结束本轮");
                        Require(!_view.IsVisible && _started == _finished, "推进后隐藏 UI 并成对通知");
                        _checked++;
                        _stage = 1;
                        break;
                    case 5:
                        if (_view.CurrentLineId == null) return;
                        Submit("dialogueClose");
                        _stage = 6;
                        break;
                    case 6:
                        if (_host.IsPlaying) return;
                        Require(!_view.IsVisible && _started == _finished, "关闭按钮终止 Yarn 并释放通知");
                        Require(!_npc.TrySpeak(), "重复交互冷却有效");
                        Require(_host.TrySpeak("archivist"), "关闭后可重新开播");
                        _stage = 7;
                        break;
                    case 7:
                        if (_view.CurrentLineId == null) return;
                        _host.gameObject.SetActive(false);
                        _stage = 8;
                        break;
                    case 8:
                        if (_host.IsPlaying) return;
                        Require(!_view.IsVisible && _started == _finished, "播放中禁用取消等待且结束通知不重复");
                        _host.gameObject.SetActive(true);
                        Require(_host.TrySpeak("archivist"), "重新启用可开播");
                        _stage = 9;
                        break;
                    case 9:
                        if (_view.CurrentLineId == null) return;
                        _view.Close();
                        _stage = 10;
                        break;
                    case 10:
                        if (_host.IsPlaying) return;
                        Require(_started == _finished, "重新启用没有重复订阅");
                        Vector2 initialGravity = new Vector2(0f, -9.81f);
                        _rules.Request(AttributeId.Gravity, RelationId.Revert, RuleSource.LevelScript);
                        UnityEngine.Object.DestroyImmediate(UnityEngine.Object.FindFirstObjectByType<RuleGame.Development.Dialogue.DialogueLabControls>().gameObject);
                        Require(Physics2D.gravity == initialGravity, "试播工具销毁恢复全局重力");
                        Finish(!_runtimeError, "20 个实际 Yarn 选句案例及作者校验、UI 推进/关闭/禁用、NPC 距离/冷却通过。");
                        break;
                }
                _deadline = EditorApplication.timeSinceStartup + 8;
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        private static void PrepareNpc()
        {
            RuleSystem.SetInstanceForTesting(_rules);
            _rules.ClearAll();
            _npc = new GameObject("NPC validation probe").AddComponent<DialogueNpc>();
            var fields = new SerializedObject(_npc);
            fields.FindProperty("_host").objectReferenceValue = _host;
            fields.FindProperty("_speakerId").stringValue = "observer";
            fields.FindProperty("_repeatDelay").floatValue = 100f;
            fields.ApplyModifiedPropertiesWithoutUndo();
            _actor = new GameObject("Interaction distance probe").transform;
            _actor.position = Vector3.right * 20;
            Require(!_npc.TryInteract(_actor), "远处角色不能交互");
            _actor.position = Vector3.zero;
            Require(_npc.TryInteract(_actor), "距离内角色可以交互");
        }

        private static void Submit(string name)
        {
            Button button = _host.GetComponent<UIDocument>().rootVisualElement.Q<Button>(name);
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
        }

        private static void Capture(string fileName = "dialogue.png")
        {
            VisualElement paper = _host.GetComponent<UIDocument>().rootVisualElement.Q("dialogueRoot");
            Require(paper.worldBound.width > 500f && paper.worldBound.xMin >= 0f && paper.worldBound.yMax <= 720f,
                "1280x720 台词纸面位于视口内");
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _surface;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Output, fileName), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        private static void Require(bool condition, string behavior)
        {
            if (!condition) throw new InvalidOperationException("验证失败：" + behavior);
            Debug.Log("[Dialogue validation] 通过：" + behavior);
        }

        private static void LogError(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // Unity 6.6 临时工程启动可能产生 Search 索引异常，单独记录，不掩盖游戏运行错误。
            if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup")) _searchError = true;
            else _runtimeError = true;
        }

        private static void Finish(bool success, string detail)
        {
            EditorApplication.update -= Step;
            SessionState.SetBool(Pending, false);
            SessionState.SetBool(VisualOnly, false);
            SessionState.SetBool(ConversationOnly, false);
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "result.json"), JsonUtility.ToJson(new Result
            { Success = success, Cases = _checked, Detail = detail, EditorSearchStartupError = _searchError }, true));
            Debug.Log("[Dialogue validation] " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private sealed class Case
        {
            public readonly string Speaker, Suffix;
            public readonly RelationId? Relation;
            public readonly bool MissingWorld, Expire;
            public readonly int CloseAfterLine;
            public Case(string speaker, string suffix, RelationId? relation = null, bool missingWorld = false, bool expire = false, int closeAfterLine = 0)
            { Speaker = speaker; Suffix = suffix; Relation = relation; MissingWorld = missingWorld; Expire = expire; CloseAfterLine = closeAfterLine; }
        }
        [Serializable] private sealed class Result { public bool Success; public int Cases; public string Detail; public bool EditorSearchStartupError; }
    }
}
