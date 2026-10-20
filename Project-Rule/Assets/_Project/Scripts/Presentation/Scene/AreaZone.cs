using System;
using System.Collections.Generic;
using RuleGame.Presentation.Audio;
using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 「进入区域就生效」的共同底座：背景与音乐共用同一块形状、同一个 Priority。
    /// </summary>
    /// <remarks>
    /// <para><b>形状 = 多个矩形的并集</b>：一个区域可以拆成若干段矩形拼出异形
    /// （和铺装器 <c>TileRegion</c> 的多矩形是同一套思路），拐角房间、L 形走廊、
    /// 跨两层的竖井都不需要再摆第二个区域。</para>
    /// <para><b>旧数据兼容</b>：没有分段时退回老的单个 <c>_size</c> 矩形；
    /// 编辑器的「把单矩形拆成多段」会把旧矩形变成第一段。</para>
    /// <para><b>为什么合并</b>：<see cref="BackdropZone"/> 与 <see cref="MusicRegion"/>
    /// 原本各写一份 <c>Contains</c> + <c>Resolve</c>，同一块区域要在场景里摆两遍、调两次。
    /// 现在 <see cref="BackdropZone"/> 直接带一份 <see cref="MusicProfile"/>，音乐和背景读的
    /// 是同一个物体、同一个形状、同一个 Priority。</para>
    /// <para><b>随分区卸载</b>：<c>OnDisable</c> 把自己摘出登记表，解不到区域就回退默认。</para>
    /// <para><b>同 Priority 取面积小的</b>：重叠时更具体的区域赢，不依赖注册顺序。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public abstract class AreaZone : MonoBehaviour
    {
        /// <summary>区域的一段矩形。<see cref="Center"/> 是相对本物体的本地坐标。</summary>
        [Serializable]
        public struct Part
        {
            [Tooltip("相对本物体的中心（米）")]
            public Vector2 Center;

            [Tooltip("尺寸（米）")]
            public Vector2 Size;

            public Rect ToRect()
            {
                return new Rect(Center - Size * 0.5f, Size);
            }
        }

        private static readonly List<AreaZone> Zones = new List<AreaZone>();

        [Tooltip("旧版单矩形。已经有分段（Parts）时忽略它。")]
        [SerializeField] private Vector2 _size = new Vector2(30f, 20f);

        [Tooltip("区域形状：多个矩形的并集（本地坐标）。空 = 用上面的单矩形。")]
        [SerializeField] private List<Part> _parts = new List<Part>();

        [Tooltip("重叠时数值高的优先。BOSS 房这类小区域用 10，整片区域用 0。")]
        [SerializeField] private int _priority;

        /// <summary>区域的外接尺寸（米）。多段时是并集的包围盒，只用于显示 / 旧工具。</summary>
        public Vector2 Size
        {
            get
            {
                if (!HasParts) return _size;

                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);
                for (int i = 0; i < _parts.Count; i++)
                {
                    Part part = _parts[i];
                    Vector2 half = new Vector2(Mathf.Abs(part.Size.x), Mathf.Abs(part.Size.y)) * 0.5f;
                    min = Vector2.Min(min, part.Center - half);
                    max = Vector2.Max(max, part.Center + half);
                }

                return max - min;
            }
        }

        public int Priority => _priority;

        /// <summary>是否已经拆成多段。</summary>
        public bool HasParts => _parts != null && _parts.Count > 0;

        /// <summary>分段列表（只读；编辑请走 Inspector 或 SerializedObject）。</summary>
        public IReadOnlyList<Part> Parts => _parts != null ? (IReadOnlyList<Part>)_parts : Array.Empty<Part>();

        /// <summary>段数（旧单矩形算 1 段）。</summary>
        public int PartCount => HasParts ? _parts.Count : 1;

        /// <summary>同 Priority 重叠时的比较值：总面积小的更具体。</summary>
        public float Area
        {
            get
            {
                if (!HasParts) return Mathf.Abs(_size.x * _size.y);

                float sum = 0f;
                for (int i = 0; i < _parts.Count; i++)
                {
                    sum += Mathf.Abs(_parts[i].Size.x * _parts[i].Size.y);
                }
                return sum;
            }
        }

        /// <summary>这块区域给音乐用的配置；null = 不参与音乐归属。</summary>
        public virtual MusicProfile Music => null;

        protected virtual void OnEnable()
        {
            // 老场景反序列化出来的新字段可能是 null；这里补一次，别让 Contains 走进空引用。
            if (_parts == null) _parts = new List<Part>();
            if (!Zones.Contains(this)) Zones.Add(this);
        }

        protected virtual void OnDisable() => Zones.Remove(this);

        /// <summary>世界坐标点是否落在这个形状里；<paramref name="margin"/> 是迟滞用的外扩（米）。</summary>
        public bool Contains(Vector3 worldPoint, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 scale = transform.lossyScale;
            float mx = margin / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float my = margin / Mathf.Max(0.001f, Mathf.Abs(scale.y));

            if (!HasParts)
            {
                return Mathf.Abs(local.x) <= Mathf.Abs(_size.x) * 0.5f + mx
                    && Mathf.Abs(local.y) <= Mathf.Abs(_size.y) * 0.5f + my;
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                Part part = _parts[i];
                if (Mathf.Abs(local.x - part.Center.x) <= Mathf.Abs(part.Size.x) * 0.5f + mx
                    && Mathf.Abs(local.y - part.Center.y) <= Mathf.Abs(part.Size.y) * 0.5f + my)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>挑出该用哪块区域：重叠取 Priority 高的，同 Priority 取面积小的，上一个区域在外扩范围内继续保留。</summary>
        public static T Resolve<T>(Vector3 point, T previous, float hysteresis, Func<T, bool> eligible = null)
            where T : AreaZone
        {
            T best = null;
            for (int i = 0; i < Zones.Count; i++)
            {
                if (!(Zones[i] is T zone) || (UnityEngine.Object)zone == null || !zone.isActiveAndEnabled) continue;
                if (eligible != null && !eligible(zone)) continue;
                if (!zone.Contains(point)) continue;
                if (best == null || zone.Priority > best.Priority
                    || (zone.Priority == best.Priority && zone.Area < best.Area)) best = zone;
            }

            if ((UnityEngine.Object)previous != null && previous.isActiveAndEnabled
                && (eligible == null || eligible(previous))
                && previous.Contains(point, hysteresis)
                && (best == null || previous.Priority >= best.Priority)) return previous;
            return best;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (!HasParts)
            {
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(_size.x, _size.y, 0.1f));
                return;
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                Part part = _parts[i];
                Gizmos.DrawWireCube(new Vector3(part.Center.x, part.Center.y, 0f),
                    new Vector3(part.Size.x, part.Size.y, 0.1f));
            }
        }
    }
}
