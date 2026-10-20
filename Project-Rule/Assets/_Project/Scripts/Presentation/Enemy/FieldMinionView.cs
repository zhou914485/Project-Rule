using UnityEngine;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>未交付正式动作时的白盒反馈；换接 EnemySpriteAnimator 后可直接移除此组件。</summary>
    public sealed class FieldMinionView : MonoBehaviour
    {
        public EnemyActor Actor;
        public SpriteRenderer Body;
        private Color _color;
        private Vector3 _scale;
        private void Awake()
        {
            _color = Body.color; _scale = Body.transform.localScale;
            EnemyHitFeedback.Attach(Actor, Body);
            RuleWheel.RuleCastVfx.Attach(Actor);
        }
        private void LateUpdate()
        {
            if (Actor.Brain == null) return;
            string state = Actor.Brain.StateName;
            bool hidden = Actor.Definition.Kind == EnemyKind.ClockMouse && state == "Idle";
            Body.enabled = !hidden;
            Color color = _color;
            if (state == "Armed") color = Color.Lerp(_color, Color.red, .5f + .5f * Mathf.Sin(Actor.Brain.ActionElapsed * 26f));
            else if (state == "Slide" || state == "Howl") color = Color.Lerp(_color, Color.yellow, .6f);
            else if (state == "Attack" || state == "Explode" || state == "Hug" || state == "BellySlap") color = Color.Lerp(_color, Color.white, .5f);
            else if (state == "Down" || Actor.Defeated) color.a = .25f;
            else if (state == "Rebirth") color.a = Mathf.Clamp01(Actor.Brain.ActionElapsed / Actor.AnimationDuration);
            Body.color = color;
            Body.transform.localScale = new Vector3(_scale.x * Actor.Facing, _scale.y, _scale.z);
        }
    }
}
