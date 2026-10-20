using System.Text;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 危险跳绳的自检：**曲线几何 + 相位推进 + 预制体装配**，不需要进 Play 模式。
    /// </summary>
    /// <remarks>
    /// 绳子最容易搞反的是「哪边拱」—— 反了的表现是「垂度调大，绳子却往天上拱」。
    /// 这里把相位 → 曲线形状这条纯函数钉死；预制体部分检查端点、精灵、参数和「没有碰撞体」
    /// （有碰撞体的话绳子就变成一堵能站上去的墙，而不是陷阱）。
    /// </remarks>
    public static class SwingRopeValidation
    {
        private const float Span = 6f;
        private const float Sag = 2f;

        [MenuItem("规则游戏/验证/危险跳绳（预制体 + 纯函数）", false, 73)]
        public static void Run()
        {
            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            void Check(string label, bool ok, string detail = "")
            {
                sb.Append(ok ? "✓ " : "✗ ").Append(label);
                if (!ok && !string.IsNullOrEmpty(detail)) sb.Append(" —— ").Append(detail);
                sb.AppendLine();
                if (ok) pass++;
                else fail++;
            }

            // ==============================================================
            //  ① 曲线几何
            // ==============================================================
            sb.AppendLine("【1】曲线几何（垂度 / 端点 / 对称）");

            Vector3 left = SwingRope.PointOnCurve(0f, Span, Sag, 1f);
            Vector3 right = SwingRope.PointOnCurve(1f, Span, Sag, 1f);
            Check("t=0 落在左端（−span/2）",
                  Near(left.x, -Span * 0.5f) && Near(left.y, 0f), left.ToString("0.###"));
            Check("t=1 落在右端（+span/2）",
                  Near(right.x, Span * 0.5f) && Near(right.y, 0f), right.ToString("0.###"));
            Check("两端点距离 = span",
                  Near(right.x - left.x, Span), (right.x - left.x).ToString("0.###"));

            Vector3 bottom = SwingRope.PointOnCurve(0.5f, Span, Sag, 1f);
            Check("相位 0°：中点在下方，离中轴正好 sag",
                  Near(bottom.y, -Sag) && Near(bottom.x, 0f), bottom.ToString("0.###"));

            Vector3 top = SwingRope.PointOnCurve(0.5f, Span, Sag, -1f);
            Check("相位 180°：中点拱到上方，离中轴也是 sag",
                  Near(top.y, Sag) && Near(top.x, 0f), top.ToString("0.###"));

            bool flat = true;
            for (int i = 0; i <= 8; i++)
            {
                Vector3 p = SwingRope.PointOnCurve(i / 8f, Span, Sag, 0f);
                if (!Near(p.y, 0f)) flat = false;
            }
            Check("相位 90°：整条绳拉成直线（y 全为 0）", flat);

            Vector3 quarterLeft = SwingRope.PointOnCurve(0.25f, Span, Sag, 1f);
            Vector3 quarterRight = SwingRope.PointOnCurve(0.75f, Span, Sag, 1f);
            Check("曲线关于中点对称", Near(quarterLeft.y, quarterRight.y)
                  && Near(quarterLeft.x, -quarterRight.x),
                  quarterLeft.ToString("0.###") + " / " + quarterRight.ToString("0.###"));

            Check("垂度 0 → 永远是一条直线",
                  Near(SwingRope.PointOnCurve(0.5f, Span, 0f, 1f).y, 0f));

            // ==============================================================
            //  ② 相位推进（旋转速度）
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("【2】旋转速度");

            float dt = 1f / 60f;
            float perFrame = 180f * dt;
            Check("180°/秒 → 一帧走 3°", Near(perFrame, 3f), perFrame.ToString("0.###"));

            int frames = Mathf.RoundToInt(180f / perFrame);
            Check("180° 相位要走 60 帧（1 秒）", frames == 60, frames + " 帧");

            float phase = 0f;
            for (int i = 0; i < frames; i++) phase = Mathf.Repeat(phase + 180f * dt, 360f);
            Check("1 秒后相位 = 180°", Near(phase, 180f), phase.ToString("0.###"));

            float cosAfter = Mathf.Cos(phase * Mathf.Deg2Rad);
            Check("1 秒后绳子从垂下翻到上拱",
                  Near(SwingRope.PointOnCurve(0.5f, Span, Sag, cosAfter).y, Sag),
                  SwingRope.PointOnCurve(0.5f, Span, Sag, cosAfter).y.ToString("0.###"));

            // ==============================================================
            //  ③ 预制体装配
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("【3】预制体装配（端点 / 精灵 / 参数 / 没有碰撞体）");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwingRopeBuilder.PrefabPath);
            if (prefab == null)
            {
                Check("预制体存在（" + SwingRopeBuilder.PrefabPath + "）", false,
                      "先跑菜单 Tools/规则游戏/地图组件/⑱ 危险跳绳");

                SwingRopeBuilder.SavePrefab(SwingRopeBuilder.PrefabPath);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SwingRopeBuilder.PrefabPath);
                if (prefab != null) sb.AppendLine("  （已自动生成预制体，可以再跑一次本验证）");
            }
            else
            {
                Check("预制体存在（" + SwingRopeBuilder.PrefabPath + "）", true);
            }

            if (prefab != null)
            {
                var rope = prefab.GetComponent<SwingRope>();
                Check("根上挂着 SwingRope 组件", rope != null);

                Check("根上没有碰撞体（绳子是陷阱不是墙）",
                      prefab.GetComponent<Collider2D>() == null);

                Transform anchorA = prefab.transform.Find("AnchorA");
                Transform anchorB = prefab.transform.Find("AnchorB");
                Check("子物体 AnchorA / AnchorB 存在", anchorA != null && anchorB != null);

                if (anchorA != null && anchorB != null)
                {
                    Check("端点位置与默认跨度一致（±3 米）",
                          Near(anchorA.localPosition.x, -Span * 0.5f)
                          && Near(anchorB.localPosition.x, Span * 0.5f),
                          anchorA.localPosition + " / " + anchorB.localPosition);
                }

                if (rope != null)
                {
                    var so = new SerializedObject(rope);

                    SerializedProperty span = so.FindProperty("_span");
                    SerializedProperty sag = so.FindProperty("_sag");
                    SerializedProperty sprite = so.FindProperty("_segmentSprite");
                    SerializedProperty radius = so.FindProperty("_hitRadius");
                    SerializedProperty thickness = so.FindProperty("_thickness");
                    SerializedProperty anchors = so.FindProperty("_anchorA");

                    Check("默认两端点距离 = 6 米",
                          span != null && Near(span.floatValue, 6f),
                          span == null ? "找不到 _span" : span.floatValue.ToString("0.###"));
                    Check("默认垂度 = 2 米",
                          sag != null && Near(sag.floatValue, 2f),
                          sag == null ? "找不到 _sag" : sag.floatValue.ToString("0.###"));
                    Check("段用的精灵已接线",
                          sprite != null && sprite.objectReferenceValue is Sprite,
                          sprite == null ? "找不到 _segmentSprite" : "未接线");
                    Check("判定半径大于视觉粗细（手感留了余量）",
                          radius != null && thickness != null
                          && radius.floatValue > thickness.floatValue,
                          radius == null || thickness == null ? "字段缺失"
                              : "r=" + radius.floatValue + " 粗=" + thickness.floatValue);
                    Check("端点装饰已接线",
                          anchors != null && anchors.objectReferenceValue is Transform,
                          anchors == null ? "找不到 _anchorA" : "未接线");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"——— 通过 {pass} / 共 {pass + fail} ———");
            Debug.Log(sb.ToString());
        }

        private static bool Near(float a, float b, float tolerance = 0.0001f)
            => Mathf.Abs(a - b) <= tolerance;
    }
}
