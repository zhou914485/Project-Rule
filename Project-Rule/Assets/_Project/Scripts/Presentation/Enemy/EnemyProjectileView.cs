using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>池化投射物复用时重新接线并清空拖尾；不修改速度、碰撞半径或命中判定。</summary>
    [DefaultExecutionOrder(40)]
    public sealed class EnemyProjectileView : MonoBehaviour
    {
        private EnemyProjectile _shot;
        private SpriteRenderer _body;
        private TrailRenderer _trail;
        private EnemyVfxProfile _profile;
        private EnemyActor _source;
        private readonly Dictionary<Sprite, Bounds> _visibleBounds = new Dictionary<Sprite, Bounds>();
        private EnemySpriteSequence ProjectileAnimation => _profile != null && _profile.ProjectileAnimations.Length > 0
            ? _profile.ProjectileAnimations[_shot.VisualIndex % _profile.ProjectileAnimations.Length] : null;
        private void Awake() => _shot = GetComponent<EnemyProjectile>();
        public void Configure(EnemyVfxProfile profile)
        {
            _profile = profile;
            if (_shot == null) _shot = GetComponent<EnemyProjectile>();
            if (_body == null)
            {
                var go = new GameObject("ProjectileVisual"); go.transform.SetParent(transform, false);
                _body = go.AddComponent<SpriteRenderer>(); _body.sortingOrder = 8;
            }
            // 子物体独占外观变换；根节点的比例和旋转仍由投射物/快照系统管理。
            GetComponent<SpriteRenderer>().enabled = false;
            if (_source != null) _source.Restored -= OnRestore;
            _source = _shot.Source;
            if (_source != null) _source.Restored += OnRestore;
            // Shot 是带透明收尾的一次性发射闪光；飞行弹体必须保持独立弹芯，不能循环该序列。
            EnemySpriteSequence animation = ProjectileAnimation;
            _body.sprite = animation != null && animation.Frames.Length > 0 ? animation.Frames[0] : profile.ProjectileSprite;
            _body.enabled = true;
            _body.sharedMaterial = animation != null && profile.ProjectileMaterial != null
                ? profile.ProjectileMaterial : profile.Material;
            _body.color = animation != null ? Color.white : profile.Accent;
            if (_trail == null)
            {
                _trail = gameObject.AddComponent<TrailRenderer>();
                _trail.sortingOrder = 7; _trail.minVertexDistance = .08f;
                _trail.numCapVertices = 4; _trail.numCornerVertices = 3;
                _trail.textureMode = LineTextureMode.Stretch;
                _trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            }
            _trail.sharedMaterial = profile.BeamMaterial;
            _trail.time = .18f; _trail.startWidth = .28f; _trail.endWidth = 0f;
            _trail.startColor = profile.Secondary; _trail.endColor = new Color(profile.Accent.r, profile.Accent.g, profile.Accent.b, 0f);
            _trail.Clear(); _trail.emitting = !profile.PhysicalProjectiles;
            FitBody();
        }
        private void FitBody()
        {
            if (_body.sprite == null) return;
            bool ground = _shot.Gravity == 0f && _shot.Radius >= .3f;
            Quaternion rotation = Quaternion.Euler(0f, 0f, ground || _profile.PhysicalProjectiles ? 0f :
                Mathf.Atan2(_shot.Velocity.y, _shot.Velocity.x) * Mathf.Rad2Deg);
            Bounds bounds = VisibleBounds(_body.sprite);
            // 保留交付素材按 PPU 配好的尺寸及逐帧伸缩；不能按整张透明画布强制归一化。
            Vector3 scale = ProjectileAnimation != null ? Vector3.one
                : ground ? new Vector3(1.7f, .8f, 1f) : Vector3.one * 1.7f;
            _body.transform.localRotation = rotation;
            _body.transform.localScale = scale;
            _body.transform.localPosition = -(rotation * Vector3.Scale(bounds.center, scale));
        }
        private Bounds VisibleBounds(Sprite sprite)
        {
            if (_visibleBounds.TryGetValue(sprite, out Bounds bounds)) return bounds;
            // Tight 网格包住真实图形；Sprite.bounds 仍可能包含整张 1280×720 的透明画布。
            Vector2[] vertices = sprite.vertices;
            bounds = vertices.Length > 0 ? new Bounds(vertices[0], Vector3.zero) : sprite.bounds;
            foreach (Vector2 vertex in vertices) bounds.Encapsulate(vertex);
            _visibleBounds.Add(sprite, bounds);
            return bounds;
        }
        private void OnRestore() { if (_trail != null) _trail.Clear(); }
        private void OnDisable()
        {
            if (_source != null) _source.Restored -= OnRestore;
            _source = null;
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
        }
        private void OnEnable()
        {
            // 公共弹池也会被无特效的杂兵复用，先恢复其原始外观，不能残留上一个 Boss 的颜色。
            _profile = null;
            GetComponent<SpriteRenderer>().enabled = true;
            if (_body != null) _body.enabled = false;
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
        }
        private void LateUpdate()
        {
            if (_shot.Source == null) return;
            if (_source != _shot.Source)
            {
                if (_source != null) _source.Restored -= OnRestore;
                var presenter = _shot.Source.GetComponent<EnemyVfxPresenter>();
                if (presenter != null && presenter.Profile != null) Configure(presenter.Profile);
                else
                {
                    _source = _shot.Source; _profile = null;
                    GetComponent<SpriteRenderer>().enabled = true;
                    if (_body != null) _body.enabled = false;
                    if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
                }
            }
            if (_profile == null) return;
            EnemySpriteSequence animation = ProjectileAnimation;
            if (animation != null && animation.Frames.Length > 0)
            {
                int frame = Mathf.FloorToInt(_shot.Age * animation.FrameRate) % animation.Frames.Length;
                _body.sprite = animation.Frames[frame];
            }
            FitBody();
            // 已发射弹体继续独立飞行，不跟随发射者的单体暂停。
            bool paused = TimeManager.Instance?.IsPaused == true;
            _trail.emitting = !paused && !_profile.PhysicalProjectiles;
            // TrailRenderer 的历史点用引擎时间清退，实体暂停时延长寿命以保留现有轨迹。
            _trail.time = paused ? 1000f : .18f;
        }
    }
}
