using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 可塌陷地板：把一块区域里的 **Logic 瓦片**清掉、把对应的**墙皮美术**隐藏，并撒一把碎块。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要清瓦片，而不是关碰撞体</b>：<c>Grid/Logic</c> 整张表共用**一个**
    /// <c>TilemapCollider2D</c>，没有「关掉某一格」的 API；而 <c>Grid/Art</c> 的精灵是铺装器生成的
    /// **独立子物体**，跟瓦片没有父子关系。所以「地板没了」必须同时做两件事：
    /// <see cref="Tilemap.SetTile"/> 清格 + 隐藏 bounds 相交的 <see cref="SpriteRenderer"/>。
    /// 做法与 <see cref="SecretWallField.RevealIn"/> 完全一致。</para>
    ///
    /// <para><b>碎块直接拿墙皮的图</b>：每一格被隐藏的 art 精灵就是那一格的碎块贴图 ——
    /// 不用另做预制体，也不用担心「碎块和地板颜色不一样」。想加碎块量就调
    /// <see cref="_debrisPerCell"/>。</para>
    ///
    /// <para><b>为什么碎块自己积分</b>：见 <see cref="TrapFloorDebris"/> —— 没有碰撞体的动态刚体会穿地。</para>
    ///
    /// <para><b>不进快照</b>：这是关卡级的一次性事件；而且 <c>CityMechanism</c> 只认一个
    /// <c>Shape</c>，多块瓦片表达不了。回溯不该把地板装回去（人会被关在下面）。</para>
    ///
    /// <para><b>计时</b>：<c>FixedUpdate</c> + <c>TimeManager.IsPaused</c> 闸门，
    /// 与 <c>BoulderFragment</c> / <c>CityPathMotor</c> 同一条做法（不用协程 —— 暂停时协程照跑）。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrapFloor : MonoBehaviour
    {
        /// <summary>一格瓦片的执行项。</summary>
        private struct CellOp
        {
            public Vector3Int Cell;
            public TileBase Tile;
            public float Time;
            public Sprite Sprite;
            public Color Color;
            public Vector3 SpawnAt;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        /// <summary>一个美术对象的隐藏项。</summary>
        private struct ArtOp
        {
            public SpriteRenderer Renderer;
            public float Time;
        }

        [Header("范围与目标")]
        [Tooltip("塌陷范围。留空则取同物体上的 Collider2D。建议用一个勾了 Is Trigger 的 BoxCollider2D 画框。\n" +
                 "⚠️ 框要正好盖住洞口，盖到墙就会把墙也一起清掉。")]
        [SerializeField] private Collider2D _area;

        [Tooltip("要被清空的 Tilemap。留空则自动在父级 Grid 里找名为 Logic 的那张。")]
        [SerializeField] private Tilemap _tilemap;

        [Tooltip("地板美术的根（铺装器生成的 SpriteRenderer 都在这里）。留空则自动找父级 Grid/Art。")]
        [SerializeField] private Transform _artRoot;

        [Header("节奏")]
        [Tooltip("按下到开始塌的延迟（秒）。")]
        [SerializeField, Min(0f)] private float _collapseDelay = 0.15f;

        [Tooltip("相邻两格的错峰（秒）。从中心往两边塌。")]
        [SerializeField, Min(0f)] private float _cellStagger = 0.05f;

        [Header("碎块")]
        [Tooltip("每格生成几个碎块。0 = 只开洞、不掉渣。")]
        [SerializeField, Min(0)] private int _debrisPerCell = 1;

        [Tooltip("碎块缩放。1 = 和原来那格一样大；0.6 左右像碎块。")]
        [SerializeField, Min(0.05f)] private float _debrisScale = 0.6f;

        [Tooltip("碎块的排序层内顺序。默认比地板（1）高一层，掉下去时不会被地板盖住。")]
        [SerializeField] private int _debrisSortingOrder = 2;

        [Tooltip("生成位置的随机抖动（世界单位）。")]
        [SerializeField, Min(0f)] private float _debrisJitter = 0.15f;

        private readonly List<CellOp> _cells = new List<CellOp>();
        private readonly List<ArtOp> _art = new List<ArtOp>();
        private readonly Dictionary<Vector3Int, float> _times = new Dictionary<Vector3Int, float>();
        private readonly Dictionary<Vector3Int, int> _indexByCell = new Dictionary<Vector3Int, int>();
        private int _cellIndex;
        private int _artIndex;
        private float _elapsed;
        private bool _running;
        private bool _collapsed;

        /// <summary>已经塌完了。</summary>
        public bool IsCollapsed => _collapsed;

        /// <summary>塌完的瞬间。</summary>
        public event Action OnCollapsed;

        // ==================================================================
        //  对外
        // ==================================================================

        /// <summary>开始塌。幂等：塌过 / 正在塌时再调没有副作用。</summary>
        public void Collapse()
        {
            if (_running || _collapsed) return;

            ResolveRefs();

            if (_tilemap == null)
            {
                Debug.LogError(
                    "[TrapFloor] 找不到 Tilemap —— 地板不会塌。把它放在 Grid 下面，或在 Inspector 里指定。", this);
                return;
            }

            BuildOps();

            if (_cells.Count == 0 && _art.Count == 0)
            {
                Debug.LogWarning(
                    "[TrapFloor] 范围里没有可清的瓦片，也没有美术 —— 检查 _area 是不是框在了地板上。", this);
            }

            _running = true;
            _elapsed = 0f;
            _cellIndex = 0;
            _artIndex = 0;
        }

        /// <summary>
        /// 把地板装回去。**只给调试 / 重开一局用** —— 正式流程里洞口是永久开的。
        /// </summary>
        public void Restore()
        {
            if (_tilemap == null) ResolveRefs();

            for (int i = 0; i < _cells.Count; i++)
            {
                if (_tilemap != null) _tilemap.SetTile(_cells[i].Cell, _cells[i].Tile);
            }

            for (int i = 0; i < _art.Count; i++)
            {
                if (_art[i].Renderer != null) _art[i].Renderer.enabled = true;
            }

            _cellIndex = _cells.Count;
            _artIndex = _art.Count;
            _running = false;
            _collapsed = false;
        }

        // ==================================================================
        //  推进
        // ==================================================================

        private void FixedUpdate()
        {
            if (!_running) return;
            if (TimeManager.Instance?.IsPaused == true) return;

            _elapsed += Time.fixedDeltaTime;

            while (_cellIndex < _cells.Count && _cells[_cellIndex].Time <= _elapsed)
            {
                ApplyCell(_cells[_cellIndex]);
                _cellIndex++;
            }

            while (_artIndex < _art.Count && _art[_artIndex].Time <= _elapsed)
            {
                ArtOp op = _art[_artIndex];
                if (op.Renderer != null) op.Renderer.enabled = false;
                _artIndex++;
            }

            if (_cellIndex >= _cells.Count && _artIndex >= _art.Count) Finish();
        }

        private void ApplyCell(CellOp op)
        {
            if (_tilemap == null) return;

            // 中途被别人改过就不动它（例如另一个机关先清了这一格）。
            if (_tilemap.GetTile(op.Cell) == op.Tile) _tilemap.SetTile(op.Cell, null);

            SpawnDebris(op);
        }

        private void Finish()
        {
            _running = false;
            _collapsed = true;
            OnCollapsed?.Invoke();

            RuleGame.Infrastructure.Persistence.WorldStateStore.SetBool(SaveStateKey, true);   // 塌了就是永久进度
        }

        private void Start()
        {
            // 读档恢复：存档里这块地板已经塌了，重进关卡不该再站上去。
            if (RuleGame.Infrastructure.Persistence.WorldStateStore.GetBool(SaveStateKey)) Collapse();
        }

        /// <summary>存档用的稳定键。</summary>
        private string SaveStateKey => RuleGame.Infrastructure.Persistence.WorldStateStore.KeyOf(this, "collapsed");

        /// <summary>用那一格被隐藏的墙皮图当碎块 —— 不用另做预制体，颜色天然一致。</summary>
        private void SpawnDebris(CellOp op)
        {
            if (op.Sprite == null || _debrisPerCell <= 0) return;

            for (int i = 0; i < _debrisPerCell; i++)
            {
                var go = new GameObject("TrapFloorDebris");

                // 跟地板同一个场景：分区卸载时一起走，也不会跑进别的场景。
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, gameObject.scene);

                // **从原来那块墙皮的位置起飞**：精灵轴心可能不在中心，用格心会对不齐，
                // 一帧之内看起来像「地板跳了一下」。
                go.transform.SetPositionAndRotation(
                    op.SpawnAt + new Vector3(
                        UnityEngine.Random.Range(-_debrisJitter, _debrisJitter),
                        UnityEngine.Random.Range(-_debrisJitter, _debrisJitter),
                        0f),
                    op.Rotation);
                go.transform.localScale = Vector3.Scale(op.Scale, Vector3.one * _debrisScale);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = op.Sprite;
                renderer.color = op.Color;
                renderer.sortingOrder = _debrisSortingOrder;

                var debris = go.AddComponent<TrapFloorDebris>();
                debris.Launch(
                    new Vector2(UnityEngine.Random.Range(-0.7f, 0.7f), UnityEngine.Random.Range(-0.3f, 0.3f)),
                    UnityEngine.Random.Range(-200f, 200f));
            }
        }

        // ==================================================================
        //  采集
        // ==================================================================

        private void BuildOps()
        {
            _cells.Clear();
            _art.Clear();
            _times.Clear();
            _indexByCell.Clear();

            Bounds area = AreaBounds();
            Vector3 centre = area.center;
            Vector3Int min = _tilemap.WorldToCell(area.min);
            Vector3Int max = _tilemap.WorldToCell(area.max);

            var pending = new List<(Vector3Int Cell, TileBase Tile, float Distance)>();
            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    TileBase tile = _tilemap.GetTile(cell);
                    if (tile == null) continue;

                    Vector3 world = _tilemap.GetCellCenterWorld(cell);
                    pending.Add((cell, tile, (world - centre).sqrMagnitude));
                }
            }

            // 从中心往两边塌：离得近的先掉。
            pending.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            for (int i = 0; i < pending.Count; i++)
            {
                float time = _collapseDelay + i * _cellStagger;
                _cells.Add(new CellOp
                {
                    Cell = pending[i].Cell,
                    Tile = pending[i].Tile,
                    Time = time,
                    Color = Color.white
                });
                _times[pending[i].Cell] = time;
                _indexByCell[pending[i].Cell] = i;
            }

            if (_artRoot == null) return;

            SpriteRenderer[] art = _artRoot.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < art.Length; i++)
            {
                SpriteRenderer renderer = art[i];
                if (renderer == null) continue;

                // **按「这块墙皮所在的格子」匹配**，不按包围盒 ——
                // 墙皮精灵常常比一格大、轴心也不一定在中心（实测某张 cell 图的
                // bounds.center 离锚点 0.56 米），用包围盒判断会把隔壁的墙也一起藏掉。
                Vector3Int cell = _tilemap.WorldToCell(renderer.transform.position);
                if (!_indexByCell.TryGetValue(cell, out int index)) continue;

                float time = _times[cell];

                // 这一格的美术图就是它的碎块图：连轴心偏移一起继承，碎块从原位起飞。
                if (renderer.sprite != null && _cells[index].Sprite == null)
                {
                    CellOp op = _cells[index];
                    op.Sprite = renderer.sprite;
                    op.Color = renderer.color;
                    op.SpawnAt = renderer.transform.position;
                    op.Rotation = renderer.transform.rotation;
                    op.Scale = renderer.transform.lossyScale;
                    _cells[index] = op;
                }

                _art.Add(new ArtOp { Renderer = renderer, Time = time });
            }

            _art.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        private void ResolveRefs()
        {
            if (_area == null) _area = GetComponent<Collider2D>();

            Grid grid = GetComponentInParent<Grid>();

            if (_tilemap == null && grid != null)
            {
                Tilemap[] maps = grid.GetComponentsInChildren<Tilemap>(true);
                for (int i = 0; i < maps.Length; i++)
                {
                    if (maps[i].name == "Logic") { _tilemap = maps[i]; break; }
                }

                if (_tilemap == null && maps.Length > 0) _tilemap = maps[0];
            }

            if (_tilemap == null) _tilemap = GetComponentInParent<Tilemap>();

            if (_artRoot == null && grid != null)
            {
                Transform art = grid.transform.Find("Art");
                if (art != null) _artRoot = art;
            }
        }

        private Bounds AreaBounds()
        {
            if (_area != null) return _area.bounds;
            return new Bounds(transform.position, Vector3.one * 0.5f);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        private void OnDrawGizmosSelected()
        {
            Bounds area = AreaBounds();
            Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(area.center, area.size);

            if (_tilemap == null) return;

            Vector3Int min = _tilemap.WorldToCell(area.min);
            Vector3Int max = _tilemap.WorldToCell(area.max);
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.7f);
            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (_tilemap.GetTile(cell) == null) continue;
                    Gizmos.DrawWireCube(_tilemap.GetCellCenterWorld(cell), Vector3.one * 0.45f);
                }
            }
        }
    }
}