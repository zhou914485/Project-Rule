using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 秋千存档点的短过场：黑场 → 隐藏玩家 / 空秋千 → 显示带人的 7 帧并循环荡秋千 →
    /// 玩家再次输入 → 黑场还原。
    /// </summary>
    /// <remarks>
    /// <para>存档本身由 <see cref="PlayerSpawnPoint"/> 在<b>攻击命中</b>时完成，本组件只负责表现。
    /// 已经存过档的秋千再被打中时，<c>PlayerSpawnPoint</c> 只重播 <c>OnActivated</c>、不再写盘 ——
    /// 所以同一段过场可以反复播，不会重复存档。</para>
    /// <para>玩家不真的坐上座位：黑场里把他传送到 <see cref="CutsceneAnchor"/>，让相机跟过去；
    /// 坐姿 7 帧是另一张完整构图，整张显示，不与空秋千叠。</para>
    /// <para>输入用 <see cref="PlayerController.BlockGameplayInput"/> 的令牌冻结；暂停时过场也跟着停。
    /// <b>坐上去之后不会自己下来</b>：7 帧循环播放，直到 <see cref="LoopUntilInput"/> 检测到
    /// 玩家再次按下玩法输入（<see cref="PlayerController.AnyGameplayInputThisFrame"/>）才进入黑场还原。
    /// 暂停键不算「继续」—— 它归暂停菜单。</para>
    /// <para><b>黑场与过场按非缩放时间推进</b>（<c>unscaledDeltaTime</c>）：规则轮盘把
    /// <c>Time.timeScale</c> 压到 0.001（<c>TimeSource.UI</c>），用 <c>Time.deltaTime</c> 的话
    /// 一次淡入要几百秒 —— 表现就是「存档已经结束、屏幕还黑着，人却能放规则」。
    /// 慢时间规则（时间 × 增强 / 削弱）同样不该拖慢这段演出。</para>
    /// </remarks>
    public sealed class SwingCheckpointCutscene : MonoBehaviour
    {
        public PlayerSpawnPoint SpawnPoint;
        public SpriteRenderer EmptySwing;
        public SpriteRenderer SitArt;
        public Sprite[] SitFrames = Array.Empty<Sprite>();
        public Transform CutsceneAnchor;
        public SwingCheckpointFade Fade;

        [Min(1f)] public float FramesPerSecond = 8f;
        [Min(0f)] public float FadeSeconds = 0.15f;
        [Min(0f)] public float CameraSettleSeconds = 0.2f;
        public bool RequireGrounded = true;

        [Tooltip("进入坐姿后一直循环荡秋千，直到玩家再次按下玩法输入才下来。关掉 = 只播一轮就还原。")]
        public bool LoopUntilInput = true;

        private bool _playing;
        private PlayerController _player;
        private IDisposable _inputToken;
        private EntityDamageable _damageable;
        private bool _wasInvulnerable;
        private Vector2 _entryPosition;
        private PlayerVisibility _visibility;

        /// <summary>过场正在进行（验证 / 调试用）。</summary>
        public bool IsPlaying => _playing;

        /// <summary>完整播完的次数（验证 / 调试用）。</summary>
        public int CompletedCount { get; private set; }

        private void OnEnable()
        {
            if (SpawnPoint != null) SpawnPoint.OnActivated += HandleActivated;
        }

        private void OnDisable()
        {
            if (SpawnPoint != null) SpawnPoint.OnActivated -= HandleActivated;
            if (!_playing) return;

            StopAllCoroutines();
            _playing = false;                     // 停掉的协程不会自己复位：重开后还要能再播
            if (Fade != null) Fade.Alpha = 0f;   // 先放画面：Restore 万一抛异常也不能把人留在黑屏里
            Restore();
        }

        private void HandleActivated(PlayerSpawnPoint point)
        {
            if (_playing || Fade == null || EmptySwing == null || SitArt == null || SitFrames == null || SitFrames.Length == 0)
                return;
            if (TimeManager.Instance?.IsPaused == true) return;

            var player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;
            if (RequireGrounded && !player.IsGrounded) return;

            StartCoroutine(Run(player));
        }

        private IEnumerator Run(PlayerController player)
        {
            _playing = true;
            _player = player;
            _entryPosition = player.transform.position;
            _inputToken = player.BlockGameplayInput();
            _damageable = player.GetComponent<EntityDamageable>();
            _wasInvulnerable = _damageable != null && _damageable.Invulnerable;
            if (_damageable != null) _damageable.SetInvulnerable(true);

            try
            {
                yield return FadeTo(1f);

                _visibility = PlayerVisibility.Hide(player);
                EmptySwing.enabled = false;
                SitArt.enabled = true;
                SitArt.sprite = SitFrames[0];

                Vector2 anchor = CutsceneAnchor != null ? CutsceneAnchor.position : SpawnPoint.transform.position;
                player.TeleportTo(anchor);
                yield return WaitUnpaused(CameraSettleSeconds);

                yield return FadeTo(0f);

                float duration = Mathf.Max(0.1f, SitFrames.Length / Mathf.Max(1f, FramesPerSecond));
                float elapsed = 0f;
                SitArt.sprite = SitFrames[0];

                // 先让坐姿出现一帧：既避开触发过场的那次攻击的按下边沿，
                // 也保证 FadeSeconds / CameraSettleSeconds 都被设成 0 时不会「刚坐上就下来」。
                yield return null;

                // 进入坐姿后一直荡：LoopUntilInput 打开时，只有玩家**再次按下**玩法输入才下来；
                // 关掉则退回旧行为 —— 播完一轮就还原。
                while (true)
                {
                    if (player == null) break;

                    if (LoopUntilInput)
                    {
                        // 暂停时冻结演出，也不接受「继续」输入（ESC 归暂停菜单）。
                        if (TimeManager.Instance?.IsPaused != true && player.AnyGameplayInputThisFrame()) break;
                    }
                    else if (elapsed >= duration)
                    {
                        break;
                    }

                    if (TimeManager.Instance?.IsPaused != true)
                    {
                        elapsed += UnityEngine.Time.unscaledDeltaTime;
                        if (LoopUntilInput) elapsed %= duration;
                        else elapsed = Mathf.Min(elapsed, duration);
                    }

                    int frame = Mathf.Clamp(Mathf.FloorToInt(elapsed * FramesPerSecond), 0, SitFrames.Length - 1);
                    SitArt.sprite = SitFrames[frame];
                    yield return null;
                }

                // 只在「播一轮」模式下把最后一帧钉住；循环模式停在玩家按键时的当前帧即可。
                if (!LoopUntilInput) SitArt.sprite = SitFrames[SitFrames.Length - 1];

                yield return FadeTo(1f);
                Restore();
                yield return FadeTo(0f);

                _playing = false;
                CompletedCount++;
            }
            finally
            {
                // 异常（空引用 / 对象中途被销毁）也要收尾：黑场、输入门禁、玩家位置都不能留在半路。
                // 正常路径在上面已经把 _playing 置 false，这里不会重复 Restore。
                if (_playing)
                {
                    _playing = false;
                    if (Fade != null) Fade.Alpha = 0f;   // 先放画面，再还原玩家 / 输入
                    Restore();
                }
            }
        }

        private void Restore()
        {
            if (_visibility != null)
            {
                _visibility.Restore();
                _visibility = null;
            }
            if (SitArt != null) SitArt.enabled = false;
            if (EmptySwing != null) EmptySwing.enabled = true;
            if (_player != null) _player.TeleportTo(_entryPosition);
            if (_damageable != null) _damageable.SetInvulnerable(_wasInvulnerable);
            if (_inputToken != null)
            {
                _inputToken.Dispose();
                _inputToken = null;
            }
            _player = null;
            _damageable = null;
        }

        private IEnumerator FadeTo(float target)
        {
            if (Fade == null) yield break;

            float start = Fade.Alpha;
            if (FadeSeconds <= 0f) { Fade.Alpha = target; yield break; }

            // 非缩放时间：轮盘把 timeScale 压到 0.001，用 deltaTime 的话这次淡入要几百秒。
            // 正式暂停（TimeManager.IsPaused）仍然把演出冻住。
            float elapsed = 0f;
            while (elapsed < FadeSeconds)
            {
                if (TimeManager.Instance?.IsPaused != true) elapsed += UnityEngine.Time.unscaledDeltaTime;
                Fade.Alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / FadeSeconds));
                yield return null;
            }
            Fade.Alpha = target;
        }

        /// <summary>等一段<b>真实</b>时间（非缩放）；正式暂停期间不计时。</summary>
        private IEnumerator WaitUnpaused(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (TimeManager.Instance?.IsPaused != true) elapsed += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>隐藏玩家身上所有 Renderer / Light2D，并记录原状态；Hurtbox 留着，靠过场无敌保护。</summary>
        private sealed class PlayerVisibility
        {
            private readonly Renderer[] _renderers;
            private readonly bool[] _rendererStates;
            private readonly Light2D[] _lights;
            private readonly bool[] _lightStates;

            private PlayerVisibility(PlayerController player)
            {
                _renderers = player.GetComponentsInChildren<Renderer>(true);
                _rendererStates = new bool[_renderers.Length];
                _lights = player.GetComponentsInChildren<Light2D>(true);
                _lightStates = new bool[_lights.Length];
            }

            public static PlayerVisibility Hide(PlayerController player)
            {
                var visibility = new PlayerVisibility(player);

                for (int i = 0; i < visibility._renderers.Length; i++)
                {
                    visibility._rendererStates[i] = visibility._renderers[i].enabled;
                    visibility._renderers[i].enabled = false;
                }
                for (int i = 0; i < visibility._lights.Length; i++)
                {
                    visibility._lightStates[i] = visibility._lights[i].enabled;
                    visibility._lights[i].enabled = false;
                }

                return visibility;
            }

            public void Restore()
            {
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _renderers[i].enabled = _rendererStates[i];
                for (int i = 0; i < _lights.Length; i++)
                    if (_lights[i] != null) _lights[i].enabled = _lightStates[i];
            }
        }
    }
}
