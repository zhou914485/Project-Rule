using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键摆一组**虚实平台**演示：A / B 两块平台 + 中间一个受击切换开关。
    /// </summary>
    /// <remarks>
    /// <para>全局参数 <see cref="PlatformSolidityState.AIsSolid"/> 默认 1：A 有实体、不透明；
    /// B 无碰撞、半透明。攻击中间的开关后变成 0：A 虚化、B 实体。</para>
    ///
    /// <para>平台底边在父物体的 y = 0；把 <c>PlatformSolidityDemo</c> 拖到地面上即可。
    /// 开关触发区盖住胸口高度（轻击 / 重击 / 纸飞机都打得到）。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑱ 虚实平台（A/B 全局切换 + 受击开关）。</para>
    /// </remarks>
    public static class PlatformSolidityDemoBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        /// <summary>实体落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "PlatformSolidityDemo";
        private const string PlatformAName = "Platform_A";
        private const string PlatformBName = "Platform_B";
        private const string SwitchName = "PlatformSoliditySwitch";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        /// <summary>平台尺寸（世界单位）。</summary>
        private static readonly Vector2 PlatformSize = new Vector2(2.5f, 0.4f);

        /// <summary>平台中心离父物体底面的高度（半个厚度，让底边落在 y = 0）。</summary>
        private const float PlatformCenterY = 0.2f;

        /// <summary>A / B 的中心左右偏移。</summary>
        private const float PlatformX = 2.5f;

        /// <summary>开关中心离底面的高度。</summary>
        private const float SwitchY = 1.2f;

        /// <summary>开关触发区：盖住胸口高度，攻击盒 / 纸飞机都能扫到。</summary>
        private static readonly Vector2 SwitchTriggerSize = new Vector2(1.6f, 1.8f);

        /// <summary>开关柱：从地面（父物体 y = 0）一直立到胸口以上。</summary>
        private static readonly Vector2 SwitchPostSize = new Vector2(0.35f, 2f);

        private static readonly Color AColor = new Color(0.35f, 0.7f, 0.95f, 1f);
        private static readonly Color BColor = new Color(0.95f, 0.6f, 0.25f, 1f);
        private static readonly Color SwitchColor = new Color(0.55f, 0.55f, 0.65f, 1f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑱ 虚实平台（A/B 全局切换 + 受击开关）", false, 70)]
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
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，平台放下去玩家会直接穿过去。");
                return;
            }

            // 演示从 1 开始：A 实体 / B 虚化。
            PlatformSolidityState.Set(true);

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建虚实平台演示");

            BuildPlatform(root.transform, sprite, font, layer, PlatformAName,
                          new Vector3(-PlatformX, PlatformCenterY, 0f),
                          PlatformSolidityMember.Role.A, AColor);

            BuildPlatform(root.transform, sprite, font, layer, PlatformBName,
                          new Vector3(PlatformX, PlatformCenterY, 0f),
                          PlatformSolidityMember.Role.B, BColor);

            BuildSwitch(root.transform, sprite, font);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log("[地图组件] 已创建虚实平台演示：A 实体 / B 虚化（全局 = 1）。\n" +
                      "· 把 PlatformSolidityDemo 拖到地面上（平台底边在父物体的 y = 0）\n" +
                      "· 攻击中间开关切换 A/B：触发区盖住胸口高度，轻击 / 重击 / 纸飞机都行\n" +
                      "· 平台变实体前会检查玩家是否卡在里面，卡住就保持虚化等玩家出来（防卡死）\n" +
                      "· 已有平台可单独挂 PlatformSolidityMember：Role 选 A/B，碰撞体 / 精灵留空会自动收集");
        }

        private static void BuildPlatform(Transform parent, Sprite sprite, Font font, int layer,
                                          string name, Vector3 localPosition,
                                          PlatformSolidityMember.Role role, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.layer = layer;
            Undo.RegisterCreatedObjectUndo(go, "创建虚实平台");

            var box = go.AddComponent<BoxCollider2D>();
            box.size = PlatformSize;
            box.offset = Vector2.zero;

            SpriteRenderer art = BuildArt(go.transform, sprite, PlatformSize, color, -1);
            TextMesh label = BuildLabel(go.transform, font,
                role == PlatformSolidityMember.Role.A ? "A" : "B",
                new Vector3(0f, PlatformSize.y * 0.5f + 0.35f, 0f));

            var member = go.AddComponent<PlatformSolidityMember>();
            member.Configure(role, box, art, label);
        }

        private static void BuildSwitch(Transform parent, Sprite sprite, Font font)
        {
            var go = new GameObject(SwitchName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, SwitchY, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建虚实开关");

            // 触发区：只认攻击，不挡路。攻击盒在胸口高度，所以触发区要盖住身体。
            var trigger = go.AddComponent<BoxCollider2D>();
            trigger.size = SwitchTriggerSize;
            trigger.offset = Vector2.zero;
            trigger.isTrigger = true;

            SpriteRenderer art = BuildArt(go.transform, sprite, SwitchPostSize, SwitchColor, 1);
            // 开关根在 y = 1.2；柱子中心下移 0.2，底边正好落在父物体的 y = 0。
            art.transform.localPosition = new Vector3(0f, -0.2f, 0f);

            TextMesh label = BuildLabel(go.transform, font, "A 实体 / B 虚化",
                                        new Vector3(0f, 0.9f, 0f));

            var toggle = go.AddComponent<PlatformSoliditySwitch>();
            toggle.Configure(label, art, true);
        }

        private static SpriteRenderer BuildArt(Transform parent, Sprite sprite, Vector2 size,
                                               Color color, int sortingOrder)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建虚实平台");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            // 平台压在角色后面；开关灯在 1（角色 0），确保看得见。
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static TextMesh BuildLabel(Transform parent, Font font, string text, Vector3 localPosition)
        {
            if (font == null) return null;

            var go = new GameObject(LabelName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            Undo.RegisterCreatedObjectUndo(go, "创建虚实平台");

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
                // 标签在白盒阶段要永远看得见，压在所有东西上面。
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
