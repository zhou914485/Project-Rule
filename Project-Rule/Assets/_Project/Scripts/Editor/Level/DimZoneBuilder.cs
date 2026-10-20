using System;
using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一块「压暗区域」（<see cref="DimZone"/>）：世界空间的一层半透明黑，
    /// 只把矩形范围内的画面压暗，矩形外不受影响。
    /// </summary>
    /// <remarks>
    /// <para><b>层级为什么这么分</b>：根对象只放 <see cref="DimZone"/> 与矩形尺寸（scale 恒为 1），
    /// 真正画出来的 <c>DimOverlay</c> 是子物体 —— 换贴图、改颜色都不会碰到根。</para>
    /// <para>白盒阶段用 <c>P4WhiteboxSquare.png</c> 拉成矩形，和单向门 / 回弹块同一张图。</para>
    /// <para>生成后出现在 Scene 视图中心，尺寸与压暗程度在 Inspector 里调。</para>
    /// </remarks>
    public static class DimZoneBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string RootName = "DimZone";

        [MenuItem("Tools/规则游戏/视觉/① 暗区（区域压暗）", false, 1)]
        public static void Create()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null) throw new InvalidOperationException("缺少白盒方块贴图：" + SpritePath);

            var root = new GameObject(RootName);
            root.transform.position = SceneViewPivot();
            Undo.RegisterCreatedObjectUndo(root, "创建暗区");

            var overlay = new GameObject(DimZone.OverlayName);
            Undo.RegisterCreatedObjectUndo(overlay, "创建暗区");
            overlay.transform.SetParent(root.transform, false);
            overlay.transform.localPosition = Vector3.zero;

            var renderer = overlay.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            // 子物体先建好，DimZone 的 OnEnable 才找得到压暗层
            DimZone zone = root.AddComponent<DimZone>();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log("[视觉] 已创建 " + RootName + "（" + zone.Size.x + " × " + zone.Size.y +
                      " 米，以物体原点为中心）。尺寸 / 压暗程度在 Inspector 里调，Order 60 不盖角色。");
        }

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
