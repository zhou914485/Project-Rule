using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Enemy;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.RuleWheel;

namespace RuleGame.Presentation.RuleWheel
{
    /// <summary>规则真实生效后的展开、驻留与回吸。每位施法者最多六条驻留、六条收尾，无战斗权限。</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(120)]
    public sealed class RuleCastVfx : MonoBehaviour
    {
        [Header("规则释放 · 神圣光潮")]
        [SerializeField, Min(.1f)] private float _expandDuration = .7f;
        [SerializeField, Min(.1f)] private float _returnDuration = .55f;
        [Tooltip("最小世界尺寸。实际扩散终点按世界相机最远角点继续放大，直到所有光圈完全出屏。")]
        [SerializeField, Min(6f)] private float _radius = 13f;
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int AgeId = Shader.PropertyToID("_Age");
        private static readonly int BurstId = Shader.PropertyToID("_Burst");
        private static readonly int CollapseId = Shader.PropertyToID("_Collapse");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int SecondaryId = Shader.PropertyToID("_Secondary");
        private static readonly int GlyphId = Shader.PropertyToID("_Glyph");
        private static readonly int WorldScaleId = Shader.PropertyToID("_WorldScale");
        private readonly Halo[] _halos = new Halo[12];
        private RuleSelectionOrbView _orb;
        private RuleWheelPresenter _wheel;
        private Camera _worldCamera;
        private EnemyActor _enemy;
        private Transform _caster;
        private EntityStatus _status;
        private IRuleSystem _rules;
        private ISnapshotSystem _snapshots;
        private Material _material;
        private EnemyVfxProfile _palette;
        private Sprite _quad;
        private MaterialPropertyBlock _properties;
        private int _sortingLayer, _sortingOrder = 12;
        private bool _initialized;

