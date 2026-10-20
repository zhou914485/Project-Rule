using System.Collections.Generic;
using RuleGame.Presentation.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace RuleGame.Editor.Animations
{
    /// <summary>
    /// 从一份**声明**重建玩家动画状态图（<c>Hero.controller</c>）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用脚本建图</b>：<c>.controller</c> 在 git 里是二进制，没法 diff、没法 review ——
    /// 「谁把哪条过渡删了」看提交记录根本看不出来。把整张图写成这份 C# 声明之后，
    /// 「谁在什么时候切到谁」就和别的代码一样能 review、能 blame。</para>
    ///
    /// <para><b>它是真相源</b>：它是「用脚本生成、脚本进仓库」形态 —— 你在窗口里手动调完，
    /// 下次要重构就重跑本脚本。⚠️ <b>重跑会覆盖窗口里的手改</b>，所以调参请改这份声明。</para>
    ///
    /// <para>参数名引用 <see cref="PlayerAnimationDriver"/> 上的常量，图和代码不会各说各话。</para>
    /// </remarks>
    public static class HeroAnimatorBuilder
    {
        private const string ControllerPath = "Assets/_Project/Art/Animations/Hero/Hero.controller";
        private const string ClipFolder     = "Assets/_Project/Art/Animations/Hero";

        // 参与重建的全部 state（名字 = 剪辑名去掉 Hero_ 前缀，也是动画参数要用的名字）
        // ⚠️ 没有 Walk：角色加速快（_groundAccel=200，0→满速约 0.04 秒），
        //    Walk 只会停**一个物理帧**就被 Run 顶掉 —— 看起来就是一次卡顿，不如不要。
        //    剪辑 Hero_Walk.anim 还在，将来若加了「按住走路」再把它加回来。
        private static readonly string[] StateNames =
        {
            // 地面
            "Idle", "Run",
            // 空中
            // ⚠️ Airborne 是**二段跳**那一下的动作（5 帧），不是滞空 —— 它是一次性动作。
            //     滞空用 Fall（美术的 air 六帧序列）。
            "JumpStart", "Airborne", "Fall",
            // Weightless 走常量：PlayerAnimationDriver 靠这个名字判断「当前是不是在播那条
            // 重力翻转动画」（要靠它压住 flipY，抵消根对象那 180°，好让剪辑**按原画**播 ——
            // 那条图自己演的就是「正立 → 倒立」），两边写死字符串容易只改一处。
            PlayerAnimationDriver.StateWeightless,
            "WallSlide", "WallJump",
            // 其它机动
            // Dash 是冲刺专属剪辑（蓄力→前倾→滑行→收势，7 帧 @24fps），2026-10-20 起不再借 Push。
            // Push 保留在图上但暂时没有入口 —— 推箱子将来要播时再连线。
            "Dash", "Push", "Land",
            // 反应
            "Hurt", "Die",
            // 战斗
            // ⚠️ 没有 DrawSword：按攻击**直接**进 Slash1，不先播拔剑（2026-10-16 定的）。
            //    剪辑 Hero_DrawSword.anim 已一并删除。
            "Slash1", "AttackCd1", "Slash2", "AttackCd2", "SlashDown", "Sheathe", "Throw",
            // 交互
            "PullOut", "PutBack", "OrbHold", "PickupOne", "PickupBoth",
        };

        // ==================================================================
        //  入口
        // ==================================================================

        [MenuItem("RuleGame/Animations/重建 Hero 动画图（会覆盖窗口里的手改）")]
        public static void Rebuild()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError("[HeroAnimatorBuilder] 找不到 " + ControllerPath);
                return;
            }

            // 每次重建都先清空,保证结果是「声明决定」而不是「声明 + 上次残留」。
            ClearParameters(controller);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            ClearStates(machine);
            ClearAnyStateTransitions(machine);

            // CD 的定格剪辑要先保证存在 —— CreateStates 会去按名字加载它们。
            EnsureCdHoldClips();

            Dictionary<string, AnimatorState> states = CreateStates(machine);

            AddParameters(controller);

            // ⚠️ 贴墙直达边必须**第一个**连：Unity 按**列表顺序**取第一条条件满足的过渡，
            //    后加的会被先加的抢走 —— 「离地 → Fall」只要求 Grounded==false，
            //    它一旦排在前面，贴墙就永远轮不到（实测：从 JumpStart 贴墙要等 0.53 秒、
            //    从 Airborne 要等 0.65 秒，屏幕上一直在播跳跃/空中动作）。
            LinkWallSlide(states);

            LinkLocomotion(states);
            LinkAir(states);
            LinkActionStates(states);
            LinkReactions(states);
            LinkCombat(states);
            LinkInteract(states);
            LinkAnyState(machine, states);
            ProtectReactions(controller);

            machine.defaultState = states["Idle"];

            // ⚠️ AnimatorControllerLayer 是**值类型**，layers[0].defaultWeight = 1 改的是临时副本、
            //    不会写回 —— 必须整个数组取出来改完再赋回去。
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].defaultWeight = 1f;                 // 磁盘上原本是 0
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[HeroAnimatorBuilder] 已重建 Hero.controller："
                      + StateNames.Length + " 个 state、"
                      + controller.parameters.Length + " 个参数。");
        }

        [MenuItem("RuleGame/Animations/修复 Hero 受伤动画打断规则（保留现有动画图）")]
        public static void RepairReactionInterruptions()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new System.InvalidOperationException("找不到 " + ControllerPath);
            ProtectReactions(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
        }

        private static void ProtectReactions(AnimatorController controller)
        {
            string parameter = PlayerAnimationDriver.ParamReacting;
            if (!System.Array.Exists(controller.parameters, item => item.name == parameter))
                controller.AddParameter(parameter, AnimatorControllerParameterType.Bool);
            foreach (var transition in controller.layers[0].stateMachine.anyStateTransitions)
            {
                var destination = transition.destinationState;
                if (destination == null || destination.name == "Hurt" || destination.name == "Die") continue;
                if (!System.Array.Exists(transition.conditions, condition => condition.parameter == parameter))
                    transition.AddCondition(AnimatorConditionMode.IfNot, 0f, parameter);
            }
        }

        // ==================================================================
        //  参数
        // ==================================================================

        private static void AddParameters(AnimatorController c)
        {
            // 持续条件：每帧重写
            c.AddParameter(PlayerAnimationDriver.ParamSpeed,          AnimatorControllerParameterType.Float);
            c.AddParameter(PlayerAnimationDriver.ParamMoving,         AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamGrounded,       AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamDashing,        AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamWallSliding,    AnimatorControllerParameterType.Bool);
            c.AddParameter(PlayerAnimationDriver.ParamGravityFlipped, AnimatorControllerParameterType.Bool);

            // 算出来的：重力反转 **且** 本轮那条翻转动画还没播过。
            // 与 GravityFlipped 分开是必须的，理由见 ParamPlayFlip 的说明。
            c.AddParameter(PlayerAnimationDriver.ParamPlayFlip,       AnimatorControllerParameterType.Bool);

            c.AddParameter(PlayerAnimationDriver.ParamRising,         AnimatorControllerParameterType.Bool);

            // 一次性事件：由玩法层的边沿事件发 Trigger
            c.AddParameter(PlayerAnimationDriver.ParamJump,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamAirJump,  AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamWallJump, AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamLand,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamHurt,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamDie,      AnimatorControllerParameterType.Trigger);

            // 攻击：起手一个、接续一个。分成两个是因为 AnyState → 拔剑的优先级高于一切，
            // 共用一个的话连段中途再按会从拔剑重来。
            c.AddParameter(PlayerAnimationDriver.ParamAttack,     AnimatorControllerParameterType.Trigger);
            c.AddParameter(PlayerAnimationDriver.ParamAttackNext, AnimatorControllerParameterType.Trigger);

            // 重击：独立的一条，不共用 Attack —— 那条走的是两刀连段（Slash1 → CD → Slash2），
            // 重击是另一套单发动作。
            c.AddParameter(PlayerAnimationDriver.ParamHeavy,      AnimatorControllerParameterType.Trigger);
        }

        private static void ClearParameters(AnimatorController c)
        {
            while (c.parameters.Length > 0) c.RemoveParameter(c.parameters[0]);
        }

        // ==================================================================
        //  State 布局
        // ==================================================================

        private static Dictionary<string, AnimatorState> CreateStates(AnimatorStateMachine sm)
        {
            // 手写坐标：图是给人看的，横平竖直比「自动排」可读得多。
            var layout = new Dictionary<string, Vector3>
            {
                { "Idle",       new Vector3(   0f,    0f) },
                { "Run",        new Vector3( 220f,    0f) },
                { "Land",       new Vector3( 440f,    0f) },

                { "JumpStart",  new Vector3(   0f, -130f) },
                { "Airborne",   new Vector3( 220f, -130f) },
                { "Fall",       new Vector3( 220f,  -70f) },
                { "Weightless", new Vector3( 440f, -130f) },
                { "WallSlide",  new Vector3( 660f, -130f) },
                { "WallJump",   new Vector3( 880f, -130f) },

                { "Push",       new Vector3(   0f, -260f) },
                { "Dash",       new Vector3(-220f, -260f) },

                { "Hurt",       new Vector3( 220f, -260f) },
                { "Die",        new Vector3( 440f, -260f) },

                { "Slash1",     new Vector3(   0f, -390f) },
                { "AttackCd1",  new Vector3( 220f, -390f) },
                { "Slash2",     new Vector3( 440f, -390f) },
                { "AttackCd2",  new Vector3( 660f, -390f) },
                { "SlashDown",  new Vector3( 880f, -390f) },
                { "Sheathe",    new Vector3(1100f, -390f) },
                { "Throw",      new Vector3(1320f, -390f) },

                { "PullOut",    new Vector3(   0f, -520f) },
                { "PickupOne",  new Vector3( 220f, -520f) },
                { "PickupBoth", new Vector3( 440f, -520f) },
                { "OrbHold",    new Vector3( 660f, -520f) },
                { "PutBack",    new Vector3( 880f, -520f) },
            };

            var found = new Dictionary<string, AnimatorState>();
            foreach (string name in StateNames)
            {
                Vector3 pos = layout.TryGetValue(name, out var p) ? p : Vector3.zero;

                AnimatorState state = sm.AddState(name, pos);
                state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    ClipFolder + "/Hero_" + name + ".anim");

                // 失重的飘浮循环放快一倍。
                // ⚠️ 设 state 的 speed，**不是**改剪辑的 frameRate —— 剪辑是美术资产，
                //    改它会波及所有引用它的地方；这里只想让这张图里播得快一点。
                if (name == PlayerAnimationDriver.StateWeightless) state.speed = 2f;

                if (state.motion == null)
                    Debug.LogWarning("[HeroAnimatorBuilder] 剪辑缺失：Hero_" + name + ".anim（该 state 会是空的）");

                found[name] = state;
            }

            sm.anyStatePosition = new Vector3(-220f, -130f);
            return found;
        }

        private static void ClearStates(AnimatorStateMachine sm)
        {
            ChildAnimatorState[] existing = sm.states;
            for (int i = existing.Length - 1; i >= 0; i--) sm.RemoveState(existing[i].state);
        }

        private static void ClearAnyStateTransitions(AnimatorStateMachine sm)
        {
            AnimatorStateTransition[] any = sm.anyStateTransitions;
            for (int i = any.Length - 1; i >= 0; i--) sm.RemoveAnyStateTransition(any[i]);
        }

        // ==================================================================
        //  CD 定格剪辑（两刀之间那 0.1 秒）
        // ==================================================================
        //
        // 为什么非得给 CD 一个剪辑：**motion 为 null 的 state，归一化时间不推进**，
        // hasExitTime 永远触发不了 —— 人会直接卡死在 CD 里。
        //
        // CD 期间演的是**待机姿势**（`Hero_Idle` 的第一帧），**不是上一刀的最后一帧**：
        // 两刀之间要读成「收回架势、站好等下一刀」，而不是「定格在半挥出去的地方」。
        //
        // ⚠️ 帧是从**上游剪辑**现取的，不写死 sprite 引用：美术重导出 Idle 之后，
        //    这里自动跟着变，不用回来改。

        /// <summary>CD 定格多少秒。和 <c>LinkCombat</c> 里那几条 <c>AttackCdN → …</c> 的 exitTime 是同一件事。</summary>
        private const float CdHoldSeconds = 0.2f;

        /// <summary>CD 定格剪辑的采样率。跟美术那批剪辑保持一致。</summary>
        private const float CdFrameRate = 12f;

        /// <summary>CD 期间定格用的是哪个序列的第几帧（0 起数）。</summary>
        private const string CdPoseClip  = "Idle";
        private const int    CdPoseFrame = 0;

        private static void EnsureCdHoldClips()
        {
            EnsureCdHoldClip("AttackCd1", CdPoseClip, CdPoseFrame);
            EnsureCdHoldClip("AttackCd2", CdPoseClip, CdPoseFrame);
        }

        /// <summary>把 <paramref name="sourceClip"/> 的第 <paramref name="frameIndex"/> 帧定格成一个 <see cref="CdHoldSeconds"/> 秒的剪辑。</summary>
        private static void EnsureCdHoldClip(string targetName, string sourceClip, int frameIndex)
        {
            string sourcePath = ClipFolder + "/Hero_" + sourceClip + ".anim";
            string targetPath = ClipFolder + "/Hero_" + targetName + ".anim";

            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sourcePath);
            if (source == null)
            {
                Debug.LogWarning("[HeroAnimatorBuilder] 找不到源剪辑 " + sourcePath + "，CD 定格跳过");
                return;
            }

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(source, binding);
            if (keys == null || keys.Length == 0)
            {
                Debug.LogWarning("[HeroAnimatorBuilder] " + sourceClip + " 没有 sprite 曲线，CD 定格跳过");
                return;
            }

            // 按时间排好再取第 frameIndex 帧（不假设 GetObjectReferenceCurve 一定按时间排好）。
            var sorted = new List<ObjectReferenceKeyframe>(keys);
            sorted.Sort((a, b) => a.time.CompareTo(b.time));
            int index = Mathf.Clamp(frameIndex, 0, sorted.Count - 1);

            var pose = sorted[index].value as Sprite;
            if (pose == null)
            {
                Debug.LogWarning("[HeroAnimatorBuilder] " + sourceClip + " 第 " + index + " 帧是空的，CD 定格跳过");
                return;
            }

            // 已存在就复用（保住 GUID，控制器 / 场景引用不会断），只覆盖曲线。
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = "Hero_" + targetName, frameRate = CdFrameRate };
                AssetDatabase.CreateAsset(clip, targetPath);
            }
            else if (!Mathf.Approximately(clip.frameRate, CdFrameRate))
            {
                clip.frameRate = CdFrameRate;   // 上一版建错了帧率也要纠回来
            }

            // ⚠️ Unity 的 <c>AnimationClip.length</c> 是**曲线算出来的**，等于
            //    「最后一帧 + 一个采样间隔」，写 m_StopTime 也压不下去
            //    （美术的 Slash1 就是这样：最后一帧 0.3333 + 1/12 = 0.4167 = 它的长度）。
            //    所以最后一帧要放在「目标时长 − 一个采样间隔」上，长度才正好落在目标上。
            float lastKeyTime = CdHoldSeconds - 1f / CdFrameRate;

            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f,          value = pose },
                new ObjectReferenceKeyframe { time = lastKeyTime, value = pose },
            });

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.stopTime = CdHoldSeconds;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            Debug.Log("[HeroAnimatorBuilder] 生成 Hero_" + targetName + ".anim：定格 "
                      + sourceClip + " 最后一帧 " + CdHoldSeconds.ToString("0.###") + " 秒"
                      + "（实际长度 " + clip.length.ToString("0.###") + "）");
        }

        // ==================================================================
        //  连线的两个小工具
        // ==================================================================

        /// <summary>拉一条 <paramref name="from"/> → <paramref name="to"/> 的过渡。</summary>
        private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to,
                                                    float duration = 0.1f,
                                                    bool exitTime = false, float at = 1f)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime       = exitTime;
            t.exitTime          = at;
            t.hasFixedDuration  = true;      // duration 按「秒」算，不按源 state 的比例
            t.duration          = duration;
            t.canTransitionToSelf = false;
            return t;
        }

        /// <summary>给一条过渡加条件。Trigger 用 <paramref name="value"/> = true 即可。</summary>
        private static void When(AnimatorStateTransition t, AnimatorConditionMode mode,
                                 string param, float value = 0f)
            => t.AddCondition(mode, value, param);

        // ==================================================================
        //  连线：地面运动
        // ==================================================================

        private static void LinkLocomotion(Dictionary<string, AnimatorState> s)
        {
            // 地面只有 **Idle ⇄ Run** 两档，没有 Walk（理由见 StateNames 上面的说明）。
            //
            // 条件用 **Moving（有没有按方向键）**，不是速度。
            //
            // 为什么不用速度：速度会被三种情况骗到 ——
            //   ① 冰面滑行：松开方向后人还在滑，按速度切就会一直播 Run；
            //   ② 被击退 / 冲刺收尾：没人按方向，横向速度却很大；
            //   ③ 起步那一帧：速度还是 0，但人已经按下了，动画会慢半拍。
            // 用输入就没有这些歧义：**动画跟着「玩家在做什么」，不是跟着「物理结果」**。
            //
            // 另外也不该用 Blend Tree：Blend Tree 靠「按权重插值曲线」工作，而精灵帧是对象引用
            // 曲线（Sprite），Unity 对它不做插值 —— 落在两档之间就是硬切。transition + 交叉淡入才对。
            AnimatorState idle = s["Idle"], run = s["Run"];

            When(Link(idle, run,  0.08f), AnimatorConditionMode.If,    PlayerAnimationDriver.ParamMoving);
            When(Link(run,  idle, 0.10f), AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamMoving);
        }

        // ==================================================================
        //  连线：空中 / 贴墙
        // ==================================================================

        // ==================================================================
        //  连线：贴墙直达
        // ==================================================================

        /// <summary>各空中的状态 <b>直达</b> WallSlide 的边 —— 一贴住墙就切，不等一次性动作播完。</summary>
        /// <remarks>
        /// <para><b>为什么需要它</b>：JumpStart 与 Airborne 原本只有「→ Fall」一条出口，
        /// 而且带 Has Exit Time（必须播到 90%/100%）。于是贴到墙之后还得等它们播完才能走到
        /// Fall、再过渡进 WallSlide。实测：从 JumpStart 出发要 <b>0.53 秒</b>、
        /// 从 Airborne 要 <b>0.65 秒</b> —— 那段时间屏幕上一直播跳跃/空中动作，
        /// 看起来就是「贴了墙却没有贴墙动画」。</para>
        ///
        /// <para><b>为什么不会误伤跳跃</b>：WallSliding 来自 VelocitySolver，它要求
        /// <b>不在上升</b>（alongUp &lt;= 0）且按住朝墙方向。起跳/二段跳的上升段不满足，
        /// 所以跳跃动作照常播完；只有真正落到墙上、且按着方向键时才切走。</para>
        ///
        /// <para>⚠️ <b>必须在 LinkLocomotion / LinkAir 之前调用</b> —— Unity 取列表里
        /// 第一条条件满足的过渡，「离地 → Fall」只要求 Grounded==false，排在前面就会抢先。</para>
        /// </remarks>
        private static void LinkWallSlide(Dictionary<string, AnimatorState> s)
        {
            foreach (string name in new[] { "Idle", "Run", "JumpStart", "Airborne" })
            {
                When(Link(s[name], s["WallSlide"], 0.08f),
                     AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallSliding);
            }
        }

        /// <summary>
        /// 进入 Weightless 的边一律<b>硬切</b>（<c>dur = 0</c>）。
        /// </summary>
        /// <remarks>
        /// 这条动画自己演的就是「正立 → 倒立」的翻滚，任何长度的淡入都会让画面先留一会儿旧姿势，
        /// 表现成「重力已经反转、人都被推着往上飞了，动画还在播正常下落」。
        /// <para>⚠️ 只压<b>进入</b>方向。反向（<c>Weightless → Fall</c>）留着 0.12 —— 那一下是
        /// 从倒立回到正常下落，两个姿势差得远，硬切反而会闪。</para>
        /// </remarks>
        private const float WeightlessEnterDuration = 0f;

        private static void LinkAir(Dictionary<string, AnimatorState> s)
        {
            // ⚠️ 这里的底座是 **Fall（滞空）**，不是 Airborne。
            //    Airborne 是**二段跳那一下**的动作，属于「一次性」，由 AnyState 的 AirJump 触发。

            // 离地 → 滞空（走下去、或在空中待着）。
            //
            // ⚠️ 按 **PlayFlip**（= 重力反转 且 **本轮**那条翻转动画还没播过）分流，
            //    不要统一绕一趟 Fall：那样要连吃两次过渡（进 Fall + 0.12 进 Weightless），
            //    表现是「人已经被重力推着往上飞了，动画还在播正常下落」。
            //    分流之后反转时**离地即进 Weightless**。
            //
            //    ⚠️ 这里判的是 PlayFlip，**不是 GravityFlipped**：本轮播过之后重力照样是反的，
            //       但那两条边要走 Fall（正常滞空图 + 根对象已经转过去 = 本来就是倒立的）。
            //       PlayFlip 的正反两面天然互斥，所以先后顺序无所谓。
            //
            //    锁的粒度是「一次重力反转」：重力一恢复成正常方向，驱动就把锁清掉 ——
            //    下次再反转又是一个新的「第一次」，照样播。理由见 PlayerAnimationDriver._flipPlayed。
            foreach (string name in new[] { "Idle", "Run" })
            {
                AnimatorStateTransition toWeightless = Link(s[name], s["Weightless"], WeightlessEnterDuration);
                When(toWeightless, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGrounded);
                When(toWeightless, AnimatorConditionMode.If,    PlayerAnimationDriver.ParamPlayFlip);

                AnimatorStateTransition toFall = Link(s[name], s["Fall"], 0.10f);
                When(toFall, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGrounded);
                When(toFall, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamPlayFlip);
            }

            // 起跳（地面跳）与二段跳统一由 AnyState 的 Jump / AirJump 触发接管，这里不重复连。

            // JumpStart 是 4 帧的一次性动作：靠 Has Exit Time 播完再进滞空，
            // 这样就**不会冻在最后一帧** —— 旧的代码驱动正是死在这里。
            // 两条出边：上升结束时提前走 / 播完兜底走。
            When(Link(s["JumpStart"], s["Fall"], 0.12f, exitTime: true, at: 0.9f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamRising);
            Link(s["JumpStart"], s["Fall"], 0.12f, exitTime: true, at: 1f);

            // 二段跳（Airborne）也是一次性动作：翻完回滞空。
            Link(s["Airborne"], s["Fall"], 0.12f, exitTime: true, at: 1f);

            // 贴墙下滑 ↔ 滞空
            // ⚠️ Fall→WallSlide 必须排在 Fall→Weightless **前面**：两者条件会同时成立
            //    （重力已反转 + 正在贴墙），排在后面就被 Weightless 抢走 —— 实测那样贴墙会播"失重"。
            When(Link(s["Fall"], s["WallSlide"], 0.08f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallSliding);
            When(Link(s["WallSlide"], s["Fall"], 0.08f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamWallSliding);

            // 重力反转 ↔ 正常
            // ⚠️ 进 Weightless 判的是 PlayFlip（要「没播过」）；出 Weightless 判的是
            //    GravityFlipped —— 重力一恢复就得走，跟那次机会用没用掉无关。
            When(Link(s["Fall"], s["Weightless"], WeightlessEnterDuration),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamPlayFlip);
            When(Link(s["Weightless"], s["Fall"], 0.12f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGravityFlipped);

            // 蹬墙上升时保留翻滚动画；上升结束后按贴墙输入进入下滑或滞空。
            When(Link(s["WallJump"], s["WallSlide"], 0.08f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallSliding);
            When(Link(s["WallJump"], s["Fall"], 0.12f),
                 AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamRising);
            Link(s["WallJump"], s["Fall"], 0.12f, exitTime: true, at: 1f);
        }

        // ==================================================================
        //  连线：冲刺 / 落地（一次性，靠 Has Exit Time 收尾）
        // ==================================================================

        private static void LinkActionStates(Dictionary<string, AnimatorState> s)
        {
            // ⚠️ 退出必须**同时**要求 Dashing == false。只看接地的话，进入 Dash 的那一帧
            //    （冲刺刚开始、Dashing 还是 true）就会立刻被这条过渡弹出去 —— 表现是「冲刺没有动画」。
            AnimatorStateTransition dashOut = Link(s["Dash"], s["Fall"], 0.08f);
            When(dashOut, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamDashing);
            When(dashOut, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamGrounded);

            dashOut = Link(s["Dash"], s["Idle"], 0.08f);
            When(dashOut, AnimatorConditionMode.IfNot, PlayerAnimationDriver.ParamDashing);
            When(dashOut, AnimatorConditionMode.If, PlayerAnimationDriver.ParamGrounded);

            // 落地：4 帧的一次性动作，播完按当前速度回到地面那一档
            When(Link(s["Land"], s["Run"], 0.08f, exitTime: true, at: 0.85f),
                 AnimatorConditionMode.Greater, PlayerAnimationDriver.ParamSpeed, 0.35f);
            Link(s["Land"], s["Idle"], 0.08f, exitTime: true, at: 0.85f);
        }

        // ==================================================================
        //  连线：受击 / 死亡
        // ==================================================================

        private static void LinkReactions(Dictionary<string, AnimatorState> s)
        {
            Link(s["Hurt"], s["Idle"], 0.1f, exitTime: true, at: 1f);
            // Die 没有出边：它是终点，播完停在那里
        }

        // ==================================================================
        //  连线：战斗连段
        // ==================================================================
        //
        // 结构：**按攻击直接挥第一刀 → 0.1 秒 CD → （第一刀期间按过就）第二刀 → 0.1 秒 CD → 回 Idle**。
        // 起手由 AnyState 的 Attack 触发（见 LinkAnyState），接续靠 AttackNext。
        // 没有拔剑、没有收剑 —— 那两段已经摘掉了。

        private static void LinkCombat(Dictionary<string, AnimatorState> s)
        {
            // 第一刀：播完进 CD1。
            Link(s["Slash1"], s["AttackCd1"], 0.05f, exitTime: true, at: 1f);

            // CD1：**同时**要求「这一格播完（= 0.1 秒）」和「有排队输入」才接第二刀。
            // ⚠️ 带条件的那条排在前面 —— Unity 按列表顺序取第一条满足的。
            // ⚠️ 两条都带 exitTime：这样「第一刀期间按的那一下」也要**等满** 0.1 秒。
            //    不带的话，触发器一进 AttackCd1 就把它立刻带走，CD 等于没有。
            When(Link(s["AttackCd1"], s["Slash2"], 0.05f, exitTime: true, at: 1f),
                 AnimatorConditionMode.If, PlayerAnimationDriver.ParamAttackNext);
            Link(s["AttackCd1"], s["Idle"], 0.1f, exitTime: true, at: 1f);

            // 第二刀：播完进 CD2，之后一律回 Idle（连段上限就是 2 刀）。
            Link(s["Slash2"], s["AttackCd2"], 0.05f, exitTime: true, at: 1f);
            Link(s["AttackCd2"], s["Idle"], 0.1f, exitTime: true, at: 1f);

            // 重击：入边在 LinkAnyState（下 + 攻击），播完收剑。
            Link(s["SlashDown"], s["Sheathe"], 0.05f, exitTime: true, at: 1f);

            Link(s["Sheathe"], s["Idle"], 0.1f, exitTime: true, at: 1f);
            Link(s["Throw"], s["Idle"], 0.1f, exitTime: true, at: 1f);
        }

        // ==================================================================
        //  连线：拾取 / 交互
        // ==================================================================
        //
        // ⚠️ 同样只有接收端 —— 道具系统还没立项，没有东西会发 Interact。

        private static void LinkInteract(Dictionary<string, AnimatorState> s)
        {
            Link(s["PullOut"], s["PickupOne"], 0.05f, exitTime: true, at: 1f);
            Link(s["PickupOne"], s["PickupBoth"], 0.05f, exitTime: true, at: 1f);
            Link(s["PickupBoth"], s["PutBack"], 0.05f, exitTime: true, at: 1f);
            Link(s["PutBack"], s["Idle"], 0.1f, exitTime: true, at: 1f);

            Link(s["OrbHold"], s["PutBack"], 0.1f, exitTime: true, at: 1f);
        }

        // ==================================================================
        //  连线：AnyState（打断一切的那几条）
        // ==================================================================
        //
        // ⚠️ **顺序 = 优先级**：Unity 按列表顺序求值，先满足的赢。
        //    死亡 > 受击 > 冲刺 > 蹬墙 > 起跳 > 落地 > 攻击（轻击 / 重击）。
        //    攻击排在最后：机动动作优先，免得一刀把冲刺 / 蹬墙取消掉。

        private static void LinkAnyState(AnimatorStateMachine sm, Dictionary<string, AnimatorState> s)
        {
            AnimatorStateTransition t;

            t = sm.AddAnyStateTransition(s["Die"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamDie);

            t = sm.AddAnyStateTransition(s["Hurt"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamHurt);

            t = sm.AddAnyStateTransition(s["Dash"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamDashing);

            t = sm.AddAnyStateTransition(s["WallJump"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamWallJump);

            t = sm.AddAnyStateTransition(s["JumpStart"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamJump);

            // 二段跳：和起跳分开（两个动作不一样），所以排在起跳后面独立一条。
            // 两者不会同时触发 —— 玩法层一次跳跃只发其中一个事件。
            t = sm.AddAnyStateTransition(s["Airborne"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamAirJump);

            t = sm.AddAnyStateTransition(s["Land"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamLand);

            // 轻击：按攻击直接挥第一刀（没有拔剑那一段了）。
            t = sm.AddAnyStateTransition(s["Slash1"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamAttack);

            // 重击（下 + 攻击）。和轻击并列排在最末：它也是攻击，机动动作优先。
            t = sm.AddAnyStateTransition(s["SlashDown"]);
            t.hasExitTime = false; t.duration = 0.05f; t.hasFixedDuration = true; t.canTransitionToSelf = false;
            When(t, AnimatorConditionMode.If, PlayerAnimationDriver.ParamHeavy);
        }
    }
}
