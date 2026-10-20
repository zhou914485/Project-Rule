using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using RuleGame.Gameplay.Player;
using RuleGame.UI.RuleWheel;

namespace RuleGame.Presentation.RuleWheel
{
    /// <summary>
    /// 轮盘的动作与柔光表现。独立 Sprite 覆盖层只在选择期间接管外观，
    /// 不播放旧剪辑里的根节点曲线，不写玩家位置、碰撞或输入。
    /// </summary>
    [DefaultExecutionOrder(100), RequireComponent(typeof(RuleWheelPresenter))]
    public sealed class RuleSelectionOrbView : MonoBehaviour
    {
        [SerializeField] private RuleOrbArt _art;
        private enum Phase { Closed, Drawing, Holding, Returning }
        private Phase _phase;
        private RuleWheelPresenter _wheel;
        private PlayerController _player;
        private Collider2D _collider;
        private EntityStatus _status;
        private Animator _nativeAnimator;
        private SpriteRenderer[] _nativeRenderers;
        private bool[] _rendererStates;
        private bool _animatorEnabled;
        private GameObject _visualRoot;
        private SpriteRenderer _body, _halo;
        private Sprite _haloSprite;
        private Light2D _light;

        // 与 SwordLightPulse 同因：URP 打包裁掉运行时灯光变体，构建版只留精灵光晕。
#if UNITY_EDITOR
        private const bool UseLight = true;
#else
        private const bool UseLight = false;
#endif
        private Volume _volume;
        private VolumeProfile _profile;
        private Camera _camera;
        private UniversalAdditionalCameraData _cameraData;
        private bool _cameraCaptured, _postProcessing, _hdr;
        private bool _ownsCameraData;
        private MaterialPropertyBlock _properties;
        private static readonly int OrbPosition = Shader.PropertyToID("_OrbPosition");
        private static readonly int GlowStrength = Shader.PropertyToID("_GlowStrength");
        private float _elapsed, _glow, _closingGlow;
        private int _drawFrame, _returnStart;
        private bool _reverseDraw;

        public Vector3 CastOrigin => _halo != null && _phase != Phase.Closed
            ? _halo.transform.position : _wheel.Player != null ? _wheel.Player.transform.position : transform.position;

        private void Awake()
        {
            _wheel = GetComponent<RuleWheelPresenter>();
            _properties = new MaterialPropertyBlock();
            RuleCastVfx.Attach(this);
        }
        private void OnEnable()
        {
            _wheel.SelectionOpened += Begin;
            _wheel.SelectionClosed += End;
        }
        private void OnDisable()
        {
            _wheel.SelectionOpened -= Begin;
            _wheel.SelectionClosed -= End;
            FinishVisual();
        }

        private void Begin()
        {
            if (_art == null || _art.Atlas == null) return;
            if (_phase != Phase.Closed) FinishVisual();
            _player = _wheel.Player;
            if (_player == null) return;
            _collider = _player.GetComponent<Collider2D>();
            _status = _player.GetComponent<EntityStatus>();
            if (_collider == null) return;
            if (_visualRoot == null) CreateVisuals();
            for (int i = 0; i < _nativeRenderers.Length; i++)
            {
                _rendererStates[i] = _nativeRenderers[i] != null && _nativeRenderers[i].enabled;
                if (_nativeRenderers[i] != null) _nativeRenderers[i].enabled = false;
            }
            _animatorEnabled = _nativeAnimator != null && _nativeAnimator.enabled;
            if (_nativeAnimator != null) _nativeAnimator.enabled = false;
            // 只借用选择期的动画时钟。原控制器保留状态，取消后继续由 P3 决定运动动画。
            _camera = _wheel.WorldCamera;
            _cameraData = _camera != null ? _camera.GetComponent<UniversalAdditionalCameraData>() : null;
            if (_camera != null)
            {
                if (_cameraData == null)
                {
                    _cameraData = _camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                    _ownsCameraData = true;
                }
                _hdr = _camera.allowHDR; _camera.allowHDR = true;
                _postProcessing = _cameraData != null && _cameraData.renderPostProcessing;
                if (_cameraData != null) _cameraData.renderPostProcessing = true;
                _cameraCaptured = true;
            }
            _visualRoot.SetActive(true);
            _wheel.SetVisualAnchor(_halo.transform);
            _phase = Phase.Drawing; _elapsed = 0f; _glow = 0f; _drawFrame = 0;
            _body.sprite = _art.Frame(0);
        }

