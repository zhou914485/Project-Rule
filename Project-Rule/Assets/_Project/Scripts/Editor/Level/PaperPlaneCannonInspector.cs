using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="PaperPlaneCannon"/> 的 Inspector：把「速度 → 伤害档位」当场算出来，
    /// 外加 Play 期的在飞数量、下一发倒计时与「立即发射 / 开火停火」按钮。
    /// </summary>
    /// <remarks>
    /// 速度是这套机关最值得调的数：它同时决定飞得多快、以及 5.2 公式里的速度档位。
    /// 这里把默认档位与原始伤害直接写出来，不用再翻 <c>ThrownKnife</c> 的表。
    /// </remarks>
    [CustomEditor(typeof(PaperPlaneCannon))]
    public sealed class PaperPlaneCannonInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var cannon = (PaperPlaneCannon)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("纸飞机", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("速度", $"{cannon.Speed:0.###} 单位/秒");
            EditorGUILayout.LabelField("默认档位", $"{cannon.DefaultSpeedScale} 档（0~3）");

            float mass = cannon.ProjectileBaseMass;
            if (mass > 0f)
            {
                float raw = ThrownKnife.RawDamageFor(mass, cannon.Speed);
                EditorGUILayout.LabelField("原始伤害（默认表）",
                    $"{raw:0.##} = 5 × {mass:0.##} kg × {cannon.DefaultSpeedScale} 档");
            }

            EditorGUILayout.LabelField("间隔", $"{cannon.Interval:0.###} 秒");
            EditorGUILayout.LabelField("最多同时", $"{cannon.MaxAlive} 架");
            EditorGUILayout.LabelField("阵营",
                cannon.Faction == Faction.Enemy ? "Enemy（只打玩家）" : "Player（只打敌人）");

            if (cannon.ProjectilePrefab == null)
            {
                EditorGUILayout.HelpBox(
                    "没有指定纸飞机预制体 —— 炮筒不会发射。\n" +
                    "把 Assets/_Project/Prefabs/PaperPlaneKnife.prefab 拖到「发射物」里。",
                    MessageType.Error);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("在飞", $"{cannon.AliveCount} 架");
                EditorGUILayout.LabelField("下一发",
                    cannon.IsFiring ? $"{cannon.NextFireIn:0.##} 秒" : "已停火");
                EditorGUILayout.LabelField("CombatSystem",
                    cannon.HasCombat ? "已找到" : "未找到（纸飞机会飞，但不会造成伤害）");
                EditorGUILayout.LabelField("炮口方向", cannon.AimDirection.ToString("0.##"));

                if (!cannon.HasCombat)
                {
                    EditorGUILayout.HelpBox(
                        "没找到 CombatSystem：纸飞机会飞，但不会造成伤害。\n" +
                        "把场景里的 CombatSystem 拖到「发射物 / 伤害出口」里。",
                        MessageType.Warning);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("立即发射一发")) cannon.FireOnce();
                    if (GUILayout.Button(cannon.IsFiring ? "停火" : "开火"))
                        cannon.SetFiring(!cannon.IsFiring);
                }

                // 让在飞数量 / 倒计时在 Play 中实时刷新。
                Repaint();
            }
            else
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "旋转炮筒（或 Muzzle 子物体）瞄准：红色小方块 = 炮口，发射方向 = 它的 +X。\n" +
                    "速度同时决定 5.2 伤害档位：≤8→0、≤12→1、≤16→2、≤20→3（默认表），" +
                    "原始伤害 = 5 × 质量 × 档位。",
                    MessageType.Info);
            }
        }
    }
}
