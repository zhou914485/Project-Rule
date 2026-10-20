using System.Text;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 跷跷板的自检：**纯函数 + 预制体装配**，不需要进 Play 模式。
    /// </summary>
    /// <remarks>
    /// <para>杠杆最容易搞反的是「哪头翻」—— 反了的表现是「重的那头翘起来」，乍看像美术摆反了。
    /// 这里把质量 → 目标倾角这条判断直接钉死；规则交互（质量 × 反转）用**把质量换成倒数**的方式验，
    /// 不需要真的起规则系统 —— 组件读的就是 <c>Rigidbody2D.mass</c> 这一个值。</para>
    /// </remarks>
    public static class SeesawValidation
    {
        private static readonly float MaxAngle = 25f;
        private static readonly float Epsilon = 0.01f;

        [MenuItem("规则游戏/验证/跷跷板（预制体 + 纯函数）", false, 72)]
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
            //  ① 质量 → 目标倾角
            // ==============================================================
            sb.AppendLine("【1】哪头重就往哪头翻");

            float leftHeavy = Seesaw.ResolveTargetAngle(18f, 8f, Epsilon, MaxAngle);
            Check("左端 18 kg、右端 8 kg → 翻到 +25°（左端沉）",
                  Near(leftHeavy, MaxAngle), leftHeavy.ToString("0.##"));

            float rightHeavy = Seesaw.ResolveTargetAngle(8f, 18f, Epsilon, MaxAngle);
            Check("左端 8 kg、右端 18 kg → 翻到 −25°（右端沉）",
                  Near(rightHeavy, -MaxAngle), rightHeavy.ToString("0.##"));

            Check("两端一样重 → 回平（0°）",
                  Near(Seesaw.ResolveTargetAngle(10f, 10f, Epsilon, MaxAngle), 0f));

            Check("两端都空 → 回平（0°）",
                  Near(Seesaw.ResolveTargetAngle(0f, 0f, Epsilon, MaxAngle), 0f));

            Check("质量差在容差内 → 回平（不抖）",
                  Near(Seesaw.ResolveTargetAngle(10f, 10.005f, Epsilon, MaxAngle), 0f));

            Check("质量差刚好超过容差 → 翻过去",
                  Near(Seesaw.ResolveTargetAngle(10f, 10.02f, Epsilon, MaxAngle), -MaxAngle));

            // ==============================================================
            //  ② 质量 × 反转：轻重互换 → 跷跷板自己翻过来
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("【2】质量 × 反转 —— 本机关的核心玩法");

            float invertedLeft = 1f * 1f / 18f;    // RuleAffectedBody 的倒数写法
            float invertedRight = 1f * 1f / 8f;

            Check("反转后质数是倒数（18 → 0.056、8 → 0.125，轻的反而更重）",
                  invertedLeft < invertedRight,
                  invertedLeft.ToString("0.####") + " < " + invertedRight.ToString("0.####"));

            float afterInvert = Seesaw.ResolveTargetAngle(invertedLeft, invertedRight, Epsilon, MaxAngle);
            Check("原来左端 18 kg 沉（+25°），反转后翻到 −25°（右端沉）",
                  Near(leftHeavy, MaxAngle) && Near(afterInvert, -MaxAngle),
                  leftHeavy.ToString("0.##") + " → " + afterInvert.ToString("0.##"));

            // ==============================================================
            //  ③ 翻动速度
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("【3】翻动速度");

            float dt = 1f / 60f;
            float oneStep = Mathf.MoveTowards(0f, MaxAngle, 90f * dt);
            Check("90°/秒 → 一帧走 1.5°，约 17 帧（0.28 秒）翻到底",
                  Near(oneStep, 1.5f, 0.001f), oneStep.ToString("0.###"));

            float settled = 0f;
            int frames = 0;
            while (!Near(settled, MaxAngle) && frames < 1000)
            {
                settled = Mathf.MoveTowards(settled, MaxAngle, 90f * dt);
                frames++;
            }
            Check("约 17 帧翻到底，且不会过冲", frames == 17 && Near(settled, MaxAngle),
                  frames + " 帧");

            // ==============================================================
            //  ④ 骑手承载几何
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("【4】骑手承载 —— 板上的人跟着板一起转");

            Vector2 pivot = Vector2.zero;
            Vector2 move = Seesaw.RotationDisplacement(new Vector2(3f, 0f), pivot, 90f);
            Check("站在 +3 处、板转 +90° → 位移 (−3, +3)",
                  Near(move.x, -3f, 0.001f) && Near(move.y, 3f, 0.001f),
                  move.ToString("0.###"));

            Check("支点上的人转多少都不动",
                  Near(Seesaw.RotationDisplacement(pivot, pivot, 90f).sqrMagnitude, 0f));

            Vector2 after = new Vector2(3f, 0f) + move;
            float alongAxis = Vector2.Dot(after, Seesaw.AxisFor(90f));
            Check("旋转后的点仍落在板轴 +3 处（不漂移）",
                  Near(alongAxis, 3f, 0.001f), alongAxis.ToString("0.####"));

            // ==============================================================
            //  ⑤ 预制体装配
            // ==============================================================
            sb.AppendLine();
            sb.AppendLine("【5】预制体装配（结构 / 层 / 刚体 / 材质 / 接线）");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SeesawBuilder.PrefabPath);
            if (prefab == null)
            {
                Check("预制体存在（" + SeesawBuilder.PrefabPath + "）", false,
                      "先跑菜单 Tools/规则游戏/地图组件/⑰ 跷跷板");
            }
            else
            {
                Check("预制体存在（" + SeesawBuilder.PrefabPath + "）", true);

                int ground = LayerMask.NameToLayer("Ground");
                var seesaw = prefab.GetComponent<Seesaw>();
                Check("根上挂着 Seesaw 组件", seesaw != null);

                var board = prefab.transform.Find("Board");
                Check("子物体 Board 存在", board != null);

                if (board != null)
                {
                    Check("板住在 Ground 层（否则玩家穿过去）", board.gameObject.layer == ground,
                          "layer=" + LayerMask.LayerToName(board.gameObject.layer));

                    var solid = board.GetComponent<Collider2D>();
                    Check("板有实心碰撞体（非 Trigger）", solid != null && !solid.isTrigger);

                    var body = board.GetComponent<Rigidbody2D>();
                    Check("板是 Kinematic 刚体", body != null && body.bodyType == RigidbodyType2D.Kinematic,
                          body == null ? "没有刚体" : body.bodyType.ToString());

                    Check("板的 localPosition 是零（转自己 = 绕支点转）",
                          Near(((Vector2)board.localPosition).sqrMagnitude, 0f),
                          board.localPosition.ToString());

                    var box = solid as BoxCollider2D;
                    Check("碰撞体 size 保持 1×1（尺寸只在 localScale 上）",
                          box != null && Near(box.size.x, 1f, 0.001f) && Near(box.size.y, 1f, 0.001f),
                          box == null ? "不是 BoxCollider2D" : box.size.ToString());

                    Check("板的世界长度 ≈ 6（localScale 撑出来的）",
                          Near(board.lossyScale.x, 6f, 0.001f), board.lossyScale.x.ToString("0.###"));

                    var material = solid != null ? solid.sharedMaterial : null;
                    Check("板挂高摩擦材质（摩擦 1、Combine = Maximum —— 上面的东西不滑）",
                          material != null
                          && Near(material.friction, 1f, 0.001f)
                          && material.frictionCombine == PhysicsMaterialCombine2D.Maximum,
                          material == null ? "没有材质" :
                              "μ=" + material.friction + " combine=" + material.frictionCombine);
                }

                if (seesaw != null)
                {
                    var so = new SerializedObject(seesaw);
                    SerializedProperty wired = so.FindProperty("_board");
                    Check("Seesaw 的「板」字段已接线（指向 Board）",
                          wired != null && wired.objectReferenceValue is Transform wiredBoard
                          && board != null && wiredBoard == board,
                          wired == null ? "找不到 _board 字段" : "指向 " + wired.objectReferenceValue);
                }

                var pivotVisual = prefab.transform.Find("Pivot");
                Check("子物体 Pivot 存在（支点装饰）", pivotVisual != null);
                if (pivotVisual != null)
                {
                    Check("支点装饰没有碰撞体", pivotVisual.GetComponent<Collider2D>() == null);
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