        private void End(bool released)
        {
            if (_phase == Phase.Closed) return;
            _reverseDraw = _phase == Phase.Drawing;
            _returnStart = _drawFrame;
            _closingGlow = _glow;
            _phase = Phase.Returning; _elapsed = 0f;
            // 收球动作独立于 RuleCastVfx 的规则生命周期光环。
        }

        private void CreateVisuals()
        {
            _nativeRenderers = _player.GetComponentsInChildren<SpriteRenderer>(true);
            _rendererStates = new bool[_nativeRenderers.Length];
            _nativeAnimator = _player.GetComponentInChildren<Animator>();
            _visualRoot = new GameObject("Rule Selection Orb");
            _visualRoot.transform.SetParent(_player.transform, false);
            _body = new GameObject("Authored Pose").AddComponent<SpriteRenderer>();
            _body.transform.SetParent(_visualRoot.transform, false);
            _body.sharedMaterial = _art.BodyMaterial;
            if (_nativeRenderers.Length > 0)
            {
                _body.sortingLayerID = _nativeRenderers[0].sortingLayerID;
                _body.sortingOrder = _nativeRenderers[0].sortingOrder;
            }
            _halo = new GameObject("Soft Hand Light").AddComponent<SpriteRenderer>();
            _halo.transform.SetParent(_visualRoot.transform, false);
            _haloSprite = Sprite.Create(Texture2D.whiteTexture,
                new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(.5f, .5f), Texture2D.whiteTexture.width, 0, SpriteMeshType.FullRect);
            _halo.sprite = _haloSprite; _halo.sharedMaterial = _art.HaloMaterial;
            _halo.sortingLayerID = _body.sortingLayerID; _halo.sortingOrder = _body.sortingOrder + 1;
            if (UseLight)
            {
                _light = _halo.gameObject.AddComponent<Light2D>();
                _light.lightType = Light2D.LightType.Point;
                _light.color = new Color(1f, .86f, .62f);
                _light.pointLightInnerRadius = .12f; _light.pointLightOuterRadius = 1.25f;
                _light.falloffIntensity = .5f; _light.shadowIntensity = .15f;
            }
            _volume = _visualRoot.AddComponent<Volume>(); _volume.isGlobal = true;
            _volume.priority = 25f; _volume.weight = 0f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = _profile.Add<Bloom>(true);
            bloom.threshold.value = .85f; bloom.intensity.value = .5f; bloom.scatter.value = .65f;
            bloom.tint.value = new Color(1f, .95f, .85f);
            _volume.sharedProfile = _profile;
        }

