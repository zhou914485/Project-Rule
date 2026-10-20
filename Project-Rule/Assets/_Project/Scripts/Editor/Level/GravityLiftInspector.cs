using UnityEditor;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="GravityLift"/> 的 Inspector：摆档位用几个按钮，省得手敲本地 y。
    /// </summary>
    /// <remarks>
    /// 摆轨道的常规流程：选中轨道 → 在 Scene 里把滑块拖到**常态**该在的高度
    /// → 点「中档 = 滑块当前位置」→ 再填「每档升 / 每档沉」。
    /// 五个「滑块移到…」按钮用来跳过去对位置（最底档 → 最高档各看一眼）。
    /// </remarks>
    [CustomEditor(typeof(GravityLift))]
    public sealed class GravityLiftInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var lift = (GravityLift)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("档位工具", EditorStyles.boldLabel);

            if (GUILayout.Button("中档 = 滑块当前位置"))
            {
                Undo.RecordObject(lift, "设置滑轨中档");
                lift.SetTiers(lift.CarriageLocalY, lift.RiseHeight, lift.SinkDepth);
                EditorUtility.SetDirty(lift);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                CarriageButton(lift, GravityLift.Tier.Lowest, "最底档");
                CarriageButton(lift, GravityLift.Tier.Low, "低档");
                CarriageButton(lift, GravityLift.Tier.Middle, "中档");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                CarriageButton(lift, GravityLift.Tier.High, "高档");
                CarriageButton(lift, GravityLift.Tier.Highest, "最高档");
            }

            EditorGUILayout.Space();
            for (int i = 0; i < GravityLift.TierCount; i++)
            {
                var tier = (GravityLift.Tier)i;
                string mark = tier == lift.CurrentTier && Application.isPlaying ? "  ◀ 当前" : "";
                EditorGUILayout.LabelField(
                    $"{GravityLift.TierName(tier)}（{tier}）",
                    $"{lift.HeightOf(tier):0.###}（本地 y）{mark}");
            }

            float span = (lift.RiseHeight + lift.SinkDepth) * 2f;
            EditorGUILayout.LabelField("全行程", $"{span:0.###} 单位（五档）");

            if (span > 0f)
            {
                float longest = Mathf.Max(lift.RiseHeight, lift.SinkDepth) * 2f;
                EditorGUILayout.LabelField("从最底到最高", $"{longest / lift.Speed:0.##} 秒");
            }
            else
            {
                EditorGUILayout.HelpBox("每档升 / 沉都是 0，这个滑轨不会动。", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("判定基准", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("基准重力（向下分量）", $"{lift.ReferenceGravity:0.###}");
            EditorGUILayout.LabelField("当前 Physics2D.gravity", Physics2D.gravity.ToString("0.###"));
            EditorGUILayout.LabelField("基准质量", $"{lift.BaseMass:0.###}");

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("基准重力 = 当前工程重力"))
                {
                    Undo.RecordObject(lift, "设置重力基准");
                    lift.SetReferenceGravity(Mathf.Abs(Physics2D.gravity.y));
                    EditorUtility.SetDirty(lift);
                }
            }

            // 两票各投了什么 —— 调关卡时一眼看出是哪一边在起作用。
            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("两票", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("重力票", Vote(GravityLift.GravityVote(Physics2D.gravity, lift.ReferenceGravity)));
                EditorGUILayout.LabelField("质量票", Vote(GravityLift.MassVote(ReadMass(), lift.BaseMass)));
                EditorGUILayout.LabelField("当前档", GravityLift.TierName(lift.CurrentTier));
                EditorGUILayout.LabelField("本帧速度", $"{lift.Velocity.y:0.###} 单位/秒");
                return;
            }

            EditorGUILayout.HelpBox(
                "档位是**本地坐标**（相对轨道原点的 y）。迁移这一段时拖根物体即可，五档跟着走。\n" +
                "⚠️ 不要缩放根物体 —— 父级缩放会被滑块继承，碰撞箱与位移都会变形。\n" +
                "重力 / 质量各投一票（+1 往上、−1 往下），两票相加：同时减小 = 上两档，一加一减 = 抵消回中档。",
                MessageType.Info);
        }

        /// <summary>一个票数的可读形式。</summary>
        private static string Vote(int vote)
        {
            if (vote > 0) return "+1（往上）";
            if (vote < 0) return "−1（往下）";
            return "0（不动）";
        }

        /// <summary>当前世界的质量属性。规则系统没起来时按默认值。</summary>
        private static AttributeValues ReadMass()
        {
            IAttributeManager attributes = RuleSystem.Instance?.Attributes;
            return attributes != null ? attributes.Values : AttributeValues.Default;
        }

        /// <summary>把滑块搬回某一档（只在编辑模式下用，运行期它自己会走）。</summary>
        private static void CarriageButton(GravityLift lift, GravityLift.Tier tier, string label)
        {
            if (!GUILayout.Button(label)) return;

            Transform carriage = lift.Carriage;
            if (carriage == null)
            {
                Debug.LogWarning("[滑轨] 没有滑块，先在「引用」栏指定。", lift);
                return;
            }

            Vector3 local = carriage.localPosition;
            local.y = lift.HeightOf(tier);

            Undo.RecordObject(carriage, "移动滑轨滑块");
            carriage.localPosition = local;

            // 摆到中档顺带把「中档」记下来，省一次手敲。
            if (tier == GravityLift.Tier.Middle)
            {
                Undo.RecordObject(lift, "设置滑轨中档");
                lift.SetTiers(local.y, lift.RiseHeight, lift.SinkDepth);
                EditorUtility.SetDirty(lift);
            }
        }
    }
}
