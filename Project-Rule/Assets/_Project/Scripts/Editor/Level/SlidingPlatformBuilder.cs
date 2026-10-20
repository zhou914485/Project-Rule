using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一套**开关驱动的滑动平台**：白盒平台 + 一个已经接好 <c>FollowMotor</c> 的按钮。
    /// </summary>
    /// <remarks>
    /// <para><b>层级</b>（根 scale 恒为 1，尺寸写在碰撞体与美术的局部缩放上）：
    /// <code>
    /// SlidingPlatformRig          ← 根：只做分组
    /// ├─ SlidingPlatform          ← Ground 层，BoxCollider2D + Rigidbody2D + SlidingPlatform
    /// │   ├─ Art                  ← 白盒方块
    /// │   └─ Label                ← 「滑动平台」
    /// └─ SliderButton             ← CityButton 实例，Toggle = true，FollowMotor = SlidingPlatform
    /// </code>
    /// 按钮是**兄弟节点**，不会跟着平台滑走 —— 它要留在站台上反复按。</para>
    ///
    /// <para><b>行为</b>：按一下按钮 → 平台滑到终点；再按一下 → 滑回起点。
    /// 半路再按会就地掉头（位置跟开关状态走，见 <see cref="SlidingPlatform"/>）。</para>
    ///
    /// <para>⚠️ <b>平台必须在 Ground 层</b>（层 8）：玩家的 <c>Slide</c> 与接地探测只认这个层。</para>
    /// </remarks>
    public static class SlidingPlatformBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string ButtonPrefabPath = "Assets/_Project/Prefabs/BrickCity/CityButton.prefab";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RigName = "SlidingPlatformRig";
        private const string PlatformName = "SlidingPlatform";
        private const string ButtonName = "SliderButton";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>白盒尺寸（世界单位）。1 格 = 0.5 单位，所以 3 宽 = 6 格。</summary>
        private static readonly Vector2 Size = new Vector2(3f, 0.4f);

        /// <summary>默认行程：从落点往右 4 个单位。之后用 Inspector 上的按钮改。</summary>
        private const float DefaultRun = 4f;

        /// <summary>按钮中心离平台左端的横向距离。</summary>
        private const float ButtonLateralGap = 0.9f;

        /// <summary>CityButton 的碰撞体高 0.35 —— 中心离它脚下表面 0.175。</summary>
        private const float ButtonHalfHeight = 0.175f;

        private static readonly Color ArtColor = new Color(0.78f, 0.70f, 1f, 1f);
        private static readonly Color LabelColor = new Color(0.14f, 0.10f, 0.24f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/㉑ 滑动平台（按钮驱动）", false, 121)]
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，平台放下去玩家踩不到。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var rig = new GameObject(RigName);
            rig.transform.position = at;
            Undo.RegisterCreatedObjectUndo(rig, "创建滑动平台");

            SlidingPlatform platform = BuildPlatform(rig.transform, at, sprite, layer);
            bool wired = BuildButton(rig.transform, platform, at);

            Selection.activeGameObject = rig;
            EditorGUIUtility.PingObject(rig);

            Debug.Log($"[地图组件] 已创建 {RigName}：白盒平台 {Size.x} × {Size.y} 单位，" +
                      $"行程 {DefaultRun} 单位（起点 → 右方 {DefaultRun}）。\n" +
                      (wired
                          ? $"· 按钮已接好：Toggle 勾着（按一下滑过去、再按一下滑回来），" +
                            $"FollowMotor 指向 {PlatformName}\n"
                          : $"· ⚠️ 没找到 {ButtonPrefabPath}，请自己放一个 CityButton，" +
                            $"Toggle 勾着、FollowMotor 拖到 {PlatformName}\n") +
                      "· 按钮只是示意站位（在平台起点左侧），按关卡需要拖到地面 / 墙边\n" +
                      "· 起终点用 Inspector 上的「起点 / 终点 = 当前位置」按钮定，速度在「节奏」里调");
        }

        private static SlidingPlatform BuildPlatform(Transform parent, Vector3 at, Sprite sprite, int layer)
        {
            var go = new GameObject(PlatformName);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            go.layer = layer;
            Undo.RegisterCreatedObjectUndo(go, "创建滑动平台");

            // 碰撞箱挂在根上：根 scale 保持 1，box.size 写的就是世界尺寸。
            var box = go.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;

            // SlidingPlatform 要求有刚体；这里先配好，省得运行期才被组件改一遍。
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var platform = go.AddComponent<SlidingPlatform>();
            platform.SetPath(at, at + new Vector3(DefaultRun, 0f, 0f));
            EditorUtility.SetDirty(platform);

            var art = new GameObject(ArtName);
            art.transform.SetParent(go.transform, false);
            Undo.RegisterCreatedObjectUndo(art, "创建滑动平台");
            // 方块精灵是 1×1 单位，缩放多少就是世界尺寸多少。
            art.transform.localScale = new Vector3(Size.x, Size.y, 1f);

            var renderer = art.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 压在地面美术（order 1）和角色（order 0）后面。
            renderer.sortingOrder = -1;

            BuildLabel(go.transform);
            return platform;
        }

        /// <summary>把 CityButton 实例摆在起点左侧，并把开关接到平台上。返回是否接上。</summary>
        private static bool BuildButton(Transform parent, SlidingPlatform platform, Vector3 at)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            if (asset == null)
            {
                Debug.LogError("[地图组件] 找不到按钮预制体：" + ButtonPrefabPath +
                               "。请自己拖一个 CityButton 并接好 FollowMotor。");
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (instance == null) return false;

            Undo.RegisterCreatedObjectUndo(instance, "创建滑动平台按钮");
            instance.name = ButtonName;
            instance.transform.SetParent(parent, false);

            // 摆在起点左侧、与平台顶面同高。位置只是示意 —— 关卡里按需要拖到地面 / 墙边。
            instance.transform.position = new Vector3(
                at.x - Size.x * 0.5f - ButtonLateralGap,
                at.y + Size.y * 0.5f + ButtonHalfHeight,
                at.z);

            var trigger = instance.GetComponent<CitySwitch>();
            if (trigger == null)
            {
                Debug.LogError("[地图组件] " + ButtonPrefabPath + " 上没有 CitySwitch —— 按钮驱动不了平台。", instance);
                return false;
            }

            // Toggle = true：按一下开、再按一下关 —— 平台跟着来回滑。
            trigger.Toggle = true;
            trigger.PressByPlayerProximity = false;   // 交互键按（E / 手柄 Y）
            trigger.FollowMotor = platform;
            EditorUtility.SetDirty(trigger);

            return true;
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
            Undo.RegisterCreatedObjectUndo(go, "创建滑动平台");

            var text = go.AddComponent<TextMesh>();
            text.text = "滑动平台";
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
