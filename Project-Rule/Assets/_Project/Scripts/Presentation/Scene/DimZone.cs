using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 一块「压暗区域」：只把矩形范围内的画面压暗，矩形外不受影响。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是在场景里铺一层半透明黑</b>：本项目的精灵全是 Unlit、相机也没开后处理
    /// （<c>m_RenderPostProcessing = 0</c>），2D 光照与 Volume 都用不上；
    /// 世界空间的一层纸是唯一「只暗这一块、不暗全屏」的做法。</para>
    /// <para><b>为什么挂在场景里而不是跟着相机</b>：暗区属于关卡几何，随分区一起加载卸载；
    /// 跟着相机走的全屏压暗是 <c>BackdropDirector</c> 那类演出的活，两者不是一件事。</para>
    /// <para><b>默认 Order 60</b>：盖住地形（1）与遮罩（50）、低于角色（100）——
    /// 区域里看起来暗下来，但人还看得清。要把角色一起压暗，把 Order 调到 120。</para>
    /// <para><b>不要放进 <c>Grid/Art</c></b>：铺装器（Ctrl+Shift+G）会清空那个容器。</para>
    /// <para><b>只改视觉</b>：不进快照、不参与规则；几块暗区重叠就是叠起来更暗。</para>
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class DimZone : MonoBehaviour
    {
        /// <summary>压暗层的子物体名。</summary>
        public const string OverlayName = "DimOverlay";

        [Tooltip("以此物体为中心的矩形尺寸（米）。")]
        [SerializeField] private Vector2 _size = new Vector2(20f, 12f);

        [Tooltip("压暗程度：0 = 不变暗，1 = 全黑。")]
        [SerializeField, Range(0f, 1f)] private float _dim = 0.4f;

        [Tooltip("压暗颜色，默认纯黑。")]
        [SerializeField] private Color _color = Color.black;

        [Tooltip("排序序号。60 = 盖住地形（1）与遮罩（50）、不盖角色（100）；120 = 连角色一起压暗。")]
        [SerializeField] private int _sortingOrder = 60;

        [Tooltip("压暗层。留空自动找名为 DimOverlay 的子物体。")]
        [SerializeField] private SpriteRenderer _overlay;

        public Vector2 Size => _size;
        public float Dim => _dim;
        public int SortingOrder => _sortingOrder;

        private void OnEnable()
        {
            Bind();
        }

        private void OnValidate()
        {
            Bind();
        }

        private void LateUpdate()
        {
            // ExecuteAlways：编辑器里拖 Inspector 的滑条也能立刻看到
            Apply();
        }

        private void Bind()
        {
            if (_overlay == null)
            {
                Transform child = transform.Find(OverlayName);
                if (child != null) _overlay = child.GetComponent<SpriteRenderer>();
            }

            // 手动挂组件、又没有 DimOverlay 子物体时兜底建一个；编辑器里不建，免得把场景弄脏。
            if (_overlay == null && Application.isPlaying) _overlay = CreateOverlay();

            Apply();
        }

        private void Apply()
        {
            if (_overlay == null) return;

            float dim = Mathf.Clamp01(_dim);

            Sprite sprite = _overlay.sprite;
            Vector2 bounds = sprite != null ? (Vector2)sprite.bounds.size : Vector2.one;
            Vector3 scale = new Vector3(
                _size.x / Mathf.Max(0.001f, bounds.x),
                _size.y / Mathf.Max(0.001f, bounds.y),
                1f);

            Transform overlay = _overlay.transform;
            if ((overlay.localScale - scale).sqrMagnitude > 0.000001f) overlay.localScale = scale;

            if (_overlay.sortingOrder != _sortingOrder) _overlay.sortingOrder = _sortingOrder;

            Color color = new Color(_color.r, _color.g, _color.b, dim);
            if (_overlay.color != color) _overlay.color = color;

            bool visible = dim > 0.001f;
            if (_overlay.enabled != visible) _overlay.enabled = visible;
        }

        private SpriteRenderer CreateOverlay()
        {
            var go = new GameObject(OverlayName);
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            Texture2D texture = Texture2D.whiteTexture;
            renderer.sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            return renderer;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.15f, 0.2f, 0.5f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_size.x, _size.y, 0f));

            Gizmos.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_dim) * 0.35f);
            Gizmos.DrawCube(Vector3.zero, new Vector3(_size.x, _size.y, 0f));
        }
    }
}
