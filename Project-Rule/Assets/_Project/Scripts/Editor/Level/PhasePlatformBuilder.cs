using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一套**虚实平台**：一块白盒平台 + 站在它顶面上的按钮，按钮已经接到「翻全部」的组上。
    /// </summary>
    /// <remarks>
    /// <para><b>玩法</b>：平台开局按 <see cref="PhasePlatform.StartsSolid"/> 是实化或虚化；
    /// 按一次按钮（E / 手柄 Y，也可以用攻击盒或纸飞机打），**隔 0.5 秒**整组平台一起翻面
    /// （延迟给玩家留出从平台上跳走的时间，见 <c>PhasePlatform._switchDelay</c>）。</para>
    ///
    /// <para><b>层级</b>（根 scale 恒为 1，尺寸写在碰撞体与美术的局部缩放上）：
    /// <code>
    /// PhasePlatforms              ← 根：挂 PhasePlatformGroup，所有按钮都指向它
    /// └─ PhasePlatform            ← Ground 层，BoxCollider2D + PhasePlatform
    ///     ├─ Art                  ← 白盒方块
    ///     ├─ Label                ← 「实化 / 虚化」
    ///     └─ PhaseButton          ← CityButton 实例，站在平台顶面上
    /// </code>
    /// 按钮是平台的**子物体**：复制平台时按钮跟着复制，烘焙好的 <c>FollowMotor</c>
    /// 仍然指向同一个组 —— 新平台只要点一下组 Inspector 的「从子物体收集平台」就接上了。</para>
    ///
    /// <para>按钮用积木城现成的 <c>Prefabs/BrickCity/CityButton.prefab</c>，只改三样：
    /// <c>Toggle = false</c>（点按式：每按一次由平台翻转一次，按完平台把按钮复位）、<c>FollowMotor = 组</c>、
    /// 位置摆到平台顶面。
    /// 按**任意一个**按钮都会翻**全部**平台（它们挂在同一个组上）。</para>
    ///
    /// <para>⚠️ <b>平台必须在 Ground 层</b>（层 8）：玩家的 <c>Slide</c> 与接地探测只认这个层，
    /// 放错层就是一块穿得过去的空气。</para>
    /// </remarks>
    public static class PhasePlatformBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string ButtonPrefabPath = "Assets/_Project/Prefabs/BrickCity/CityButton.prefab";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RigName = "PhasePlatforms";
        private const string PlatformName = "PhasePlatform";
        private const string ArtName = "Art";
        private const string LabelName = "Label";
        private const string ButtonName = "PhaseButton";

        /// <summary>白盒尺寸（世界单位）：4 × 0.5 ≈ 8 格宽 × 1 格厚。</summary>
        internal static readonly Vector2 PlatformSize = new Vector2(4f, 0.5f);

        /// <summary>CityButton 的碰撞体高 0.35 —— 按钮中心离平台顶面 0.175 才正好贴着顶面。</summary>
        private const float ButtonHalfHeight = 0.175f;

        /// <summary>白盒标签在平台顶面之上多高 —— 要让开站在顶面上的按钮。</summary>
        private const float LabelLift = 0.7f;

        private static readonly Color SolidTint = new Color(0.62f, 0.86f, 1f, 1f);
        private static readonly Color LabelColor = new Color(0.08f, 0.10f, 0.22f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑯ 虚实平台（按钮切换）", false, 68)]
        public static void CreateRig()
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，虚实平台既站不住也挡不了路。");
                return;
            }

            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[地图组件] 找不到中文字体 " + FontPath + "，白盒标签会显示不出来。");
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RigName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建虚实平台");

            var group = root.AddComponent<PhasePlatformGroup>();
            PhasePlatform platform = BuildPlatform(root.transform, sprite, font, layer);

            group.SetPlatforms(new[] { platform });
            EditorUtility.SetDirty(group);

            bool wired = BuildButton(platform.transform, group);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RigName}：平台 {PlatformSize.x} × {PlatformSize.y} 单位（开局实化），" +
                      "按钮站在平台顶面上。\n" +
                      (wired
                          ? "· 按钮已接好：**点按式**（Toggle 不勾，按完由平台复位，任何时刻都能再按），" +
                            "FollowMotor 指向 PhasePlatformGroup —— 按任意一块平台上的按钮，**整组**平台一起翻" +
                            "（按下后 0.5 秒才切换）\n"
                          : $"· ⚠️ 没找到 {ButtonPrefabPath}，请自己拖一个 CityButton 到平台顶面，" +
                            "**Toggle 不勾**、FollowMotor 指向 PhasePlatformGroup\n") +
                      "· 加更多平台：复制 PhasePlatform 子物体（按钮会一起复制、仍然指向同一个组），" +
                      "再点组 Inspector 的「从子物体收集平台」\n" +
                      $"· 平台必须在 {SolidLayerName} 层；开局实 / 虚在 PhasePlatform 的「初始状态」里改，" +
                      "切换延迟在「切换节奏」里改");
        }

        /// <summary>建一块白盒平台（Ground 层 + 碰撞体 + 白盒美术 / 标签），返回组件。</summary>
        private static PhasePlatform BuildPlatform(Transform parent, Sprite sprite, Font font, int layer)
        {
            var go = new GameObject(PlatformName);
            go.transform.SetParent(parent, false);
            go.layer = layer;
            Undo.RegisterCreatedObjectUndo(go, "创建虚实平台");

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = go.AddComponent<BoxCollider2D>();
            box.size = PlatformSize;
            box.offset = Vector2.zero;

            PhasePlatform platform = go.AddComponent<PhasePlatform>();

            // 白盒方块是 1×1 单位，缩放多少就是世界尺寸多少。
            var artGo = new GameObject(ArtName);
            artGo.transform.SetParent(go.transform, false);
            artGo.transform.localScale = new Vector3(PlatformSize.x, PlatformSize.y, 1f);
            Undo.RegisterCreatedObjectUndo(artGo, "创建虚实平台");

            var art = artGo.AddComponent<SpriteRenderer>();
            art.sprite = sprite;
            art.color = SolidTint;
            // 和平移平台一致：压在地面美术和角色后面。
            art.sortingOrder = -1;

            TextMesh label = BuildLabel(go.transform, font,
                                        PhasePlatform.LabelText(true),
                                        new Vector3(0f, PlatformSize.y * 0.5f + LabelLift, 0f));

            platform.SetWhitebox(art, label);
            platform.SetStartsSolid(true);
            EditorUtility.SetDirty(platform);
            EditorUtility.SetDirty(box);

            return platform;
        }

        /// <summary>把 CityButton 预制体实例摆在平台**顶面正中**，并把开关接到组上。返回是否接上。</summary>
        /// <remarks>按钮挂在平台下面（子物体）：复制平台时按钮跟着走，<c>FollowMotor</c> 也仍然指向同一个组。</remarks>
        private static bool BuildButton(Transform platform, PhasePlatformGroup group)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            if (asset == null)
            {
                Debug.LogError("[地图组件] 找不到按钮预制体：" + ButtonPrefabPath +
                               "。请自己拖一个 CityButton 到平台顶面并接好 FollowMotor。");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (instance == null) return false;

            Undo.RegisterCreatedObjectUndo(instance, "创建虚实平台按钮");
            instance.name = ButtonName;
            instance.transform.SetParent(platform, false);
            // 按钮碰撞体高 0.35：中心抬到「平台顶面 + 半高」就正好坐在顶面上。
            instance.transform.localPosition = new Vector3(0f, PlatformSize.y * 0.5f + ButtonHalfHeight, 0f);

            var trigger = instance.GetComponent<CitySwitch>();
            if (trigger == null)
            {
                Debug.LogError("[地图组件] " + ButtonPrefabPath + " 上没有 CitySwitch —— 按钮驱动不了平台。", instance);
                return false;
            }

            trigger.Toggle = false;                 // 点按式：每按一次由平台翻转一次，按完平台把按钮复位
            trigger.PressByPlayerProximity = false; // 交互键按（E / 手柄 Y）
            trigger.FollowMotor = group;            // 拖「组」，任意一个按钮都翻全部
            EditorUtility.SetDirty(trigger);

            return true;
        }

        private static TextMesh BuildLabel(Transform parent, Font font, string text, Vector3 localPosition)
        {
            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Undo.RegisterCreatedObjectUndo(go, "创建虚实平台");

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = 0.05f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = LabelColor;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (font != null && font.material != null) renderer.sharedMaterial = font.material;
                // 标签在白盒阶段要永远看得见，压在所有东西上面。
                renderer.sortingOrder = 100;
            }

            return mesh;
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
