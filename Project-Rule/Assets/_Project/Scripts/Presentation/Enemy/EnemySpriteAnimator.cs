using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>
    /// 直接播放交付的 Sprite 帧。固定身体比例与锚点，物理插值负责移动，
    /// 不使用逐帧缩放、程序形变或 Animator 过渡去混合不同尺寸。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class EnemySpriteAnimator : MonoBehaviour
    {
        public EnemyActor Actor;
        public EnemyAnimationSet AnimationSet;
        private SpriteRenderer _renderer;
        private readonly Dictionary<string, EnemySpriteSequence> _sequences = new Dictionary<string, EnemySpriteSequence>();
        private EnemySpriteSequence _current;
        private float _time;
        private Vector3 _anchor;
        private Vector2 _clockFace;
        private bool _burrowed;
        private float _visibleAlpha;
        public Vector3 ClockFaceWorld => transform.TransformPoint(_clockFace);
        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _visibleAlpha = _renderer.color.a;
            _anchor = transform.localPosition;
            if (Actor == null) Actor = GetComponentInParent<EnemyActor>();
            EnemyHitFeedback.Attach(Actor, _renderer);
            RuleWheel.RuleCastVfx.Attach(Actor);
            if (AnimationSet != null)
                foreach (EnemySpriteSequence sequence in AnimationSet.Sequences) _sequences[sequence.Action] = sequence;
        }
        private void OnEnable()
        {
            if (Actor == null) Actor = GetComponentInParent<EnemyActor>();
            Actor.AnimationChanged += Select;
            Actor.Restored += RestoreTime;
            Select();
        }
        private void OnDisable()
        {
            SetBurrowed(false);
            if (Actor == null) return;
            Actor.AnimationChanged -= Select;
            Actor.Restored -= RestoreTime;
        }
        private void Select()
        {
            _time = 0f;
            string action = Actor.AnimationName;
            if (Actor.Definition.Kind == EnemyKind.RabbitBoss && Actor.Brain?.Phase >= 2
                && action != "Dead" && action != "Flip") action = "Flight";
            _sequences.TryGetValue(action, out _current);
            if (_current == null) _sequences.TryGetValue("Idle", out _current);
        }
        private void RestoreTime() { Select(); _time = Actor.Brain?.ActionElapsed ?? 0f; }
        private void LateUpdate()
        {
            if (_renderer == null || Actor == null) return;
            if (TimeManager.Instance?.IsPaused != true && Actor.Status?.IsPaused != true) _time += Time.deltaTime;
            if (_current != null && _current.Frames.Length > 0)
            {
                float duration = Mathf.Max(0.05f, Actor.AnimationDuration);
                int frame = Mathf.FloorToInt(_current.NativeFrameRate
                    ? _time * _current.FrameRate : _time / duration * _current.Frames.Length);
                frame = Actor.AnimationLoop ? frame % _current.Frames.Length : Mathf.Min(frame, _current.Frames.Length - 1);
                _renderer.sprite = _current.Frames[frame];
                _renderer.flipX = (Actor.Facing < 0f) == _current.FacesRight;
                if (_current.ClockFaceCenters != null && frame < _current.ClockFaceCenters.Length)
                {
                    _clockFace = _current.ClockFaceCenters[frame];
                    if (_renderer.flipX) _clockFace.x = -_clockFace.x;
                }
            }
            SetBurrowed(Actor.Definition.Kind == EnemyKind.RabbitBoss && Actor.Brain?.StateName == "Burrow"
                && !_sequences.ContainsKey("Burrow"));
            // 始终从原始锚点计算，避免重力反转时累计位移或改变碰撞体比例。
            bool inverted = !Actor.IsFlying && Actor.Motor.Up.y < 0f;
            transform.localPosition = inverted ? new Vector3(_anchor.x, -_anchor.y, _anchor.z) : _anchor;
            transform.localRotation = Quaternion.Euler(0f, 0f, inverted ? 180f : 0f);
        }
        private void SetBurrowed(bool value)
        {
            if (_renderer == null || value == _burrowed) return;
            Color color = _renderer.color;
            if (value) _visibleAlpha = color.a;
            color.a = value ? _visibleAlpha * .3f : _visibleAlpha;
            _renderer.color = color;
            _burrowed = value;
        }
    }
}
