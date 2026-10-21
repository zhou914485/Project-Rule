using RuleGame.Gameplay.Player;
using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 常驻的贴屏背景层：按玩家所在的 <see cref="BackdropZone"/> 切换背景图。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不把背景摆在场景里</b>：世界是分区流式加载的，相邻分区会同时在場；
    /// 相机常驻在 GameRoot 上。场景里的世界坐标背景会在分区边界接缝、分区卸载时闪黑，
    /// 还会被 <c>WorldPartitionBuilder</c> 算进分区矩形。</para>
    /// <para><b>它是一层纸</b>：每帧按相机视野做 cover 缩放，永远盖满屏幕；不随玩家移动，
    /// 也不随规则选择把 ortho 压到 2.8 而放大 —— 读起来就是「背景在很远的地方」。</para>
    /// <para><b>淡化</b>：新图先满不透明垫在下面（Order -1000），旧图盖在上面淡出（Order -999）。
    /// 不能两张各半透明对淡 —— 半透明叠在相机底色上中间会发灰。</para>
    /// <para><b>不碰玩法状态</b>：全部用 <c>unscaledDeltaTime</c>，不读规则 / 暂停 / 快照，
    /// 与 <c>MusicDirector</c> 同一条纪律。</para>
    /// </remarks>
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    public sealed class BackdropDirector : MonoBehaviour
    {
        /// <summary>新图垫在下面：比一切世界物体（房子 -20、地形 1、遮罩 50）都低。</summary>
        public const int BackOrder = -1000;

        /// <summary>当前图 / 正在淡出的旧图：仍在 Back 之上，所以淡化时旧图盖着新图。</summary>
        public const int FrontOrder = -999;

        [SerializeField] private BackdropProfile _defaultProfile;

        [Tooltip("当前图。Order 固定 -999。")]
        [SerializeField] private SpriteRenderer _front;

        [Tooltip("切换中的新图。Order 固定 -1000，永远垫在 Front 下面。")]
        [SerializeField] private SpriteRenderer _back;

        [Tooltip("判定跟随谁。留空每 0.5 秒自动找 PlayerController。")]
        [SerializeField] private Transform _focus;

        [Tooltip("离开区域后还保留多久（米）。防边界抖动。")]
        [SerializeField, Min(0f)] private float _boundaryHysteresis = 1f;

        [Tooltip("目标背景连续稳定多久才真的切（秒）。")]
        [SerializeField, Min(0f)] private float _regionDelay = 0.2f;

        [Tooltip("cover 之后再多留一点，防边缘采样露底。")]
        [SerializeField, Range(1f, 1.2f)] private float _overscan = 1.02f;

        [SerializeField, Min(0.05f)] private float _focusRetrySeconds = 0.5f;

        private Camera _camera;
        private BackdropZone _zone;
        private BackdropProfile _current;
        private BackdropProfile _candidate;
        private float _candidateTime;
        private float _focusTimer;
        private bool _fading;
        private float _fadeElapsed;
        private float _fadeSeconds;

        /// <summary>现在显示的是哪张背景。</summary>
        public BackdropProfile CurrentProfile => _current;

        /// <summary>玩家此刻所在的背景区域；不在任何区域时为 null。</summary>
        public BackdropZone CurrentZone => _zone;

        /// <summary>正在交叉淡化。</summary>
        public bool IsFading => _fading;

        public SpriteRenderer FrontRenderer => _front;
        public SpriteRenderer BackRenderer => _back;

        private void Awake()
        {
            _camera = GetComponentInParent<Camera>(true);
            if (_camera == null) Debug.LogError("[背景] 必须挂在相机下面，否则算不出 cover。", this);
        }

        private void Start()
        {
            if (_front == null || _back == null)
            {
                Debug.LogError("[背景] 缺 Front / Back 两个 SpriteRenderer。", this);
                enabled = false;
                return;
            }

            _front.enabled = false;
            _back.enabled = false;

            if (_defaultProfile == null)
            {
                Debug.LogWarning("[背景] 没有默认背景，开局会看到相机底色。", this);
            }
            else if (_defaultProfile.Sprite == null)
            {
                // 2026-10-21 踩过：配置资产的 _sprite 用了错误的 fileID（内部 ID 而不是 21300000），
                // 引用会静默变成 null，画面只剩相机底色 —— 这里必须说清楚是哪份配置。
                Debug.LogError("[背景] 默认配置 " + _defaultProfile.name + " 的贴图引用是空的（_sprite 丢了）。", this);
            }
            else
            {
                Apply(_defaultProfile);
            }
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            RefreshFocus(dt);
            RefreshZone(dt);
            TickFade(dt);
            Fit();
        }

        /// <summary>直接切到某张背景（BossEncounter 之类的演出钩子用），跳过区域判定。</summary>
        public void SetProfile(BackdropProfile profile)
        {
            if (profile == null || profile.Sprite == null) return;
            Switch(profile);
        }

        /// <summary>回到默认背景（重开 / 读档时用）。</summary>
        public void ResetToDefault()
        {
            if (_defaultProfile != null) Switch(_defaultProfile);
        }

        private void RefreshFocus(float dt)
        {
            if (_focus != null) return;

            _focusTimer -= dt;
            if (_focusTimer > 0f) return;
            _focusTimer = _focusRetrySeconds;

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null) _focus = player.transform;
        }

        private void RefreshZone(float dt)
        {
            _zone = _focus != null
                ? BackdropZone.Resolve(_focus.position, _zone, _boundaryHysteresis)
                : null;

            BackdropProfile desired = _zone != null && _zone.Profile != null ? _zone.Profile : null;
            if (desired == null) desired = _defaultProfile;
            if (desired == null || desired == _current)
            {
                _candidate = null;
                _candidateTime = 0f;
                return;
            }

            if (desired != _candidate)
            {
                _candidate = desired;
                _candidateTime = 0f;
            }

            _candidateTime += dt;
            if (_candidateTime < _regionDelay) return;
            Switch(desired);
        }

        private void Switch(BackdropProfile profile)
        {
            if (profile == null || profile.Sprite == null) return;
            if (_fading) CompleteFade();

            bool hadPrevious = _current != null && _front != null && _front.enabled && _front.sprite != null;
            _current = profile;
            _candidate = null;
            _candidateTime = 0f;

            if (!hadPrevious)
            {
                Apply(profile);
                return;
            }

            // 新图先满不透明垫在下面
            _back.sprite = profile.Sprite;
            _back.color = Opaque(profile.Tint);
            _back.sortingOrder = BackOrder;
            _back.enabled = true;

            // 旧图盖在上面淡出
            _front.sortingOrder = FrontOrder;
            _fading = true;
            _fadeElapsed = 0f;
            _fadeSeconds = profile.FadeSeconds;
            ApplyCameraClearColor(profile);
        }

        private void Apply(BackdropProfile profile)
        {
            if (profile == null || profile.Sprite == null || _front == null) return;

            _front.sprite = profile.Sprite;
            _front.color = Opaque(profile.Tint);
            _front.sortingOrder = FrontOrder;
            _front.enabled = true;

            if (_back != null)
            {
                _back.enabled = false;
                _back.sprite = null;
            }

            _fading = false;
            ApplyCameraClearColor(profile);
        }

        private void TickFade(float dt)
        {
            if (!_fading) return;

            _fadeElapsed += dt;
            float t = Mathf.Clamp01(_fadeElapsed / Mathf.Max(0.01f, _fadeSeconds));

            if (_front != null)
            {
                Color color = _front.color;
                color.a = 1f - t;
                _front.color = color;
            }

            if (t >= 1f) CompleteFade();
        }

        /// <summary>旧图退场：停用旧的，两张交换角色，新的成为不透明的 Front。</summary>
        private void CompleteFade()
        {
            _fading = false;
            if (_front != null) _front.enabled = false;

            SpriteRenderer finished = _front;
            _front = _back;
            _back = finished;

            if (_front != null)
            {
                Color color = _front.color;
                color.a = 1f;
                _front.color = color;
                _front.sortingOrder = FrontOrder;
                _front.enabled = true;
            }

            if (_back != null)
            {
                _back.enabled = false;
                _back.sprite = null;
            }
        }

        /// <summary>按相机当前视野做 cover：永远盖满，短边优先补齐。</summary>
        private void Fit()
        {
            FitOne(_front);
            FitOne(_back);
        }

        private void FitOne(SpriteRenderer renderer)
        {
            if (renderer == null || !renderer.enabled || renderer.sprite == null || _camera == null) return;

            renderer.transform.localPosition = new Vector3(0f, 0f, 1f);

            float worldHeight = _camera.orthographicSize * 2f;
            float worldWidth = worldHeight * _camera.aspect;
            Vector2 size = renderer.sprite.bounds.size;
            if (size.x <= 0.0001f || size.y <= 0.0001f) return;

            float scale = Mathf.Max(worldWidth / size.x, worldHeight / size.y) * _overscan;
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void ApplyCameraClearColor(BackdropProfile profile)
        {
            if (_camera == null) return;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = profile.ClearColor;
        }

        private static Color Opaque(Color tint)
        {
            tint.a = 1f;
            return tint;
        }
    }
}
