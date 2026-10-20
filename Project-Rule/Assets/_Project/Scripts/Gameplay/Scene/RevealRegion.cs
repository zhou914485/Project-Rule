using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 揭示区域：玩家**进入**就清掉这里的黑色遮罩（并揭掉暗墙），**离开**遮罩自动补回去。
    /// </summary>
    /// <remarks>
    /// <para><b>形状可以是多个矩形</b>：区域会收集自己（以及所有子物体）上的
    /// <see cref="BoxCollider2D"/>，玩家落在**任意一个**矩形里就算进入。
    /// 异形房间用几个长方形拼起来即可 —— 加个子物体、挂 BoxCollider2D 就行。</para>
    ///
    /// <para><b>为什么要「离开就补回去」</b>：遮罩是为了挡住摄像机看到别的场景。
    /// 玩家走到哪、哪块才亮，离开后重新变黑。</para>
    ///
    /// <para><b>暗墙只揭一次</b>：遮罩是「视线」，暗墙是「世界」——
    /// 找到过的暗门不会再变回墙（它的瓦片和墙皮已经永久清掉了）。</para>
    ///
    /// <para><b>为什么不用触发器回调</b>：玩家是 Kinematic 刚体 + <c>Rigidbody2D.Slide</c>，
    /// 默认不与静态碰撞体产生触发回调。这里的 BoxCollider2D 只当**范围定义**，
    /// 判定靠比较坐标，和物理无关。</para>
    ///
    /// <para><b>引用可以留空</b>：<c>Mask</c> 自动找同一个 Grid 下名为 Mask 的 Tilemap，
    /// <c>SecretWalls</c> 自动找场景里的 <see cref="SecretWallField"/>。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class RevealRegion : MonoBehaviour
    {
        [Header("触发")]
        [Tooltip("谁进来算数。留空则自动找场景里的 PlayerController。")]
        [SerializeField] private Transform _revealer;

        [Header("要揭开的东西")]
        [Tooltip("黑色遮罩层（黑瓦片 Tilemap）。留空则自动找同一个 Grid 下名为 Mask 的 Tilemap。")]
        [SerializeField] private Tilemap _mask;

        [Tooltip("暗墙管理组件（挂在逻辑 Tilemap 上）。留空则自动找场景里的 SecretWallField。")]
        [SerializeField] private SecretWallField _secretWalls;

        [Header("行为")]
        [Tooltip("离开区域后把遮罩补回去。关掉 = 揭开一次后永久可见。")]
        [SerializeField] private bool _restoreMaskOnExit = true;

        [Tooltip("遮罩淡出/补回的时长（秒）：0.5~1 之间比较像「门被打开」。<= 0 = 立刻消失。")]
        [SerializeField] private float _fadeDuration = 0.75f;

        private readonly List<BoxCollider2D> _boxes = new List<BoxCollider2D>();
        private readonly Dictionary<Vector3Int, TileBase> _clearedMask = new Dictionary<Vector3Int, TileBase>();

        private BoxCollider2D _area;
        private bool _inside;
        private bool _secretRevealed;
        private float _nextRevealerSearch;

        // 淡出动画：把这一片遮罩瓦片临时挪到 Mask_Fade 层上，只淡这一层。
        private Tilemap _fadeTilemap;
        private Color _fadeBase = Color.white;
        private float _fadeElapsed;
        private bool _fading;
        private bool _restoring;

        /// <summary>玩家当前是否在区域内（遮罩此刻是清掉的）。</summary>
        public bool IsInside => _inside;

        /// <summary>暗墙是否已经被揭掉（只发生一次）。</summary>
        public bool IsSecretRevealed => _secretRevealed;

        /// <summary>
        /// 组成区域的矩形（自己的 + 子物体的）。**每次访问都重新收集**，方便编辑器里随时加。
        /// </summary>
        public IReadOnlyList<BoxCollider2D> Boxes
        {
            get
            {
                CollectBoxes();
                return _boxes.ToArray();
            }
        }

        /// <summary>所有矩形的并集包围盒（诊断用；实际判定按每个矩形分别算）。</summary>
        public Bounds Bounds
        {
            get
            {
                CollectBoxes();
                if (_boxes.Count == 0) return new Bounds(transform.position, Vector3.zero);

                Bounds b = _boxes[0].bounds;
                for (int i = 1; i < _boxes.Count; i++) b.Encapsulate(_boxes[i].bounds);
                return b;
            }
        }

        private void Awake()
        {
            _area = GetComponent<BoxCollider2D>();
            if (_area != null) _area.isTrigger = true;

            CollectBoxes();
            for (int i = 0; i < _boxes.Count; i++)
                if (_boxes[i] != null) _boxes[i].isTrigger = true;

            if (_revealer == null) TryFindRevealer();

            // ⚠️ 引用必须是**同一个场景**里的。多场景叠加编辑时很容易拖成跨场景引用 ——
            // Unity 存场景时会报 "Cross scene references are not supported" 并且**不会保存**该引用。
            // 这里直接不认跨场景引用，改成自己找同场景的。
            if (!IsSameScene(_mask)) _mask = FindMaskTilemap();
            if (!IsSameScene(_secretWalls)) _secretWalls = FindSecretWalls();
        }

        /// <summary>引用是否指向**本场景**里的对象（跨场景引用 Unity 不会保存）。</summary>
        private bool IsSameScene(Component other)
        {
            return other != null && other.gameObject.scene == gameObject.scene;
        }

        /// <summary>找同场景的暗墙组件：优先同一个 Grid 下 Logic 上的那个。</summary>
        private SecretWallField FindSecretWalls()
        {
            Transform scope = transform.parent != null ? transform.parent : transform.root;
            foreach (var map in scope.GetComponentsInChildren<Tilemap>(true))
            {
                if (!map.gameObject.name.Equals("Logic", System.StringComparison.OrdinalIgnoreCase)) continue;
                var field = map.GetComponent<SecretWallField>();
                if (field != null) return field;
            }

            foreach (var field in FindObjectsByType<SecretWallField>(FindObjectsInactive.Include))
                if (IsSameScene(field)) return field;

            return null;
        }

        private void Update()
        {
            TickFade();

            // 关卡可能是**运行时才生成玩家**（PlayerSpawn 之类），Awake 那一刻找不到很正常 ——
            // 找不到就每隔半秒再试一次，别把「还没生成」当成「没人来」。
            if (_revealer == null && !TryFindRevealer()) return;

            if (Contains(_revealer.position)) Enter();
            else if (_restoreMaskOnExit) Exit();
        }

        /// <summary>
        /// 找玩家（只在没找到时调用）。只认**已激活**的玩家。
        /// </summary>
        /// <remarks>
        /// ⚠️ **不按场景过滤**：本项目的玩家是 <c>Boot</c> 场景里的常驻对象
        /// （<c>GameRoot/BootStarter</c> 生成，跨场景不销毁），它的 <c>scene</c> 永远不是关卡场景 ——
        /// 一旦要求「同场景」，区域就永远找不到玩家，表现就是**遮罩不清**。
        /// 关卡按世界分区并排摆放（各自坐标不重叠），所以按位置判定就够了。
        /// </remarks>
        private bool TryFindRevealer()
        {
            if (UnityEngine.Time.time < _nextRevealerSearch) return false;
            _nextRevealerSearch = UnityEngine.Time.time + 0.5f;

            foreach (var player in FindObjectsByType<RuleGame.Gameplay.Player.PlayerController>(FindObjectsInactive.Include))
            {
                if (player == null || !player.gameObject.activeInHierarchy) continue;

                _revealer = player.transform;
                return true;
            }

            return false;
        }

        /// <summary>点是否落在任意一个矩形里。</summary>
        public bool Contains(Vector3 point)
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                var box = _boxes[i];
                if (box == null || !box.enabled) continue;
                if (box.bounds.Contains(point)) return true;
            }
            return false;
        }

        /// <summary>进入：清遮罩（一次），并揭掉暗墙（永远只做一次）。</summary>
        public void Enter()
        {
            if (_inside) return;
            if (_restoring) FinishRestore();       // 上一段还在往回淡就进人：先把遮罩归位，再重新淡出
            _inside = true;

            for (int i = 0; i < _boxes.Count; i++)
            {
                var box = _boxes[i];
                if (box == null) continue;

                Bounds bounds = box.bounds;
                if (_mask != null) BeginMaskFade(bounds);
                if (!_secretRevealed && _secretWalls != null) _secretWalls.RevealIn(bounds);
            }

            _secretRevealed = true;
        }

        /// <summary>离开：把遮罩淡回来（时长同样用 <c>_fadeDuration</c>，不会硬切）。</summary>
        public void Exit()
        {
            if (!_inside) return;
            _inside = false;

            // 淡到一半就走人：把临时层清掉，遮罩立刻整块补回原样
            if (_fading && !_restoring) CancelFade();

            if (_mask == null || _clearedMask.Count == 0) return;

            if (_fading && _restoring) return;      // 已经在往回淡了，别重复启动

            if (_fadeDuration <= 0f || EnsureFadeTilemap() == null)
            {
                foreach (var kv in _clearedMask) _mask.SetTile(kv.Key, kv.Value);
                _clearedMask.Clear();
                return;
            }

            BeginMaskRestore();
        }

        private void CollectBoxes()
        {
            _boxes.Clear();
            var found = GetComponentsInChildren<BoxCollider2D>(true);
            for (int i = 0; i < found.Length; i++) _boxes.Add(found[i]);
        }

        // ==================================================================
        //  遮罩淡出
        // ==================================================================

        /// <summary>
        /// 把矩形范围内的黑瓦片从 Mask 层「摘下来」挂到临时淡出层上，再慢慢把这一层淡掉。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么要另开一层</b>：Tilemap 的透明度是**整层共用一个 color**，
        /// 而每格颜色被瓦片资产上的 <c>TileFlags.LockColor</c> 锁住了、改不动。
        /// 直接淡 Mask 的 color 会把整张地图的遮罩一起淡掉，所以只把这一片挪出来单独淡。</para>
        /// <para><b>代价</b>：每帧只写一次 <c>Tilemap.color</c>，与格数无关。</para>
        /// </remarks>
        private void BeginMaskFade(Bounds bounds)
        {
            if (_mask == null) return;

            Tilemap fade = EnsureFadeTilemap();
            if (fade == null)
            {
                ClearMask(bounds);      // 建不出临时层就退回「立刻消失」
                return;
            }

            if (MoveMaskTiles(bounds, fade) == 0) return;

            if (_fadeDuration <= 0f)
            {
                fade.ClearAllTiles();   // 不要动画：立刻消失（瓦片已记在 _clearedMask 里，离开照样补回来）
                return;
            }

            _restoring = false;
            _fading = true;
            _fadeElapsed = 0f;
            SetFadeAlpha(_fadeBase.a);
        }

        /// <summary>往回补遮罩：先把记下来的瓦片以 alpha 0 铺到淡出层，再淡进来，结束后搬回 Mask。</summary>
        private void BeginMaskRestore()
        {
            Tilemap fade = EnsureFadeTilemap();
            if (fade == null) return;

            foreach (var kv in _clearedMask)
            {
                fade.SetTile(kv.Key, kv.Value);
                _mask.SetTile(kv.Key, null);
            }

            _restoring = true;
            _fading = true;
            _fadeElapsed = 0f;
            SetFadeAlpha(0f);
        }

        /// <summary>推进淡出/淡入。暂停时不动 —— 和伤害、物理一致，暂停就是整个世界停住。</summary>
        private void TickFade()
        {
            if (!_fading || _fadeTilemap == null) return;
            if (RuleGame.Infrastructure.Time.TimeManager.Instance?.IsPaused == true) return;

            _fadeElapsed += UnityEngine.Time.deltaTime;
            float t = _fadeDuration <= 0f ? 1f : Mathf.Clamp01(_fadeElapsed / _fadeDuration);
            float eased = t * t * (3f - 2f * t);        // smoothstep：两头缓，不像线性那样机械

            if (_restoring)
            {
                SetFadeAlpha(_fadeBase.a * eased);
                if (t < 1f) return;

                _fading = false;
                _restoring = false;
                foreach (var kv in _clearedMask) _mask.SetTile(kv.Key, kv.Value);
                _clearedMask.Clear();
                _fadeTilemap.ClearAllTiles();
                return;
            }

            SetFadeAlpha(Mathf.Lerp(_fadeBase.a, 0f, eased));
            if (t >= 1f)
            {
                _fading = false;
                _fadeTilemap.ClearAllTiles();
            }
        }

        /// <summary>中断淡出（离开区域时用）。</summary>
        /// <summary>把正在回填的瓦片立刻归位（重进区域时用，避免遮罩补到一半又被打断）。</summary>
        private void FinishRestore()
        {
            _fading = false;
            _restoring = false;
            if (_fadeTilemap != null) _fadeTilemap.ClearAllTiles();
            if (_mask == null) return;
            foreach (var kv in _clearedMask) _mask.SetTile(kv.Key, kv.Value);
            _clearedMask.Clear();
        }

        private void CancelFade()
        {
            _fading = false;
            _restoring = false;
            if (_fadeTilemap != null) _fadeTilemap.ClearAllTiles();
        }

        /// <summary>把范围内的遮罩瓦片搬到淡出层；同时记进 <c>_clearedMask</c>，离开时补回 Mask。</summary>
        private int MoveMaskTiles(Bounds bounds, Tilemap fade)
        {
            BoundsInt cells = _mask.cellBounds;
            Vector3Int min = _mask.WorldToCell(bounds.min);
            Vector3Int max = _mask.WorldToCell(bounds.max);
            int xMin = Mathf.Max(min.x, cells.xMin);
            int xMax = Mathf.Min(max.x, cells.xMax);
            int yMin = Mathf.Max(min.y, cells.yMin);
            int yMax = Mathf.Min(max.y, cells.yMax);

            int moved = 0;
            for (int x = xMin; x <= xMax; x++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    var tile = _mask.GetTile(c);
                    if (tile == null) continue;

                    _clearedMask[c] = tile;
                    fade.SetTile(c, tile);
                    _mask.SetTile(c, null);
                    moved++;
                }
            }
            return moved;
        }

        /// <summary>建（或复用）临时淡出层：挂在同一个 Grid 下，排序层与顺序跟 Mask 一致。</summary>
        private Tilemap EnsureFadeTilemap()
        {
            if (_fadeTilemap != null) return _fadeTilemap;
            if (_mask == null) return null;

            Transform parent = _mask.transform.parent != null ? _mask.transform.parent : _mask.transform;
            var go = new GameObject("Mask_Fade");
            go.transform.SetParent(parent, false);

            // ⚠️ Tilemap 组件**不带**渲染器（它的 RequireComponent 只有 Transform）。
            // 只 AddComponent<Tilemap>() 的话这一层永远不显示 —— 遮罩就「啪」地消失，淡出等于没做。
            _fadeTilemap = go.AddComponent<Tilemap>();
            go.transform.localPosition = _mask.transform.localPosition;
            go.transform.localRotation = _mask.transform.localRotation;
            go.transform.localScale = _mask.transform.localScale;

            var fadeRenderer = go.GetComponent<TilemapRenderer>();
            if (fadeRenderer == null) fadeRenderer = go.AddComponent<TilemapRenderer>();

            var maskRenderer = _mask.GetComponent<TilemapRenderer>();
            if (fadeRenderer != null && maskRenderer != null)
            {
                fadeRenderer.sortingLayerID = maskRenderer.sortingLayerID;
                fadeRenderer.sortingOrder = maskRenderer.sortingOrder;
                fadeRenderer.mode = maskRenderer.mode;
                fadeRenderer.sharedMaterial = maskRenderer.sharedMaterial;
            }

            _fadeBase = _mask.color;            // 和 Mask 同色同透明度，淡出起点才对得上
            _fadeTilemap.color = _fadeBase;
            return _fadeTilemap;
        }

        private void SetFadeAlpha(float a)
        {
            if (_fadeTilemap == null) return;
            _fadeTilemap.color = new Color(_fadeBase.r, _fadeBase.g, _fadeBase.b, a);
        }

        private void ClearMask(Bounds bounds)
        {
            BoundsInt cells = _mask.cellBounds;
            Vector3Int min = _mask.WorldToCell(bounds.min);
            Vector3Int max = _mask.WorldToCell(bounds.max);
            int xMin = Mathf.Max(min.x, cells.xMin);
            int xMax = Mathf.Min(max.x, cells.xMax);
            int yMin = Mathf.Max(min.y, cells.yMin);
            int yMax = Mathf.Min(max.y, cells.yMax);

            for (int x = xMin; x <= xMax; x++)
            {
                for (int y = yMin; y <= yMax; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    var tile = _mask.GetTile(c);
                    if (tile == null) continue;
                    _clearedMask[c] = tile;     // 记下来，离开时补回去
                    _mask.SetTile(c, null);
                }
            }
        }

        private Tilemap FindMaskTilemap()
        {
            Transform scope = transform.parent != null ? transform.parent : transform.root;
            foreach (var map in scope.GetComponentsInChildren<Tilemap>(true))
                if (map.gameObject.name.Equals("Mask", System.StringComparison.OrdinalIgnoreCase)) return map;

            foreach (var map in FindObjectsByType<Tilemap>(FindObjectsInactive.Include))
                if (IsSameScene(map) && map.gameObject.name.Equals("Mask", System.StringComparison.OrdinalIgnoreCase)) return map;

            return null;
        }
    }
}
