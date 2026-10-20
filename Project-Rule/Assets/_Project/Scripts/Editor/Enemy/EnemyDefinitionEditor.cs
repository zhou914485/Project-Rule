using System.Collections.Generic;
using RuleGame.Gameplay.Enemy;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Enemy
{
    /// <summary>仅翻译编辑器显示，不重命名序列化字段；保留范围约束、Undo 和多对象编辑。</summary>
    [CustomEditor(typeof(EnemyDefinition)), CanEditMultipleObjects]
    public sealed class EnemyDefinitionEditor : UnityEditor.Editor
    {
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { nameof(EnemyDefinition.Kind), "敌人类型" },
            { nameof(EnemyDefinition.DisplayName), "显示名称" },
            { nameof(EnemyDefinition.BodySize), "身体碰撞尺寸" },
            { nameof(EnemyDefinition.Health), "最大生命" },
            { nameof(EnemyDefinition.Damage), "基础伤害" },
            { nameof(EnemyDefinition.ContactDamage), "启用接触伤害" },
            { nameof(EnemyDefinition.ContactInterval), "接触伤害间隔（秒）" },
            { nameof(EnemyDefinition.ContactPadding), "接触判定外扩距离" },
            { nameof(EnemyDefinition.Energy), "最大能量" },
            { nameof(EnemyDefinition.EnergyRegen), "每秒能量恢复" },
            { nameof(EnemyDefinition.RuleAttribute), "施法属性" },
            { nameof(EnemyDefinition.RuleRelation), "施法关系" },
            { nameof(EnemyDefinition.MoveSpeed), "基础移动速度" },
            { nameof(EnemyDefinition.AttackRange), "近战攻击距离" },
            { nameof(EnemyDefinition.AttackCooldown), "攻击冷却（秒）" },
            { nameof(EnemyDefinition.ProjectileBudget), "同时存活弹体上限" },
            { nameof(EnemyDefinition.ProjectileLifetime), "弹体最长寿命（秒）" },
            { nameof(EnemyDefinition.BrickGroundWaves), "积木砸地冲击波轮数" },
            { nameof(EnemyDefinition.BrickWaveInterval), "积木冲击波间隔（秒）" },
            { nameof(EnemyDefinition.BrickSlamRange), "积木砸地横向半径（米）" },
            { nameof(EnemyDefinition.BrickThrowCount), "积木每轮投掷数量" },
            { nameof(EnemyDefinition.BrickThrowSpread), "积木投掷总扇角（度）" },
            { nameof(EnemyDefinition.BrickThrowSpeed), "积木投掷速度" },
            { nameof(EnemyDefinition.RabbitLobCount), "兔子上抛落点数量" },
            { nameof(EnemyDefinition.RabbitGroundVolleyCount), "兔子一阶段连续投掷次数" },
            { nameof(EnemyDefinition.RabbitGroundMagicInterval), "兔子一阶段施法检查间隔（秒）" },
            { nameof(EnemyDefinition.RabbitGroundFanCount), "兔子一阶段低位快弹数量（0 关闭）" },
            { nameof(EnemyDefinition.RabbitGroundFanSpread), "兔子低位快弹总扇角（度）" },
            { nameof(EnemyDefinition.RabbitGroundFanSpeed), "兔子低位快弹速度" },
            { nameof(EnemyDefinition.RabbitGroundArcStagger), "兔子错峰落弹弧顶倍率" },
            { nameof(EnemyDefinition.RabbitLandingSpread), "兔子落点间距（米）" },
            { nameof(EnemyDefinition.RabbitFanCount), "兔子每轮散射数量" },
            { nameof(EnemyDefinition.RabbitFanSpread), "兔子散射总扇角（度）" },
            { nameof(EnemyDefinition.BatVolleyWaves), "蝙蝠交错弹幕轮数" },
            { nameof(EnemyDefinition.BatFanCount), "蝙蝠每轮散射数量" },
            { nameof(EnemyDefinition.BatVolleyInterval), "蝙蝠弹幕间隔（秒）" },
            { nameof(EnemyDefinition.BatFanSpread), "蝙蝠散射总扇角（度）" },
            { nameof(EnemyDefinition.BatRingCount), "蝙蝠环形弹数量（0 关闭）" },
            { nameof(EnemyDefinition.ClockTimeRuleInterval), "时钟变速结束后间隔（游戏秒）" },
            { nameof(EnemyDefinition.ClockExtraBarrageEnabled), "默认开启额外环形弹幕" },
            { nameof(EnemyDefinition.ClockBurstCount), "时钟一阶段环形弹数量" },
            { nameof(EnemyDefinition.ClockBurstInterval), "时钟环形弹幕间隔（秒）" },
            { nameof(EnemyDefinition.ClockBurstWarning), "时钟环形弹幕预警（秒）" },
            { nameof(EnemyDefinition.ClockBurstSpeed), "时钟环形弹速度" },
            { nameof(EnemyDefinition.ClockBeamWidth), "时针激光宽度（米）" },
            { nameof(EnemyDefinition.ClockBeamWidthStep), "后续指针激光增宽（米）" },
            { nameof(EnemyDefinition.BossRestDuration), "进攻后休息时长（秒）" },
            { nameof(EnemyDefinition.ClockBeamVolleysBeforeRest), "每轮指针攻击次数" },
            { nameof(EnemyDefinition.BrickAdvanceDuration), "压近最长时长（秒）" },
            { nameof(EnemyDefinition.BrickAdvanceSpeedMultiplier), "压近速度倍率" },
            { nameof(EnemyDefinition.BrickAdvanceCooldown), "压近冷却（秒）" },
            { nameof(EnemyDefinition.BrickComboLimit), "后续阶段连招上限" },
            { nameof(EnemyDefinition.ClockChargeDuration), "抱头乱跑持续时长（秒）" },
            { nameof(EnemyDefinition.ClockChargeSpeedMultiplier), "冲撞速度倍率" },
            { nameof(EnemyDefinition.ClockChargeCooldown), "冲撞冷却（秒）" },
            { nameof(EnemyDefinition.ClockBeamWarning), "每次激光预警（秒）" },
            { nameof(EnemyDefinition.ClockBeamDuration), "每次激光持续（秒）" },
            { nameof(EnemyDefinition.ClockBeamMaxSweepAngle), "单束最大扫射角（度）" },
            { nameof(EnemyDefinition.ClockBeamSpread), "激光封路夹角（度）" },
            { nameof(EnemyDefinition.RabbitBurrowDuration), "藏身撤退最长时长（秒）" },
            { nameof(EnemyDefinition.RabbitBurrowSpeedMultiplier), "藏身撤退速度倍率" },
            { nameof(EnemyDefinition.RabbitBurrowCooldown), "藏身撤退冷却（秒）" },
            { nameof(EnemyDefinition.RabbitEscapeDistance), "触发藏身的近身距离" },
            { nameof(EnemyDefinition.RabbitBurstCount), "翻转后散射轮数" },
            { nameof(EnemyDefinition.RabbitBurstInterval), "散射轮次间隔（秒）" },
            { nameof(EnemyDefinition.RabbitRepositionDuration), "地面换位最长时长（秒）" },
            { nameof(EnemyDefinition.RabbitAimLockLead), "投掷提前锁定时间（秒）" },
            { nameof(EnemyDefinition.RabbitThrowArcHeight), "站立投掷上抛高度（米）" },
            { nameof(EnemyDefinition.RabbitAirHeight), "翻转后飞行高度差" },
            { nameof(EnemyDefinition.RabbitAirMoveDuration), "空中换侧参考时长（秒）" },
            { nameof(EnemyDefinition.RabbitAirSpeedMultiplier), "空中换侧速度倍率" },
            { nameof(EnemyDefinition.RabbitFanAngle), "散射相邻弹夹角（度）" },
            { nameof(EnemyDefinition.PreferredDistance), "理想作战距离" },
            { nameof(EnemyDefinition.SpacingTolerance), "距离调整缓冲" },
            { nameof(EnemyDefinition.RangedRange), "远程攻击距离" },
            { nameof(EnemyDefinition.RetreatSpeedMultiplier), "后撤速度倍率" },
            { nameof(EnemyDefinition.StaggerDamageThreshold), "累计破韧伤害阈值" },
            { nameof(EnemyDefinition.StaggerResetDelay), "破韧累计重置间隔（秒）" },
            { nameof(EnemyDefinition.HurtCooldown), "硬直保护冷却（秒）" },
            { nameof(EnemyDefinition.BossHurtDuration), "普通受击硬直上限（秒）" },
            { nameof(EnemyDefinition.GravityScale), "自身重力倍率" },
            { nameof(EnemyDefinition.Flying), "启用飞行移动" },
            { nameof(EnemyDefinition.FlightAcceleration), "飞行加速度" },
            { nameof(EnemyDefinition.BatOrbitDistance), "空中换位水平距离" },
            { nameof(EnemyDefinition.BatHoverHeight), "战斗换位高度差" },
            { nameof(EnemyDefinition.BatDashCooldown), "俯冲冷却（秒）" },
            { nameof(EnemyDefinition.BatDashWindup), "俯冲蓄力时长（秒）" },
            { nameof(EnemyDefinition.BatDashDuration), "俯冲最长时长（秒）" },
            { nameof(EnemyDefinition.BatEvadeCooldown), "近身撤离冷却（秒）" },
            { nameof(EnemyDefinition.BatEvadeDuration), "近身撤离时长（秒）" },
            { nameof(EnemyDefinition.BatRecoverDuration), "俯冲后恢复时长（秒）" },
            { nameof(EnemyDefinition.BatApproachDistance), "主动近身水平距离" },
            { nameof(EnemyDefinition.BatApproachHeight), "主动近身高度差" },
            { nameof(EnemyDefinition.BatApproachDuration), "尝试近身时长（秒）" },
            { nameof(EnemyDefinition.BatApproachSpeedMultiplier), "主动近身速度倍率" },
            { nameof(EnemyDefinition.BatRestHeightRatio), "休息巡飞高度比例" },
            { nameof(EnemyDefinition.BatRestPatrolDistance), "休息巡飞单侧距离" },
            { nameof(EnemyDefinition.BatRestSpeedMultiplier), "休息巡飞速度倍率" },
            { nameof(EnemyDefinition.ImpactFraction), "动作命中时点比例" },
            { nameof(EnemyDefinition.RabbitCycle), "兔子周期藏身间隔（秒）" },
            { nameof(EnemyDefinition.RabbitThrowInterval), "普通投掷动作时长（秒）" },
            { nameof(EnemyDefinition.ClockHourInterval), "时针停火间隔（秒）" },
            { nameof(EnemyDefinition.ClockMinuteInterval), "分针停火间隔（秒）" },
            { nameof(EnemyDefinition.ClockSecondInterval), "秒针停火间隔（秒）" },
            { nameof(EnemyDefinition.ClockHourRotationPeriod), "时针旋转一圈（秒）" },
            { nameof(EnemyDefinition.ClockMinuteRotationPeriod), "分针旋转一圈（秒）" },
            { nameof(EnemyDefinition.ClockSecondRotationPeriod), "秒针旋转一圈（秒）" },
            { nameof(EnemyDefinition.ClockCoreWindow), "核心暴露时长（秒）" },
            { nameof(EnemyDefinition.ClockCoreHealth), "核心生命" },
            { nameof(EnemyDefinition.MinionSenseRange), "索敌距离" },
            { nameof(EnemyDefinition.MinionPatrolDistance), "出生点两侧巡逻距离" },
            { nameof(EnemyDefinition.MinionIgnoreDuration), "攻击后无视时间（秒）" },
            { nameof(EnemyDefinition.MinionLeashRange), "离出生点最远追击距离" },
            { nameof(EnemyDefinition.MinionTargetMemory), "失去视线后搜索时间（秒）" },
            { nameof(EnemyDefinition.MinionStaggerThreshold), "小怪累计伤害硬直阈值" },
            { nameof(EnemyDefinition.MinionHurtDuration), "小怪普通硬直时长上限（秒）" },
            { nameof(EnemyDefinition.MinionPreferredRange), "远程小怪理想交战距离" },
            { nameof(EnemyDefinition.MinionAimLockFraction), "近战前摇转向截止比例" },
            { nameof(EnemyDefinition.MinionNavigationEnabled), "启用地面平台寻路" },
            { nameof(EnemyDefinition.MinionJumpHeight), "寻路最大跳高（0 禁止跳跃）" },
            { nameof(EnemyDefinition.MinionMaxDrop), "寻路最大安全落差" },
            { nameof(EnemyDefinition.MinionNavigationSpacing), "寻路平台采样间距" },
            { nameof(EnemyDefinition.MinionRepathInterval), "寻路重算间隔（秒）" },
            { nameof(EnemyDefinition.MinionReassembles), "散架后可重组" },
            { nameof(EnemyDefinition.MinionRespawnDelay), "散架等待重组（秒）" },
            { nameof(EnemyDefinition.MinionFuseDuration), "自爆引信时长（秒）" },
            { nameof(EnemyDefinition.MinionExplosionRadius), "爆炸半径" },
            { nameof(EnemyDefinition.MinionHopInterval), "落地后跳跃间隔（秒）" },
            { nameof(EnemyDefinition.MinionHopHeight), "普通跳跃高度" },
            { nameof(EnemyDefinition.MinionHighHopHeight), "交替高跳高度" },
            { nameof(EnemyDefinition.MinionMouseRange), "发条鼠单侧最远距离" },
            { nameof(EnemyDefinition.MinionMouseCooldown), "钻地后的冷却（秒）" },
            { nameof(EnemyDefinition.MinionHugDuration), "拥抱最长持续（秒）" },
            { nameof(EnemyDefinition.MinionHugTickInterval), "拥抱伤害间隔（秒）" },
            { nameof(EnemyDefinition.MinionSkillInterval), "技能轮换间隔（秒）" },
            { nameof(EnemyDefinition.MinionSlideDuration), "滑行最长持续（秒）" },
            { nameof(EnemyDefinition.MinionDashMultiplier), "冲向玩家速度倍率" },
            { nameof(EnemyDefinition.MinionProjectileSpeed), "小怪投射物速度" },
            { nameof(EnemyDefinition.RadiationRadius), "辐射半径" },
            { nameof(EnemyDefinition.RadiationWarning), "辐射预警（秒）" },
            { nameof(EnemyDefinition.RadiationDuration), "持续辐射（秒）" },
            { nameof(EnemyDefinition.RadiationRest), "停止辐射（秒）" },
            { nameof(EnemyDefinition.RadiationHitInterval), "辐射伤害间隔（秒）" },
            { nameof(EnemyDefinition.TopRestitution), "碰撞弹性（0–1）" },
            { nameof(EnemyDefinition.TopImpactCooldown), "同一物体碰撞保护（秒）" },
            { nameof(EnemyDefinition.TopInitialDirection), "初始平移方向（-1 左 / 1 右）" },
            { nameof(EnemyDefinition.Timings), "动作时长配置" },
        };
        private static readonly Dictionary<string, string> EnumLabels = new Dictionary<string, string>
        {
            { "BrickMinion", "积木小怪" }, { "BrickBoss", "积木 Boss" }, { "ClockBoss", "发条 Boss" },
            { "RabbitBoss", "兔子 Boss" }, { "BatBoss", "蝙蝠 Boss" }, { "BatMinion", "蝙蝠小怪" },
            { "StoneGuard", "石头人小怪" },
            { "ExplosiveBlock", "自爆方块" },
            { "BrickSoldier", "积木士兵" },
            { "SpringToy", "弹簧小怪" },
            { "ClockMouse", "发条鼠" },
            { "ClockBomb", "发条炸弹" },
            { "PlushBird", "毛绒玩具鸟" },
            { "HugBear", "抱抱熊" },
            { "PlushSeal", "毛绒海豹" },
            { "DiscoBall", "迪斯科球" }, { "SpinningTop", "陀螺怪" },
            { "Gravity", "重力" }, { "Time", "时间" }, { "Friction", "摩擦" },
            { "Mass", "质量" }, { "Damage", "伤害" }, { "Causality", "因果" },
            { "Revert", "反转" }, { "Nullify", "失效" }, { "Constant", "恒定" },
            { "Enhance", "增强" }, { "Weaken", "削弱" }
        };
        private readonly List<SerializedProperty> _properties = new List<SerializedProperty>();
        private void OnEnable()
        {
            _properties.Clear();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                _properties.Add(iterator.Copy());
            }
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            foreach (var property in _properties)
            {
                if (property.name == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(property, new GUIContent("脚本"));
                    continue;
                }
                string caption = Labels.TryGetValue(property.name, out string translated) ? translated : property.displayName;
                var label = new GUIContent(caption, property.tooltip);
                if (property.propertyType == SerializedPropertyType.Enum) DrawEnum(property, label);
                else if (property.name == nameof(EnemyDefinition.Timings)) DrawTimings(property, label);
                else EditorGUILayout.PropertyField(property, label, true);
            }
            serializedObject.ApplyModifiedProperties();
        }
        private static void DrawEnum(SerializedProperty property, GUIContent label)
        {
            string[] names = property.enumNames;
            var options = new GUIContent[names.Length];
            for (int i = 0; i < names.Length; i++)
                options[i] = new GUIContent(EnumLabels.TryGetValue(names[i], out string translated) ? translated : names[i]);
            bool mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(label, property.enumValueIndex, options);
            if (EditorGUI.EndChangeCheck()) property.enumValueIndex = selected;
            EditorGUI.showMixedValue = mixed;
        }
        private static void DrawTimings(SerializedProperty property, GUIContent label)
        {
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, label, true);
            if (!property.isExpanded) return;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative("Array.size"), new GUIContent("动作数量"));
                for (int i = 0; i < property.arraySize; i++)
                {
                    var entry = property.GetArrayElementAtIndex(i);
                    EditorGUILayout.LabelField($"动作 {i + 1}", EditorStyles.miniBoldLabel);
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Action"), new GUIContent("动作标识", "与动画动作名保持一致，例如 Slash。"));
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("Duration"), new GUIContent("时长（秒）"));
                }
            }
        }
    }
}
