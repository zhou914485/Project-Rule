using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**攻击延长平台**：玩家攻击后沿指定方向变长。
    /// </summary>
    /// <remarks>
    /// <para>白盒结构与平移平台一致：碰撞箱挂在根上（根 scale 保持 1，<c>size</c> 直接就是世界尺寸），
    /// 美术放 <c>Art</c> 子物体上撑缩放。**不要把缩放设在根上** —— 子物体会继承缩放，
    /// 标签会被横向拉长、纵向压扁，长度也会被缩放多乘一遍。</para>
    ///
    /// <para>默认朝右：攻击前 2.5 单位、攻击后 5.5 单位、厚 0.4。
    /// 生成后直接在 Inspector 里改「长度 / 延长方向」，Scene 视图会同时画出攻击前（灰蓝）
    /// 和攻击后（绿）两个框。</para>
    ///
    /// <para><b>默认保持 2 秒自动缩回</b>：长到最长后 2 秒自己缩回攻击前长度；
    /// 想让它一直保持，把「节奏 / 自动缩回」改成 0。</para>
    ///
    /// <para><b>默认「玩家攻击」触发</b>：玩家每一刀真正出手时就长；只想让被砍中的那个长，
    /// 在 Inspector 的「触发方式」里改成「打中平台」。</para>
    ///
    /// <para><b>落在 Ground 层</b>（层 8）：玩家的 <c>Slide</c> / 接地探测只认这个层，
    /// 放错层平台就是一块穿得过去的空气。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑰ 攻击延长平台。</para>
    /// </remarks>
    public static class AttackExtendedPlatformBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>实体落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "AttackExtendedPlatform";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>白盒默认长度（世界单位）。1 格 = 0.5 单位。</summary>
        private const float LengthBefore = 2.5f;
        private const float LengthAfter = 5.5f;
        private const float Thickness = 0.4f;

        /// <summary>长到最长后保持多久再自动缩回（秒）。0 = 一直保持。</summary>
        private const float RetractAfter = 2f;

        private static readonly Color ArtColor = new Color(0.62f, 0.42f, 0.92f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑰ 攻击延长平台（玩家攻击后变长）", false, 68)]
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，平台放下去玩家踩不到。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            root.layer = layer;
            Undo.RegisterCreatedObjectUndo(root, "创建攻击延长平台");

            var box = root.AddComponent<BoxCollider2D>();
            box.size = new Vector2(LengthBefore, Thickness);
            box.offset = Vector2.zero;

            // 组件要求有刚体；这里先配好，省得运行期才被组件改一遍。
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;

            var platform = root.AddComponent<AttackExtendedPlatform>();
            platform.Configure(LengthBefore, LengthAfter, Thickness,
                               AttackExtendedPlatform.ExtendDirection.Right);
            platform.SetAutoRetract(RetractAfter);

            Transform art = BuildArt(root.transform, sprite);
            TextMesh label = BuildLabel(root.transform);

            platform.SetWhitebox(art, label);
            EditorUtility.SetDirty(platform);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（层级 {SolidLayerName}）：" +
                      $"攻击前 {LengthBefore} → 攻击后 {LengthAfter} 单位，朝右。\n" +
                      $"· 长到最长后保持 {RetractAfter} 秒自动缩回（「节奏 / 自动缩回」改成 0 = 一直保持）\n" +
                      "· 默认「玩家攻击」触发：玩家每一刀真正出手时就长（场景里同模式的平台会一起长）\n" +
                      "· 只想让打中的那个长，在 Inspector 的「触发方式」里改成「打中平台」\n" +
                      "· 长度 / 方向 / 节奏都在 Inspector 里调");
        }

        /// <summary>
        /// 给当前打开的场景里**已有**的攻击延长平台统一打开自动缩回。
        /// </summary>
        /// <remarks>
        /// 老版本的字段默认值是 0（一直保持），已经摆进场景的实例会把这个 0 序列化下来；
        /// 只改代码默认值不会影响它们。这个菜单在原地改内存里的场景，可撤销，不碰磁盘文件。
        /// </remarks>
        [MenuItem("Tools/规则游戏/地图组件/⑰a 场景内攻击延长平台：自动缩回 2 秒", false, 69)]
        public static void ApplyAutoRetractToScene()
        {
            AttackExtendedPlatform[] platforms = Object.FindObjectsByType<AttackExtendedPlatform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (platforms.Length == 0)
            {
                Debug.Log("[地图组件] 当前场景里没有 AttackExtendedPlatform。");
                return;
            }

            for (int i = 0; i < platforms.Length; i++)
            {
                Undo.RecordObject(platforms[i], "设置攻击延长平台自动缩回");
                platforms[i].SetAutoRetract(RetractAfter);
                EditorUtility.SetDirty(platforms[i]);
            }

            Debug.Log($"[地图组件] 已把场景里 {platforms.Length} 个攻击延长平台的自动缩回设为 " +
                      $"{RetractAfter} 秒（可撤销）。想单独调某个平台，改它 Inspector 的「节奏 / 自动缩回」。");
        }

        /// <summary>白盒方块：1×1 单位的精灵，缩放到攻击前的尺寸。</summary>
        private static Transform BuildArt(Transform parent, Sprite sprite)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(LengthBefore, Thickness, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建攻击延长平台");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 压在地面美术（order 1）和角色（order 0）后面：站上去时人是挡在平台前的。
            renderer.sortingOrder = -1;

            return go.transform;
        }

        /// <summary>白盒标签：写在平台上方，跟着两个长度改文字。</summary>
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
            go.transform.localPosition = new Vector3(0f, Thickness * 0.5f + 0.35f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建攻击延长平台");

            var text = go.AddComponent<TextMesh>();
            text.text = AttackExtendedPlatform.LabelText(LengthBefore, LengthAfter);
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
