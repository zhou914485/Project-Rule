using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**纸飞机炮筒**：每隔一段时间从红色炮口射出一架 <see cref="ThrownKnife"/>。
    /// </summary>
    /// <remarks>
    /// <para>白盒结构：根上是实心碰撞箱（Ground 层，玩家可以站上去），
    /// <c>Art</c> 是炮身，<c>Muzzle</c> 是炮口（红色小方块，方向 = 它的 +X），
    /// <c>Label</c> 是白盒标签。旋转炮筒就能瞄准。</para>
    ///
    /// <para>生成时会自动把 <c>Assets/_Project/Prefabs/PaperPlaneKnife.prefab</c> 接进
    /// 「发射物」，并把场景里找到的 <c>CombatSystem</c> 接进「伤害出口」；
    /// 场景里没有就留空，运行时会自己找。</para>
    ///
    /// <para>默认：速度 16（5.2 的 2 档）、间隔 1 秒、最多同时 24 架。
    /// 用法：菜单 Tools/规则游戏/地图组件/⑰ 纸飞机炮筒（持续发射纸飞机）。</para>
    /// </remarks>
    public static class PaperPlaneCannonBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string ProjectilePath = "Assets/_Project/Prefabs/PaperPlaneKnife.prefab";

        /// <summary>实体落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "PaperPlaneCannon";
        private const string ArtName = "Art";
        private const string MuzzleName = "Muzzle";
        private const string LabelName = "Label";

        /// <summary>炮身尺寸（世界单位）。</summary>
        private static readonly Vector2 Size = new Vector2(1.2f, 0.8f);

        /// <summary>炮口相对根物体的偏移（世界单位）。在碰撞箱外面一点，纸飞机不会一出膛就撞炮身。</summary>
        private const float MuzzleOffset = 0.75f;

        private const float MuzzleMarkerSize = 0.16f;

        private const float DefaultSpeed = 16f;
        private const float DefaultInterval = 1f;
        private const float DefaultStartDelay = 0.5f;
        private const int DefaultMaxAlive = 24;

        private static readonly Color ArtColor = new Color(0.58f, 0.42f, 0.28f, 1f);
        private static readonly Color MuzzleColor = new Color(0.95f, 0.25f, 0.2f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑰ 纸飞机炮筒（持续发射纸飞机）", false, 69)]
        public static void Create()
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，炮筒放下去玩家会直接穿过去。");
                return;
            }

            GameObject projectileAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
            ThrownKnife projectile = projectileAsset != null ? projectileAsset.GetComponent<ThrownKnife>() : null;
            if (projectile == null)
            {
                Debug.LogError("[地图组件] 找不到纸飞机预制体（或它身上没有 ThrownKnife）：" + ProjectilePath);
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建纸飞机炮筒");

            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            var cannon = root.AddComponent<PaperPlaneCannon>();

            BuildArt(root.transform, sprite);
            Transform muzzle = BuildMuzzle(root.transform, sprite);
            BuildLabel(root.transform);

            // 场景里已经有一个 CombatSystem 就顺手接上；没有就留空，运行时自己找。
            CombatSystem combat = UnityEngine.Object.FindAnyObjectByType<CombatSystem>();

            cannon.Configure(projectile, muzzle, combat,
                             DefaultSpeed, DefaultInterval, DefaultStartDelay, DefaultMaxAlive);
            EditorUtility.SetDirty(cannon);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）：" +
                      $"速度 {DefaultSpeed}、间隔 {DefaultInterval} 秒、最多 {DefaultMaxAlive} 架。" +
                      "红色小方块 = 炮口，旋转炮筒就能瞄准；长度 / 速度 / 间隔都在 Inspector 里调。" +
                      (combat != null ? "CombatSystem 已自动接上。" : "场景里没有 CombatSystem，运行时自动找。"));
        }

        /// <summary>炮身：白盒方块，压在角色后面。</summary>
        private static void BuildArt(Transform parent, Sprite sprite)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(Size.x, Size.y, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建纸飞机炮筒");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 压在地面美术（order 1）和角色（order 0）后面：站上去时人是挡在炮筒前的。
            renderer.sortingOrder = -1;
        }

        /// <summary>
        /// 炮口：位置是出生点，+X 是发射方向。红色小方块只是白盒标记，不参与碰撞。
        /// </summary>
        private static Transform BuildMuzzle(Transform parent, Sprite sprite)
        {
            var go = new GameObject(MuzzleName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(MuzzleOffset, 0f, 0f);
            go.transform.localScale = new Vector3(MuzzleMarkerSize, MuzzleMarkerSize, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建纸飞机炮筒");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = MuzzleColor;
            // 白盒阶段要一眼看到炮口，压在所有东西上面。
            renderer.sortingOrder = 100;

            return go.transform;
        }

        /// <summary>白盒标签。</summary>
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
            go.transform.localPosition = new Vector3(0f, Size.y * 0.5f + 0.35f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建纸飞机炮筒");

            var text = go.AddComponent<TextMesh>();
            text.text = "纸飞机炮筒";
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
