using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**白盒平移平台**（滑板车 / 升降台 / 巡逻踏板都是它）。
    /// </summary>
    /// <remarks>
    /// <para>白盒阶段用「方块 + 汉字」占位：<c>Art</c> 子物体是一张压扁的白盒方块，
    /// <c>Label</c> 子物体写着组件名，一眼能认出这坨东西是什么。</para>
    ///
    /// <para><b>层级为什么这么分</b>：碰撞箱挂在根上（根 scale 恒为 1，<c>size</c> 直接就是世界尺寸），
    /// 美术放 <c>Art</c> 子物体上撑缩放。**不要把缩放设在根上** ——
    /// 子物体会继承缩放，标签会被横向拉长、纵向压扁。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：玩家的接触探测只认这个层，
    /// 放错层平台就是一块穿不过去的空气。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/① 滑板车（平移平台）。
    /// 生成后会出现在 Scene 视图的中心，随后用 Inspector 上的按钮定起点 / 终点。</para>
    /// </remarks>
    public static class MovingPlatformBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "MovingPlatform";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>白盒尺寸（世界单位）。1 格 = 0.5 单位，所以 2.5 宽 = 5 格。</summary>
        private static readonly Vector2 Size = new Vector2(2.5f, 0.4f);

        /// <summary>默认路径长度：从落点往右 4 个单位。之后用 Inspector 上的按钮改。</summary>
        private const float DefaultRun = 4f;

        private static readonly Color ArtColor = new Color(0.98f, 0.78f, 0.32f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/① 滑板车（平移平台）", false, 50)]
        public static void CreateScooter()
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，平台放下去玩家踩不到。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建平移平台");

            BuildBody(root, sprite);
            BuildLabel(root.transform);

            var platform = root.AddComponent<MovingPlatform>();
            // 默认是待客模式：放下来先停在起点，玩家踩上去才出发。
            // 默认给一段可见的行程，省得忘了填终点；要站着不动就把终点拖到起点上。
            platform.SetPath(at, at + new Vector3(DefaultRun, 0f, 0f));
            EditorUtility.SetDirty(platform);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）。" +
                      $"用 Inspector 上的「起点 / 终点 = 当前位置」按钮定路径，速度在「节奏」里调。" +
                      $"当前行程 {DefaultRun} 个单位；默认**站上去才出发**，到终点后回起点停住。");
        }

        private static void BuildBody(GameObject root, Sprite sprite)
        {
            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            // MovingPlatform 要求有刚体；这里先配好，省得运行期才被组件改一遍。
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建平移平台");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少 —— 和碰撞箱同尺寸。
            art.transform.localScale = new Vector3(Size.x, Size.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 压在地面美术（order 1）和角色（order 0）后面：站上去时人是挡在平台前的。
            renderer.sortingOrder = -1;
        }

        private static void BuildLabel(Transform parent)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。");
                return;
            }

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            // 根 scale 是 1，所以这里的局部坐标就是世界距离，不会被压扁。
            go.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建平移平台");

            var text = go.AddComponent<TextMesh>();
            text.text = "滑板车";
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
