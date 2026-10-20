using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 让**齿轮瓦片**转起来：逐帧给 Tilemap 里指定的瓦片格子写旋转矩阵。
    /// </summary>
    /// <remarks>
    /// <para><b>瓦片是静态的，但格子不是</b>：Tilemap 允许单个格子带变换矩阵
    /// （<see cref="Tilemap.SetTransformMatrix"/>）。每帧把同一个角度写进所有齿轮格子，
    /// 一整片齿轮就一起转 —— 位置、图层、判定全都不动，只是贴图朝向在变。</para>
    ///
    /// <para><b>判定不受影响</b>：<see cref="HazardField"/> 按格子查，不读矩阵。
    /// 转的只是画面。</para>
    ///
    /// <para><b>挂哪</b>：Logic 层的 Tilemap 上（和 <see cref="HazardField"/> 同一个物体）。
    /// 菜单 <c>Tools/规则游戏/关卡瓦片/⑥</c> 会自动挂好并填上齿轮瓦片，不用手接。</para>
    ///
    /// <para><b>为什么不用 Animated Tile</b>：齿轮是 12 齿、每 30° 就和自己重合，
    /// 帧动画的步进得按 30° 的约数算，帧数一少就跳；而且所有齿轮只有同一个相位。
    /// 写矩阵是一张图、任意角度、每帧连续。</para>
    ///
    /// <para>⚠️ 每帧写矩阵会让 Tilemap 重建受影响区块的网格。齿轮集中在一片时开销很小；
    /// 若以后齿轮散满整张图、帧率掉了，再考虑换 Animated Tile。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Tilemap))]
    public sealed class GearTileSpinner : MonoBehaviour
    {
        /// <summary>重新扫描齿轮格子的间隔（秒）。新铺的齿轮最多这么久就会被带上。</summary>
        private const float RescanInterval = 0.5f;

        [Header("转什么")]
        [Tooltip("哪些瓦片算「会转的齿轮」。菜单登记时会自动填。清空 = 不转。")]
        [SerializeField] private TileBase[] _spinTiles = new TileBase[0];

        [Tooltip("转速（度 / 秒）。负值反向。")]
        [SerializeField] private float _degreesPerSecond = 120f;

        private Tilemap _tilemap;
        private readonly List<Vector3Int> _cells = new List<Vector3Int>();
        private float _angle;
        private float _nextRescan;

        private void Awake() => _tilemap = GetComponent<Tilemap>();

        private void OnEnable()
        {
            _angle = 0f;
            _nextRescan = RescanInterval;
            Rescan();
            Apply();
        }

        /// <summary>停用 / 退出播放时把角度还原，别让格子停在斜着的状态。</summary>
        private void OnDisable() => ResetMatrices();

        private void Update()
        {
            // 暂停：与别的按帧推进的表现读同一个出口（暂停时返回 0）。
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = TimeManager.Instance != null
                ? TimeManager.Instance.DeltaTime
                : UnityEngine.Time.deltaTime;
            if (dt <= 0f) return;

            _nextRescan -= dt;
            if (_nextRescan <= 0f)
            {
                _nextRescan = RescanInterval;
                Rescan();
            }

            _angle = Mathf.Repeat(_angle + _degreesPerSecond * dt, 360f);
            Apply();
        }

        /// <summary>重新收集当前地图上的齿轮格子。铺完新齿轮不想等半秒可以手动调。</summary>
        public void Rescan()
        {
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();

            _cells.Clear();
            if (_tilemap == null || _spinTiles == null || _spinTiles.Length == 0) return;

            BoundsInt bounds = _tilemap.cellBounds;
            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                TileBase tile = _tilemap.GetTile(cell);
                if (tile != null && Contains(tile)) _cells.Add(cell);
            }
        }

        private bool Contains(TileBase tile)
        {
            for (int i = 0; i < _spinTiles.Length; i++)
                if (_spinTiles[i] == tile) return true;
            return false;
        }

        private void Apply()
        {
            if (_cells.Count == 0) return;

            Matrix4x4 matrix = RotationAroundCellCenter(_angle);
            for (int i = 0; i < _cells.Count; i++)
                _tilemap.SetTransformMatrix(_cells[i], matrix);
        }

        private void ResetMatrices()
        {
            if (_tilemap == null) return;
            for (int i = 0; i < _cells.Count; i++)
                _tilemap.SetTransformMatrix(_cells[i], Matrix4x4.identity);
        }

        /// <summary>
        /// 绕格子中心旋转。Tilemap 的格子变换原点在**左下角**（0,0），
        /// 而贴图是居中铺满整格的，所以要先平移到 (0.5, 0.5) 再转。
        /// </summary>
        public static Matrix4x4 RotationAroundCellCenter(float degrees)
        {
            return Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.Euler(0f, 0f, degrees), Vector3.one)
                 * Matrix4x4.TRS(new Vector3(-0.5f, -0.5f, 0f), Quaternion.identity, Vector3.one);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给生成工具用：接上要转的瓦片并立刻重扫一次。</summary>
        public void Configure(TileBase[] spinTiles)
        {
            _spinTiles = spinTiles;
            Rescan();
        }

        /// <summary>当前的齿轮瓦片列表（编辑器工具幂等判断用）。</summary>
        public TileBase[] SpinTiles => _spinTiles;
    }
}
