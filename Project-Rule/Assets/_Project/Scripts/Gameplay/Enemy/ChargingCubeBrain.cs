using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>
    /// 冲撞小方：看见玩家就加速撞上去，靠身体接触扣血。
    /// </summary>
    /// <remarks>
    /// <para><b>没有攻击盒、没有爆炸</b>：伤害由 <c>EnemyActor.TickContactDamage</c> 在
    /// <c>ContactDamage</c> 打开时结算（同一条 <c>CombatSystem</c> 出口、同一条击退），
    /// 死亡走基类的 <c>Dead</c>，不召唤任何范围伤害。</para>
    /// <para><b>摩擦规则由 EnemyMotor 统一作用于冲刺速度与加减速</b>：×增强（3 倍）→ 只能挪，×削弱 / ×失效 → 打滑冲过头。
    /// Kinematic 敌人不吃材质摩擦，所以必须自己读 <c>FrictionScale</c>，与回弹块同一条写法。</para>
    /// <para>「摩擦 × 反转」由基类统一处理（意图与位移反向），这里不用管。</para>
    /// </remarks>
    public sealed class ChargingCubeBrain : FieldMinionBrain
    {
        /// <summary>撞上之后停一下再冲，避免贴着玩家来回抖。</summary>
        private const float BumpPause = .25f;

        /// <summary>两次「撞」之间的最小间隔（秒）。</summary>
        private const float BumpCooldown = .6f;

        private float _nextBumpAt;

        protected override string FirstAction => "Idle";

        public ChargingCubeBrain(EnemyActor actor) : base(actor) { }

        private void ApplyChargeSpeed() => Actor.Motor.SpeedMultiplier = Config.MinionDashMultiplier;

        protected override void EnterBehavior(string name)
        {
            if (name == "Charge") ApplyChargeSpeed();
            else if (name == "Bump")
            {
                Actor.Motor.Stop();
                // 显式给时长：Timings 里没配「Bump」时不会退回 0.8 秒的长停顿。
                Actor.Cue("Bump", BumpPause);
            }
        }

        protected override void TickBehavior(string name)
        {
            if (name == "Idle")
            {
                Patrol();
                if (SeesTarget) Go("Charge");
                return;
            }

            if (name == "Charge")
            {
                ApplyChargeSpeed();
                ChargeTowards();
                if (TouchesTarget && CooldownAge >= _nextBumpAt)
                {
                    _nextBumpAt = CooldownAge + BumpCooldown;
                    Go("Bump");
                    return;
                }
                if (!CanPursue) Go("Idle");
                return;
            }

            if (name == "Bump" && Finished) Go(SeesTarget ? "Charge" : "Idle");
        }

        protected override object CaptureBehavior() => _nextBumpAt;
        protected override void RestoreBehavior(object state) { if (state is float value) _nextBumpAt = value; }

        /// <summary>
        /// 直接朝目标压过去，**不查横向障碍**。
        /// </summary>
        /// <remarks>
        /// <para>基类的 <c>Chase()</c> 会在「正前方有墙」时把意图清零，而 <c>HasWallAhead</c> 的掩码里
        /// 带着 <c>PlayerMask</c> —— 冲撞怪会**停在玩家身前 0.7 米**、正好够不到受击箱，永远撞不上。</para>
        /// <para>它的设计就是「撞上去」，所以这里只按目标横坐标推进；撞到之后由 <c>Bump</c> 负责停下。</para>
        /// </remarks>
        private void ChargeTowards()
        {
            if (!CanPursue) { Actor.Motor.Intent = Vector2.zero; return; }
            float offset = (SeesTarget ? PredictTarget(.2f).x : LastSeenPosition.x) - Actor.transform.position.x;
            if (Config.MinionNavigationEnabled && Actor.Motor.HasTerrainWallAhead(Mathf.Sign(offset)))
            { Chase(); return; }
            Actor.FaceDirection(Vector2.right * offset);
            Actor.Motor.Intent = Mathf.Abs(offset) > .12f ? Vector2.right * Mathf.Sign(offset) : Vector2.zero;
        }
    }
}