        private sealed class Halo
        {
            public SpriteRenderer Renderer;
            public AttributeId Attribute;
            public RelationId Relation;
            public Vector3 Offset, Position;
            public float Age, ReturnAge, ReturnRadius, Radius, MaxRadius;
            public bool Active, Returning;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ListenForPlayers()
        {
            RuleWheelPresenter.Ready -= PlayerReady;
            RuleWheelPresenter.Ready += PlayerReady;
        }
        private static void PlayerReady(RuleWheelPresenter wheel) => Attach(wheel);
        public static RuleCastVfx Attach(RuleWheelPresenter wheel)
        {
            if (wheel == null) return null;

            var view = wheel.GetComponent<RuleCastVfx>();
            if (view == null) view = wheel.gameObject.AddComponent<RuleCastVfx>();
            view._wheel = wheel;
            view._orb = wheel.GetComponent<RuleSelectionOrbView>();
            // Ready 可能在本帧 Start 阶段新增组件；立即订阅，不能漏掉同帧第一次释放。
            if (view.isActiveAndEnabled) view.Bind();
            return view;
        }
        public static RuleCastVfx Attach(RuleSelectionOrbView orb)
        {
            if (orb == null) return null;
            return Attach(orb.GetComponent<RuleWheelPresenter>());
        }
        public static RuleCastVfx Attach(EnemyActor enemy)
        {
            if (enemy == null) return null;
            var view = enemy.GetComponent<RuleCastVfx>();
            if (view == null) view = enemy.gameObject.AddComponent<RuleCastVfx>();
            view._enemy = enemy;
            return view;
        }
        private void Awake()
        {
            _properties = new MaterialPropertyBlock();
            _material = Resources.Load<Material>("RuleCastVfx/StarHalo");
            if (_material == null)
            {
                Debug.LogError("规则星环缺少 Resources/RuleCastVfx/StarHalo 材质。", this);
                enabled = false;
            }
        }
        private void Start() => Bind();
        private void OnEnable() { if (_initialized) Bind(); }
        private void Bind()
        {
            Unbind();
            _initialized = true;
            _caster = _enemy != null ? _enemy.transform : _wheel != null ? _wheel.Player?.transform : null;
            if (_caster == null) return;
            _worldCamera = _wheel != null ? _wheel.WorldCamera : Camera.main;
            _status = _caster.GetComponent<EntityStatus>();
            var enemyEffects = _enemy != null ? _enemy.GetComponent<EnemyVfxPresenter>() : null;
            _palette = enemyEffects != null ? enemyEffects.Profile : null;
            var body = _caster.GetComponentInChildren<SpriteRenderer>();
            if (body != null) { _sortingLayer = body.sortingLayerID; _sortingOrder = body.sortingOrder + 6; }
            _rules = RuleSystem.Instance;
            _snapshots = SnapshotSystem.Instance;
            if (_rules != null)
            {
                _rules.OnRuleActivated += Activated;
                _rules.OnRuleExpired += Expired;
            }
            if (_snapshots != null) _snapshots.OnRestored += Restored;
            if (_enemy != null) _enemy.Feedback += Feedback;
        }
        private void Unbind()
        {
            if (_rules != null) { _rules.OnRuleActivated -= Activated; _rules.OnRuleExpired -= Expired; }
            if (_snapshots != null) _snapshots.OnRestored -= Restored;
            if (_enemy != null) _enemy.Feedback -= Feedback;
            _rules = null; _snapshots = null;
        }
        private void Activated(AttributeId attribute, RelationId relation, RuleSource source)
        {
            if (_wheel != null && source == RuleSource.Player)
                Play(attribute, relation, PlayerOrigin);
        }
        private Vector3 PlayerOrigin
        {
            get
            {
                if (_orb != null) return _orb.CastOrigin;
                if (_caster == null) return Vector3.zero;

                float facing = _wheel != null && _wheel.Player != null ? _wheel.Player.Facing : 1f;
                return _caster.position + new Vector3(facing * .45f, .25f, 0f);
            }
        }
        private float CanvasRadius(Vector3 origin)
        {
            float minimum = _enemy != null && _enemy.Definition.IsBoss ? _radius * 1.16f : _radius;
            if (_worldCamera == null) _worldCamera = Camera.main;
            if (_worldCamera == null) return Mathf.Max(minimum, 40f);
            float depth = Vector3.Dot(origin - _worldCamera.transform.position, _worldCamera.transform.forward);
            float farthest = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = _worldCamera.ViewportToWorldPoint(new Vector3(i & 1, i >> 1, Mathf.Max(.01f, depth)));
                farthest = Mathf.Max(farthest, Vector2.Distance(origin, corner));
            }
            // 最内侧延迟环位于主环的 59%，还需计入宽辉光。3.2 倍裕量保证整套光潮越过角点。
            // MaxRadius 的 .9 才是最终主环的世界半径（半画布 1.25 × 归一化半径 .72）。
            return Mathf.Max(minimum, (farthest * 3.2f + 2f) / .9f);
        }
        private void Feedback(EnemyFeedback cue)
        {
            if (cue.Kind != EnemyFeedbackKind.RuleCast || !cue.RuleAttribute.HasValue || !cue.RuleRelation.HasValue) return;
            Vector3 up = _enemy.Motor.Up;
            Vector3 origin = cue.Position + up * Mathf.Min(.65f, _enemy.Definition.BodySize.y * .22f);
            Play(cue.RuleAttribute.Value, cue.RuleRelation.Value, origin);
        }
        private void Play(AttributeId attribute, RelationId relation, Vector3 position)
        {
            if (_material == null || _caster == null) return;
            Halo halo = null;
            foreach (Halo candidate in _halos)
                if (candidate != null && !candidate.Active) { halo = candidate; break; }
            if (halo == null)
                for (int i = 0; i < _halos.Length; i++)
                    if (_halos[i] == null) { halo = _halos[i] = CreateHalo(); break; }
            if (halo == null)
            {
                // 极端同帧反复覆盖时只替换最旧的收尾，不挤掉仍生效的六个属性。
                foreach (Halo candidate in _halos)
                    if (candidate.Returning && (halo == null || candidate.ReturnAge > halo.ReturnAge)) halo = candidate;
            }
            if (halo == null) return;
            halo.Attribute = attribute; halo.Relation = relation;
            halo.Position = position; halo.Offset = position - _caster.position;
            halo.Age = halo.ReturnAge = 0f; halo.Returning = false; halo.Active = true;
            halo.MaxRadius = CanvasRadius(position) + (int)attribute * .22f;
            halo.Radius = .12f / (halo.MaxRadius * 1.25f);
            Present(halo);
        }
        private Halo CreateHalo()
        {
            if (_quad == null)
            {
                var texture = Texture2D.whiteTexture;
                _quad = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f,
                    texture.width, 0, SpriteMeshType.FullRect);
                _quad.name = "Rule Star Halo Quad";
                _quad.hideFlags = HideFlags.HideAndDontSave;
            }
            var go = new GameObject("Rule Star Halo") { layer = _caster.gameObject.layer };
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _quad; renderer.sharedMaterial = _material;
            renderer.sortingLayerID = _sortingLayer; renderer.sortingOrder = _sortingOrder;
            renderer.enabled = false;
            return new Halo { Renderer = renderer };
        }
        private void Expired(AttributeId attribute)
        {
            foreach (Halo halo in _halos)
                if (halo != null && halo.Active && !halo.Returning && halo.Attribute == attribute)
                {
                    BeginReturn(halo);
                    Present(halo);
                }
        }
        private static void BeginReturn(Halo halo)
        {
            halo.Returning = true; halo.ReturnAge = 0f;
            halo.ReturnRadius = halo.Radius;
        }
        private void LateUpdate()
        {
            if (_caster == null || (_enemy != null && _enemy.Defeated) || (_status != null && _status.Health <= 0f))
            { Clear(); return; }
            // 用真实秒让释放在轮盘 0.001 倍速/时间失效时仍清晰；正式暂停严格冻结。
            float dt = TimeManager.Instance?.IsPaused == true ? 0f : Time.unscaledDeltaTime;
            foreach (Halo halo in _halos)
            {
                if (halo == null || !halo.Active) continue;
                halo.Age += dt;
                halo.Position = _caster.position + halo.Offset;
                // 镜头从轮盘特写拉回、超宽屏或移动时继续扩大终点，不能按释放瞬间的小视野停住。
                if (!halo.Returning && dt > 0f) halo.MaxRadius = Mathf.Max(halo.MaxRadius, CanvasRadius(halo.Position));
                // 时间反转会在成功事件之前静默回退规则表，可能没有后续 Expired 通知。
                // 这种瞬时法术完整展开一次后自行收束，不能遗留永久驻留光潮。
                if (!halo.Returning && halo.Age >= _expandDuration
                    && _rules != null && !_rules.Table.IsActive(halo.Attribute, halo.Relation))
                    BeginReturn(halo);
                if (halo.Returning)
                {
                    halo.ReturnAge += dt;
                    if (halo.ReturnAge >= _returnDuration)
                    { halo.Active = false; halo.Renderer.enabled = false; continue; }
                    float t = halo.ReturnAge / _returnDuration;
                    halo.Radius = Mathf.Lerp(halo.ReturnRadius, .0005f, 1f - (1f - t) * (1f - t));
                }
                else
                {
                    float t = Mathf.Clamp01(halo.Age / _expandDuration);
                    halo.Radius = Mathf.Lerp(.12f / (halo.MaxRadius * 1.25f), .72f, Mathf.SmoothStep(0f, 1f, t));
                }
                Present(halo);
            }
        }
        private void Present(Halo halo)
        {
            Transform visual = halo.Renderer.transform;
            visual.SetPositionAndRotation(halo.Position, Quaternion.identity);
            Vector3 parent = transform.lossyScale;
            float diameter = halo.MaxRadius * 2.5f;
            visual.localScale = new Vector3(diameter / Mathf.Max(.001f, Mathf.Abs(parent.x)),
                diameter / Mathf.Max(.001f, Mathf.Abs(parent.y)), 1f);
            float returning = halo.Returning ? halo.ReturnAge / _returnDuration : 0f;
            float burst = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f, _expandDuration + .18f, halo.Age));
            Color accent = _palette != null ? _palette.Accent : Color.white;
            Color secondary = _palette != null ? _palette.Secondary : accent;
            _properties.Clear();
            _properties.SetFloat(RadiusId, halo.Radius);
            _properties.SetFloat(AgeId, halo.Age);
            _properties.SetFloat(BurstId, burst);
            _properties.SetFloat(CollapseId, returning);
            _properties.SetFloat(OpacityId, halo.Returning ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.84f, 1f, returning))
                : burst);
            _properties.SetFloat(WorldScaleId, halo.MaxRadius / 13f);
            _properties.SetColor(AccentId, accent); _properties.SetColor(SecondaryId, secondary);
            _properties.SetFloat(GlyphId, (int)halo.Attribute);
            halo.Renderer.SetPropertyBlock(_properties);
            // 规则仍生效时保留回吸状态，完全展开后停止渲染，不在屏幕里留圆圈/射线。
            halo.Renderer.enabled = halo.Returning || halo.Age < _expandDuration + .18f;
        }
        private void Restored()
        {
            Clear();
            // 快照没有敌方施法者身份，不能把旧时间线的光环归给错误敌人。
            // 玩家来源可确定，静默恢复驻留环，不重播释放冲击。
            if (_wheel == null || _rules == null) return;
            foreach (var pair in _rules.Table.Slots)
                if (pair.Value.Source == RuleSource.Player)
                {
                    Play(pair.Key, pair.Value.Relation, PlayerOrigin);
                    foreach (Halo halo in _halos)
                        if (halo != null && halo.Active && halo.Attribute == pair.Key)
                        { halo.Age = _expandDuration + 1f; halo.Radius = .72f; Present(halo); }
                }
        }
        private void Clear()
        {
            foreach (Halo halo in _halos)
                if (halo != null) { halo.Active = false; halo.Renderer.enabled = false; }
        }
        private void OnDisable() { Unbind(); Clear(); }
        private void OnDestroy()
        {
            Unbind();
            foreach (Halo halo in _halos)
                if (halo != null && halo.Renderer != null) Destroy(halo.Renderer.gameObject);
            if (_quad != null) Destroy(_quad);
        }
    }
}
