using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>
    /// 冲撞小方的表现：两帧迈步、按速度前倾、冲刺拉长，死亡缩灭并炸一把同色碎片。
    /// </summary>
    /// <remarks>
    /// <para>跑动不逐帧画：脚是两帧，倾斜与拉伸由速度算出来 —— 比多画 4 帧便宜，也不会和碰撞体错位。</para>
    /// <para>死亡只是缩小 + 淡出，<b>没有爆炸</b>；<c>Dead</c> 动作走完由 <c>EnemyActor</c> 停用整个物体。</para>
    /// </remarks>
    public sealed class ChargingCubeView : MonoBehaviour
    {
        public EnemyActor Actor;
        public SpriteRenderer Body;
        public Sprite Idle;
        public Sprite RunA;
        public Sprite RunB;

        [Tooltip("迈步帧率。")]
        [Min(1f)] public float RunFps = 10f;

        [Tooltip("全速冲刺时的前倾角度。")]
        [Min(0f)] public float MaxLean = 8f;

        [Tooltip("死亡缩小到消失用的时间；不要超过 Dead 动作时长。")]
        [Min(.05f)] public float DeathDuration = .3f;

        [Tooltip("死亡碎片数量；0 = 不炸碎片。")]
        [Min(0)] public int DeathChips = 8;

        private Vector3 _baseScale;
        private Color _baseColor;
        private float _runTime;
        private float _deathAge = -1f;
        private bool _chipsSpawned;

        private void Awake()
        {
            if (Actor == null) Actor = GetComponentInParent<EnemyActor>();
            if (Body == null) Body = GetComponentInChildren<SpriteRenderer>();
            _baseScale = Body.transform.localScale;
            _baseColor = Body.color;
            EnemyHitFeedback.Attach(Actor, Body);
            RuleWheel.RuleCastVfx.Attach(Actor);
        }

        private void LateUpdate()
        {
            if (Actor == null || Body == null || Actor.Brain == null) return;

            bool dead = Actor.Defeated || Actor.Brain.StateName == "Dead";
            if (dead)
            {
                TickDeath();
                return;
            }

            if (TimeManager.Instance?.IsPaused == true || Actor.Status.IsPaused) return;

            float speed = Mathf.Abs(Actor.Motor.Velocity.x);
            if (speed > .35f)
            {
                _runTime += Time.deltaTime * Mathf.Max(1f, RunFps);
                Body.sprite = (Mathf.FloorToInt(_runTime) & 1) == 0 ? RunA : RunB;
            }
            else
            {
                _runTime = 0f;
                Body.sprite = Idle;
            }

            float chargeSpeed = Mathf.Max(.01f, Actor.Definition.MoveSpeed * Actor.Definition.MinionDashMultiplier);
            float t = Mathf.Clamp01(speed / chargeSpeed);
            float lean = t * MaxLean * Mathf.Sign(Actor.Motor.Velocity.x == 0f ? Actor.Facing : Actor.Motor.Velocity.x);
            // scale 为负会把旋转一起镜像，所以这里用 flipX 翻脸、旋转只跟世界速度方向走。
            float stretch = 1f + .10f * Mathf.Clamp01(speed / 4f);
            Body.transform.localScale = new Vector3(_baseScale.x * stretch, _baseScale.y / stretch, _baseScale.z);
            Body.transform.localRotation = Quaternion.Euler(0f, 0f, -lean);
            Body.flipX = Actor.Facing < 0f;
            Body.color = _baseColor;
        }

        private void TickDeath()
        {
            if (_deathAge < 0f)
            {
                _deathAge = 0f;
                if (!_chipsSpawned)
                {
                    _chipsSpawned = true;
                    EnemyDeathChips.Spawn(transform.position + Vector3.up * .2f, Body.sprite,
                        Body.sharedMaterial, _baseColor, DeathChips);
                }
            }

            _deathAge += Time.deltaTime;
            float t = Mathf.Clamp01(_deathAge / Mathf.Max(.05f, DeathDuration));
            Body.transform.localScale = _baseScale * (1f - .85f * t);
            Color color = _baseColor;
            color.a = _baseColor.a * (1f - t);
            Body.color = color;
        }
    }
}
