using UnityEditor;
using UnityEngine;
using RuleGame.Presentation.SceneComponents;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键放一条**世界空间文字提示**（按键教程 / 机关说明 / 路牌）。
    /// </summary>
    /// <remarks>
    /// <para>生成在 Scene 视图中心，默认排序 <c>−10</c>（背景层：近景背景 −20 之前、逻辑 −2 之后），
    /// 带一层阴影子物体，字体直接用工程里的 Noto Sans CJK。</para>
    ///
    /// <para><b>不生成预制体</b>：提示的内容与位置都是关卡专属的，和墙上的文字一样随场景走。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/提示/① 文字提示（背景层）。</para>
    /// </remarks>
    public static class WorldHintTextBuilder
    {
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";
        private const string RootName = "HintText";
        private const string SampleText = "按 A / D 移动　空格 跳跃";

        [MenuItem("Tools/规则游戏/提示/① 文字提示（背景层）", false, 70)]
        public static void CreateHint()
        {
            Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogError("[文字提示] 找不到中文字体：" + FontPath + "，无法生成。");
                return;
            }

            Vector3 at = SceneViewPivot();

            var root = new GameObject(RootName);
            root.transform.position = at;
            Undo.RegisterCreatedObjectUndo(root, "创建文字提示");

            // TextMesh 与组件必须在同一个物体上：组件按 GetComponent<TextMesh>() 取它。
            var mesh = root.AddComponent<TextMesh>();
            mesh.font = font;

            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer != null && font.material != null)
            {
                renderer.sharedMaterial = font.material;
            }

            var hint = root.AddComponent<WorldHintText>();
            hint.SetText(SampleText);
            hint.RefreshVisual();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);

            Debug.Log($"[文字提示] 已创建 {RootName}（Sorting Order −10，背景层）。" +
                      "想做成墙上的常驻教程：把「出现条件 → 触发半径」设成 0；" +
                      "只教一次就勾「只显示一次」。");
        }

        /// <summary>没开 Scene 视图时退回原点，不至于把物体扔到看不见的地方。</summary>
        private static Vector3 SceneViewPivot()
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }
    }
}
