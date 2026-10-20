using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using RuleGame.Presentation.Audio;
using UnityEngine;

namespace RuleGame.Presentation.Player
{
    /// <summary>实际扣血驱动的闪身、定向爆点与墨色碎屑。原角色材质和颜色不被改写。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerHurtFeedback : MonoBehaviour
    {
        [Header("受击视觉")]
        [SerializeField, Range(.06f, .25f)] private float _flashDuration = .10f;
        [SerializeField, Range(.12f, .4f)] private float _burstDuration = .20f;
        [SerializeField, Range(0f, .25f)] private float _cameraStrength = .13f;
        [SerializeField] private Color _accent = new Color(.16f, .055f, .08f);

        private const int MoteCount = 18;
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private PlayerController _player;
        private EntityStatus _status;
        private EntityDamageable _damageable;
        private SpriteRenderer _body, _flash, _burst;
        private SnapshotSystem _snapshots;
        private Material _material;
        private SfxPlayer _fallbackAudio;
        private Sprite _quad;
        private MaterialPropertyBlock _properties;
        private readonly Mote[] _motes = new Mote[MoteCount];
        private float _age;
        private bool _active, _showInvincibility;
        private Vector3 _origin;
        private Vector2 _away, _up;
        private float _size;
        private Transform _motionRoot, _bodyParent;
        private Color _bodyColor, _appliedColor;
        private bool _tintApplied, _lethal;

        private sealed class Mote
        {
            public SpriteRenderer Renderer;
            public Vector2 Velocity;
            public float Size, Duration;
        }

        private void Awake()
        {
            _properties = new MaterialPropertyBlock();
            _player = GetComponent<PlayerController>();
            _status = GetComponent<EntityStatus>();
            _damageable = GetComponent<EntityDamageable>();
            _fallbackAudio = GetComponentInParent<SfxPlayer>();
            _body = GetComponentInChildren<SpriteRenderer>();
            _material = Resources.Load<Material>("PlayerHurtFeedback_Mat");
            if (_body == null || _damageable == null || _material == null)
            {
                Debug.LogError("[PlayerHurtFeedback] 缺少角色 SpriteRenderer、EntityDamageable 或受击材质。", this);
                enabled = false;
                return;
            }
            Texture2D texture = Texture2D.whiteTexture;
            _quad = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * .5f, texture.width);
            _quad.name = "PlayerHurtQuad";
            _quad.hideFlags = HideFlags.HideAndDontSave;
            // Animator 继续驱动原 Visual，受击变形只叠在独立父节点上，不碰身体和判定。
            if (_body.transform != transform)
            {
                _bodyParent = _body.transform.parent;
                _motionRoot = new GameObject("HurtMotionRoot").transform;
                _motionRoot.SetParent(_bodyParent, false);
                _body.transform.SetParent(_motionRoot, false);
            }
            _flash = CreateRenderer("HurtFlash", _body.transform);
            _burst = CreateRenderer("HurtBurst", transform);
            for (int i = 0; i < MoteCount; i++)
                _motes[i] = new Mote { Renderer = CreateRenderer("HurtMote", transform) };
        }

        private SpriteRenderer CreateRenderer(string objectName, Transform parent)
        {
            var go = new GameObject(objectName) { layer = _body.gameObject.layer };
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = _material;
            renderer.sprite = _quad;
            renderer.enabled = false;
            return renderer;
        }

        private void OnEnable()
        {
            if (_damageable != null) _damageable.OnDamageTaken += Play;
            BindSnapshots();
        }

        private void OnDisable()
        {
            if (_damageable != null) _damageable.OnDamageTaken -= Play;
            if (_snapshots != null) _snapshots.OnRestored -= Clear;
            _snapshots = null;
            Clear();
        }

        private void OnDestroy()
        {
            RestoreBodyTint();
            if (_motionRoot != null)
            {
                ResetBodyPose();
                if (_body != null && _bodyParent != null) _body.transform.SetParent(_bodyParent, false);
                Destroy(_motionRoot.gameObject);
            }
            if (_flash != null) Destroy(_flash.gameObject);
            if (_burst != null) Destroy(_burst.gameObject);
            foreach (Mote mote in _motes)
                if (mote != null && mote.Renderer != null) Destroy(mote.Renderer.gameObject);
            if (_quad != null) Destroy(_quad);
        }

