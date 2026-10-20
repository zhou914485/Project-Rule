using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一个**玩家触碰消失器**：玩家碰到后，自己和一个「目标」物件一起消失。
    /// </summary>
    /// <remarks>
    /// <para>生成一个 1×1 的 Trigger + 半透明白盒标记；「目标」留空 —— 生成后把场景里
    /// 要让其消失的物件（门 / 平台 / 道具）拖进 Inspector 的「目标」即可。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/㉑ 玩家触碰消失（同时让目标消失）。</para>
    /// </remarks>
    public static class PlayerTouchDisappearBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        private const string RootName = "PlayerTouchDisappear";
        private const string ArtName = "Art";
        private const string LabelName = "Label";

        private static readonly Vector2 Size = Vector2.one;
        private static readonly Color ArtColor = new Color(0.3f, 0.9f, 0.9f, 0.55f);
        private static readonly Color LabelColor = new Color(0.12f, 0.11f, 0.20f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/㉑ 玩家触碰消失（同时让目标消失）", false, 72)]
        public static void Create()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建玩家触碰消失器");

            // 检测用 Trigger：玩家可以穿过去，不会挡路。
            var box = root.AddComponent<BoxCollider2D>();
            box.size = Size;
            box.offset = Vector2.zero;
            box.isTrigger = true;

            BuildArt(root.transform, sprite);
            BuildLabel(root.transform);

            // 目标留空：用户自己把要让其消失的物件拖进 Target。
            root.AddComponent<PlayerTouchDisappear>();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[地图组件] 已创建 {RootName}（1×1 Trigger）。" +
                      "把要让其消失的物件从场景拖进 Inspector 的「目标 / Object To Disappear」。\n" +
                      "· 玩家身体碰到 → 自己 + 目标一起消失（默认 SetActive(false)，可恢复）\n" +
                      "· 目标留空 = 只消失自己\n" +
                      "· 只想让玩家触发；敌人 / 箱子 / 纸飞机碰到不算");
        }

        private static void BuildArt(Transform parent, Sprite sprite)
        {
            var go = new GameObject(ArtName);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(Size.x, Size.y, 1f);
            Undo.RegisterCreatedObjectUndo(go, "创建玩家触碰消失器");

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ArtColor;
            // 半透明标记放在角色前面一点，方便关卡里看见。
            renderer.sortingOrder = 5;
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
            go.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            Undo.RegisterCreatedObjectUndo(go, "创建玩家触碰消失器");

            var text = go.AddComponent<TextMesh>();
            text.text = "触碰消失";
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
