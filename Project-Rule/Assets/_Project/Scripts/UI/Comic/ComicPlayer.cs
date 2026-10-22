using System;
using System.Collections;
using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.Launch;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.UI.Comic
{
    /// <summary>
    /// 剧情漫画播放器：整页贴图 + 点击翻页 + 黑场 / 溶解转场。
    /// </summary>
    /// <remarks>
    /// <para><b>点击翻页，不是自动播放</b>（2026-10-21 定）：时长不用调，节奏交给玩家。
    /// 代价是必须让玩家知道「可以点」—— 等待时右下角出 <c>▼</c> 提示，换页与淡入期间不接收输入。</para>
    /// <para><b>转场不一刀切</b>：素材里有三组「同一构图只改一处」的页
    /// （11→12→13 人沉下去、15→16 熊出现、18→19 手伸进来）。它们只差约 2% 像素，
    /// 过一遍黑场会把「变化」读成「换了一张图」，所以走 <see cref="ComicTransition.Dissolve"/>；
    /// 其余页走黑场。分档由导入工具按实测像素差烤进资产，运行时不做图像比较。</para>
    /// <para><b>黑页不占点击</b>：1.png / 6.png 是纯黑（且互为同一张图），点击没有任何视觉反馈。
    /// 这类页标记成 <see cref="ComicPage.AutoAdvance"/>，停一下自己往下走。</para>
    /// <para><b>为什么自己建 UI</b>：它要同时活在 Boot（开场）与分区里（结尾），
    /// 挂在哪个场景的预制体上都要多一处接线。改成运行时自建 UIDocument、
    /// 复用场景里已有的 <c>PanelSettings</c>，开机即用、零接线。
    /// 贴图从 <c>Resources/Comic</c> 取，也能在 Inspector 里手动指定覆盖。</para>
    /// <para><b>暂停照老规矩</b>：<see cref="TimeManager.SetPaused"/> + <see cref="EntityStatus.SetPaused"/>
    /// + 输入门禁租约，<b>绝不碰 <c>Time.timeScale</c></b>，淡入淡出全走非缩放时间。
    /// 播放期间 <c>PauseMenuPresenter.AnyOverlayOpen()</c> 认 <see cref="IsShowing"/>，ESC 让给本组件当「跳过」。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ComicPlayer : MonoBehaviour
    {
        /// <summary>开场序列在 Resources 下的路径（不带扩展名）。</summary>
        public const string IntroResourcePath = "Comic/Comic_Intro";

        /// <summary>结尾序列在 Resources 下的路径。</summary>
        public const string EndingResourcePath = "Comic/Comic_Ending";

        /// <summary>最终关卡场景。只有这里的 Boss 被打死才播结尾。</summary>
        private const string FinalScenePath = "Assets/_Project/Scenes/Castle/castle.unity";

        [Header("序列（留空则从 Resources/Comic 取）")]
        [SerializeField] private ComicSequence _intro;
        [SerializeField] private ComicSequence _ending;

        [Header("转场（秒，非缩放时间）")]
        [SerializeField, Min(0f)] private float _dipOut = 0.14f;
        [SerializeField, Min(0f)] private float _dipHold = 0.06f;
        [SerializeField, Min(0f)] private float _dipIn = 0.18f;
        [SerializeField, Min(0f)] private float _sceneHold = 0.35f;
        [SerializeField, Min(0f)] private float _dissolve = 0.14f;

        [Header("输入")]
        [Tooltip("翻页刚结束的这段时间里不认输入（防止连点一次跳两页 / 按住空格连翻）。")]
        [SerializeField, Min(0f)] private float _inputGrace = 0.2f;

        [Tooltip("进入等待后多久才显示 ▼ 提示（太早会闪）。")]
        [SerializeField, Min(0f)] private float _hintDelay = 0.5f;

        [Tooltip("开场里按 ESC 跳过；开头这段时间不认（防止从菜单带过来的按键直接跳过）。")]
        [SerializeField, Min(0f)] private float _skipArmDelay = 0.8f;

        [Tooltip("Boss 死后隔多久起结尾（留出死亡表现的时间，秒）。")]
        [SerializeField, Min(0f)] private float _endingDelay = 1.2f;

        private static ComicPlayer _instance;

        /// <summary>当前实例；没有就是没装（正常流程下开机就有）。</summary>
        public static ComicPlayer Instance => _instance;

        /// <summary>正在播（暂停菜单据此让路）。</summary>
        public bool IsShowing { get; private set; }

        private UIDocument _document;
        private VisualElement _root;
        private Image _imageFront;
        private Image _imageBack;
        private VisualElement _clickCatcher;
        private Label _hint;
        private Button _skipButton;

        private PlayerController _player;
        private EntityStatus _status;
        private IDisposable _inputLease;
        private bool _ownsPause;
        private bool _pausedBefore;
        private bool _statusPausedBefore;

        // 播放期间被藏起来的 UI（HUD / 轮盘 / 主菜单…），播完原样还回去。
        private readonly List<UIDocument> _hiddenDocuments = new List<UIDocument>();
        private readonly List<DisplayStyle> _hiddenDisplays = new List<DisplayStyle>();

        private bool _acceptAdvance;
        private bool _advanceQueued;
        private bool _skipRequested;
        private bool _allowSkip;
        private float _acceptAt;
        private float _skipArmedAt;

        // ==================================================================
        //  安装
        // ==================================================================

        /// <summary>开机自动装一份（DontDestroyOnLoad）。</summary>
        /// <remarks>没有场景接线，所以用运行时钩子：Boot 场景一加载就有，
        /// 结尾那条 <see cref="BossEncounter.Cleared"/> 订阅才能不漏。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => EnsureInstance();

        /// <summary>取实例，没有就建一个。</summary>
        public static ComicPlayer EnsureInstance()
        {
            if (_instance != null) return _instance;

            GameObject host = new GameObject("[剧情漫画]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _instance = host.AddComponent<ComicPlayer>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            BossEncounter.Cleared += HandleBossCleared;
        }

        private void OnDestroy()
        {
            BossEncounter.Cleared -= HandleBossCleared;
            if (_instance == this) _instance = null;
        }

        // ==================================================================
        //  对外
        // ==================================================================

        /// <summary>
        /// 播开场。没有序列 / 已经有东西在播时返回 false，调用方照常往下走。
        /// </summary>
        /// <param name="onFinished">播完（或被跳过）之后回调一次。</param>
        public bool TryPlayIntro(Action onFinished)
        {
            ComicSequence sequence = _intro != null ? _intro : Resources.Load<ComicSequence>(IntroResourcePath);
            if (sequence == null || sequence.Count == 0)
            {
                Debug.LogWarning("[Comic] 没找到开场序列 —— 跳过开场。" +
                                 "（跑一次 Tools/规则游戏/剧情漫画/① 导入素材并生成序列）", this);
                return false;
            }

            return Play(sequence, onFinished, true);
        }

        /// <summary>直接播一段序列（演出调试 / 结尾用）。</summary>
        public bool Play(ComicSequence sequence, Action onFinished, bool allowSkip)
        {
            if (sequence == null || sequence.Count == 0) return false;
            if (IsShowing) return false;

            StartCoroutine(PlayRoutine(sequence, onFinished, allowSkip));
            return true;
        }

        // ==================================================================
        //  演出
        // ==================================================================

        private IEnumerator PlayRoutine(ComicSequence sequence, Action onFinished, bool allowSkip)
        {
            IsShowing = true;
            _allowSkip = allowSkip;
            _skipRequested = false;
            _acceptAdvance = false;
            _advanceQueued = false;
            _skipArmedAt = Time.unscaledTime + _skipArmDelay;

            yield return EnsureUi();

            if (_root == null)
            {
                // 没 UI 就别把游戏停在黑屏上：收尾、照常继续。
                Debug.LogError("[Comic] 没能建起漫画界面（场景里没有可用的 PanelSettings）—— 直接收尾。", this);
                IsShowing = false;
                onFinished?.Invoke();
                yield break;
            }

            // 中途异常（引用被销毁 / 场景卸载）也要把黑幕、暂停、输入门禁收回来。
            TakePause();
            ShowRoot(true);

            try
            {
                // 放进来是为了「藏了就必须还」：HideOtherUi 自己出意外时，finally 仍会把 UI 还原。
                HideOtherUi();

                Texture2D previous = null;

                for (int i = 0; i < sequence.Count; i++)
                {
                    ComicPage page = sequence.Get(i);

                    yield return EnterPage(page, previous, i == 0);
                    if (page.Image != null) previous = page.Image;

                    if (page.AutoAdvance)
                    {
                        // 黑页：停一下自己走，不占一次点击。
                        yield return WaitUnscaled(page.BlackHold);
                        continue;
                    }

                    BeginAdvanceWait();

                    // 提示晚一点出（太早会闪），但这段时间里的点击照样算数 —— 别让急着翻页的人等一个提示。
                    float hintAt = Time.unscaledTime + _hintDelay;
                    while (Time.unscaledTime < hintAt && !_advanceQueued && !_skipRequested) yield return null;

                    if (!_advanceQueued && _hint != null) _hint.style.visibility = Visibility.Visible;
                    while (!_advanceQueued && !_skipRequested) yield return null;
                    EndAdvanceWait();

                    if (_hint != null) _hint.style.visibility = Visibility.Hidden;
                    if (_skipRequested) break;
                }

                // 收尾：淡到黑再交还控制权，别让最后一页直接「啪」地消失。
                yield return Fade(_imageFront, 1f, 0f, _dipOut);
            }
            finally
            {
                EndAdvanceWait();
                RestoreOtherUi();
                ShowRoot(false);
                ReleasePause();
                IsShowing = false;

                // 收尾之后界面才允许被下一段复用；元素本身留着，不必每次重建。
                if (_hint != null) _hint.style.visibility = Visibility.Hidden;

                // 回调放在 finally 里：中途抛异常（引用被销毁 / 场景卸载）也不能把
                // 「开场播完 → 开始游戏」这条链断在半路，否则玩家会卡在黑屏。
                onFinished?.Invoke();
            }
        }

        /// <summary>进入一页：按本页的转场把上一页换下去。</summary>
        private IEnumerator EnterPage(ComicPage page, Texture2D previous, bool first)
        {
            ComicTransition transition = first ? ComicTransition.Dip : page.Transition;

            if (transition == ComicTransition.Dissolve && previous != null)
            {
                // 下一张压在上面淡入，旧的留在下面 —— 共享的分格一个像素都不会跳。
                _imageBack.image = previous;
                _imageBack.style.opacity = 1f;
                _imageFront.image = page.Image;
                _imageFront.style.opacity = 0f;

                yield return Fade(_imageFront, 0f, 1f, _dissolve);

                _imageBack.image = null;
                _imageBack.style.opacity = 0f;
                yield break;
            }

            bool scene = transition == ComicTransition.SceneDip;
            float outSeconds = scene ? _dipOut * 1.4f : _dipOut;
            float inSeconds = scene ? _dipIn * 1.4f : _dipIn;

            if (!first) yield return Fade(_imageFront, 1f, 0f, outSeconds);

            _imageFront.image = page.Image;
            _imageFront.style.opacity = 0f;
            yield return Fade(_imageFront, 0f, 1f, inSeconds);

            if (!first) yield return WaitUnscaled(scene ? _sceneHold : _dipHold);
        }

        // ==================================================================
        //  输入
        // ==================================================================

        private void BeginAdvanceWait()
        {
            // 不重置 _skipRequested：淡入 / 提示延迟里按下的 ESC 也算数。
            _advanceQueued = false;
            _acceptAt = Time.unscaledTime + _inputGrace;
            _acceptAdvance = true;
        }

        private void EndAdvanceWait()
        {
            _acceptAdvance = false;
            _advanceQueued = false;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            TryQueueAdvance();
        }

        private void Update()
        {
            if (!IsShowing) return;

            if (_allowSkip && Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame
                && Time.unscaledTime >= _skipArmedAt)
            {
                _skipRequested = true;
                return;
            }

            if (!_acceptAdvance || Time.unscaledTime < _acceptAt) return;

            // 鼠标走 PointerDown（上面）；这里只兜键盘 / 手柄：UI Toolkit 的事件系统在不同输入配置下
            // 表现不完全一样，多一条轮询不会导致一次点击翻两页 —— 只要 _acceptAdvance 是「消费即关」的。
            if (Keyboard.current != null
                && (Keyboard.current.spaceKey.wasPressedThisFrame
                    || Keyboard.current.enterKey.wasPressedThisFrame
                    || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                TryQueueAdvance();
                return;
            }

            if (Gamepad.current != null
                && (Gamepad.current.buttonSouth.wasPressedThisFrame
                    || Gamepad.current.startButton.wasPressedThisFrame))
            {
                TryQueueAdvance();
            }
        }

        private void TryQueueAdvance()
        {
            if (!_acceptAdvance) return;
            if (Time.unscaledTime < _acceptAt) return;
            _advanceQueued = true;
        }

        // ==================================================================
        //  界面（运行时自建）
        // ==================================================================

        private IEnumerator EnsureUi()
        {
            if (_root != null) yield break;

            PanelSettings settings = FindPanelSettings();
            if (settings == null) yield break;

            GameObject host = new GameObject("Comic UI");
            host.transform.SetParent(transform, false);

            // 先关掉再加组件：UIDocument 在 OnEnable 里按 panelSettings 建树，
            // 组件加完再赋 panelSettings 会多绕一圈（不同版本行为不完全一致）。
            host.SetActive(false);
            UIDocument document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            document.sortingOrder = 240;
            _document = document;
            host.SetActive(true);

            float deadline = Time.unscaledTime + 2f;
            while (document.rootVisualElement == null && Time.unscaledTime < deadline) yield return null;

            VisualElement root = document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[Comic] UIDocument 没有建出根节点。", this);
                yield break;
            }

            Build(root);
        }

        private static PanelSettings FindPanelSettings()
        {
            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                UIDocument document = documents[i];
                if (document == null || document.panelSettings == null) continue;
                if (document.GetComponent<ComicPlayer>() != null) continue;
                return document.panelSettings;
            }

            return null;
        }

        private void Build(VisualElement root)
        {
            _root = root;
            root.Clear();

            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.top = 0f;
            root.style.right = 0f;
            root.style.bottom = 0f;
            root.style.backgroundColor = Color.black;
            root.style.display = DisplayStyle.None;

            // 背景层：只在 Dissolve 时出场。
            _imageBack = MakeImage();
            root.Add(_imageBack);

            // 前景层：始终是当前页。
            _imageFront = MakeImage();
            root.Add(_imageFront);

            // 点击接收层：铺满、透明，压在两张图之上、跳过按钮之下。
            _clickCatcher = new VisualElement { pickingMode = PickingMode.Position };
            Fill(_clickCatcher);
            _clickCatcher.RegisterCallback<PointerDownEvent>(OnPointerDown);
            root.Add(_clickCatcher);

            _hint = new Label("点击继续　▼")
            {
                pickingMode = PickingMode.Ignore
            };
            _hint.style.position = Position.Absolute;
            _hint.style.left = 0f;
            _hint.style.right = 0f;
            _hint.style.bottom = 56f;
            _hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hint.style.fontSize = 24f;
            _hint.style.color = new Color(1f, 1f, 1f, 0.55f);
            _hint.style.visibility = Visibility.Hidden;
            root.Add(_hint);

            _skipButton = new Button(OnSkipClicked) { text = "跳过 ▶▶" };
            _skipButton.style.position = Position.Absolute;
            _skipButton.style.top = 24f;
            _skipButton.style.right = 32f;
            _skipButton.style.fontSize = 18f;
            _skipButton.style.color = new Color(1f, 1f, 1f, 0.6f);
            _skipButton.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _skipButton.style.borderTopWidth = 0f;
            _skipButton.style.borderBottomWidth = 0f;
            _skipButton.style.borderLeftWidth = 0f;
            _skipButton.style.borderRightWidth = 0f;
            _skipButton.style.display = DisplayStyle.None;
            root.Add(_skipButton);
        }

        private static Image MakeImage()
        {
            Image image = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };

            Fill(image);
            image.style.opacity = 0f;
            return image;
        }

        private static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.top = 0f;
            element.style.right = 0f;
            element.style.bottom = 0f;
        }

        // ==================================================================
        //  屏蔽其它 UI
        // ==================================================================

        /// <summary>
        /// 播放期间把场上其它所有 UIDocument 藏起来。
        /// </summary>
        /// <remarks>
        /// <para>不靠 sortingOrder 压：漫画是整屏黑底 + 一张图，只要 HUD 还画在任何一个面板里，
        /// 血量条就会浮在黑场上面 —— 那一下比没有动画还出戏。</para>
        /// <para>只改 root 的 <c>display</c>，不动组件启用状态：别人的 <c>OnEnable</c> 缓存
        /// （<c>AbilityUnlockOverlay</c> 那种）不能被我们重跑一遍。</para>
        /// </remarks>
        private void HideOtherUi()
        {
            RestoreOtherUi();

            UIDocument[] documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                UIDocument document = documents[i];
                if (document == null || document == _document) continue;

                VisualElement root = document.rootVisualElement;
                if (root == null) continue;

                _hiddenDocuments.Add(document);
                _hiddenDisplays.Add(root.style.display.value);
                root.style.display = DisplayStyle.None;
            }
        }

        /// <summary>把 <see cref="HideOtherUi"/> 藏起来的 UI 原样还回去。</summary>
        private void RestoreOtherUi()
        {
            for (int i = 0; i < _hiddenDocuments.Count; i++)
            {
                UIDocument document = _hiddenDocuments[i];
                if (document == null) continue;

                VisualElement root = document.rootVisualElement;
                if (root != null) root.style.display = _hiddenDisplays[i];
            }

            _hiddenDocuments.Clear();
            _hiddenDisplays.Clear();
        }

        private void ShowRoot(bool show)
        {
            if (_root == null) return;

            if (show)
            {
                _imageFront.image = null;
                _imageFront.style.opacity = 0f;
                _imageBack.image = null;
                _imageBack.style.opacity = 0f;
                if (_hint != null) _hint.style.visibility = Visibility.Hidden;
                if (_skipButton != null)
                    _skipButton.style.display = _allowSkip ? DisplayStyle.Flex : DisplayStyle.None;

                _root.style.display = DisplayStyle.Flex;
            }
            else
            {
                _root.style.display = DisplayStyle.None;
            }
        }

        private void OnSkipClicked()
        {
            if (!_allowSkip) return;
            if (Time.unscaledTime < _skipArmedAt) return;
            _skipRequested = true;
        }

        // ==================================================================
        //  暂停
        // ==================================================================

        private void TakePause()
        {
            _player = FindAnyObjectByType<PlayerController>();
            if (_player != null) _player.TryGetComponent(out _status);

            if (TimeManager.Instance != null)
            {
                _pausedBefore = TimeManager.Instance.IsPaused;
                TimeManager.Instance.SetPaused(true);
            }

            if (_status != null)
            {
                _statusPausedBefore = _status.IsPaused;
                _status.SetPaused(true);
            }

            _inputLease = _player != null ? _player.BlockGameplayInput() : null;
            _ownsPause = true;
        }

        private void ReleasePause()
        {
            if (_inputLease != null)
            {
                _inputLease.Dispose();
                _inputLease = null;
            }

            if (!_ownsPause) return;
            _ownsPause = false;

            if (TimeManager.Instance != null) TimeManager.Instance.SetPaused(_pausedBefore);
            if (_status != null) _status.SetPaused(_statusPausedBefore);
            _status = null;
        }

        // ==================================================================
        //  结尾
        // ==================================================================

        private void HandleBossCleared(BossEncounter boss)
        {
            if (IsShowing) return;

            // 调试场景直接开跑、没有从 Boot 开局时，别把结尾演出来。
            if (GameRoot.Instance == null || !GameRoot.Instance.HasStarted) return;

            // 只有最后一关的 Boss 才播结尾。中间的 Boss（积木国等）打死只开门、继续玩。
            if (!IsFinalBoss(boss)) return;

            ComicSequence sequence = _ending != null ? _ending : Resources.Load<ComicSequence>(EndingResourcePath);
            if (sequence == null || sequence.Count == 0)
            {
                // 没资产就什么都不做：跑去回主菜单会把正在玩的存档踢回标题。
                Debug.LogWarning("[Comic] 没找到结尾序列 —— 不播结尾。" +
                                 "（跑一次 Tools/规则游戏/剧情漫画/① 导入素材并生成序列）", this);
                return;
            }

            StartCoroutine(PlayEndingAfterDelay(sequence));
        }

        private IEnumerator PlayEndingAfterDelay(ComicSequence sequence)
        {
            yield return WaitUnscaled(_endingDelay);
            Play(sequence, ReturnToMenu, false);
        }

        /// <summary>
        /// Boss 是不是最终关卡（castle）里的那一个。
        /// </summary>
        /// <remarks>结尾只属于最终 Boss；中间关卡的 Boss 由 <see cref="BossEncounter"/> 自己开门，
        /// 不能因为共用 <see cref="BossEncounter.Cleared"/> 就把玩家踢回标题。</remarks>
        private static bool IsFinalBoss(BossEncounter boss)
        {
            if (boss == null) return false;

            string scenePath = boss.gameObject.scene.path;
            return !string.IsNullOrEmpty(scenePath)
                && string.Equals(scenePath, FinalScenePath, StringComparison.Ordinal);
        }

        private void ReturnToMenu()
        {
            if (GameRoot.Instance != null) GameRoot.Instance.StopForMenu();

            MainMenuPresenter menu = FindAnyObjectByType<MainMenuPresenter>(FindObjectsInactive.Include);
            if (menu != null) menu.ShowMenu();
        }

        // ==================================================================
        //  工具
        // ==================================================================

        private IEnumerator Fade(VisualElement element, float from, float to, float seconds)
        {
            if (element == null) yield break;

            if (seconds <= 0.0001f)
            {
                element.style.opacity = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                element.style.opacity = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            element.style.opacity = to;
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
