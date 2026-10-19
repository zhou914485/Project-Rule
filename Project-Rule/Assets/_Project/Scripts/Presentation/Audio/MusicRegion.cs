using System.Collections.Generic;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    /// <summary>按玩家位置判断音乐归属，不依赖物理触发或分区预加载事件。</summary>
    [DisallowMultipleComponent]
    public sealed class MusicRegion : MonoBehaviour
    {
        private static readonly List<MusicRegion> Regions = new List<MusicRegion>();

        [SerializeField] private MusicProfile _profile;
        [Tooltip("以此物体为中心的本地矩形，不需要碰撞体")]
        [SerializeField] private Vector2 _size = new Vector2(30f, 30f);
        [Tooltip("重叠时数值高的优先；同优先级通过迟滞避免来回换曲")]
        [SerializeField] private int _priority;

        public MusicProfile Profile => _profile;
        public int Priority => _priority;
        public Vector2 Size => _size;

        private void OnEnable()
        {
            if (!Regions.Contains(this)) Regions.Add(this);
        }

        private void OnDisable() => Regions.Remove(this);

        public bool Contains(Vector3 worldPoint, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            // 迟滞单位为世界米，区域缩放后也保持一致。
            Vector3 scale = transform.lossyScale;
            float mx = margin / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float my = margin / Mathf.Max(0.001f, Mathf.Abs(scale.y));
            return Mathf.Abs(local.x) <= Mathf.Abs(_size.x) * 0.5f + mx
                && Mathf.Abs(local.y) <= Mathf.Abs(_size.y) * 0.5f + my;
        }

        public static MusicRegion Resolve(Vector3 point, MusicRegion previous, float hysteresis)
        {
            MusicRegion best = null;
            for (int i = 0; i < Regions.Count; i++)
            {
                MusicRegion region = Regions[i];
                if (region == null || !region.isActiveAndEnabled || region.Profile == null
                    || !region.Profile.IsPlayable || !region.Contains(point)) continue;
                if (best == null || region.Priority > best.Priority) best = region;
            }

            if (previous != null && previous.isActiveAndEnabled && previous.Profile != null
                && previous.Profile.IsPlayable && previous.Contains(point, hysteresis)
                && (best == null || previous.Priority >= best.Priority)) return previous;
            return best;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_size.x, _size.y, 0.1f));
        }
    }
}
