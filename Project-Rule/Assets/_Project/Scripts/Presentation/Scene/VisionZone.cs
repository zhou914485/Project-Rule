using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 一片「视野受限区」：玩家走进矩形范围后，画面只剩他身边一圈亮，其余全黑。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是跟在玩家身上的一块黑纸</b>：本项目的精灵全是 Unlit、相机也没开后处理
    /// （<c>m_RenderPostProcessing = 0</c>），2D 光照与 Volume 都用不上；
    /// 一张中间透明、四周纯黑的精灵跟着玩家走，是唯一不需要额外 shader 的做法。</para>
    /// <para><b>为什么洞能既按米算、又永远盖满屏幕</b>：精灵以玩家为中心，边长取相机视野的
    /// <b>整条对角线</b>（玩家站到画面任何一个角，离最远那个角的距离也不超过对角线长度），
    /// 圆洞半径按世界米数写进贴图；只有半径或相机远近（规则选择会压 ortho）变了才重画贴图。</para>
    /// <para><b>只改视觉</b>：不进快照、不参与规则；敌人和道具在洞外一样看不见，这是有意的。
    /// HUD 走 Canvas，不受影响。</para>
    /// <para><b>区域可以重叠</b>：按 <c>Priority</c> 只让一块生效，不会画出两个洞。</para>
    /// <para><b>不要放进 <c>Grid/Art</c></b>：铺装器（Ctrl+Shift+G）会清空那个容器。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VisionZone : MonoBehaviour
    {
        private const string MaskName = "VisionMask";
        private const int TextureSize = 256;
        private const float CoverMargin = 1.05f;

        private static readonly List<VisionZone> Zones = new List<VisionZone>();

        [Tooltip("以此物体为中心的矩形（米）：玩家在这个范围里才受视野限制。")]
        [SerializeField] private Vector2 _size = new Vector2(30f, 20f);

        [Tooltip("区域重叠时数值高的优先。")]
        [SerializeField] private int _priority;

        [Tooltip("看得见的半径（米，以玩家为中心）。")]
        [SerializeField, Min(0.1f)] private float _viewRadius = 3.5f;

        [Tooltip("圆形视野边缘的柔化宽度（米）。0 = 硬边。")]
        [SerializeField, Min(0f)] private float _feather = 1.2f;

        [Tooltip("进入 / 离开时淡入淡出的时长（秒）。0 = 瞬间。")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 0.35f;

        [Tooltip("黑幕的排序序号。默认 200，高于玩家（100）。")]
        [SerializeField] private int _sortingOrder = 200;

        [Tooltip("判定跟随谁。留空每 0.5 秒自动找 PlayerController。")]
        [SerializeField] private Transform _focus;

        private SpriteRenderer _mask;
        private Texture2D _texture;
        private Sprite _sprite;
        private Color32[] _pixels;
        private Camera _camera;
        private float _alpha;
        private float _builtReach;
        private float _builtRadius;
        private float _focusTimer;

        public Vector2 Size => _size;
        public float ViewRadius => _viewRadius;

        /// <summary>世界坐标点是否落在这个矩形里；<paramref name="margin"/> 是外扩（米）。</summary>
        public bool Contains(Vector3 worldPoint, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector3 scale = transform.lossyScale;
            float mx = margin / Mathf.Max(0.001f, Mathf.Abs(scale.x));
            float my = margin / Mathf.Max(0.001f, Mathf.Abs(scale.y));
            return Mathf.Abs(local.x) <= Mathf.Abs(_size.x) * 0.5f + mx
                && Mathf.Abs(local.y) <= Mathf.Abs(_size.y) * 0.5f + my;
        }

        private void Awake()
        {
            _camera = Camera.main;
        }

        private void OnEnable()
        {
            if (!Zones.Contains(this)) Zones.Add(this);
        }

        private void OnDisable()
        {
            Zones.Remove(this);
            _alpha = 0f;
            if (_mask != null) _mask.enabled = false;
        }

        private void OnDestroy()
        {
            if (_mask != null) Destroy(_mask.gameObject);
            if (_sprite != null) Destroy(_sprite);
            if (_texture != null) Destroy(_texture);
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            RefreshFocus(dt);

            float target = ResolveOwner() == this ? 1f : 0f;
            _alpha = _fadeSeconds <= 0.0001f
                ? target
                : Mathf.MoveTowards(_alpha, target, dt / _fadeSeconds);

            if (_alpha <= 0.001f)
            {
                if (_mask != null) _mask.enabled = false;
                return;
            }

            EnsureMask();
            Apply();
        }

        private void RefreshFocus(float dt)
        {
            if (_focus != null) return;

            _focusTimer -= dt;
            if (_focusTimer > 0f) return;
            _focusTimer = 0.5f;

            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player != null) _focus = player.transform;
        }

        /// <summary>当前位置上该由哪块区域生效：重叠取 Priority 高的。</summary>
        private VisionZone ResolveOwner()
        {
            if (_focus == null) return null;

            Vector3 point = _focus.position;
            VisionZone best = null;
            for (int i = 0; i < Zones.Count; i++)
            {
                VisionZone zone = Zones[i];
                if (zone == null || !zone.isActiveAndEnabled || !zone.Contains(point)) continue;
                if (best == null || zone._priority > best._priority) best = zone;
            }

            return best;
        }

        private void EnsureMask()
        {
            if (_mask != null) return;

            var go = new GameObject(MaskName);
            go.hideFlags = HideFlags.DontSave;

            _mask = go.AddComponent<SpriteRenderer>();
            _mask.sortingOrder = _sortingOrder;
            _mask.color = Color.black;

            _texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "VisionMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f),
                TextureSize);
            _mask.sprite = _sprite;

            _pixels = new Color32[TextureSize * TextureSize];
        }

        private void Apply()
        {
            if (_focus == null) return;

            float reach = ViewDiagonal() * CoverMargin;
            float radius = Mathf.Max(0.05f, _viewRadius);

            Transform mask = _mask.transform;
            Vector3 position = _focus.position;
            position.z = 0f;
            mask.position = position;
            mask.localScale = new Vector3(reach * 2f, reach * 2f, 1f);

            if (_texture == null || Mathf.Abs(reach - _builtReach) > reach * 0.01f
                || Mathf.Abs(radius - _builtRadius) > 0.01f)
            {
                BuildTexture(reach, radius);
            }

            _mask.sortingOrder = _sortingOrder;
            _mask.color = new Color(0f, 0f, 0f, _alpha);
            _mask.enabled = true;
        }

        /// <summary>照相机当前视野做一张「中间透明、四周纯黑」的贴图；半径按世界米数折算。</summary>
        private void BuildTexture(float reach, float radius)
        {
            float feather = Mathf.Max(0.0001f, _feather);

            for (int y = 0; y < TextureSize; y++)
            {
                float py = (y + 0.5f) / TextureSize - 0.5f;
                for (int x = 0; x < TextureSize; x++)
                {
                    float px = (x + 0.5f) / TextureSize - 0.5f;

                    // 贴图半边长 = 精灵的半边长（世界米），所以归一化距离乘 2 * reach 就是米
                    float worldDistance = Mathf.Sqrt(px * px + py * py) * 2f * reach;
                    float alpha = Mathf.SmoothStep(
                        0f, 1f,
                        Mathf.InverseLerp(radius, radius + feather, worldDistance));

                    _pixels[y * TextureSize + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false, false);

            _builtReach = reach;
            _builtRadius = radius;
        }

        /// <summary>视野整条对角线的长度：玩家站到画面任何位置，到最远那个角的距离都不超过它。</summary>
        private float ViewDiagonal()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null || !_camera.orthographic) return 10f;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            return Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) * 2f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.6f, 0.2f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_size.x, _size.y, 0f));

            // 只是参考：真正的圆跟着玩家走
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _viewRadius);
        }
    }
}
