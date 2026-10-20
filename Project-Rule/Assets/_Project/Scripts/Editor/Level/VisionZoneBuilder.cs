using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一片「视野受限区」（<see cref="VisionZone"/>）：玩家走进矩形后，
    /// 画面只剩他身边一圈亮，其余全黑。
    /// </summary>
    /// <remarks>
    /// <para>黑幕是运行时生成的（不进场景文件），所以这里只摆一个带组件的空物体；
    /// 矩形范围与视野半径在 Inspector 里调，选中时 Gizmo 会画出矩形和参考圆。</para>
    /// <para>区域可以重叠，按 <c>Priority</c> 只让一块生效。</para>
    /// </remarks>
    public static class VisionZoneBuilder
    {
        private const string RootName = "VisionZone";

        [MenuItem("Tools/规则游戏/视觉/② 视野受限区（进区域只剩身边一圈）", false, 2)]
        public static void Create()
        {
            var root = new GameObject(RootName);
            root.transform.position = SceneViewPivot();
            Undo.RegisterCreatedObjectUndo(root, "创建视野受限区");

            VisionZone zone = root.AddComponent<VisionZone>();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log("[视觉] 已创建 " + RootName + "（" + zone.Size.x + " × " + zone.Size.y +
                      " 米，视野半径 " + zone.ViewRadius + " 米）。进入范围后其余画面全黑。");
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