        private void LateUpdate()
        {
            if (_phase == Phase.Closed) return;
            if (_player == null || !_player.isActiveAndEnabled || _status == null || _status.Health <= 0f)
            { _wheel.Close(); FinishVisual(); return; }
            _elapsed += Time.unscaledDeltaTime;
            float fps = Mathf.Max(1f, _art.FramesPerSecond);
            int frame;
            if (_phase == Phase.Drawing)
            {
                // 8 帧掏出以独立时长快速播放；8 FPS 仅用于托球待机，避免起手每帧停留 125 ms。
                float progress = _elapsed / Mathf.Max(.05f, _art.DrawDuration);
                _drawFrame = Mathf.Min(_art.DrawCount - 1, Mathf.FloorToInt(progress * _art.DrawCount));
                frame = _drawFrame;
                _glow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, .95f, progress));
                if (progress >= 1f) { _phase = Phase.Holding; _elapsed = 0f; }
            }
            else if (_phase == Phase.Holding)
            {
                frame = _art.DrawCount + Mathf.FloorToInt(_elapsed * fps) % _art.HoldCount;
                _glow = Mathf.MoveTowards(_glow, 1f, Time.unscaledDeltaTime * 4f);
            }
            else
            {
                // 归还输入后立即允许移动/跳跃，不为了收球动作继续锁住主角。
                if (Mathf.Abs(_player.Velocity.x) > .15f || !_player.IsGrounded)
                { FinishVisual(); return; }
                int count = _reverseDraw ? _returnStart + 1 : _art.ReturnCount;
                float duration = Mathf.Max(.05f, _art.ReturnDuration * (_reverseDraw ? count / (float)_art.DrawCount : 1f));
                int index = Mathf.FloorToInt(_elapsed / duration * count);
                if (index >= count) { FinishVisual(); return; }
                frame = _reverseDraw ? Mathf.Max(0, _returnStart - index)
                    : _art.DrawCount + _art.HoldCount + index;
                _glow = _closingGlow * (1f - Mathf.SmoothStep(0f, 1f, _elapsed / duration));
            }
            _body.sprite = _art.Frame(frame);
            Vector2 up = Physics2D.gravity.sqrMagnitude > .001f ? -Physics2D.gravity.normalized : Vector2.up;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);
            Vector3 feet = _collider.bounds.center - (Vector3)up * _collider.bounds.extents.y;
            // 物理支撑面有皮肤距离，显示用脚底对齐地面；不移动玩家或碰撞体。
            if (_player.IsGrounded)
            {
                RaycastHit2D ground = Physics2D.Raycast(feet, -up, .2f, 1 << 8);
                if (ground.collider != null) feet = ground.point;
            }
            float scale = _collider.bounds.size.y / Mathf.Max(.1f, _art.BodyHeight);
            _body.transform.SetPositionAndRotation(feet, rotation);
            SetWorldScale(_body.transform, Vector3.one * scale);
            _body.flipX = (_player.Facing > 0f) == (up.y > 0f); // 交付原图朝左。
            Vector2 offset = _art.OrbOffset; if (_body.flipX) offset.x = -offset.x;
            Vector3 orb = feet + rotation * (Vector3)(offset * scale);
            _halo.transform.position = orb;
            float breath = 1f + Mathf.Sin(Time.unscaledTime * 1.7f) * .025f;
            float spread = _phase == Phase.Returning ? 1f + Mathf.Min(.14f, _elapsed * .3f) : 1f;
            SetWorldScale(_halo.transform, Vector3.one * (.75f * scale * breath * spread));
            _halo.color = new Color(1f, .88f, .65f, _glow * .28f);
            if (_light != null) _light.intensity = _glow * .38f; _volume.weight = _glow;
            _properties.SetVector(OrbPosition, orb); _properties.SetFloat(GlowStrength, _glow);
            _body.SetPropertyBlock(_properties);
        }

        private static void SetWorldScale(Transform target, Vector3 scale)
        {
            Vector3 parent = target.parent.lossyScale;
            target.localScale = new Vector3(scale.x / Mathf.Max(.001f, Mathf.Abs(parent.x)),
                scale.y / Mathf.Max(.001f, Mathf.Abs(parent.y)), 1f);
        }
        private void FinishVisual()
        {
            if (_phase == Phase.Closed) return;
            _phase = Phase.Closed;
            if (_wheel != null) _wheel.SetVisualAnchor(null);
            if (_visualRoot != null) _visualRoot.SetActive(false);
            if (_nativeRenderers != null)
                for (int i = 0; i < _nativeRenderers.Length; i++)
                    if (_nativeRenderers[i] != null) _nativeRenderers[i].enabled = _rendererStates[i];
            if (_nativeAnimator != null) _nativeAnimator.enabled = _animatorEnabled;
            if (_cameraCaptured)
            {
                if (_camera != null) _camera.allowHDR = _hdr;
                if (_cameraData != null) _cameraData.renderPostProcessing = _postProcessing;
                _cameraCaptured = false;
            }
        }
        private void OnDestroy()
        {
            if (_visualRoot != null) Destroy(_visualRoot);
            if (_profile != null) Destroy(_profile);
            if (_haloSprite != null) Destroy(_haloSprite);
            if (_ownsCameraData && _cameraData != null) Destroy(_cameraData);
        }
    }
}