        private void BindSnapshots()
        {
            if (_snapshots == SnapshotSystem.Instance) return;
            if (_snapshots != null) _snapshots.OnRestored -= Clear;
            _snapshots = SnapshotSystem.Instance;
            if (_snapshots != null) _snapshots.OnRestored += Clear;
        }

        private void Play(DamageInfo damage)
        {
            if (!_player.isActiveAndEnabled || _status.IsPaused || TimeManager.Instance?.IsPaused == true) return;
            RestoreBodyTint();
            // 正式入口已有 PlayerSfxPresenter；敌人实验室只有音源池，补齐其受击播声。
            if (GetComponent<PlayerSfxPresenter>() == null) _fallbackAudio?.Play(SfxCue.Hurt);
            _active = true;
            _lethal = _status.Health <= 0f;
            _showInvincibility = _status.Health > 0f;
            _age = 0f;
            _up = Physics2D.gravity.sqrMagnitude > .0001f ? -Physics2D.gravity.normalized : Vector2.up;
            _away = EntityDamageable.ResolveKnockbackDirection(transform.position, damage.Attacker, .2f);
            // 使用身体碰撞体而非大画布精灵的 bounds，爆点始终贴在躯干迎击侧。
            Collider2D collider = _player.GetComponent<Collider2D>();
            Bounds bounds = collider != null ? collider.bounds : _body.bounds;
            _size = Mathf.Clamp(bounds.size.y * 2.1f, 1.7f, 3.2f);
            _origin = bounds.center - new Vector3(_away.x * bounds.extents.x * .65f, 0, 0);
            _origin.z = _body.transform.position.z;
            for (int i = 0; i < MoteCount; i++)
            {
                float spread = -1.1f + (i * .618034f % 1f) * 2.5f;
                float speed = 2.1f + (i * .754878f % 1f) * 4.5f;
                _motes[i].Velocity = new Vector2(_away.x * Mathf.Cos(spread), 0) * speed
                    + _up * (Mathf.Sin(spread) * speed + 2f);
                _motes[i].Size = .045f + (i * .381966f % 1f) * .10f;
                _motes[i].Duration = .23f + (i * .237f % 1f) * .21f;
            }
            Camera camera = Camera.main;
            if (camera != null && camera.GetComponent<BossCameraImpulse>() == null)
                camera.gameObject.AddComponent<BossCameraImpulse>();
            BossCameraImpulse.Kick(_away, _cameraStrength * (_lethal ? 1.35f : 1f), .18f);
            Present();
        }

        private void LateUpdate()
        {
            BindSnapshots();
            Advance(Time.deltaTime);
        }

        private void Advance(float deltaTime)
        {
            if (_body == null || _flash == null) return;
            if (!_player.isActiveAndEnabled) { Clear(); return; }
            if (!_status.IsPaused && TimeManager.Instance?.IsPaused != true)
                _age += deltaTime;
            Present();
        }

