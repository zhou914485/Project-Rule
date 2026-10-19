namespace RuleGame.Presentation.Audio
{
    // 已存入 SfxBank，新增成员只追加，不改变现有序号。
    public enum SfxCue
    {
        Jump, AirJump, WallJump, Land, Dash, SwordLight, SwordHeavy, Hurt,
        WheelOpen, WheelPreview, WheelConfirm, WheelCancel, WheelBack, WheelError,
        EnemyMelee, EnemyShot, EnemyBeam, EnemyHitWood, EnemyHitMetal, EnemyHitSoft,
        EnemyDeathWood, EnemyDeathMetal, EnemyDeathSoft, SwitchOn, SwitchOff,
        ImpactStone, ImpactMetal, ImpactWood, BreakStone, BreakWood, Explosion,
        MenuClick, MenuBack, MenuConfirm,
        BossRuleCast, BossSummon, BossPhaseChange,
        BrickBossWindup, BrickBossSlam, BrickBossThrow, BrickBossSplit,
        ClockBossCharge, ClockBossBarrageWarning, ClockBossBeam, ClockBossRewind, ClockBossStun,
        RabbitBossBurrow, RabbitBossEmerge, RabbitBossShot,
        BatBossWindup, BatBossDash, BatBossFlap, BatBossRoar
    }
}
