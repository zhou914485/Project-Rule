using System;
using System.Collections;
using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.UI.Launch
{
    /// <summary>
    /// 解锁演出的全屏黑幕：暂停游戏，把「本次获得的能力 / 词条」写在黑幕上，
    /// 等玩家按下新的输入之后再放回画面、恢复游戏。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不再是一行 toast</b>：解锁是这一关最重要的正反馈，一条会自己消失的提示
    /// 既容易被错过，也会和别的事件抢注意力。改成「黑幕 + 暂停 + 等玩家按键」，
    /// 想错过都难，也顺手把「这一下归你」讲清楚。</para>
    /// <para><b>暂停只有两条腿</b>（照 <c>PauseMenuPresenter</c> 的老规矩）：
    /// <see cref="TimeManager.SetPaused"/> 停物理步进与规则时钟 +
    /// <see cref="EntityStatus.SetPaused"/> 冻结冷却这类按帧推进的状态。
    /// <b>绝不碰 <c>Time.timeScale</c></b>；进入时记旧值、退出时恢复，
    /// 免得把轮盘 / 存档过场的暂停态一起放掉。</para>
    /// <para><b>「按任意键」只认按下边沿</b>：<see cref="PlayerController.AnyGameplayInputThisFrame"/>
    /// 不受输入门禁影响（门禁正被本组件拿着），过场里那套「按任意键继续」的同一个入口。
    /// ESC / 手柄 Start 这里也算「继续」—— 黑幕上按 ESC 不该再弹一层暂停菜单；
    /// <c>PauseMenuPresenter.AnyOverlayOpen()</c> 认 <see cref="IsShowing"/>，会让路。</para>
    /// <para><b>读档不播</b>：<c>PlayerProgress.RestoreUnlocks</c> 期间不发解锁事件
    /// （那不是「刚获得」，是「本来就有」），否则读档会被一整串黑幕堵住。</para>
    /// <para>黑幕住在生命 / 能量条那份 UIDocument 里（sorting 10）：盖住 HUD 与世界，
    /// 主菜单（1000）仍然可以压在上面。同一次拿到多项时排队，一条按一下。</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class AbilityUnlockOverlay : MonoBehaviour
    {
        [Tooltip("玩家控制器；留空自动找。用来取输入门禁与「有没有新的玩法输入」。")]
        [SerializeField] private PlayerController _player;

        [Tooltip("玩家的解锁账本；留空自动找。解锁事件只从这里来。")]
        [SerializeField] private PlayerProgress _progress;

        [Tooltip("黑幕淡入时长（秒，非缩放时间）。")]
        [SerializeField, Min(0f)] private float _fadeInSeconds = 0.25f;

        [Tooltip("黑幕淡出时长（秒，非缩放时间）。")]
        [SerializeField, Min(0f)] private float _fadeOutSeconds = 0.2f;

        [Tooltip("淡入结束后再等这一会儿才认「继续」—— 触发奖励的那一下按键不能被当成继续。")]
        [SerializeField, Min(0f)] private float _inputGraceSeconds = 0.15f;

        /// <summary>一条待展示的解锁信息。</summary>
        private readonly struct Notice
        {
            public readonly string Kicker;
            public readonly string Headline;
            public readonly string Description;

            public Notice(string kicker, string headline, string description)
            {
                Kicker = kicker;
                Headline = headline;
                Description = description;
            }
        }

        // 与 RuleWheelPresenter 的扇面文字保持一致（枚举顺序即下标）。
        private static readonly string[] AttributeWords = { "重力", "时间", "摩擦", "质量", "伤害/生命", "因果" };
        private static readonly string[] RelationWords = { "反转", "失效", "恒定", "增强", "削弱" };

        private readonly Queue<Notice> _pending = new Queue<Notice>();

        private VisualElement _screen;
        private Label _kicker;
        private Label _headline;
        private Label _description;
        private Label _hint;

        private PlayerController _boundPlayer;
        private PlayerProgress _boundProgress;
        private EntityStatus _status;
        private IDisposable _inputLease;

        private float _nextBindCheck;
        private bool _showing;
        private bool _ownsPause;
        private bool _pausedBefore;
        private bool _statusPausedBefore;
        private bool _bound;

        /// <summary>黑幕此刻是否显示（暂停菜单 / 其它模态据此让路）。</summary>
        public bool IsShowing => _showing;

        /// <summary>还在排队的解锁条数（调试 / 验证用）。</summary>
        public int PendingCount => _pending.Count;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void OnEnable() => Bind();

        /// <summary>取元素 + 挂解锁事件。UIDocument 与本组件同物体时先后不保证，所以也放 Start 再试一次。</summary>
        private void Bind()
        {
            if (_bound) return;

            UIDocument document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root == null) return;

            _screen = root.Q("abilityScreen");
            _kicker = root.Q<Label>("abilityKicker");
            _headline = root.Q<Label>("abilityName");
            _description = root.Q<Label>("abilityDesc");
            _hint = root.Q<Label>("abilityHint");

            if (_screen == null || _headline == null)
            {
                Debug.LogError("[AbilityUnlock] PlayerVitals.uxml 缺少 abilityScreen / abilityName —— 解锁黑幕不会出现。", this);
                enabled = false;
                return;
            }

            _bound = true;
            HideImmediately();
            if (_hint != null) _hint.text = "按任意键继续";
        }

        private void Start()
        {
            Bind();
            BindRefs();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            _pending.Clear();
            _showing = false;
            HideImmediately();
            ReleasePause();
            UnbindProgress();

            // 再启用时 UIDocument 会重建一棵树：旧的元素引用全部作废，得重新取一次。
            _screen = null;
            _kicker = null;
            _headline = null;
            _description = null;
            _hint = null;
            _bound = false;
        }

        private void Update()
        {
            if (!_bound) Bind();

            // 玩家 / 账本可能比 HUD 晚生成：每秒重试一次，别每帧 Find。
            if ((_boundPlayer == null || _boundProgress == null) && Time.unscaledTime >= _nextBindCheck)
            {
                _nextBindCheck = Time.unscaledTime + 1f;
                BindRefs();
            }
        }

        // ==================================================================
        //  绑定
        // ==================================================================

        private void BindRefs()
        {
            if (_boundPlayer == null)
                _boundPlayer = _player != null ? _player : FindAnyObjectByType<PlayerController>();

            if (_boundProgress != null) return;

            _boundProgress = _progress != null
                ? _progress
                : _boundPlayer != null
                    ? _boundPlayer.GetComponent<PlayerProgress>()
                    : FindAnyObjectByType<PlayerProgress>();
            if (_boundProgress == null) return;

            _boundProgress.AbilityUnlocked += OnAbilityUnlocked;
            _boundProgress.AttributeUnlocked += OnAttributeUnlocked;
            _boundProgress.RelationUnlocked += OnRelationUnlocked;
        }

        private void UnbindProgress()
        {
            if (_boundProgress == null) return;

            _boundProgress.AbilityUnlocked -= OnAbilityUnlocked;
            _boundProgress.AttributeUnlocked -= OnAttributeUnlocked;
            _boundProgress.RelationUnlocked -= OnRelationUnlocked;
            _boundProgress = null;
        }

        private void OnAbilityUnlocked(PlayerAbility ability)
            => Enqueue(new Notice("获得新能力", AbilityName(ability), AbilityDescription(ability)));

        private void OnAttributeUnlocked(AttributeId attribute)
            => Enqueue(new Notice("获得规则属性", AttributeWords[(int)attribute],
                "在规则轮盘里和已解锁的关系组合使用"));

        private void OnRelationUnlocked(RelationId relation)
            => Enqueue(new Notice("获得规则关系", RelationWords[(int)relation],
                "在规则轮盘里和已解锁的属性组合使用"));

        private void Enqueue(Notice notice)
        {
            if (_screen == null)
            {
                // 没有黑幕可用时别静默吞掉：至少留一条日志，也别把游戏暂停在一张看不见的屏幕上。
                Debug.Log("[解锁] " + notice.Kicker + "：" + notice.Headline);
                return;
            }

            _pending.Enqueue(notice);
            if (!_showing) StartCoroutine(PlayRoutine());
        }

        // ==================================================================
        //  演出
        // ==================================================================

        private IEnumerator PlayRoutine()
        {
            _showing = true;
            Show();
            TakePause();

            // 中途抛异常（引用被销毁 / 场景卸载）也要收尾：黑幕、暂停、输入门禁都不能留在半路。
            try
            {
                yield return Fade(0f, 1f, _fadeInSeconds);
                yield return WaitUnscaled(_inputGraceSeconds);

                while (_pending.Count > 0)
                {
                    Apply(_pending.Dequeue());

                    // 暂停菜单 / 对话都不会在这时候打开，玩家按下的这一下就是「继续」。
                    while (!ContinuePressed()) yield return null;

                    // 同一次按键不连带跳过排队的下一条。
                    yield return null;
                }

                yield return Fade(1f, 0f, _fadeOutSeconds);
            }
            finally
            {
                HideImmediately();
                ReleasePause();
                _showing = false;
            }
        }

        private void Apply(Notice notice)
        {
            if (_kicker != null) _kicker.text = notice.Kicker;
            if (_headline != null) _headline.text = notice.Headline;
            if (_description != null) _description.text = notice.Description;
        }

        /// <summary>本帧是否有「继续」的输入。ESC / Start 也算 —— 黑幕上不该再弹暂停菜单。</summary>
        private bool ContinuePressed()
        {
            if (_boundPlayer != null && _boundPlayer.AnyGameplayInputThisFrame()) return true;

            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;

            if (Gamepad.current != null
                && (Gamepad.current.startButton.wasPressedThisFrame
                    || Gamepad.current.buttonSouth.wasPressedThisFrame)) return true;

            return false;
        }

        /// <summary>非缩放时间。轮盘把 <c>Time.timeScale</c> 压到 0.001 时缩放时间几乎不前进。</summary>
        private IEnumerator WaitUnscaled(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (_screen == null) yield break;

            if (seconds <= 0.0001f)
            {
                _screen.style.opacity = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                _screen.style.opacity = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
                yield return null;
            }

            _screen.style.opacity = to;
        }

        // ==================================================================
        //  显隐 / 暂停
        // ==================================================================

        private void Show()
        {
            if (_screen == null) return;

            _screen.style.opacity = 0f;
            _screen.style.display = DisplayStyle.Flex;
        }

        private void HideImmediately()
        {
            if (_screen == null) return;

            _screen.style.opacity = 0f;
            _screen.style.display = DisplayStyle.None;
        }

        private void TakePause()
        {
            if (_boundPlayer != null) _boundPlayer.TryGetComponent(out _status);

            if (TimeManager.Instance != null)
            {
                _pausedBefore = TimeManager.Instance.IsPaused;
                TimeManager.Instance.SetPaused(true);
            }

            if (_status != null)
            {
                _statusPausedBefore = _status.IsPaused;
                _status.SetPaused(true);
            }

            _inputLease = _boundPlayer != null ? _boundPlayer.BlockGameplayInput() : null;
            _ownsPause = true;
        }

        private void ReleasePause()
        {
            if (_inputLease != null)
            {
                _inputLease.Dispose();
                _inputLease = null;
            }

            if (!_ownsPause) return;
            _ownsPause = false;

            if (TimeManager.Instance != null) TimeManager.Instance.SetPaused(_pausedBefore);
            if (_status != null) _status.SetPaused(_statusPausedBefore);
            _status = null;
        }

        // ==================================================================
        //  文案
        // ==================================================================

        private static string AbilityName(PlayerAbility ability) => ability switch
        {
            PlayerAbility.WallClimb => "爬墙跳",
            PlayerAbility.AirJump => "二段跳",
            PlayerAbility.Attack => "攻击",
            PlayerAbility.Throw => "飞刀",
            _ => "能力",
        };

        private static string AbilityDescription(PlayerAbility ability) => ability switch
        {
            PlayerAbility.WallClimb => "沿墙下滑，还能蹬墙起跳",
            PlayerAbility.AirJump => "空中再跳一次",
            PlayerAbility.Attack => "挥动武器 —— 攻击也能扳动机关",
            PlayerAbility.Throw => "掷出纸飞机飞刀",
            _ => "这个角色多了一项本事",
        };
    }
}
