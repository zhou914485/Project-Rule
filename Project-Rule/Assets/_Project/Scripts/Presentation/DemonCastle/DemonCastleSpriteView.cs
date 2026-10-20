using UnityEngine;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Gameplay.Scene.DemonCastle;

namespace RuleGame.Presentation.DemonCastle
{
    /// <summary>已交付图片的轻量表现：按钮按机关状态选帧，其余序列在暂停时冻结。</summary>
    public sealed class DemonCastleSpriteView : MonoBehaviour
    {
        public SpriteRenderer Target;
        public Sprite[] Frames;
        public CitySwitch Switch;
        public DemonFirework Firework;
        [Min(.1f)] public float FramesPerSecond = 6f;
        private float _elapsed;

        private void LateUpdate()
        {
            if (Target == null || Frames == null || Frames.Length == 0) return;
            if (Firework != null)
            {
                int index = Firework.Armed ? Mathf.FloorToInt((1f - Firework.Remaining / Mathf.Max(.1f, Firework.Fuse)) * Frames.Length) : 0;
                Target.sprite = Frames[Mathf.Clamp(index, 0, Frames.Length - 1)];
                Target.enabled = !Firework.Spent;
                Target.color = Color.white;
                return;
            }
            if (!CityMechanism.Paused) _elapsed = Mathf.Repeat(_elapsed + Time.deltaTime, Frames.Length / Mathf.Max(.1f, FramesPerSecond));
            int frame = Switch != null ? (Switch.IsOn ? 1 : 0) : Mathf.FloorToInt(_elapsed * FramesPerSecond);
            var sprite = Frames[Mathf.Clamp(frame, 0, Frames.Length - 1)];
            if (Target.sprite != sprite) Target.sprite = sprite;
            if (Switch != null && Target.color != Color.white) Target.color = Color.white;
        }
        private void OnDisable() => _elapsed = 0f;
    }
}
