using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuleGame.Presentation.Player
{
    /// <summary>判定开窗时播放剑气，三个复用槽位。范围固定在出手位置，不随之后移动延长。</summary>
    [DefaultExecutionOrder(50), DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    public sealed class PlayerAttackVfx : MonoBehaviour
    {
        [Header("剑气")]
        [SerializeField] private Color _lightColor = new Color(.8f, .9f, 1f, 1f);
        [SerializeField] private Color _heavyColor = new Color(.94f, .97f, 1f, 1f);
        [SerializeField, Min(.05f)] private float _lightDuration = .14f;
        [SerializeField, Min(.05f)] private float _heavyDuration = .20f;

        [Header("挥斩光效")]
        [SerializeField, Range(0f, 2f)] private float _glowStrength = .65f;
        [SerializeField, Range(0f, 2f)] private float _lightIntensity = .9f;
        [SerializeField, Min(.1f)] private float _lightRadiusScale = 1.25f;

        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int HeavyId = Shader.PropertyToID("_Heavy");
        private static readonly int GlowOnlyId = Shader.PropertyToID("_GlowOnly");
        private static readonly int GlowScaleId = Shader.PropertyToID("_GlowScale");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");
        private readonly Slot[] _slots = new Slot[3];
        private PlayerController _player;
        private EntityStatus _status;
        private SpriteRenderer _heroRenderer;
        private SnapshotSystem _snapshots;
        private Material _material;
        private Mesh _quad;
        private int _nextSlot;
        private SwordLightPulse _lightPulse;

        private sealed class Slot
        {
            public MeshRenderer Renderer;
            public MeshRenderer Glow;
            public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
            public readonly MaterialPropertyBlock GlowProperties = new MaterialPropertyBlock();
            public float Age, Duration;
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _status = GetComponent<EntityStatus>();
            _heroRenderer = GetComponentInChildren<SpriteRenderer>();
            // Resources 材质显式引用 Shader，Player 构建也能保留它。
            _material = Resources.Load<Material>("PlayerSwordWave_Mat");
            if (_material == null)
                Debug.LogError("[PlayerAttackVfx] 缺少 Resources/PlayerSwordWave_Mat。", this);

            // ⚠️ 补光的构造会 new 一个 Light2D；URP 配置不对时它会抛异常，
            //    那样 _lightPulse 就是 null，LateUpdate 每帧 NRE。这里兜住并说清楚原因。
            try
            {
                _lightPulse = new SwordLightPulse(transform, _heroRenderer);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlayerAttackVfx] 剑光补光初始化失败，本次运行关闭补光：{e.GetType().Name}: {e.Message}", this);
            }
        }

        private void OnEnable()
        {
            _player.OnAttackStruck += Play;
            if (_status != null) _status.OnHealthChanged += OnHealthChanged;
            BindSnapshots();
        }

        private void OnDisable()
        {
            _player.OnAttackStruck -= Play;
            if (_status != null) _status.OnHealthChanged -= OnHealthChanged;
            if (_snapshots != null) _snapshots.OnRestored -= Clear;
            _snapshots = null;
            Clear();
        }

        private void BindSnapshots()
        {
            if (_snapshots == SnapshotSystem.Instance) return;
            if (_snapshots != null) _snapshots.OnRestored -= Clear;
            _snapshots = SnapshotSystem.Instance;
            if (_snapshots != null) _snapshots.OnRestored += Clear;
        }

        private void Play(PlayerAttackCue cue)
        {
            if (_material == null || !_player.isActiveAndEnabled
                || (_status != null && (_status.IsPaused || _status.Health <= 0f))
                || TimeManager.Instance?.IsPaused == true) return;
            if (cue.Size.x <= 0f || cue.Size.y <= 0f) return;

            Slot slot = _slots[_nextSlot] ?? (_slots[_nextSlot] = CreateSlot());
            _nextSlot = (_nextSlot + 1) % _slots.Length;
            slot.Age = 0f;
            slot.Duration = Mathf.Max(.05f, cue.Heavy ? _heavyDuration : _lightDuration);
            slot.Renderer.transform.position = new Vector3(cue.Center.x, cue.Center.y, transform.position.z);
            // 只翻 UV/几何方向，不旋转或放大判定范围；重力反转也不改世界水平判定盒。
            float vertical = cue.Heavy || (cue.ComboIndex & 1) == 0 ? 1f : -1f;
            slot.Renderer.transform.localScale = new Vector3(cue.Size.x * (cue.Facing < 0f ? -1f : 1f),
                cue.Size.y * vertical, 1f);
            slot.Renderer.sortingLayerID = _heroRenderer != null ? _heroRenderer.sortingLayerID : 0;
            slot.Renderer.sortingOrder = (_heroRenderer != null ? _heroRenderer.sortingOrder : 0) + 2;
            slot.Properties.SetColor(TintId, cue.Heavy ? _heavyColor : _lightColor);
            slot.Properties.SetFloat(HeavyId, cue.Heavy ? 1f : 0f);
            slot.Properties.SetFloat(ProgressId, 0f);
            slot.Renderer.SetPropertyBlock(slot.Properties);
            slot.Renderer.enabled = true;

            Vector3 size = slot.Renderer.transform.localScale;
            // 光晕外扩只表达发光，白色刀刃仍维持原判定尺寸。
            Vector2 glowScale = new Vector2(1f + .3f / cue.Size.x, 1f + .3f / cue.Size.y);
            slot.Glow.transform.position = slot.Renderer.transform.position;
            slot.Glow.transform.localScale = new Vector3(size.x * glowScale.x, size.y * glowScale.y, 1f);
            slot.Glow.sortingLayerID = slot.Renderer.sortingLayerID;
            slot.Glow.sortingOrder = slot.Renderer.sortingOrder - 1;
            slot.GlowProperties.SetColor(TintId, new Color(.48f, .76f, 1f, 1f));
            slot.GlowProperties.SetFloat(HeavyId, cue.Heavy ? 1f : 0f);
            slot.GlowProperties.SetFloat(GlowOnlyId, 1f);
            slot.GlowProperties.SetVector(GlowScaleId, new Vector4(glowScale.x, glowScale.y, 0f, 0f));
            slot.GlowProperties.SetFloat(GlowStrengthId, _glowStrength);
            slot.GlowProperties.SetFloat(ProgressId, 0f);
            slot.Glow.SetPropertyBlock(slot.GlowProperties);
            slot.Glow.enabled = _glowStrength > 0f;
            _lightPulse?.Pulse(cue.Center, Mathf.Max(1.25f, Mathf.Max(cue.Size.x, cue.Size.y) * _lightRadiusScale),
                _lightIntensity * (cue.Heavy ? 1.35f : 1f), slot.Duration);
        }

        private Slot CreateSlot()
        {
            if (_quad == null)
            {
                _quad = new Mesh { name = "PlayerSwordWaveQuad" };
                _quad.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f),
                    new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
                _quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                _quad.RecalculateBounds();
            }
            // 不挂到玩家缩放/动画节点下，保证世界范围和出手时的判定完全相同。
            var go = new GameObject("PlayerSwordWave") { layer = gameObject.layer };
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var glowObject = new GameObject("PlayerSwordGlow") { layer = gameObject.layer };
            glowObject.AddComponent<MeshFilter>().sharedMesh = _quad;
            var glow = glowObject.AddComponent<MeshRenderer>();
            glow.sharedMaterial = _material;
            glow.shadowCastingMode = ShadowCastingMode.Off;
            glow.receiveShadows = false;
            glow.lightProbeUsage = LightProbeUsage.Off;
            glow.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new Slot { Renderer = renderer, Glow = glow };
        }

        private void LateUpdate()
        {
            BindSnapshots();
            if (!_player.isActiveAndEnabled || _player.IsHurt || (_status != null && _status.Health <= 0f))
            {
                Clear();
                return;
            }
            if ((_status != null && _status.IsPaused) || TimeManager.Instance?.IsPaused == true)
            {
                // 单体暂停时其他实体仍可能移动/停用；反光跟随接收者，灯光寿命保持冻结。
                _lightPulse?.Advance(0f);
                return;
            }
            _lightPulse?.Advance(Time.deltaTime);
            foreach (Slot slot in _slots)
            {
                if (slot == null || !slot.Renderer.enabled) continue;
                slot.Age += Time.deltaTime;
                if (slot.Age >= slot.Duration)
                {
                    slot.Renderer.enabled = false;
                    slot.Glow.enabled = false;
                    continue;
                }
                slot.Properties.SetFloat(ProgressId, slot.Age / slot.Duration);
                slot.Renderer.SetPropertyBlock(slot.Properties);
                slot.GlowProperties.SetFloat(ProgressId, slot.Age / slot.Duration);
                slot.Glow.SetPropertyBlock(slot.GlowProperties);
            }
        }

        private void OnHealthChanged(float health, float maximum)
        {
            if (health <= 0f) Clear();
        }

        private void Clear()
        {
            foreach (Slot slot in _slots)
            {
                if (slot == null) continue;
                if (slot.Renderer != null) slot.Renderer.enabled = false;
                if (slot.Glow != null) slot.Glow.enabled = false;
            }
            _lightPulse?.Clear();
        }

        private void OnDestroy()
        {
            foreach (Slot slot in _slots)
            {
                if (slot == null) continue;
                if (slot.Renderer != null) Destroy(slot.Renderer.gameObject);
                if (slot.Glow != null) Destroy(slot.Glow.gameObject);
            }
            _lightPulse?.Dispose();
            if (_quad != null) Destroy(_quad);
        }
    }
}
