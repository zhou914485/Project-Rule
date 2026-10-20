using System;
using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 区域套件：一套「这个王国的石头长什么样」。
    /// </summary>
    /// <remarks>
    /// <para><b>只存素材，不存位置</b>：位置由 <see cref="TileRegion"/> 在场景里划分。
    /// 这样同一套素材可以在多个分区复用，也不会出现「全局区域表跟场景脱钩」的问题。</para>
    ///
    /// <para><b>为什么四个朝向共用一批方块</b>：区域素材都是「1 格一块」，
    /// 靠旋转让粗糙边 / 齿轮边朝外，所以不需要为每个朝向单独准备图。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "TileRegionKit", menuName = "RuleGame/瓦片/区域套件")]
    public sealed class TileRegionKit : ScriptableObject
    {
        [Tooltip("显示名（日志、Inspector 用），如「发条王国」")]
        [SerializeField] private string _displayName = "未命名区域";

        [Tooltip("1 格方块，四个朝向共用。按格子坐标挑变体，最少 1 张")]
        [SerializeField] private Sprite[] _blocks = Array.Empty<Sprite>();

        [Tooltip("变体哈希偏移：让相邻区域即使格子坐标相同也不会排出一样的图")]
        [SerializeField] private int _variantSeed;

        [Tooltip("链式变体：第 k 张的「右半」= 第 k+1 张的「左半」，铺装时按列（或行）轮流取 —— 相邻两格拼出来颜色是连续的。积木王国的方块必须勾这个")]
        [SerializeField] private bool _chainVariants;

        [Tooltip("刺（Hazard 格）的美术：一格一张。留空 = 刺不铺美术，保持逻辑瓦片原样")]
        [SerializeField] private Sprite _spike;

        [Tooltip("九宫格切分（16 张，顺序：左上/上/右上 · 柱顶/左/心/右/柱中 · 左下/下/右下/柱底 · 平台左/中/右/单块）。" +
                 "填了它，铺装器就按「露哪条边」选块，而不是把 16 张当随机变体")]
        [SerializeField] private Sprite[] _nineSlice = Array.Empty<Sprite>();

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;

        public Sprite[] Blocks => _blocks;

        public int VariantSeed => _variantSeed;

        /// <summary>变体是否是一条「首尾相接的链」（相邻格拼起来颜色连续）。</summary>
        public bool ChainVariants => _chainVariants;

        /// <summary>九宫格 16 块（角色顺序见 <c>_nineSlice</c> 的 Tooltip）；空 = 这套素材不支持九宫格。</summary>
        public Sprite[] NineSlice => _nineSlice;

        /// <summary>这套素材有没有按角色切好的九宫格。</summary>
        public bool HasNineSlice => _nineSlice != null && _nineSlice.Length == 16;

        /// <summary>刺（Hazard 格）用的图；空 = 不铺。</summary>
        public Sprite Spike => _spike;

        /// <summary>
        /// 链式变体按坐标顺序取；否则按哈希取（石头那种一块块独立的素材）。
        /// </summary>
        /// <param name="cell">格子坐标</param>
        /// <param name="vertical">true = 沿 Y 递增（竖排的墙），false = 沿 X 递增（地板 / 天花板）</param>
        public int VariantIndex(Vector3Int cell, bool vertical)
        {
            if (!IsUsable) return 0;

            if (!_chainVariants)
            {
                int h = (cell.x + _variantSeed) * 73856093 ^ (cell.y - _variantSeed) * 19349663;
                h = (h ^ (h >> 13)) & 0x7fffffff;
                return h % _blocks.Length;
            }

            int along = vertical ? cell.y : cell.x;
            int index = (along + _variantSeed) % _blocks.Length;
            return index < 0 ? index + _blocks.Length : index;
        }

        /// <summary>这套素材能不能用（没配方块就该回退默认套件）。</summary>
        public bool IsUsable => _blocks != null && _blocks.Length > 0;

        /// <summary>按格子坐标稳定地挑一个变体。</summary>
        public Sprite BlockAt(Vector3Int cell)
        {
            if (!IsUsable) return null;

            int h = (cell.x + _variantSeed) * 73856093 ^ (cell.y - _variantSeed) * 19349663;
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return _blocks[h % _blocks.Length];
        }
    }
}
