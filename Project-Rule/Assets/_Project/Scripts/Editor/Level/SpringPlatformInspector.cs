using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="SpringPlatform"/> 的 Inspector：把「这根弹簧在常见载重下压多深」当场算给策划看。
    /// </summary>
    /// <remarks>
    /// 摆弹簧只要**拖动物体** —— 锚点 = 摆放位置往下「自然长度」，跟着一起搬。
    /// 不需要再单独摆一个底部固定点。
    /// </remarks>
    [CustomEditor(typeof(SpringPlatform))]
    public sealed class SpringPlatformInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var platform = (SpringPlatform)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("弹簧", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("自然长度", $"{platform.RestLength:0.###} 米");
            EditorGUILayout.LabelField("压缩长度", $"{platform.CompressedLength:0.###} 米");
            EditorGUILayout.LabelField("行程", $"{platform.Travel:0.###} 米");
            EditorGUILayout.LabelField("锚点（世界坐标）",
                Application.isPlaying
                    ? platform.AnchorPoint.ToString("0.###")
                    : ((Vector2)platform.transform.position - Vector2.up * platform.RestLength).ToString("0.###"));

            SerializedProperty k = serializedObject.FindProperty("_springConstant");
            serializedObject.Update();
            if (k != null && k.floatValue > 0f)
            {
                // 无阻尼时「从一端到另一端」是半个周期 ≈ π/ω，ω = √(k/m)。实际有阻尼会稍慢一点。
                float settle = Mathf.PI / Mathf.Sqrt(k.floatValue);
                EditorGUILayout.LabelField("1 kg 压到底耗时", $"≈ {settle:0.##} 秒（无阻尼近似）");

                // 胡克定律在编辑器里的预览：常见载重各压多深。
                EditorGUILayout.LabelField("玩家 1 kg 压入", $"{platform.EquilibriumSqueeze(1f):0.###} 米");
                EditorGUILayout.LabelField("铁箱 8 kg 压入", $"{platform.EquilibriumSqueeze(8f):0.###} 米");
            }

            if (platform.Travel <= 0f)
            {
                EditorGUILayout.HelpBox("行程为 0：自然长度和压缩长度一样，这根弹簧不会动。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"压缩深度按胡克定律：x = 载重·g / k，夹在 0 ~ {platform.Travel:0.###} 米。\n" +
                    "k 调大压得浅、调小压得深；重力规则会改 g，所以同样的东西在不同规则下压得不一样深。",
                    MessageType.Info);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("阶段", platform.PhaseName);
                EditorGUILayout.LabelField("载重", $"{platform.LoadMass:0.###} kg");
                EditorGUILayout.LabelField("压缩深度", $"{platform.Squeeze:0.###} 米");
                EditorGUILayout.LabelField("压缩量", $"{platform.Compression:0.###}");

                // 让这些数字在 play 中实时刷新（OnInspectorGUI 本身不会每帧自动重绘）。
                Repaint();
            }
        }
    }
}
