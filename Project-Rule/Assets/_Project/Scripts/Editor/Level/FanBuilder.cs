using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**白盒风扇** —— 朝一个方向吹出一条风道（风场的实体载体）。
    /// </summary>
    /// <remarks>
    /// <para><b>层级</b>：碰撞体挂在根上（根 scale 恒为 1），美术与标签挂子物体 ——
    /// 与其余地图组件同一套，**不要把缩放设在根上**（标签会被拉扁）。</para>
    ///
    /// <para><b>碰撞体为什么不是机身那么大</b>：它是 <c>WindSensor</c> 的**查询入口**，
    /// 必须罩住整条风道（玩家在风道里才查得到这个组件）。创建后由
    /// <see cref="Fan"/> 按风道尺寸自动同步，Inspector 里改长度 / 宽度它会跟着变。</para>
    ///
    /// <para>⚠️ <b>必须落 Wind 层</b>：玩家的 <c>WindSensor</c> 只查这个层。这里会自动设好；
    /// 手动复制粘贴风扇时注意别丢。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/② 风扇（吹出风场）。</para>
    /// </remarks>
    public static class FanBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>风场层：玩家的 <c>MotionTuning.WindLayer</c> 必须指向它。</summary>
        private const string WindLayerName = "Wind";

        private const string RootName = "Fan";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>机身白盒尺寸（世界单位）。1 格 = 0.5 单位，所以这是 2×2 格。</summary>
        private static readonly Vector2 BodySize = new Vector2(1f, 1f);

        private static readonly Color ArtColor = new Color(0.62f, 0.78f, 0.88f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/② 风扇（吹出风场）", false, 51)]
        public static void CreateFan()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return;
            }

            int layer = LayerMask.NameToLayer(WindLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + WindLayerName + " 层，" +
                               "风扇放下去玩家的风场探测查不到 —— 等于没放。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建风扇");

            // 碰撞体是 WindSensor 的查询入口，尺寸交给 Fan 按风道同步（这里先摆个初值）。
            var area = root.AddComponent<BoxCollider2D>();
            area.isTrigger = true;

            SpriteRenderer body = BuildBody(root.transform, sprite);
            TextMesh label = BuildLabel(root.transform);

            var fan = root.AddComponent<Fan>();
            fan.SetWhitebox(body, label);
            EditorUtility.SetDirty(fan);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（{WindLayerName} 层，朝{WayText(fan.Direction)}吹）。" +
                      $"风道 {fan.Length:0.#} 长 × {fan.Width:0.#} 宽，" +
                      $"出风口风力 {fan.Force:0.#}N，梯度 {fan.ForceGradient:0.#} N/m，" +
                      $"阻力 {fan.Drag:0.#}，竖直阻尼 {fan.VerticalDamping:0.#}。\n" +
                      "· 调范围：改「风道长度 / 宽度」—— Scene 里的蓝框与碰撞体实时跟着变\n" +
                      "· 调间距：改「力的梯度」—— 相邻重力档的落点间距 = m·Δg ÷ 梯度（梯度越小分得越开）\n" +
                      "· 调阻尼：「空气阻力」水平竖直都收；「竖直阻尼」只收 y 速度 —— 人在风里上下弹就加它\n" +
                      "· 朝上吹时 Scene 里会画出 ×削弱 / 正常 / ×增强 三条平衡高度线（「预览」里可关）\n" +
                      "· 只想看风、不想看见风扇：关掉「白盒 → 显示本体」（风照常吹）\n" +
                      "⚠️ 风道下方要有地形：风是判定不是墙，被托出风道边缘的人会直接掉下去。");
        }

        /// <summary>机身白盒：一块占位方块。风道本身不铺美术，由 Gizmo 与碰撞体示意。</summary>
        /// <returns>机身的渲染器 —— 交给 <see cref="Fan.SetWhitebox"/>，「显示本体」开关靠它隐藏。</returns>
        private static SpriteRenderer BuildBody(Transform parent, Sprite sprite)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "创建风扇");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少。
            go.transform.localScale = new Vector3(BodySize.x, BodySize.y, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 与地形美术同层之下、角色之后：人从风扇前面走过。
            renderer.sortingOrder = -1;

            return renderer;
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
            // 根 scale 是 1，所以这里的局部坐标就是世界距离。
            go.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建风扇");

            var text = go.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (font.material != null) renderer.sharedMaterial = font.material;
                // 标签在白盒阶段要永远看得见，压在所有东西上面。
                renderer.sortingOrder = 100;
            }

            return text;
        }

        /// <summary>风向的中文说法 —— 与 <see cref="Fan.LabelText"/> 是同一套措辞。</summary>
        private static string WayText(Fan.BlowDirection direction)
        {
            switch (direction)
            {
                case Fan.BlowDirection.Right: return "右";
                case Fan.BlowDirection.Up:    return "上";
                case Fan.BlowDirection.Left:  return "左";
                default:                      return "下";
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
