using System;
using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 世界空间的文字提示：按键教程、机关说明、路牌。
    /// </summary>
    /// <remarks>
    /// <para><b>默认放在背景层</b>：用 <c>Sorting Order</c> 控制先后，默认 −10 ——
    /// 在近景背景（−20）前面、逻辑白盒（−2）与角色（0）后面。文字不会挡住玩法，
    /// 也不会抢输入（本组件没有碰撞体、不读任何按键）。</para>
    ///
    /// <para><b>两种用法</b>：把 <c>Trigger Radius</c> 设成 0 = 墙上的常驻教程；
    /// 设成大于 0 = 玩家走进半径才淡入。显示 / 隐藏都按非缩放时间推进，
    /// 所以规则轮盘暂停、慢时间规则都不会把淡入拖成几百秒。</para>
    ///
    /// <para><b>为什么用 TextMesh 而不是 UI Toolkit</b>：这是关卡里的世界物体
    /// （跟着关卡一起搬、一起缩放、排序在背景层），与白盒标签同一套字体。
    /// 屏幕上的 HUD / 对话框仍然走 <c>RuleGame.UI</c>。</para>
    ///
    /// <para>阴影是一个同字体的子物体 <c>Shadow</c>，由本组件自动创建与同步。
    /// 不要手动改它的文字 / 颜色 —— 每次 <see cref="RefreshVisual"/> 都会覆盖。</para>
    /// </remarks>
    [RequireComponent(typeof(TextMesh))]
    [DisallowMultipleComponent]
    public sealed class WorldHintText : MonoBehaviour
    {
        private const string ShadowName = "Shadow";
        private const float PollInterval = 0.15f;

        // ==================================================================
        //  文字
        // ==================================================================

        [Header("文字")]
        [SerializeField, TextArea(1, 6), Tooltip("提示正文。支持 TextMesh 富文本（<b>、<i>、<color=#FFD166>）。")]
        private string _text = "按 A / D 移动　空格 跳跃";

        [SerializeField, Min(1), Tooltip("字号。中文字体用 64 就够，实际世界大小由 Character Size 决定。")]
        private int _fontSize = 64;

        [SerializeField, Min(0.001f), Tooltip("世界尺寸：0.07 约等于 0.65 米高的一行字（镜头正交 4 时约占屏幕 1/12）。")]
        private float _characterSize = 0.07f;

        [SerializeField, Tooltip("文字颜色。背景很亮时改成深色，或者直接用富文本给某几个字改色。")]
        private Color _color = new Color(1f, 0.95f, 0.86f, 1f);

        [SerializeField, Tooltip("多行文字的整体锚点。墙上的教程用 MiddleCenter，路牌可用 MiddleLeft。")]
        private TextAnchor _anchor = TextAnchor.MiddleCenter;

        [SerializeField, Tooltip("每一行内部的水平对齐。")]
        private TextAlignment _alignment = TextAlignment.Center;

        [SerializeField, Min(0.5f), Tooltip("行距倍率。")]
        private float _lineSpacing = 1.15f;

        [SerializeField, Tooltip("允许 <b> / <color> 这类富文本标签。")]
        private bool _richText = true;

        // ==================================================================
        //  层级 / 阴影
        // ==================================================================

        [Header("层级（背景层）")]
        [SerializeField, Tooltip("Sorting Order（只有 Default 排序层）。背景层默认 −10：Bg_Far −30 < Bg_Near −20 < 本提示 −10 < 逻辑 −2 < 角色 0；想让提示压在角色前面就改成 10。")]
        private int _sortingOrder = -10;

        [Header("阴影")]
        [SerializeField, Tooltip("在文字后面垫一层深色描影，保证浅色背景上也读得清。")]
        private bool _shadow = true;

        [SerializeField, Tooltip("阴影颜色。Alpha 是阴影自身的浓度，再乘当前淡入透明度。")]
        private Color _shadowColor = new Color(0f, 0f, 0f, 0.6f);

        [SerializeField, Tooltip("阴影相对文字的偏移（世界单位）。默认往右下压一点点。")]
        private Vector2 _shadowOffset = new Vector2(0.035f, -0.035f);

        // ==================================================================
        //  出现条件
        // ==================================================================

        [Header("出现条件")]
        [SerializeField, Min(0f), Tooltip("玩家进入这个半径就淡入（按提示自身位置算距离）。0 = 一直显示，适合墙上的常驻教程。")]
        private float _triggerRadius = 6f;

        [SerializeField, Min(0f), Tooltip("玩家进入半径后，等几秒再淡入。防止跑过去时一闪而过。")]
        private float _showDelay = 0f;

        [SerializeField, Min(0f), Tooltip("完整显示后停留几秒自动收起。0 = 玩家待在里面就一直显示。")]
        private float _holdDuration = 0f;

        [SerializeField, Tooltip("整关只显示一次：第一次完整显示后，离开再回来也不再出现（ResetHint() 可解锁）。")]
        private bool _showOnce = false;

        // ==================================================================
        //  表现
        // ==================================================================

        [Header("表现")]
        [SerializeField, Min(0f), Tooltip("淡入 / 淡出时长（秒，非缩放时间）。0 = 瞬间。")]
        private float _fadeDuration = 0.25f;

        [SerializeField, Min(0f), Tooltip("上下浮动幅度（米）。背景墙上的文字保持 0；悬浮提示可以给 0.03。")]
        private float _bobAmplitude = 0f;

        [SerializeField, Min(0f), Tooltip("浮动速度（每秒来回次数）。")]
        private float _bobSpeed = 1.2f;

        // ==================================================================
        //  运行时
        // ==================================================================

        private TextMesh _mesh;
        private TextMesh _shadowMesh;
        private MeshRenderer _renderer;
        private MeshRenderer _shadowRenderer;
        private PlayerController _player;
        private Vector3 _baseLocalPosition;

        private float _alpha;
        private bool _fullyShown;
        private bool _hasShown;
        private bool _inside;
        private bool _wasInside;
        private bool _delayDone;
        private float _delayTimer;
        private float _holdTimer;
        private bool _holdDone;
        private bool _entryBlocked;
        private bool _manual;
        private float _manualRemaining;
        private float _pollTimer;

        // ==================================================================
        //  公开接口
        // ==================================================================

        /// <summary>完整显示过一次（<c>Show Once</c> 用它判断）。</summary>
        public bool HasShown => _hasShown;

        /// <summary>当前是否看得见（淡入中算可见）。</summary>
        public bool IsVisible => _alpha > 0.001f;

        /// <summary>改文字（等价于 <see cref="Text"/>，给生成器与 UnityEvent 用）。</summary>
        public void SetText(string text) => Text = text;

        /// <summary>文字内容。运行时改也会立即刷新。</summary>
        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                RefreshVisual();
            }
        }

        /// <summary>立刻显示（无视半径与「只显示一次」），直到 <see cref="Hide"/> 或 <see cref="ResetHint"/>。</summary>
        public void Show()
        {
            _manual = true;
            _manualRemaining = float.PositiveInfinity;
            _entryBlocked = false;
            _holdDone = false;
            _holdTimer = 0f;
        }

        /// <summary>显示一段时间后自动收起（秒）。期间同样无视半径。</summary>
        public void ShowFor(float seconds)
        {
            if (seconds <= 0f)
            {
                Hide();
                return;
            }

            _manual = true;
            _manualRemaining = seconds;
            _entryBlocked = false;
            _holdDone = false;
            _holdTimer = 0f;
        }

        /// <summary>收起，并且在玩家离开半径前不再自动弹出来（再次进入会按正常规则显示）。</summary>
        public void Hide()
        {
            _manual = false;
            _manualRemaining = 0f;
            _entryBlocked = true;
            _holdDone = true;
        }

        /// <summary>清掉「已显示过 / 手动收起」的状态，让提示可以重新按半径出现。</summary>
        public void ResetHint()
        {
            _manual = false;
            _manualRemaining = 0f;
            _hasShown = false;
            _entryBlocked = false;
            _holdDone = false;
            _holdTimer = 0f;
            _delayDone = _showDelay <= 0f;   // 已经站在半径里时不会再触发「进入」，这里直接按延迟结果落定
            _delayTimer = 0f;
            _alpha = 0f;
            _fullyShown = false;
            ApplyAlpha();
        }

        /// <summary>
        /// 把 Inspector 里的文字 / 尺寸 / 颜色 / 排序重新写进 TextMesh，并同步阴影。
        /// 换了字体或生成器改了默认值时调一次即可；运行时也会在 Awake 自动调。
        /// </summary>
        public void RefreshVisual()
        {
            if (_mesh == null) _mesh = GetComponent<TextMesh>();
            if (_mesh == null) return;

            _mesh.text = _text ?? string.Empty;
            _mesh.fontSize = Mathf.Max(1, _fontSize);
            _mesh.characterSize = Mathf.Max(0.001f, _characterSize);
            _mesh.lineSpacing = Mathf.Max(0.5f, _lineSpacing);
            _mesh.anchor = _anchor;
            _mesh.alignment = _alignment;
            _mesh.richText = _richText;
            _mesh.color = _color;

            if (_renderer == null) _renderer = _mesh.GetComponent<MeshRenderer>();
            if (_renderer != null) _renderer.sortingOrder = _sortingOrder;

            EnsureShadow();
            ApplyAlpha();
        }

        /// <summary>完整显示时触发（可以用它播一声提示音）。</summary>
        public event Action OnShown;

        /// <summary>完全收起时触发。</summary>
        public event Action OnHidden;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _mesh = GetComponent<TextMesh>();
            _baseLocalPosition = transform.localPosition;
            _alpha = 0f;
            RefreshVisual();
        }

        private void OnEnable()
        {
            // TextMesh / 阴影可能被外部改过；重新启用时按当前透明度刷新一遍。
            if (_mesh == null) return;
            RefreshVisual();
            ApplyAlpha();
        }

        private void OnValidate()
        {
            _triggerRadius = Mathf.Max(0f, _triggerRadius);
            _showDelay = Mathf.Max(0f, _showDelay);
            _holdDuration = Mathf.Max(0f, _holdDuration);
            _fadeDuration = Mathf.Max(0f, _fadeDuration);
            _bobAmplitude = Mathf.Max(0f, _bobAmplitude);
            _bobSpeed = Mathf.Max(0f, _bobSpeed);
            _characterSize = Mathf.Max(0.001f, _characterSize);
            _fontSize = Mathf.Max(1, _fontSize);

            if (!Application.isPlaying) _baseLocalPosition = transform.localPosition;
            RefreshVisual();
        }

        private void Reset()
        {
            _mesh = GetComponent<TextMesh>();
            _baseLocalPosition = transform.localPosition;
            RefreshVisual();
        }

        private void Update()
        {
            Tick(UnityEngine.Time.unscaledDeltaTime);
        }

        /// <summary>推进一帧（验证 / 测试用）。正常运行时由 <see cref="Update"/> 用非缩放时间调用。</summary>
        public void Tick(float dt)
        {
            if (dt < 0f) return;

            UpdateBob();
            PollInside(dt);
            UpdateManual(dt);

            if (_inside && !_wasInside) BeginEntry();
            else if (!_inside && _wasInside) EndEntry();
            _wasInside = _inside;

            // 进入后的延迟：到点才允许显示，避免玩家跑过时闪一下。
            if (_inside && !_delayDone && _showDelay > 0f)
            {
                _delayTimer += dt;
                if (_delayTimer >= _showDelay) _delayDone = true;
            }

            bool want = _manual || (_inside && _delayDone && !_holdDone && !_entryBlocked);

            // 停留计时只在「完全显示」之后开始；到点就收起，玩家继续待着也不重弹。
            if (want && !_manual && _holdDuration > 0f && _alpha >= 1f)
            {
                _holdTimer += dt;
                if (_holdTimer >= _holdDuration)
                {
                    _holdDone = true;
                    want = false;
                }
            }

            float target = want ? 1f : 0f;
            float previous = _alpha;
            if (_fadeDuration <= 0f) _alpha = target;
            else _alpha = Mathf.MoveTowards(_alpha, target, dt / _fadeDuration);

            if (!Mathf.Approximately(previous, _alpha)) ApplyAlpha();

            if (_alpha >= 1f && !_fullyShown)
            {
                _fullyShown = true;
                _hasShown = true;
                OnShown?.Invoke();
            }
            else if (_alpha <= 0f && _fullyShown)
            {
                _fullyShown = false;
                OnHidden?.Invoke();
            }
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>玩家是否在半径内。距离轮询而不是触发回调 —— 与 PlayerSpawnPoint 同一条做法。</summary>
        private void PollInside(float dt)
        {
            if (_triggerRadius <= 0f)
            {
                _inside = true;
                return;
            }

            _pollTimer -= dt;
            if (_pollTimer > 0f) return;
            _pollTimer = PollInterval;

            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            if (_player == null)
            {
                _inside = false;
                return;
            }

            Vector2 delta = (Vector2)_player.transform.position - (Vector2)transform.position;
            _inside = delta.sqrMagnitude <= _triggerRadius * _triggerRadius;
        }

        private void BeginEntry()
        {
            _delayTimer = 0f;
            _delayDone = _showDelay <= 0f;
            _holdTimer = 0f;
            _holdDone = false;
            // 「只显示一次」在**重新进入**时才拦截；第一次显示期间 hasShown 变真不能把它自己关掉。
            _entryBlocked = _showOnce && _hasShown;
        }

        private void EndEntry()
        {
            _delayTimer = 0f;
            _delayDone = false;
            _holdTimer = 0f;
            _holdDone = false;
        }

        private void UpdateManual(float dt)
        {
            if (!_manual || float.IsPositiveInfinity(_manualRemaining)) return;

            _manualRemaining -= dt;
            if (_manualRemaining > 0f) return;

            // ShowFor 的时间到：收起，并挡住半径逻辑，别让它下一帧又弹出来。
            _manual = false;
            _manualRemaining = 0f;
            _entryBlocked = true;
        }

        private void UpdateBob()
        {
            if (_bobAmplitude <= 0f || _bobSpeed <= 0f) return;

            float phase = UnityEngine.Time.unscaledTime * _bobSpeed * Mathf.PI * 2f;
            transform.localPosition = _baseLocalPosition + Vector3.up * (Mathf.Sin(phase) * _bobAmplitude);
        }

        private void EnsureShadow()
        {
            if (_shadowMesh == null)
            {
                Transform existing = transform.Find(ShadowName);
                if (existing != null) _shadowMesh = existing.GetComponent<TextMesh>();
            }

            if (!_shadow)
            {
                if (_shadowMesh != null) _shadowMesh.gameObject.SetActive(false);
                return;
            }

            if (_shadowMesh == null)
            {
                var go = new GameObject(ShadowName);
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(_shadowOffset.x, _shadowOffset.y, 0f);
                _shadowMesh = go.AddComponent<TextMesh>();
                _shadowRenderer = go.GetComponent<MeshRenderer>();
            }

            GameObject shadowGo = _shadowMesh.gameObject;
            if (!shadowGo.activeSelf) shadowGo.SetActive(true);
            if (_shadowRenderer == null) _shadowRenderer = shadowGo.GetComponent<MeshRenderer>();

            shadowGo.transform.localPosition = new Vector3(_shadowOffset.x, _shadowOffset.y, 0f);
            _shadowMesh.font = _mesh.font;
            _shadowMesh.text = _mesh.text;
            _shadowMesh.fontSize = _mesh.fontSize;
            _shadowMesh.characterSize = _mesh.characterSize;
            _shadowMesh.lineSpacing = _mesh.lineSpacing;
            _shadowMesh.anchor = _mesh.anchor;
            _shadowMesh.alignment = _mesh.alignment;
            _shadowMesh.richText = _mesh.richText;

            if (_shadowRenderer != null)
            {
                _shadowRenderer.sortingOrder = _sortingOrder - 1;
                if (_mesh.font != null && _mesh.font.material != null)
                {
                    _shadowRenderer.sharedMaterial = _mesh.font.material;
                }
            }
        }

        private void ApplyAlpha()
        {
            // 编辑模式永远按「完全显示」预览：Inspector 里改完能直接看到位置和大小。
            float alpha = Application.isPlaying ? _alpha : 1f;

            if (_mesh != null)
            {
                Color color = _color;
                color.a *= alpha;
                _mesh.color = color;
            }

            if (_shadowMesh != null)
            {
                Color color = _shadowColor;
                color.a *= alpha;
                _shadowMesh.color = color;
            }
        }
    }
}
