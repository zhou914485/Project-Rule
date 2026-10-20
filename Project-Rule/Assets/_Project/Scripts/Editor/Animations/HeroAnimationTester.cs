using System.Reflection;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using RuleGame.Presentation.Player;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Animations
{
    /// <summary>
    /// 玩家动画的 Play 模式测试窗：把「键盘按不出来」的那几个动作给出按钮。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：<c>Hero.controller</c> 里有两类状态在游戏里根本走不到，
    /// 但它们在图上、也有素材，需要能单独看一眼对不对。</para>
    /// <list type="bullet">
    /// <item><b>受伤 / 死亡</b> —— 由掉血驱动，而场上没有任何东西会打玩家
    /// （敌人 AI 还没做），所以键盘按不出这两个动作。</item>
    /// <item><b>没有入边的状态</b>（下劈 / 扔飞镖 / 拾取链 / 托光球）—— 玩法侧的触发方
    /// （道具系统、下劈输入）还不存在。这些按钮用 <c>CrossFade</c> **强制播放**，
    /// 只为了看动画本身，**不代表它们已经可用**。</item>
    /// </list>
    ///
    /// <para>⚠️ 强制播放正是在绕开状态机 —— 也就是 <see cref="PlayerAnimationDriver"/>
    /// 文档里明说不该在生产代码里做的事。所以播完会被该 state 自己的出边收走，
    /// 中途也可能被 AnyState 打断，这是预期行为，不是 bug。</para>
    ///
    /// <para>伤害按钮默认<b>直接改血</b>，可用开关切回<b>真实链路</b>：
    /// <c>EntityDamageable.ApplyDamage</c> → <c>EntityStatus.ApplyHealthDelta</c> →
    /// <c>OnHealthChanged</c> → 驱动发 <c>Hurt</c> / <c>Die</c> 触发器。
    /// 这正是动画真正监听的那条路径，但受 0.5 秒无敌帧限制。</para>
    /// </remarks>
    public class HeroAnimationTester : EditorWindow
    {
        [MenuItem("RuleGame/Animations/动画测试窗（Play 模式）")]
        public static void Open()
        {
            var window = GetWindow<HeroAnimationTester>("动画测试");
            window.minSize = new Vector2(320f, 460f);
        }

        // ==================================================================
        //  运行时引用（每帧重解，切场景 / 重进 Play 都能自愈）
        // ==================================================================

        private PlayerAnimationDriver _driver;
        private Animator         _animator;
        private EntityStatus     _status;
        private EntityDamageable _damageable;

        /// <summary>
        /// 默认直接改血，**不走承受方**。
        /// </summary>
        /// <remarks>
        /// <para>原因：承受方有 0.5 秒无敌帧，走真实链路时「受伤 −10」之后再点「致死」会被
        /// <b>静默吞掉</b>（按钮按了没反应，很像坏了）。测试工具默认要确定。</para>
        /// <para>想要保真测试就取消勾选 —— 那时会走抗性 + 无敌帧，正是游戏里的行为。</para>
        /// </remarks>
        private bool _bypassReceiver = true;
        private Vector2 _scroll;

        private static readonly BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        // ==================================================================
        //  GUI
        // ==================================================================

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("先进入 Play 模式。本窗口只测运行中的玩家。", MessageType.Info);
                return;
            }

            ResolveReferences();
            if (_driver == null || _animator == null)
            {
                EditorGUILayout.HelpBox(
                    "场景里找不到挂着 PlayerAnimationDriver 的玩家。\n"
                    + "用 Scenes/Test_RuleSystem.unity（里面的 Player 已经接好了）。",
                    MessageType.Warning);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawStatus();
            DrawDamage();
            DrawOneShots();
            DrawUnreachable();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>懒解析，避免 <c>Awake</c> 之外的组件顺序问题（见项目里的踩坑记录）。</summary>
        private void ResolveReferences()
        {
            if (_driver != null && _animator != null) return;

            _driver = Object.FindAnyObjectByType<PlayerAnimationDriver>();
            if (_driver == null) return;

            if (_animator   == null) _animator   = _driver.GetComponentInChildren<Animator>();
            if (_status     == null) _status     = _driver.GetComponent<EntityStatus>();
            if (_damageable == null) _damageable = _driver.GetComponent<EntityDamageable>();
        }

        // ------------------------------------------------------------------

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("当前 state", CurrentStateName());
                EditorGUILayout.TextField("生命",
                    _status != null ? _status.Health.ToString("0.#") + " / " + _status.HealthMax.ToString("0.#") : "（无 EntityStatus）");
                EditorGUILayout.TextField("无敌 / 已死",
                    _damageable != null ? _damageable.IsInvincible + " / " + _damageable.IsDead : "（无 EntityDamageable）");
            }

            EditorGUILayout.Space(6f);
        }

        /// <summary>正在播的剪辑名（去掉 <c>Hero_</c> 前缀）。取第一个层的当前剪辑，够用。</summary>
        private string CurrentStateName()
        {
            AnimatorClipInfo[] clips = _animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0 || clips[0].clip == null) return "（过渡中）";

            string name = clips[0].clip.name;
            return name.StartsWith("Hero_") ? name.Substring(5) : name;
        }

        // ------------------------------------------------------------------

        private void DrawDamage()
        {
            EditorGUILayout.LabelField("受伤 / 死亡（没有按键可达）", EditorStyles.boldLabel);

            _bypassReceiver = EditorGUILayout.ToggleLeft(
                "直接改血（关掉 = 走真实承受方：含抗性 + 0.5s 无敌帧，连点会被吞）", _bypassReceiver);

            if (_status == null || _damageable == null)
            {
                EditorGUILayout.HelpBox("玩家身上缺 EntityStatus 或 EntityDamageable。", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("受伤 −10")) ApplyDamage(10f);
                if (GUILayout.Button("受伤 −25")) ApplyDamage(25f);
                if (GUILayout.Button("致死"))     ApplyDamage(float.MaxValue);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("回满血"))   Heal();
                if (GUILayout.Button("重置（清死亡 / 无敌标记）")) ResetReceiver();
            }

            EditorGUILayout.Space(6f);
        }

        /// <summary>
        /// 施加一次伤害。默认走承受方（含无敌帧与抗性）；勾了跳过就直接改血。
        /// </summary>
        /// <remarks><b>不经 <c>CombatSystem</c></b>：那会先跑伤害规则（可能有「失效」「反转」），
        /// 测动画时反而看不出是动画的问题还是规则把伤害吃掉了。</remarks>
        private void ApplyDamage(float amount)
        {
            if (amount <= 0f) return;

            ClearReactionTriggers();

            if (_bypassReceiver)
            {
                _status.ApplyHealthDelta(-Mathf.Min(amount, _status.HealthMax));
                return;
            }

            _damageable.ApplyDamage(new DamageInfo
            {
                Attacker  = null,
                Target    = _damageable,
                Amount    = amount,
                Knockback = 0f,          // 不推人，方便盯着动画看
            });
        }

        /// <summary>
        /// 清掉上一次留下的待触发触发器。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ Unity 的 Trigger <b>只在过渡被走通时才消耗</b>。若「受伤」触发时人已经在
        /// <c>Hurt</c> 里（<c>AnyState → Hurt</c> 不允许自过渡），那颗触发器就一直挂着；
        /// 之后一旦切到别的状态就被它劫走 —— 实测<b>死亡动画只播 0.07 秒就被切回受伤</b>。</para>
        /// <para>游戏里这条路基本走不到（0.5 秒无敌帧会挡掉窗口内的第二次受伤），
        /// 但测试工具会连着点，所以每次点之前先清干净，保证「一次点击 = 一个动作」。</para>
        /// </remarks>
        private void ClearReactionTriggers()
        {
            if (_animator == null) return;
            _animator.ResetTrigger(PlayerAnimationDriver.ParamHurt);
            _animator.ResetTrigger(PlayerAnimationDriver.ParamDie);
        }

        private void Heal()
        {
            ClearReactionTriggers();
            if (_status != null) _status.ApplyHealthDelta(_status.HealthMax);
        }

        /// <summary>
        /// 把生命回满，并清掉承受方自己记的两个「一次性」标记，好反复测死亡。
        /// </summary>
        /// <remarks>⚠️ 标记是 <see cref="EntityDamageable"/> 的私有运行时字段（无公开 API）。
        /// 用反射是为了不给生产代码加只服务于测试的接口。</remarks>
        private void ResetReceiver()
        {
            Heal();
            if (_damageable == null) return;

            typeof(EntityDamageable).GetField("_died", PrivateInstance)
                ?.SetValue(_damageable, false);
            typeof(EntityDamageable).GetField("_invincibleLeft", PrivateInstance)
                ?.SetValue(_damageable, 0f);
        }

        // ------------------------------------------------------------------

        private void DrawOneShots()
        {
            EditorGUILayout.LabelField("一次性动作（键盘也能触发，这里给快捷方式）", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("起跳"))    Trigger(PlayerAnimationDriver.ParamJump);
                if (GUILayout.Button("二段跳"))  Trigger(PlayerAnimationDriver.ParamAirJump);
                if (GUILayout.Button("蹬墙跳"))  Trigger(PlayerAnimationDriver.ParamWallJump);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("落地"))    Trigger(PlayerAnimationDriver.ParamLand);
                if (GUILayout.Button("出刀"))    Trigger(PlayerAnimationDriver.ParamAttack);
                if (GUILayout.Button("连段"))    Trigger(PlayerAnimationDriver.ParamAttackNext);
                if (GUILayout.Button("重击"))    Trigger(PlayerAnimationDriver.ParamHeavy);
            }

            EditorGUILayout.Space(6f);
        }

        // ------------------------------------------------------------------

        /// <summary>图上**没有入边**、所以游戏里走不到的状态。强制播放只用于看动画。</summary>
        private static readonly string[] UnreachableStates =
        {
            "Throw",
            "PullOut", "PickupOne", "PickupBoth", "PutBack", "OrbHold",
        };

        private void DrawUnreachable()
        {
            EditorGUILayout.LabelField("未接入的状态（图上没有入边）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "这些 state 的素材和连线都在图上，但没有任何过渡指向它们 —— "
                + "玩法侧的下劈输入、道具 / 扔飞镖系统还没做。\n"
                + "按钮用 CrossFade 强制播放，只看动画；播完会被它们自己的出边收走。",
                MessageType.Info);

            for (int i = 0; i < UnreachableStates.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(UnreachableStates[i])) ForcePlay(UnreachableStates[i]);
                    if (i + 1 < UnreachableStates.Length)
                    {
                        string next = UnreachableStates[i + 1];
                        if (GUILayout.Button(next)) ForcePlay(next);
                    }
                }
            }
        }

        // ==================================================================
        //  执行
        // ==================================================================

        private void Trigger(string param)
        {
            if (_animator == null) return;

            // 先清再设：同一个触发器若上次没被消耗会挂着，连点时会「隔一次才响」。
            _animator.ResetTrigger(param);
            _animator.SetTrigger(param);
        }

        private void ForcePlay(string stateName)
        {
            if (_animator != null) _animator.CrossFade(stateName, 0.05f, 0);
        }
    }
}
