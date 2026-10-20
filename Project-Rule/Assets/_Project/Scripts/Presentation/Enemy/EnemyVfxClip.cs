using UnityEngine;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>作者特效的离屏烘焙帧；运行时不依赖 Effekseer 原生插件。</summary>
    [CreateAssetMenu(menuName = "RuleGame/Enemy/VFX Clip")]
    public sealed class EnemyVfxClip : ScriptableObject
    {
        public Texture2D Atlas;
        public Vector2Int Grid = new Vector2Int(8, 8);
        [Min(1)] public int FrameCount = 64;
        [Min(1f)] public float FramesPerSecond = 30f;
        [Min(0)] public int FirstFrame;
        [Tooltip("片段全部有效帧的可见像素边界（归一化，忽略透明边距）。由特效校准菜单计算。")]
        public Rect VisibleRect = new Rect(0f, 0f, 1f, 1f);
        public float Duration => Mathf.Max(1, FrameCount - FirstFrame) / Mathf.Max(1f, FramesPerSecond);
        // Editor 热重载会恢复 private 字段；运行时生成的 Sprite 缓存不能参与序列化。
        [System.NonSerialized] private Sprite[] _frames;

        public Sprite Frame(float age)
        {
            if (Atlas == null || Grid.x < 1 || Grid.y < 1) return null;
            int count = Mathf.Clamp(FrameCount, 1, Grid.x * Grid.y);
            if (_frames == null || _frames.Length != count || _frames[0] == null)
            {
                ReleaseFrames();
                _frames = new Sprite[count];
                float width = Atlas.width / (float)Grid.x, height = Atlas.height / (float)Grid.y;
                for (int i = 0; i < count; i++)
                    _frames[i] = Sprite.Create(Atlas, new Rect(i % Grid.x * width,
                        (Grid.y - 1 - i / Grid.x) * height, width, height), new Vector2(.5f, .5f),
                        width, 0, SpriteMeshType.FullRect);
            }
            int first = Mathf.Clamp(FirstFrame, 0, count - 1);
            int index = Mathf.Clamp(first + Mathf.FloorToInt(age * Mathf.Max(1f, FramesPerSecond)), first, count - 1);
            return _frames[index];
        }

        private void OnDisable() => ReleaseFrames();
        private void ReleaseFrames()
        {
            if (_frames == null) return;
            foreach (Sprite frame in _frames)
                if (frame != null) { if (Application.isPlaying) Destroy(frame); else DestroyImmediate(frame); }
            _frames = null;
        }
    }
}
