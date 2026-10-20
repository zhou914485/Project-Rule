using System;
using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Enemy
{
    // 新类型只追加，保留现有资产的枚举值。
    public enum EnemyKind { BrickMinion, BrickBoss, ClockBoss, RabbitBoss, BatBoss, BatMinion,
        StoneGuard, ExplosiveBlock, BrickSoldier, SpringToy, ClockMouse, ClockBomb, PlushBird, HugBear, PlushSeal,
        ChargingCube, DiscoBall, SpinningTop }

    [Serializable]
    public struct EnemyActionTiming
    {
        public string Action;
        [Min(0.05f)] public float Duration;
    }

    /// <summary>共用配置资产；生命、阶段、冷却等运行状态必须放在实例上。</summary>
    [CreateAssetMenu(menuName = "RuleGame/Enemy/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public EnemyKind Kind;
        public string DisplayName;
        public bool IsBoss => Kind == EnemyKind.BrickBoss || Kind == EnemyKind.ClockBoss
            || Kind == EnemyKind.RabbitBoss || Kind == EnemyKind.BatBoss;
        public Vector2 BodySize = new Vector2(1.8f, 3f);
        public float Health = 100f;
        public float Damage = 10f;
        public bool ContactDamage = true;
        [Min(0.05f)] public float ContactInterval = 0.6f;
        [Tooltip("身体接触伤害向外检测的余量，覆盖移动解算保留的微小间隙。")]
        [Min(0f)] public float ContactPadding = 0.05f;
        public float Energy = 100f;
        public float EnergyPerHit = 4f;
        public float EnergyRegen = 2f;
        public AttributeId RuleAttribute = AttributeId.Gravity;
        public RelationId RuleRelation = RelationId.Weaken;
        public float MoveSpeed = 2.5f;
        public float AttackRange = 3.5f;
        public float AttackCooldown = 1.8f;
        [Header("弹幕压力")]
        [Tooltip("单个 Boss 同时存活的弹体上限；命中、超时或回溯移除后归还名额。")]
        [Range(16, 384)] public int ProjectileBudget = 192;
        [Min(1f)] public float ProjectileLifetime = 7f;
        [Range(1, 5)] public int BrickGroundWaves = 1;
        [Min(.12f)] public float BrickWaveInterval = .24f;
        [Tooltip("砸地横向半径，0 仅发原有冲击波。")]
        [Min(0f)] public float BrickSlamRange;
        [Range(1, 15)] public int BrickThrowCount = 1;
        [Range(0f, 120f)] public float BrickThrowSpread = 54f;
        [Min(1f)] public float BrickThrowSpeed = 9f;
        [Range(1, 11)] public int RabbitLobCount = 1;
        [Tooltip("一阶段每套连续投掷次数；每次使用完整投掷动画与独立锁定，整套结束后休息。")]
        [Range(1, 4)] public int RabbitGroundVolleyCount = 1;
        [Tooltip("一阶段两次施法检查的间隔；已开始的连续投掷不会被施法中断。")]
        [Min(5f)] public float RabbitGroundMagicInterval = 8f;
        [Tooltip("一阶段每次上抛同时释放的低位瞄准散射数量；0 关闭。共用本次锁定点，不追踪玩家。")]
        [Range(0, 15)] public int RabbitGroundFanCount;
        [Range(10f, 100f)] public float RabbitGroundFanSpread = 60f;
        [Min(1f)] public float RabbitGroundFanSpeed = 14f;
        [Tooltip("一阶段隔颗提高弧顶，令同次投掷的落弹错峰到达；1 保持原弧线。")]
        [Range(1f, 4f)] public float RabbitGroundArcStagger = 1f;
        [Min(.1f)] public float RabbitLandingSpread = 1.1f;
        [Range(3, 17)] public int RabbitFanCount = 3;
        [Range(0f, 160f)] public float RabbitFanSpread;
        [Range(1, 8)] public int BatVolleyWaves = 4;
        [Range(3, 21)] public int BatFanCount = 5;
        [Min(.1f)] public float BatVolleyInterval = .25f;
        [Range(10f, 160f)] public float BatFanSpread = 48f;
        [Range(0, 48)] public int BatRingCount;
        [Header("时钟变速与齿轮弹幕")]
        [Tooltip("上一条时间规则结束后的间隔（游戏秒）；0 关闭自动变速。时间槽有生效规则时等待，不提前覆盖。")]
        [Min(0f)] public float ClockTimeRuleInterval;
        [Tooltip("额外环形弹幕默认关闭；白盒中可独立切换，不影响策划原有激光和乱跑。")]
        public bool ClockExtraBarrageEnabled;
        [Range(0, 48)] public int ClockBurstCount;
        [Min(.4f)] public float ClockBurstInterval = 1.6f;
        [Min(.2f)] public float ClockBurstWarning = .45f;
        [Min(1f)] public float ClockBurstSpeed = 8f;
        [Min(.05f)] public float ClockBeamWidth = .1f;
        [Min(0f)] public float ClockBeamWidthStep = .08f;
        [Header("Boss 进攻后的喘息")]
        [Tooltip("一套攻击结束后不再出招的时间；地面 Boss 原地休息，蝙蝠高空巡飞。不随阶段加速缩短。")]
        [Min(0.1f)] public float BossRestDuration = 1.8f;
        // 保留旧资产的序列化字段；独立激光不再按轮数进入休息。
        [HideInInspector] public int ClockBeamVolleysBeforeRest = 2;
        [Header("积木压迫与连招")]
        [Min(0.2f)] public float BrickAdvanceDuration = 0.9f;
        [Min(1f)] public float BrickAdvanceSpeedMultiplier = 2.2f;
        [Min(0.5f)] public float BrickAdvanceCooldown = 4f;
        [Tooltip("第二阶段起一套攻击的最大招数，每招仍有完整前摇，结束后休息。")]
        [Range(1, 3)] public int BrickComboLimit = 2;
        [Header("发条封路与冲撞")]
        [Min(0.2f)] public float ClockChargeDuration = 3f;
        [Min(1f)] public float ClockChargeSpeedMultiplier = 3.5f;
        [Min(1f)] public float ClockChargeCooldown = 7f;
        [Tooltip("每次短暂扫射开始造成伤害前的预警时间。")]
        [Min(0.3f)] public float ClockBeamWarning = 0.7f;
        [Min(0.05f)] public float ClockBeamDuration = 0.35f;
        [Tooltip("每束激光最多扫过的角度；实际持续时间取配置时长与该角度对应时长的较小值，不改变指针转速。")]
        [Range(30f, 180f)] public float ClockBeamMaxSweepAngle = 90f;
        // 旧版瞄准散射参数，仅保留序列化兼容；扫射方向完全由指针决定。
        [HideInInspector] public float ClockBeamSpread = 14f;
        [Header("兔子藏身与弹幕")]
        [Min(0.2f)] public float RabbitBurrowDuration = 1f;
        [Min(1f)] public float RabbitBurrowSpeedMultiplier = 2.8f;
        [Min(1f)] public float RabbitBurrowCooldown = 5f;
        [Min(1f)] public float RabbitEscapeDistance = 2.8f;
        [Range(1, 3)] public int RabbitBurstCount = 2;
        [Min(0.2f)] public float RabbitBurstInterval = 0.35f;
        [Header("兔子瞄准与飞行战术")]
        [Min(0.6f)] public float RabbitRepositionDuration = 2f;
        [Min(0.1f)] public float RabbitAimLockLead = 0.35f;
        [Tooltip("站立投掷的弧顶高于出手点和目标的距离；高抛使用 1.6 倍。倒立散射不使用此参数。")]
        [Min(0.3f)] public float RabbitThrowArcHeight = 1f;
        [Tooltip("翻转后相对玩家的目标高度差；起飞会同时保证身体与地面的安全间距。")]
        [Min(1f)] public float RabbitAirHeight = 2.4f;
        [Tooltip("单次空中换位的最长尝试时间。散射仍须离地并拉开侧向距离；连续换位失败时改用普通抛投。")]
        [Min(0.5f)] public float RabbitAirMoveDuration = 1.8f;
        [Min(1f)] public float RabbitAirSpeedMultiplier = 1.6f;
        [Range(5f, 40f)] public float RabbitFanAngle = 18f;
        [Header("Boss 距离控制")]
        [Min(0.5f)] public float PreferredDistance = 5f;
        [Min(0.1f)] public float SpacingTolerance = 0.6f;
        [Min(0.5f)] public float RangedRange = 9f;
        [Range(0.1f, 1f)] public float RetreatSpeedMultiplier = 0.8f;
        [Header("受击硬直")]
        [Tooltip("Boss 短时间累计受到这么多实际伤害才进入受击状态；小怪使用独立阈值。")]
        [Min(1f)] public float StaggerDamageThreshold = 30f;
        [Tooltip("连续这么久没有有效命中，清空累计硬直伤害。冷却期间不累计。")]
        [Min(0.1f)] public float StaggerResetDelay = 1.5f;
        [Tooltip("受击动作结束后，允许再次进入硬直前的冷却。期间仍正常扣血。")]
        [Min(0f)]
        public float HurtCooldown = 2f;
        [Tooltip("Boss 普通受击硬直时长上限；不影响阶段虚弱、发条眩晕或小怪。")]
        [Min(0.05f)] public float BossHurtDuration = 0.45f;
        public float GravityScale = 1f;
        public bool Flying;
        [Min(1f)] public float FlightAcceleration = 24f;
        [Header("蝙蝠空战")]
        [Min(1f)] public float BatOrbitDistance = 4.5f;
        [Min(0.5f)] public float BatHoverHeight = 3f;
        [Min(1f)] public float BatDashCooldown = 4f;
        [Min(0.2f)] public float BatDashWindup = 0.5f;
        [Min(0.1f)] public float BatDashDuration = 0.65f;
        [Min(0.5f)] public float BatEvadeCooldown = 2.5f;
        [Min(0.1f)] public float BatEvadeDuration = 0.65f;
        [Min(0.1f)] public float BatRecoverDuration = 0.45f;
        [Header("蝙蝠主动近身")]
        [Min(0.5f)] public float BatApproachDistance = 2.5f;
        [Tooltip("接近主角时的高度差，较低的值便于近战；身体仍受地面与场地边界限制。")]
        [Min(0f)] public float BatApproachHeight = 0.8f;
        [Min(0.5f)] public float BatApproachDuration = 2.4f;
        [Min(0.5f)] public float BatApproachSpeedMultiplier = 1.15f;
        [Header("蝙蝠休息巡飞")]
        [Tooltip("休息目标高度占场地高度的比例，仍受身体与场地边界限制。")]
        [Range(0.5f, 0.95f)] public float BatRestHeightRatio = 0.8f;
        [Tooltip("以进入休息的位置为中心，向两侧来回飞行的距离。")]
        [Min(0.5f)] public float BatRestPatrolDistance = 2.5f;
        [Tooltip("休息巡飞相对基础飞行速度的倍率，不随阶段加速。")]
        [Range(0.1f, 1f)] public float BatRestSpeedMultiplier = 0.85f;
        public float ImpactFraction = 0.6f;
        public float RabbitCycle = 20f;
        public float RabbitThrowInterval = 2f;
        [Header("时钟独立指针")]
        [Tooltip("每束激光消失后，到下一次预警开始的停火间隔。与指针转速独立。")]
        [Min(0.3f)] public float ClockHourInterval = 12f;
        [Min(0.3f)] public float ClockMinuteInterval = 6f;
        [Min(0.3f)] public float ClockSecondInterval = 3f;
        [Header("指针旋转速度（秒/圈）")]
        [Min(1f)] public float ClockHourRotationPeriod = 24f;
        [Min(1f)] public float ClockMinuteRotationPeriod = 12f;
        [Min(1f)] public float ClockSecondRotationPeriod = 6f;
        public float ClockCoreWindow = 3f;
        public float ClockCoreHealth = 15f;
        [Header("关卡小怪：感知与巡逻")]
        [Min(.5f)] public float MinionSenseRange = 6f;
        [Min(.2f)] public float MinionPatrolDistance = 3f;
        [Min(.1f)] public float MinionIgnoreDuration = 3f;
        [Tooltip("以出生点为圆心的追击半径；圈外目标不索敌、不起手攻击，脱战后返回。")]
        [Min(1f)] public float MinionLeashRange = 8f;
        [Tooltip("失去视线后只搜索最后看到的位置这么久，超时返回；不会持续追踪墙后的实时位置。")]
        [Min(0f)] public float MinionTargetMemory = 1.5f;
        [Tooltip("在受击累计重置时间内承受此伤害才硬直；仍会正常扣血和闪白。")]
        [Min(1f)] public float MinionStaggerThreshold = 12f;
        [Tooltip("小怪普通硬直时长上限，包含 Boss 召唤物；恢复后再计算完整的硬直冷却。")]
        [Min(.05f)] public float MinionHurtDuration = .25f;
        [Min(.5f)] public float MinionPreferredRange = 4f;
        [Tooltip("近战前摇可转向的比例，超过后锁定方向，留下绕背躲避的机会。")]
        [Range(0f, .5f)] public float MinionAimLockFraction = .25f;
        [Header("关卡小怪：平台寻路")]
        public bool MinionNavigationEnabled = true;
        [Tooltip("普通地面小怪可跳越的高度；0 禁止主动跳跃，弹簧怪沿用自己的跳高。")]
        [Min(0f)] public float MinionJumpHeight = 1.6f;
        [Tooltip("主动下落允许的最大高度差；没有可达落脚点时保留悬崖保护。")]
        [Min(0f)] public float MinionMaxDrop = 2.5f;
        [Tooltip("平台采样间距；窄于此间距的平台可能不参与路线。每次最多采样 192 个落脚点。")]
        [Range(.3f, 1f)] public float MinionNavigationSpacing = .5f;
        [Tooltip("路线重算间隔，使用游戏物理时钟；运动中的跳跃不逐帧改选路线。")]
        [Min(.2f)] public float MinionRepathInterval = .65f;
        [Header("关卡小怪：重组与自爆")]
        public bool MinionReassembles;
        [Min(.1f)] public float MinionRespawnDelay = 7f;
        [Min(.1f)] public float MinionFuseDuration = 1f;
        [Min(.2f)] public float MinionExplosionRadius = 2.5f;
        [Header("关卡小怪：跳跃与钻地")]
        [Min(.1f)] public float MinionHopInterval = .5f;
        [Min(.2f)] public float MinionHopHeight = 2f;
        [Min(.2f)] public float MinionHighHopHeight = 3.2f;
        [Min(.5f)] public float MinionMouseRange = 8f;
        [Min(.1f)] public float MinionMouseCooldown = 3f;
        [Header("关卡小怪：拥抱与技能")]
        [Min(.1f)] public float MinionHugDuration = 3f;
        [Min(.1f)] public float MinionHugTickInterval = 1f;
        [Min(.1f)] public float MinionSkillInterval = 5f;
        [Min(.2f)] public float MinionSlideDuration = 1.2f;
        [Min(1f)] public float MinionDashMultiplier = 3f;
        [Min(1f)] public float MinionProjectileSpeed = 7f;
        [Header("迪斯科球：辐射周期")]
        [Min(.5f)] public float RadiationRadius = 8f;
        [Min(.1f)] public float RadiationWarning = .75f;
        [Min(.1f)] public float RadiationDuration = 4f;
        [Min(.1f)] public float RadiationRest = 2f;
        [Min(.05f)] public float RadiationHitInterval = .5f;
        [Header("陀螺：碰撞反弹")]
        [Range(0f, 1f)] public float TopRestitution = .9f;
        [Min(.05f)] public float TopImpactCooldown = .12f;
        [Range(-1f, 1f)] public float TopInitialDirection = 1f;
        public EnemyActionTiming[] Timings = Array.Empty<EnemyActionTiming>();

        public float Duration(string action, float fallback = 0.8f)
        {
            foreach (EnemyActionTiming entry in Timings)
                if (entry.Action == action) return Mathf.Max(0.05f, entry.Duration);
            return fallback;
        }
    }
}
