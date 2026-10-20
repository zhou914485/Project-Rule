using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>攻击延展平台的换皮：收缩态用卷起的毯子、展开态用铺开的毯子。</summary>
    /// <remarks>
    /// <para>平台自己的 <c>Art</c> 白盒会被脚本按「长度 × 厚度」拉伸，直接换图会把毯子拉变形。
    /// 这里另挂一个 <c>Skin</c> 子物体，按状态选图、按世界尺寸反算缩放，不动平台的碰撞与机关参数。</para>
    /// </remarks>
    [ExecuteAlways]
    public sealed class AttackExtendedPlatformSkin : MonoBehaviour
    {
        public AttackExtendedPlatform Platform;
        public SpriteRenderer Skin;
        public Sprite Rolled;
        public Sprite Spread;

        [Tooltip("卷起态最多允许多高（世界单位）；毯子卷起来比平台厚，但不能厚到把人埋进去")]
        [Min(0.1f)] public float RolledMaxHeight = 0.9f;

        private void OnEnable() => Apply();

        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (Platform == null || Skin == null) return;

            Transform source = Platform.transform.Find("Art");
            if (source == null) return;

            bool open = Platform.IsExtended || Platform.IsExtending;
            Sprite sprite = open ? Spread : Rolled;
            if (sprite == null) return;

            if (Skin.sprite != sprite) Skin.sprite = sprite;

            float length = Mathf.Max(0.1f, source.localScale.x);
            float height = open
                ? Mathf.Max(0.1f, source.localScale.y)
                : Mathf.Min(length * sprite.bounds.size.y / sprite.bounds.size.x, RolledMaxHeight);

            Transform parent = Skin.transform.parent;
            Vector3 parentLossy = parent == null ? Vector3.one : parent.lossyScale;
            Skin.transform.localScale = new Vector3(
                length / sprite.bounds.size.x / Mathf.Max(0.0001f, parentLossy.x),
                height / sprite.bounds.size.y / Mathf.Max(0.0001f, parentLossy.y),
                1f);
        }
    }
}
