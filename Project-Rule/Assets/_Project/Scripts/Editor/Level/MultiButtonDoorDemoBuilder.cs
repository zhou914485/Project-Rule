using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键摆一组**多按钮门**演示：2 个实体压力按钮 + 1 扇上滑门 + 1 个压按钮的铁箱。
    /// </summary>
    /// <remarks>
    /// <para>按钮检测所有带非静态 <c>Rigidbody2D</c> 的实体；地面 / Tilemap 没有刚体，
    /// 不会误触。铁箱是 Dynamic 刚体，所以能一直压住左边按钮；玩家站到右边按钮上，
    /// 两个按钮都按下 → 门向上滑开。</para>
    ///
    /// <para><b>纸飞机</b>：纸飞机没有碰撞体，不能靠重叠查询；它走
    /// <c>ThrownKnife</c> → <c>IContactSwitch.ImpactByContact</c> 的撞击入口，
    /// 按钮碰一下就锁成按下（默认永久，<c>_latchSeconds</c> 可改）。所以也可以朝右边按钮
    /// 扔纸飞机，配合左边铁箱把门保持开，人再走过去。</para>
    ///
    /// <para>按钮数量 = 门的 <c>Buttons</c> 数组长度。演示只放 2 个；要 3 个就再复制一个
    /// <c>Button_B</c>，拖进门的数组即可。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑲ 多按钮门（所有按钮按下才开）。</para>
    /// </remarks>
    public static class MultiButtonDoorDemoBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string CrateMaterialPath = "Assets/_Project/Data/Physics/CrateMaterial2D.asset";

        /// <summary>实体落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "MultiButtonDoorDemo";
        private const string DoorName = "Door";
        private const string ButtonAName = "Button_A";
        private const string ButtonBName = "Button_B";
        private const string CrateName = "Crate";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>门尺寸（世界单位）。默认门洞 1 宽、2.4 高。</summary>
        private static readonly Vector2 DoorSize = new Vector2(1f, 2.4f);

        /// <summary>关门时门中心的高度：底边在父物体 y = 0。</summary>
        private const float DoorCenterY = 1.2f;

        /// <summary>开门时向上滑的距离。</summary>
        private static readonly Vector2 DoorOpenOffset = new Vector2(0f, 2.8f);

        private const float DoorSpeed = 4f;

        /// <summary>按钮检测区：盖住按钮上方 0.6 米，玩家 / 箱子都能压到。</summary>
        private static readonly Vector2 ButtonAreaSize = new Vector2(1.4f, 0.6f);
        private static readonly Vector2 ButtonAreaOffset = new Vector2(0f, 0.3f);

        private static readonly Vector2 ButtonPlateSize = new Vector2(1.2f, 0.15f);

        /// <summary>两个按钮离门的距离。</summary>
        private const float ButtonX = 3f;

        private const float CrateMass = 8f;
        private static readonly Vector2 CrateSize = Vector2.one;

        private static readonly Color DoorColor = new Color(0.55f, 0.6f, 0.7f, 1f);
        private static readonly Color AColor = new Color(0.35f, 0.7f, 0.95f, 1f);
        private static readonly Color BColor = new Color(0.95f, 0.6f, 0.25f, 1f);
        private static readonly Color CrateColor = new Color(0.70f, 0.72f, 0.78f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑲ 多按钮门（所有按钮按下才开）", false, 71)]
        public static void Create()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return;
            }

            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，跳过标签。");
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，门和箱子都不会挡人。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建多按钮门演示");

            EntityPressureButton buttonA = BuildButton(
                root.transform, sprite, font, ButtonAName,
                new Vector3(-ButtonX, 0f, 0f), AColor, "按钮 A");

            EntityPressureButton buttonB = BuildButton(
                root.transform, sprite, font, ButtonBName,
                new Vector3(ButtonX, 0f, 0f), BColor, "按钮 B");

            MultiButtonDoor door = BuildDoor(root.transform, sprite, font, layer, out TextMesh doorLabel);
            door.Configure(new[] { buttonA, buttonB }, DoorOpenOffset, DoorSpeed, doorLabel);
            EditorUtility.SetDirty(door);

            // 左边按钮上压一个铁箱：Dynamic 刚体会一直把它按下去。
            BuildCrate(root.transform, sprite, font, layer,
                       new Vector3(-ButtonX, CrateSize.y * 0.5f, 0f));

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log("[地图组件] 已创建多按钮门演示：2 个按钮 + 1 扇上滑门 + 左边按钮上压了一个铁箱。\n" +
                      "· 把 MultiButtonDoorDemo 拖到地面上（门底边在父物体的 y = 0）\n" +
                      "· 站到右边按钮上 → 两个按钮都按下 → 门开；离开 / 推走铁箱 → 门关\n" +
                      "· 朝右边按钮扔纸飞机 / 用攻击碰一下 → 按钮锁成按下（默认永久），" +
                      "配合左边铁箱，门保持开，人能走过去\n" +
                      "· 压力检测所有带 Rigidbody2D 的非静态实体（玩家 / 敌人 / 铁箱 / 石头 / 巨石）；" +
                      "地面 / 地形没有刚体，不会误触\n" +
                      "· 按钮 Inspector 有「解除纸飞机锁定」；按钮数量 = Door 的「按钮」数组长度，自己加 / 删\n" +
                      "· 门关时如果玩家在门洞里，会保持打开等玩家出来（防夹；只对玩家）");
        }

        private static MultiButtonDoor BuildDoor(Transform parent, Sprite sprite, Font font, int layer,
                                                 out TextMesh label)
        {
            var go = new GameObject(DoorName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, DoorCenterY, 0f);
            go.layer = layer;
            Undo.RegisterCreatedObjectUndo(go, "创建多按钮门");

            var box = go.AddComponent<BoxCollider2D>();
            box.size = DoorSize;
            box.offset = Vector2.zero;

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            BuildArt(go.transform, sprite, DoorSize, DoorColor, -1);

            // 标签挂在父物体上（不跟着门往上跑），门在头顶时就看不见状态了。
            label = BuildLabel(parent, font, "门 · 关", new Vector3(0f, 2.7f, 0f));

            return go.AddComponent<MultiButtonDoor>();
        }

        private static EntityPressureButton BuildButton(Transform parent, Sprite sprite, Font font,
                                                        string name, Vector3 localPosition,
                                                        Color color, string labelPrefix)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Undo.RegisterCreatedObjectUndo(go, "创建压力按钮");

            // 只认攻击 / 身体的检测区；Trigger 不挡路。
            var area = go.AddComponent<BoxCollider2D>();
            area.size = ButtonAreaSize;
            area.offset = ButtonAreaOffset;
            area.isTrigger = true;

            SpriteRenderer plate = BuildArt(go.transform, sprite, ButtonPlateSize, color, -1);
            plate.transform.localPosition = new Vector3(0f, 0.08f, 0f);

            TextMesh label = BuildLabel(go.transform, font, labelPrefix, new Vector3(0f, 0.9f, 0f));

            var button = go.AddComponent<EntityPressureButton>();
            button.Configure(area, plate.transform, label, labelPrefix);
            return button;
        }

        private static void BuildCrate(Transform parent, Sprite sprite, Font font, int layer,
                                       Vector3 localPosition)
        {
            var go = new GameObject(CrateName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.layer = layer;
            Undo.RegisterCreatedObjectUndo(go, "创建铁箱");

            var box = go.AddComponent<BoxCollider2D>();
            box.size = CrateSize;
            box.offset = Vector2.zero;

            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(CrateMaterialPath);
            if (material != null)
            {
                box.sharedMaterial = material;
            }
            else
            {
                Debug.LogWarning("[地图组件] 找不到 " + CrateMaterialPath + "，铁箱会用默认摩擦。");
            }

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = CrateMass;
            body.gravityScale = 1f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            BuildArt(go.transform, sprite, CrateSize, CrateColor, -1);
            BuildLabel(go.transform, font, "铁箱", new Vector3(0f, 0.75f, 0f));

            // 两个组件一起挂：质量 / 摩擦规则 + 玩家推力。
            var affected = go.AddComponent<RuleAffectedBody>();
            go.AddComponent<PushableBody>();
            EditorUtility.SetDirty(affected);
        }

        private static SpriteRenderer BuildArt(Transform parent, Sprite sprite, Vector2 size,
                                               Color color, int sortingOrder)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建多按钮门演示");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static TextMesh BuildLabel(Transform parent, Font font, string text, Vector3 localPosition)
        {
            if (font == null) return null;

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Undo.RegisterCreatedObjectUndo(go, "创建多按钮门演示");

            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.font = font;
            label.fontSize = 64;
            label.characterSize = 0.05f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null)
            {
                renderer.sharedMaterial = font.material;
            }
            if (renderer != null)
            {
                renderer.sortingOrder = 100;
            }

            return label;
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
