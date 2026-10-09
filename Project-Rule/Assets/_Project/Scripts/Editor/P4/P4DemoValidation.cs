using System;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure.Rules;
using RuleGame.Development.P4;
using RuleGame.UI.P4;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.P4
{
    /// <summary>
    /// 临时工程副本中的批处理 Play Mode 验证，会切换场景、创建探针并退出编辑器。
    /// BuildAndValidate 检查整套 UI；ValidateGravityIntegration 只检查重力接入。
    /// 不在用户正在编辑的工程中运行，执行方式见 Doc/P4开发起步.md。
    /// </summary>
    public static class P4DemoValidation
    {
        private const string PendingKey = "RuleGame.P4.BatchValidation";
        private const string GravityModeKey = "RuleGame.P4.GravityValidation";
        private const string SimulationModeKey = "RuleGame.P4.InitialSimulationMode";
        private static P4HudPresenter _hud;
        private static P4DemoController _demo;
        private static UIDocument _document;
        private static int _stage;
        private static double _nextStep;
        private static string _pausedTime;
        private static bool _failed;
        private static int _confirmCount;
        private static RenderTexture _captureSurface;
        private static bool _editorSearchStartupError;
        private static bool _gravityOnly;
        private static float _pausedRemaining;
        private static Rigidbody2D _pauseProbe;
        private static Vector2 _pausedPosition;
        private static string OutputPath => Environment.GetEnvironmentVariable("P4_VALIDATION_OUTPUT") ?? "Temp/P4Validation";

        public static void BuildAndValidate()
        {
            if (!Application.isBatchMode)
            {
                throw new InvalidOperationException("Run P4 validation in a disposable batch project, not the user's open Editor.");
            }
            try
            {
                Directory.CreateDirectory(OutputPath);
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/SampleScene.unity");
                P4DemoSceneBuilder.Build();
                EditorSceneManager.OpenScene(P4DemoSceneBuilder.ScenePath);
                var demo = UnityEngine.Object.FindFirstObjectByType<P4DemoController>();
                demo.Configure(UnityEngine.Object.FindFirstObjectByType<P4HudPresenter>(),
                    AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/UI/P4/Resources/P4RulePreviews.json"),
                    P4DemoSceneBuilder.EnsureGravityDemoCatalog());
                EditorUtility.SetDirty(demo);
                var pawnBody = UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>().GetComponent<Rigidbody2D>();
                pawnBody.bodyType = RigidbodyType2D.Kinematic;
                pawnBody.gravityScale = 0f;
                pawnBody.useFullKinematicContacts = true;
                EditorUtility.SetDirty(pawnBody);
                EditorSceneManager.SaveScene(demo.gameObject.scene);
                SessionState.SetBool(PendingKey, true);
                SessionState.SetInt(SimulationModeKey, (int)Physics2D.simulationMode);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        public static void ValidateGravityIntegration()
        {
            if (!Application.isBatchMode)
            {
                throw new InvalidOperationException("Run gravity validation in a disposable batch project.");
            }
            SessionState.SetBool(GravityModeKey, true);
            BuildAndValidate();
        }

        // Play Mode 域重载会清空静态字段，SessionState 保存运行标记以恢复验证流程。
        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(PendingKey, false))
            {
                return;
            }
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _gravityOnly = SessionState.GetBool(GravityModeKey, false);
            Application.logMessageReceived += OnLogMessage;
            if (EditorApplication.isPlaying)
            {
                BeginRuntimeChecks();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                BeginRuntimeChecks();
            }
        }

        private static void BeginRuntimeChecks()
        {
            Application.runInBackground = true;
            EditorApplication.isPaused = false;
            if (_captureSurface == null)
            {
                _captureSurface = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                _captureSurface.Create();
                UnityEngine.Object.FindFirstObjectByType<Camera>().targetTexture = _captureSurface;
                UnityEngine.Object.FindFirstObjectByType<UIDocument>().panelSettings.targetTexture = _captureSurface;
            }
            EditorApplication.update -= Step;
            _nextStep = EditorApplication.timeSinceStartup + 1.5;
            EditorApplication.update += Step;
        }

        private static void OnLogMessage(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // Unity 6.6 Search can throw while indexing a fresh disposable project on domain reload.
                // Keep this specific Editor-only error in the report; it is not a P4 runtime failure.
                if (stack.Contains("UnityEditor.Search.SearchDatabase") && stack.Contains("IndexationOnStartup"))
                {
                    _editorSearchStartupError = true;
                    Debug.LogWarning("P4 validation recorded a separate Unity Editor Search startup indexing error.");
                    return;
                }
                _failed = true;
                Debug.LogWarning("P4 validation observed an error: " + message);
            }
        }

        private static void Step()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < _nextStep)
            {
                return;
            }
            try
            {
                if (_gravityOnly)
                {
                    StepGravity();
                    return;
                }
                VisualElement root;
                switch (_stage++)
                {
                    case 0:
                        _hud = UnityEngine.Object.FindFirstObjectByType<P4HudPresenter>();
                        _demo = UnityEngine.Object.FindFirstObjectByType<P4DemoController>();
                        Require(_hud != null && _demo != null, "Scene contains HUD and demo data source");
                        _document = _hud.GetComponent<UIDocument>();
                        root = _document.rootVisualElement;
                        Require(root.panel != null && root.Q("hudRoot").resolvedStyle.width > 1000f
                            && root.Q("hudRoot").resolvedStyle.height > 600f, "Runtime UI fills its reference viewport");
                        Require(Mathf.Approximately(_demo.Energy, 100f), "Initial demo energy");
                        DemoRuleCatalog catalog = JsonUtility.FromJson<DemoRuleCatalog>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/UI/P4/Resources/P4RulePreviews.json").text);
                        Require(catalog.Previews.Count == 35 && root.Q<Label>("combinationCount").text.Contains("35"), "All 35 matrix combinations available");
                        int pending = 0;
                        foreach (DemoRulePreview preview in catalog.Previews)
                        {
                            _demo.ResetDemo();
                            _hud.SetAssemblyOpen(true);
                            ClickButton(preview.AttributeId);
                            ClickButton(preview.RelationId);
                            Require(root.Q<Label>("previewDescription").text == preview.Description, "Matrix description: " + preview.Title);
                            if (preview.PendingDesign)
                            {
                                pending++;
                                Require(root.Q<Label>("previewReason").text.Contains("未定稿"), "Pending design label: " + preview.Title);
                            }
                            ClickButton("confirmButton");
                            Require(root.Q(preview.AttributeId + "Rule").Q<Label>(className: "rule-title").text == preview.Title
                                && !root.Q(preview.AttributeId + "Rule").ClassListContains("hidden")
                                && Mathf.Approximately(_demo.Energy, 100f - (preview.AttributeId == "gravity"
                                    ? RuleSystem.Instance.GetCost(AttributeId.Gravity, Enum.Parse<RelationId>(preview.RelationId, true)).Energy
                                    : preview.Cost)), "Matrix confirmation: " + preview.Title);
                        }
                        Require(pending == 4, "Four pending designs remain labeled and testable");
                        _demo.ResetDemo();
                        foreach (string attribute in new[] { "gravity", "time", "friction", "mass", "relation", "damage", "causality" })
                        {
                            _hud.SetAssemblyOpen(true);
                            _hud.SelectAttribute(attribute);
                            _hud.SelectRelation("constant");
                            _hud.ConfirmSelection();
                            Require(!root.Q(attribute + "Rule").ClassListContains("hidden"), "Concurrent rule slot: " + attribute);
                        }
                        Screenshot("01-hud.png");
                        _demo.ResetDemo();
                        _hud.OnConfirmRequested += CountConfirmation;
                        ClickButton("assembleButton");
                        _hud.SelectAttribute("damage");
                        _hud.SelectRelation("constant");
                        Require(_demo.IsWorldPaused, "Assembly pauses demo movement and timers");
                        _nextStep = EditorApplication.timeSinceStartup + 0.3;
                        break;
                    case 1:
                        Screenshot("02-assembly.png");
                        root = _document.rootVisualElement;
                        Require(root.Q<Label>("selectedAttribute").layout.width >= 160f, "Long attribute word has room");
                        Require(root.Q<Button>("causality").worldBound.yMax < root.Q<Label>("combinationCount").worldBound.yMin
                            && root.Q<Button>("confirmButton").worldBound.yMax < root.Q("hudRoot").worldBound.yMax, "All word rows and footer fit the viewport");
                        _hud.SelectAttribute("gravity");
                        _hud.SelectRelation("revert");
                        ClickButton("confirmButton");
                        Require(_confirmCount == 1 && Mathf.Approximately(_demo.Energy, 80f), "Confirmation spends once");
                        Require(!_hud.IsAssemblyOpen, "Confirmation returns to HUD");
                        root = _document.rootVisualElement;
                        Require(!root.Q("gravityRule").ClassListContains("hidden"), "Active rule shown");
                        _hud.SetAssemblyOpen(true);
                        _pausedTime = root.Q("gravityRule").Q<Label>(className: "rule-time").text;
                        _nextStep = EditorApplication.timeSinceStartup + 1.2;
                        break;
                    case 2:
                        root = _document.rootVisualElement;
                        Require(root.Q("gravityRule").Q<Label>(className: "rule-time").text == _pausedTime, "Countdown freezes while assembling");
                        _hud.SetAssemblyOpen(false);
                        _nextStep = EditorApplication.timeSinceStartup + 1.3;
                        break;
                    case 3:
                        root = _document.rootVisualElement;
                        Require(root.Q("gravityRule").Q<Label>(className: "rule-time").text != _pausedTime, "Countdown resumes after closing");
                        _hud.SetAssemblyOpen(true);
                        _hud.SelectRelation("weaken");
                        _hud.ConfirmSelection();
                        Require(root.Q("gravityRule").Q<Label>(className: "rule-title").text == "重力削弱", "Same attribute replaces its display");
                        Require(Mathf.Approximately(_demo.Energy, 60f), "Replacement spends new cost");
                        _hud.SetAssemblyOpen(true);
                        _hud.SelectAttribute("friction");
                        _hud.SelectRelation("revert");
                        Require(root.Q<Button>("confirmButton").enabledSelf && root.Q<Label>("previewReason").text.Contains("未定稿"), "Undecided combination remains testable and labeled");
                        _hud.SelectAttribute("gravity");
                        _hud.SelectRelation("nullify");
                        _hud.ConfirmSelection();
                        _hud.SetAssemblyOpen(true);
                        _hud.ConfirmSelection();
                        _hud.SetAssemblyOpen(true);
                        _hud.ConfirmSelection();
                        Require(Mathf.Approximately(_demo.Energy, 0f) && !root.Q<Button>("confirmButton").enabledSelf, "Insufficient energy blocked");
                        for (int repeat = 0; repeat < 3; repeat++)
                        {
                            _hud.enabled = false;
                            _hud.enabled = true;
                            _demo.ResetDemo();
                        }
                        _confirmCount = 0;
                        _hud.SetAssemblyOpen(true);
                        ClickButton("confirmButton");
                        Require(_confirmCount == 1 && Mathf.Approximately(_demo.Energy, 80f), "Re-enable does not multiply click subscriptions");
                        _demo.ResetDemo();
                        P4DemoPawn pawn = UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>();
                        P4DemoGoal goal = UnityEngine.Object.FindFirstObjectByType<P4DemoGoal>();
                        pawn.GetComponent<Rigidbody2D>().position = goal.transform.position;
                        Physics2D.SyncTransforms();
                        SimulationMode2D previousMode = Physics2D.simulationMode;
                        Physics2D.simulationMode = SimulationMode2D.Script;
                        Physics2D.Simulate(0.02f);
                        Physics2D.simulationMode = previousMode;
                        _nextStep = EditorApplication.timeSinceStartup + 0.4;
                        break;
                    case 4:
                        Require(_document.rootVisualElement.Q<Label>("objective").text.Contains("已抵达"), "Goal trigger completes the whitebox route");
                        _demo.ResetDemo();
                        Require(!_demo.IsWorldPaused && Mathf.Approximately(_demo.Energy, 100f), "Reset restores the demo");
                        Require(UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>().GetComponent<Rigidbody2D>().position.x < 1f, "Reset returns pawn to spawn");
                        Finish(!_failed, _failed ? "Runtime Console reported errors; inspect validation log." : "All 35 combinations, 4 pending labels, 7 simultaneous slots, full layout, costs, pause/resume, replacement and reset passed.");
                        break;
                }
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void StepGravity()
        {
            switch (_stage++)
            {
                case 0:
                    _hud = UnityEngine.Object.FindFirstObjectByType<P4HudPresenter>();
                    _demo = UnityEngine.Object.FindFirstObjectByType<P4DemoController>();
                    _document = _hud.GetComponent<UIDocument>();
                    Require(RuleSystem.Instance != null, "P2 rule system initialized by the demo");
                    RuleCatalog catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(P4DemoSceneBuilder.GravityCatalogPath);
                    float[] expectedY = { 9.81f, 0f, -9.81f, -19.62f, -2.943f };
                    var probe = new GameObject("Validation gravity probe", typeof(Rigidbody2D));
                    Rigidbody2D probeBody = probe.GetComponent<Rigidbody2D>();
                    probeBody.position = new Vector2(1000f, 0f);
                    P4DemoPawn pawn = UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>();
                    Rigidbody2D pawnBody = pawn.GetComponent<Rigidbody2D>();
                    Require(pawnBody.bodyType == RigidbodyType2D.Kinematic && Physics2D.simulationMode == SimulationMode2D.Script,
                        "Kinematic actor and manual physics match the design");
                    for (int index = 0; index < 5; index++)
                    {
                        RelationId relation = (RelationId)index;
                        _demo.ResetDemo();
                        _hud.SetAssemblyOpen(true);
                        _hud.SelectRelation(relation.ToString().ToLowerInvariant());
                        RuleDef definition = catalog.Get(AttributeId.Gravity, relation);
                        Require(definition != null && _document.rootVisualElement.Q<Label>("previewCost").text.Contains(definition.ActivationCost.ToString("0")), "Official gravity cost: " + relation);
                        ClickButton("confirmButton");
                        ActiveRule slot = RuleSystem.Instance.Table.GetSlot(AttributeId.Gravity).Value;
                        Require(Mathf.Abs(Physics2D.gravity.y - expectedY[index]) < 0.001f
                            && slot.Relation == relation && Mathf.Approximately(slot.Remaining, definition.Duration)
                            && Mathf.Approximately(_demo.Energy, 100f - definition.ActivationCost), "UI applies P2 gravity and spends once: " + relation);
                        probeBody.linearVelocity = Vector2.zero;
                        probeBody.WakeUp();
                        pawnBody.simulated = true;
                        // 在空处测量加速度，避免平台碰撞把正确的速度清零。
                        pawn.transform.position = new Vector3(1f, 3f, 0f);
                        pawnBody.position = new Vector2(1f, 3f);
                        pawnBody.WakeUp();
                        Physics2D.SyncTransforms();
                        Vector2 startPosition = pawnBody.position;
                        pawn.SendMessage("FixedUpdate");
                        SimulationMode2D mode = Physics2D.simulationMode;
                        Physics2D.simulationMode = SimulationMode2D.Script;
                        Physics2D.Simulate(0.02f);
                        Physics2D.simulationMode = mode;
                        Require(Mathf.Abs(probeBody.linearVelocity.y - expectedY[index] * 0.02f) < 0.01f
                            && Mathf.Abs(pawn.Velocity.y - expectedY[index] * pawn.GravityScale * 0.02f) < 0.01f
                            && Mathf.Abs(pawnBody.position.y - startPosition.y - expectedY[index] * pawn.GravityScale * 0.02f * 0.02f) < 0.001f,
                            "Kinematic player and dynamic body share world gravity: " + relation
                            + "; probe=" + probeBody.linearVelocity.y + "; pawn=" + pawnBody.linearVelocity.y
                            + "; motor=" + pawn.Velocity.y + "; displacement=" + (pawnBody.position - startPosition)
                            + "; paused=" + _demo.IsWorldPaused);
                    }
                    UnityEngine.Object.DestroyImmediate(probe);

                    _demo.ResetDemo();
                    _hud.SetAssemblyOpen(true);
                    ClickButton("confirmButton");
                    Collider2D ceiling = GameObject.Find("Ceiling_ForFutureGravityRules").GetComponent<Collider2D>();
                    // 使用出生点上方的空处；手工调整后的上层平台占据天花板中心区域。
                    Vector2 ceilingPosition = new Vector2(1f,
                        ceiling.bounds.min.y - pawn.GetComponent<Collider2D>().bounds.extents.y - 0.05f);
                    pawn.transform.position = ceilingPosition;
                    pawnBody.position = ceilingPosition;
                    pawnBody.linearVelocity = Vector2.zero;
                    Physics2D.SyncTransforms();
                    pawn.RequestJump();
                    pawn.SendMessage("FixedUpdate");
                    Physics2D.Simulate(Time.fixedDeltaTime);
                    Require(pawn.Velocity.y < 0f && pawnBody.position.y < ceilingPosition.y,
                        "Jump moves away from ceiling under reversed gravity; grounded=" + pawn.IsGrounded
                        + "; velocity=" + pawn.Velocity + "; position=" + pawnBody.position
                        + "; start=" + ceilingPosition + "; gravity=" + Physics2D.gravity + "; paused=" + _demo.IsWorldPaused);
                    _hud.SetAssemblyOpen(true);
                    _hud.SelectAttribute("friction");
                    _hud.SelectRelation("nullify");
                    ClickButton("confirmButton");
                    Require(Mathf.Approximately(Physics2D.gravity.y, 9.81f)
                        && _document.rootVisualElement.Q<Label>("statusMessage").text.Contains("预览"), "Other attributes remain preview-only");

                    _demo.ResetDemo();
                    _hud.SetAssemblyOpen(true);
                    ClickButton("confirmButton");
                    _hud.SetAssemblyOpen(true);
                    _pauseProbe = new GameObject("Paused dynamic probe", typeof(Rigidbody2D)).GetComponent<Rigidbody2D>();
                    _pauseProbe.transform.position = new Vector3(1000f, 0f, 0f);
                    _pauseProbe.position = new Vector2(1000f, 0f);
                    _pausedPosition = _pauseProbe.position;
                    _nextStep = EditorApplication.timeSinceStartup + 0.3;
                    break;
                case 1:
                    _pausedRemaining = RuleSystem.Instance.Table.GetSlot(AttributeId.Gravity).Value.Remaining;
                    Require(!UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>().GetComponent<Rigidbody2D>().simulated, "Assembly freezes the demo body");
                    Screenshot("gravity-assembly.png");
                    _nextStep = EditorApplication.timeSinceStartup + 1.2;
                    break;
                case 2:
                    Require(Mathf.Approximately(RuleSystem.Instance.Table.GetSlot(AttributeId.Gravity).Value.Remaining, _pausedRemaining), "Assembly freezes the P2 rule countdown");
                    Require(_pauseProbe.position == _pausedPosition, "Assembly freezes dynamic world objects too");
                    _hud.SetAssemblyOpen(false);
                    _nextStep = EditorApplication.timeSinceStartup + 0.6;
                    break;
                case 3:
                    Require(RuleSystem.Instance.Table.GetSlot(AttributeId.Gravity).Value.Remaining < _pausedRemaining, "P2 countdown resumes");
                    Require(_pauseProbe.position.y > _pausedPosition.y, "Dynamic world objects resume under reversed gravity");
                    UnityEngine.Object.DestroyImmediate(_pauseProbe.gameObject);
                    float remaining = RuleSystem.Instance.Table.GetSlot(AttributeId.Gravity).Value.Remaining;
                    RuleSystem.Instance.Tick(remaining + 0.01f);
                    Require(!RuleSystem.Instance.Table.IsActive(AttributeId.Gravity)
                        && Mathf.Approximately(Physics2D.gravity.y, -9.81f)
                        && _document.rootVisualElement.Q("gravityRule").ClassListContains("hidden"), "Expiry restores gravity and removes HUD slot");
                    P4DemoPawn motor = UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>();
                    Rigidbody2D motorBody = motor.GetComponent<Rigidbody2D>();
                    _demo.ResetDemo();
                    for (int step = 0; step < 25; step++)
                    {
                        motor.SendMessage("FixedUpdate");
                        Physics2D.Simulate(Time.fixedDeltaTime);
                    }
                    Require(motor.GetComponent<Collider2D>().bounds.min.y >= GameObject.Find("Ground_Left").GetComponent<Collider2D>().bounds.max.y - 0.01f
                        && Mathf.Abs(motor.Velocity.y) < 0.001f, "Kinematic sweep lands on the floor without penetration");
                    _hud.SetAssemblyOpen(true);
                    _hud.SelectRelation("nullify");
                    ClickButton("confirmButton");
                    motor.transform.position = new Vector3(-1.8f, 2f, 0f);
                    motorBody.position = new Vector2(-1.8f, 2f);
                    Physics2D.SyncTransforms();
                    motor.SetMoveInput(-1f);
                    for (int step = 0; step < 12; step++)
                    {
                        motor.SendMessage("FixedUpdate");
                        Physics2D.Simulate(Time.fixedDeltaTime);
                    }
                    Require(motor.GetComponent<Collider2D>().bounds.min.x >= GameObject.Find("Left_Boundary").GetComponent<Collider2D>().bounds.max.x - 0.01f,
                        "Kinematic sweep blocks static walls");
                    _demo.ResetDemo();
                    for (int release = 0; release < 5; release++)
                    {
                        _hud.SetAssemblyOpen(true);
                        ClickButton("confirmButton");
                    }
                    _hud.SetAssemblyOpen(true);
                    Vector2 before = Physics2D.gravity;
                    ClickButton("confirmButton");
                    Require(Mathf.Approximately(_demo.Energy, 0f) && Physics2D.gravity == before
                        && !_document.rootVisualElement.Q<Button>("confirmButton").enabledSelf, "Insufficient energy cannot change gravity or spend twice");
                    _demo.ResetDemo();
                    Require(Mathf.Approximately(Physics2D.gravity.y, -9.81f)
                        && !RuleSystem.Instance.Table.IsActive(AttributeId.Gravity)
                        && Mathf.Approximately(_demo.Energy, 100f), "Reset clears real rules and restores energy");
                    _hud.SetAssemblyOpen(true);
                    ClickButton("confirmButton");
                    UnityEngine.Object.FindFirstObjectByType<P4DemoPawn>().enabled = false;
                    UnityEngine.Object.DestroyImmediate(_demo);
                    Require(RuleSystem.Instance == null && Mathf.Approximately(Physics2D.gravity.y, -9.81f)
                        && (int)Physics2D.simulationMode == SessionState.GetInt(SimulationModeKey, -1), "Scene teardown restores gravity, simulation mode and singleton");
                    Finish(!_failed, _failed ? "Runtime errors; inspect gravity log." : "Five gravity combinations apply through P2; kinematic actor, dynamic body, costs, ceiling jump, manual physics pause, expiry, reset and teardown passed.");
                    break;
            }
        }

        private static void CountConfirmation(string attribute, string relation) => _confirmCount++;

        private static void ClickButton(string name)
        {
            Button button = _document.rootVisualElement.Q<Button>(name);
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        private static void Require(bool condition, string behavior)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Failed: " + behavior);
            }
            Debug.Log("[P4 validation] Passed: " + behavior);
        }

        private static void Screenshot(string fileName)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _captureSurface;
            var screenshot = new Texture2D(_captureSurface.width, _captureSurface.height, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, screenshot.width, screenshot.height), 0, 0);
            screenshot.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(OutputPath, fileName), screenshot.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(screenshot);
        }

        private static void Finish(bool success, string detail)
        {
            EditorApplication.update -= Step;
            SessionState.SetBool(PendingKey, false);
            SessionState.SetBool(GravityModeKey, false);
            Directory.CreateDirectory(OutputPath);
            if (_captureSurface != null)
            {
                _captureSurface.Release();
            }
            File.WriteAllText(Path.Combine(OutputPath, "result.json"), JsonUtility.ToJson(new Result
            {
                Success = success,
                Detail = detail,
                EditorSearchStartupError = _editorSearchStartupError
            }, true));
            Debug.Log("[P4 validation] " + detail);
            EditorApplication.Exit(success ? 0 : 1);
        }

        [Serializable]
        private sealed class Result
        {
            public bool Success;
            public string Detail;
            public bool EditorSearchStartupError;
        }
    }
}
