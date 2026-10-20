using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**白盒弹簧平台**（可伸缩铁丝弹簧）。
    /// </summary>
    /// <remarks>
    /// <para><b>层级为什么这么分</b>：碰撞箱挂在根上（根 scale 恒为 1，<c>size</c> 直接就是世界尺寸），
    /// 美术放 <c>Art</c> / <c>Spring</c> 子物体上撑缩放。**不要把缩放设在根上** ——
    /// 子物体会继承缩放，标签会被横向拉长、纵向压扁，弹簧柱的拉伸也会被再乘一次。</para>
    ///
    /// <para><b>根 = 平台本身，锚点是数据字段</b>：与平移平台（根 = 平台、起点终点存字段）同一套。
    /// 运行期只有平台在动，锚点固定 —— 摆位用 Inspector 上的「锚点 = 平台正下方」按钮。</para>
    ///
    /// <para><b>弹簧柱是 1 世界单位高的图</b>：组件按「缩放 y = 高度」拉伸它。
    /// 换正式美术时那张图也要按 1 单位高做，否则拉伸比例会不对。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：玩家的接触探测只认这个层，
    /// 放错层弹簧就是一块穿不过去的空气。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑥ 铁丝弹簧（可伸缩弹簧平台）。</para>
    /// </remarks>
    public static class SpringPlatformBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "SpringPlatform";
        private const string ArtName = "Art";
        private const string SpringName = "Spring";
        private const string LabelName = "Label";

        /// <summary>白盒尺寸（世界单位）。1 格 = 0.5 单位，所以 1.6 宽 ≈ 3 格。</summary>
        private static readonly Vector2 PlankSize = new Vector2(1.6f, 0.35f);

        /// <summary>默认自然长度 / 压缩长度：站上去会缩掉 60%。</summary>
        private const float DefaultRestLength = 2f;
        private const float DefaultCompressedLength = 0.8f;

        private static readonly Color ArtColor   = new Color(0.98f, 0.78f, 0.32f, 1f);
        private static readonly Color SpringColor = new Color(0.78f, 0.45f, 0.42f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑥ 铁丝弹簧（可伸缩弹簧平台）", false, 55)]
        public static void CreateSpring()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，弹簧放下去玩家踩不到。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建弹簧平台");

            var box = root.AddComponent<BoxCollider2D>();
            box.size = PlankSize;
            box.offset = Vector2.zero;

            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var spring = BuildSpringPost(root.transform, sprite);
            BuildPlank(root.transform, sprite);
            TextMesh label = BuildLabel(root.transform);

            var platform = root.AddComponent<SpringPlatform>();
            // 锚点不用单独配：它就是「摆放位置往下 自然长度」，拖动物体时整体跟着走。
            platform.SetWhitebox(spring.transform, label);
            EditorUtility.SetDirty(platform);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）。" +
                      $"锚点 = 摆放位置往下 {DefaultRestLength} 米（拖动物体会整体搬走）。" +
                      $"压缩深度按胡克定律：x = 载重·g / k，k=16 时 1 kg 的玩家压 0.61 米、8 kg 的铁箱压到底。" +
                      $"平台摩擦默认 1.5（普通地面是 1，越大越抓地），在「摩擦」里调。");
        }

        /// <summary>弹簧柱：1 世界单位高的方块，组件按当前长度拉伸它。</summary>
        private static GameObject BuildSpringPost(Transform parent, Sprite sprite)
        {
            var go = new GameObject(SpringName);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "创建弹簧平台");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = SpringColor;
            // 压在平台面（order −1）后面：两者的交叠处让平台面盖住柱子。
            renderer.sortingOrder = -2;

            return go;
        }

        private static void BuildPlank(Transform parent, Sprite sprite)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "创建弹簧平台");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少 —— 和碰撞箱同尺寸。
            go.transform.localScale = new Vector3(PlankSize.x, PlankSize.y, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 压在地面美术（order 1）和角色（order 0）后面：站上去时人是挡在平台前的。
            renderer.sortingOrder = -1;
        }

        private static TextMesh BuildLabel(Transform parent)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。");
                return null;
            }

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            // 根 scale 是 1，所以这里的局部坐标就是世界距离，不会被压扁。
            go.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建弹簧平台");

            var text = go.AddComponent<TextMesh>();
            text.text = "弹簧";
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null)
            {
                renderer.sharedMaterial = font.material;
            }
            if (renderer != null)
            {
                // 标签在白盒阶段要永远看得见，压在所有东西上面。
                renderer.sortingOrder = 100;
            }

            return text;
        }

        /// <summary>没开 Scene 视图时退回原点，不至于把物体扔到看不见的地方。</summary>
        private static Vector3 SceneViewPivot()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;

            Vector3 pivot = view.pivot;
            pivot.z = 0f;
            return pivot;
        }
    }
}