        private void Present()
        {
            RestoreBodyTint();
            bool visible = _body.enabled && _body.gameObject.activeInHierarchy && _body.sprite != null;
            bool initialFlash = _active && _age < _flashDuration;
            bool blinking = _showInvincibility && _status.Health > 0f && _damageable.InvincibilityRemaining > 0f;
            _flash.enabled = visible && (initialFlash || blinking);
            if (_flash.enabled)
            {
                _flash.sprite = _body.sprite;
                _flash.flipX = _body.flipX;
                _flash.flipY = _body.flipY;
                _flash.drawMode = _body.drawMode;
                _flash.size = _body.size;
                _flash.maskInteraction = _body.maskInteraction;
                _flash.spriteSortPoint = _body.spriteSortPoint;
                _flash.color = new Color(1, 1, 1, _body.color.a);
                float opacity = initialFlash ? 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(_flashDuration * .5f, _flashDuration, _age))
                    : .25f;
                SetProperties(_flash, 0f, initialFlash ? 0f : 1f, opacity, _body.sortingOrder + 1);
            }
            if (blinking && !initialFlash)
            {
                _bodyColor = _body.color;
                _appliedColor = _bodyColor;
                _appliedColor.a *= (Mathf.FloorToInt(_age * 20f) & 1) == 0 ? 1f : .22f;
                _body.color = _appliedColor;
                _tintApplied = true;
            }
            ApplyBodyPose();
            float burstProgress = _age / Mathf.Max(.01f, _burstDuration);
            _burst.enabled = visible && _active && burstProgress < 1f;
            if (_burst.enabled)
            {
                Place(_burst.transform, _origin, _size, _size,
                    Mathf.Atan2(_away.y, _away.x) * Mathf.Rad2Deg);
                SetProperties(_burst, 1f, burstProgress, 1f, _body.sortingOrder + 3);
            }
            foreach (Mote mote in _motes)
            {
                float t = _age / mote.Duration;
                mote.Renderer.enabled = visible && _active && t < 1f;
                if (!mote.Renderer.enabled) continue;
                Vector2 offset = mote.Velocity * _age - _up * (7f * _age * _age);
                float size = mote.Size * (1f - .8f * t * t);
                Place(mote.Renderer.transform, _origin + (Vector3)offset, size, size * MoteStretch(mote),
                    Mathf.Atan2(mote.Velocity.y, mote.Velocity.x) * Mathf.Rad2Deg - 90f);
                SetProperties(mote.Renderer, 2f, t, 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1f, t)), _body.sortingOrder + 2);
            }
            if (_age > .5f) _active = false;
            if (!blinking) _showInvincibility = false;
        }

        private static float MoteStretch(Mote mote) => Mathf.Lerp(1.8f, 3.2f, Mathf.Clamp01(mote.Velocity.magnitude / 7f));

        private void ApplyBodyPose()
        {
            if (_motionRoot == null) return;
            if (!_active || _lethal || _age >= .24f) { ResetBodyPose(); return; }
            float recoil = Mathf.Pow(1f - Mathf.Clamp01(_age / .24f), 3f);
            float side = _away.x * (_player.IsBodyFlipped ? -1f : 1f);
            _motionRoot.localPosition = _motionRoot.parent.InverseTransformVector((Vector3)_away * (.09f * recoil));
            _motionRoot.localRotation = Quaternion.Euler(0, 0, -side * 13f * recoil);
            float squash = 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01(_age / .1f));
            _motionRoot.localScale = new Vector3(1f - .10f * squash, 1f + .10f * squash, 1f);
        }

        private void ResetBodyPose()
        {
            if (_motionRoot == null) return;
            _motionRoot.localPosition = Vector3.zero;
            _motionRoot.localRotation = Quaternion.identity;
            _motionRoot.localScale = Vector3.one;
        }

        private void RestoreBodyTint()
        {
            // Animator 或其它表现可以写新颜色；只归还本组件仍持有的那一次 alpha 覆盖。
            if (_tintApplied && _body != null && _body.color == _appliedColor) _body.color = _bodyColor;
            _tintApplied = false;
        }

        private void Place(Transform visual, Vector3 position, float width, float height, float angle)
        {
            visual.SetPositionAndRotation(position, Quaternion.Euler(0, 0, angle));
            Vector3 scale = transform.lossyScale;
            visual.localScale = new Vector3(width / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                height / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1f);
        }

        private void SetProperties(SpriteRenderer renderer, float mode, float progress, float opacity, int order)
        {
            renderer.sortingLayerID = _body.sortingLayerID;
            renderer.sortingOrder = order;
            _properties.Clear();
            _properties.SetFloat(ModeId, mode);
            _properties.SetFloat(ProgressId, Mathf.Clamp01(progress));
            _properties.SetFloat(OpacityId, opacity);
            _properties.SetColor(AccentId, _accent);
            renderer.SetPropertyBlock(_properties);
        }

        private void Clear()
        {
            BossCameraImpulse.CancelKick();
            RestoreBodyTint();
            ResetBodyPose();
            _active = _showInvincibility = false;
            if (_flash != null) _flash.enabled = false;
            if (_burst != null) _burst.enabled = false;
            foreach (Mote mote in _motes)
                if (mote != null && mote.Renderer != null) mote.Renderer.enabled = false;
        }
    }
}
