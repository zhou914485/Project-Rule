using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**白盒激光发射块**：沿水平轨道往返移动，并从块身上射出一条激光（默认朝下）。
    /// </summary>
    /// <remarks>
    /// <para><b>层级为什么这么分</b>：碰撞箱挂在根上（根 scale 恒为 1，<c>size</c> 直接就是世界尺寸），
    /// 美术放 <c>Art</c> 子物体上撑缩放，激光束放 <c>Beam</c> 子物体上由组件按长度拉伸。
    /// **不要把缩放设在根上** —— 子物体会继承缩放，标签会被拉长、激光的伸缩也会被再乘一次。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：块是一块能站、能挡路的实心障碍，
    /// 玩家的 <c>Slide</c> 与接地探测只认这个层。激光本身没有碰撞体，只做判定。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑳ 激光发射块（水平移动 + 激光）。
    /// 生成后用 Inspector 上的「起点 / 终点 = 当前位置」按钮定轨道。</para>
    /// </remarks>
    public static class LaserBlockBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "LaserBlock";
        private const string ArtName = "Art";
        private const string BeamName = "Beam";
        private const string GlowName = "Glow";
        private const string LabelName = "Label";

        /// <summary>白盒块尺寸（世界单位）。1 格 = 0.5 单位，所以 0.8 = 1.6 格。</summary>
        private static readonly Vector2 BlockSize = new Vector2(0.8f, 0.8f);

        /// <summary>默认轨道长度：从落点往右 4 个单位。之后用 Inspector 上的按钮改。</summary>
        private const float DefaultRun = 4f;

        private static readonly Color ArtColor     = new Color(0.55f, 0.36f, 0.78f, 1f);
        private static readonly Color BeamColor    = new Color(1f, 0.28f, 0.30f, 1f);
        private static readonly Color GlowColor    = new Color(1f, 0.35f, 0.35f, 0.22f);
        private static readonly Color LabelColor   = new Color(0.12f, 0.11f, 0.20f, 1f);

        /// <summary>激光外圈相对内芯的宽度倍数（Glow 是 Beam 的子物体，只改自己的 y 缩放）。</summary>
        private const float GlowWidthScale = 2.8f;

        [MenuItem("Tools/规则游戏/地图组件/⑳ 激光发射块（水平移动 + 激光）", false, 72)]
        public static void CreateLaserBlock()
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，激光块放下去玩家会穿过去。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建激光块");

            BuildBody(root, sprite);
            Transform beam = BuildBeam(root.transform, sprite);
            BuildLabel(root.transform);

            var block = root.AddComponent<LaserBlock>();
            block.SetStopLayers(1 << layer);
            block.SetWhitebox(beam);
            // 默认给一段可见的行程，省得忘了填终点；要让块原地不动就把终点拖到起点上。
            block.SetPath(at.x, at.x + DefaultRun);
            EditorUtility.SetDirty(block);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）。" +
                      $"默认**朝下**发射、长度 8，行程 {DefaultRun} 个单位。\n" +
                      "用 Inspector 上的「起点 / 终点 = 当前位置」定轨道，速度、激光长度 / 方向、伤害都在 Inspector 里调。");
        }

        private static void BuildBody(GameObject root, Sprite sprite)
        {
            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = BlockSize;
            box.offset = Vector2.zero;

            // LaserBlock 要求有刚体；这里先配好，省得运行期才被组件改一遍。
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var art = new GameObject(ArtName);
            art.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建激光块");
            art.transform.localScale = new Vector3(BlockSize.x, BlockSize.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 与平台一样压在角色后面。
            renderer.sortingOrder = -1;
        }

        /// <summary>激光束：内芯 <c>Beam</c> 由组件拉伸，外圈 <c>Glow</c> 是它的子物体。</summary>
        private static Transform BuildBeam(Transform parent, Sprite sprite)
        {
            var beam = new GameObject(BeamName);
            beam.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(beam, "创建激光块");

            var core = beam.AddComponent<SpriteRenderer>();
            core.sprite = sprite;
            core.color = BeamColor;
            // 激光是危险物，画在角色前面。
            core.sortingOrder = 3;

            var glow = new GameObject(GlowName);
            glow.transform.SetParent(beam.transform, false);
            Undo.RegisterCreatedObjectUndo(glow, "创建激光块");
            // Beam 的 x 缩放 = 激光长度，Glow 只在自己的 y 上放大 —— 于是外圈和激光等长、更粗。
            glow.transform.localScale = new Vector3(1f, GlowWidthScale, 1f);

            var glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = sprite;
            glowRenderer.color = GlowColor;
            glowRenderer.sortingOrder = 2;

            return beam.transform;
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
            go.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建激光块");

            var text = go.AddComponent<TextMesh>();
            text.text = "激光";
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
