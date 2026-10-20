using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一扇**白盒单向可破坏门**：站到可破坏的一侧挥刀就碎，另一侧是一堵实心的墙。
    /// </summary>
    /// <remarks>
    /// <para>白盒阶段用「方块 + 汉字」占位，与 <c>OneWayGateBuilder</c> / <c>MovingPlatformBuilder</c>
    /// 同一套约定：<c>Art</c> 子物体是一块白盒方块，<c>Label</c> 写着 <c>破门 →</c> / <c>破门 ↑</c> ——
    /// 箭头就是「从哪一侧砍得开」。</para>
    ///
    /// <para><b>两个入口</b>：④ 竖板（水平方向砍开）、④b 横板（竖直方向砍开）——
    /// 与单向门的 ③ / ③b 一一对应，关卡里换着摆时形状不用另想。</para>
    ///
    /// <para><b>层级为什么这么分</b>：碰撞箱（<c>BoxCollider2D</c>）挂在根上，
    /// 根 scale 恒为 1、<c>size</c> 直接就是世界尺寸；美术放 <c>Art</c> 子物体上撑缩放。
    /// **不要把缩放设在根上** —— 子物体会继承缩放，标签会被横向拉长、纵向压扁。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：玩家的 <c>MotionTuning.SolidLayer</c> 只认这个层，
    /// 放错层这扇门就只是一块能穿过去的空气。</para>
    ///
    /// <para><b>不需要刚体</b>：门自己不动，就是一块静态碰撞体 —— 玩家侧的
    /// <c>Rigidbody2D.Slide</c> 查询照样扫得到它。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/④（竖板）或 ④b（横板）。生成后会出现在 Scene 视图中心，
    /// 方向在 Inspector 的「可破坏方向」里改 —— 改完白盒标签和 gizmo 箭头会一起跟着变。</para>
    /// </remarks>
    public static class BreakableGateBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "BreakableGate";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>水平砍开的白盒尺寸（世界单位）：竖板，1 格厚 × 8 格高。
        /// 和单向门同尺寸 —— 8 格高是为了让「从门顶上跳过去」没那么容易（水平门还是应该配天花板用）。</summary>
        internal static readonly Vector2 HorizontalSize = new Vector2(0.5f, 4f);

        /// <summary>竖直砍开的白盒尺寸（世界单位）：横板，8 格宽 × 1 格厚。</summary>
        internal static readonly Vector2 VerticalSize = new Vector2(4f, 0.5f);

        /// <summary>白盒方块的颜色。木色 —— 和蓝色的单向门、黄色的平移平台区分开。</summary>
        private static readonly Color ArtColor = new Color(0.62f, 0.40f, 0.24f, 1f);

        private static readonly Color LabelColor = new Color(0.08f, 0.10f, 0.22f, 1f);

        /// <summary>某个可破坏方向该用的白盒尺寸。</summary>
        internal static Vector2 SizeFor(OneWayGate.PassDirection pass)
        {
            return IsVertical(pass) ? VerticalSize : HorizontalSize;
        }

        /// <summary>白盒标签相对门中心的位置：横板放到板外右侧（别挡住站在板上的玩家），竖板写在门中间。</summary>
        internal static Vector3 LabelOffset(OneWayGate.PassDirection pass, Vector2 size)
        {
            return IsVertical(pass)
                ? new Vector3(size.x * 0.5f + 0.7f, 0f, 0f)
                : Vector3.zero;
        }

        internal static bool IsVertical(OneWayGate.PassDirection pass)
        {
            return pass == OneWayGate.PassDirection.ToUp || pass == OneWayGate.PassDirection.ToDown;
        }

        [MenuItem("Tools/规则游戏/地图组件/④ 单向可破坏门（水平 · 从侧面砍开）", false, 53)]
        public static void CreateHorizontalGate()
        {
            CreateGate(OneWayGate.PassDirection.ToRight, HorizontalSize,
                       "水平砍开：站在门的左侧挥刀就碎，从右侧怎么砍都不算。" +
                       "门要够高、或者上面有天花板，否则玩家能从门顶跳过去。");
        }

        [MenuItem("Tools/规则游戏/地图组件/④b 单向可破坏门（竖直 · 从下方砍开）", false, 53)]
        public static void CreateVerticalGate()
        {
            CreateGate(OneWayGate.PassDirection.ToUp, VerticalSize,
                       "竖直砍开：站在板子下方挥刀就碎，站到板上面砍不算。" +
                       "把方向改成「从上往下」就是一块只能从上方砍开的天花板。");
        }

        private static void CreateGate(OneWayGate.PassDirection pass, Vector2 size, string hint)
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，门放下去挡不住玩家。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;                    // 挡路的那块必须住在 Ground 层
            Undo.RegisterCreatedObjectUndo(root, "创建单向可破坏门");

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = size;
            box.offset = Vector2.zero;

            BreakableGate gate = root.AddComponent<BreakableGate>();
            gate.SetPass(pass);                    // 先定方向，标签文字跟着它走

            SpriteRenderer art = BuildArt(root, sprite, size);
            TextMesh label = BuildLabel(root.transform, pass, LabelOffset(pass, size));
            gate.SetWhitebox(art, label);
            EditorUtility.SetDirty(gate);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            string shape = IsVertical(pass) ? "横板" : "竖板";
            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）：{shape} {size.x} × {size.y} 单位，" +
                      $"**以物体原点为中心**。{hint}" +
                      " ⚠️ 只认玩家：站在可破坏的一侧**挥刀**才碎，走过去撞上去不会碎；" +
                      "敌人和子弹两个方向都被挡。");
        }

        private static SpriteRenderer BuildArt(GameObject root, Sprite sprite, Vector2 size)
        {
            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建单向可破坏门");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少 —— 和碰撞箱同尺寸。
            art.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 和平移平台 / 单向门一致：压在地面美术和角色后面，人从门里穿过去时是挡在门前的。
            renderer.sortingOrder = -1;
            return renderer;
        }

        private static TextMesh BuildLabel(Transform parent, OneWayGate.PassDirection pass, Vector3 offset)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。");
                return null;
            }

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            // 竖板写在门正中央；横板写到板外右侧 —— 箭头就是「从哪一侧砍得开」。
            go.transform.localPosition = offset;
            Undo.RegisterCreatedObjectUndo(go, "创建单向可破坏门");

            var text = go.AddComponent<TextMesh>();
            text.text = BreakableGate.LabelText(pass);
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
