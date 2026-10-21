using System;
using UnityEngine;

namespace RuleGame.UI.Comic
{
    /// <summary>进入某一页时用哪种转场。</summary>
    public enum ComicTransition
    {
        /// <summary>黑场：淡出 → 全黑 → 淡入。大多数换页用它。</summary>
        Dip,

        /// <summary>原地溶解：不经过全黑，只在两层之间交叉淡入。相邻页「同一构图只动一点」时用它。</summary>
        Dissolve,

        /// <summary>黑场加长版：用在场景交界，给观众一口气。</summary>
        SceneDip
    }

    /// <summary>漫画序列里的一页。</summary>
    [Serializable]
    public struct ComicPage
    {
        [Tooltip("整页贴图（3500×2600 原图，导入时压到 2048）。")]
        [SerializeField] private Texture2D _image;

        [Tooltip("从上一页进入这一页时用哪种转场。第一页不看这个字段。")]
        [SerializeField] private ComicTransition _transition;

        [Tooltip("不等点击，停一会儿自己往下走。黑页用它 —— 让玩家点一次纯黑等于点空气。")]
        [SerializeField] private bool _autoAdvance;

        [Tooltip("自动前进前先停多久（秒，非缩放时间）。")]
        [SerializeField, Min(0f)] private float _blackHold;

        public ComicPage(Texture2D image, ComicTransition transition, bool autoAdvance, float blackHold)
        {
            _image = image;
            _transition = transition;
            _autoAdvance = autoAdvance;
            _blackHold = blackHold;
        }

        public Texture2D Image => _image;

        public ComicTransition Transition => _transition;

        public bool AutoAdvance => _autoAdvance;

        public float BlackHold => _blackHold;
    }

    /// <summary>一段漫画：开场（1–19）与结尾（20–21）各一份资产。</summary>
    [CreateAssetMenu(menuName = "规则游戏/剧情漫画/序列", fileName = "Comic_New")]
    public sealed class ComicSequence : ScriptableObject
    {
        [SerializeField] private ComicPage[] _pages = Array.Empty<ComicPage>();

        public int Count => _pages?.Length ?? 0;

        public ComicPage Get(int index) => _pages[index];

        /// <summary>只给导入工具写数据用；运行时不调用。</summary>
        public void SetPages(ComicPage[] pages) => _pages = pages ?? Array.Empty<ComicPage>();
    }
}
