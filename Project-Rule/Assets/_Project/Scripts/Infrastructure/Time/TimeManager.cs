using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Infrastructure.Time
{
    /// <summary>
    /// <see cref="ITimeManager"/> 的默认实现：按来源去重的倍率表 + 暂停开关。
    /// </summary>
    /// <remarks>
    /// <para><b>纯 C# 类</b>（与 <c>WorldRegistry</c> / <c>RuleSystem</c> 同一套单例模式），
    /// 由 <c>RuleSystemDriver</c> 在 Awake 里初始化、OnDestroy 里 Reset。</para>
    /// <para><b>输出分发是「写时生效」</b>：合成倍率一变就写 <c>Time.timeScale</c>，
    /// 不需要每帧驱动；只有 <see cref="DeltaTime"/> 依赖调用方处于帧回调里
    /// （它直读 <c>Time.deltaTime</c>）。</para>
    /// </remarks>
    public sealed class TimeManager : ITimeManager
    {
        // ==================================================================
        //  单例
        // ==================================================================

        private static TimeManager _instance;

        /// <summary>全局单例。由 <c>RuleSystemDriver</c> 在 Awake 里初始化。</summary>
        public static TimeManager Instance => _instance;

        /// <summary>
        /// 初始化单例，并把引擎时间重置到干净状态。
        /// </summary>
        /// <remarks>重复调用会覆盖（重开关卡 / 切场景时有用）。
        /// <c>Time.timeScale</c> 与 <c>Physics2D.simulationMode</c> 都是<b>全局状态</b>，
        /// 上一次会话留在暂停 / 0.5 倍速里的值不该带进新的一局。</remarks>
        public static void Initialize()
        {
            _instance = new TimeManager();

            // 注意：本命名空间（RuleGame.Infrastructure.Time）会遮蔽 UnityEngine.Time，
            // 必须全限定。
            UnityEngine.Time.timeScale = 1f;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
        }

        /// <summary>仅测试用：注入替身。</summary>
        public static void SetInstanceForTesting(TimeManager instance) => _instance = instance;

        /// <summary>清空单例。切场景 / 退出时调用，让下次重新初始化。</summary>
        public static void Reset() => _instance = null;

        // ==================================================================
        //  状态
        // ==================================================================

        /// <summary>各来源的倍率。一个来源一个值，重复推送覆盖 —— 「按来源去重并乘法合成」。</summary>
        private readonly Dictionary<TimeSource, float> _scales = new Dictionary<TimeSource, float>();

        private bool _paused;
        private bool _mechanicsEnabled = true;
        private float _currentScale = 1f;

        public float TimeScale => _currentScale;
        public bool  IsPaused  => _paused;
        public bool  TimeMechanicsEnabled => _mechanicsEnabled;

        /// <inheritdoc />
        /// <remarks>暂停时返回 0；否则直读 <c>Time.deltaTime</c> —— 它已经随
        /// <c>Time.timeScale</c> 缩放，所以「时间变慢，规则也变慢」（架构 D12）自动成立。</remarks>
        public float DeltaTime => _paused ? 0f : UnityEngine.Time.deltaTime;

        // ==================================================================
        //  时间来源
        // ==================================================================

        public void PushScale(TimeSource source, float scale)
        {
            // 负倍率只会是 bug（Unity 侧没有任何「时间倒放」的语义），拒绝而不是静默夹到 0。
            if (scale < 0f)
            {
                Debug.LogError($"[TimeManager] 倍率不能为负：{source} 请求 {scale}，已忽略。");
                return;
            }

            _scales[source] = scale;
            Synthesize();
        }

        public void RemoveScale(TimeSource source)
        {
            if (!_scales.Remove(source)) return;    // 来源不存在：静默忽略
            Synthesize();
        }

        /// <summary>
        /// 乘法合成所有来源并分发。
        /// </summary>
        /// <remarks>
        /// 只有合成结果真的变了才写引擎、才广播 —— 避免重复推送同一值时
        /// 订阅者（VFX / 音频）被无谓地刷一遍。
        /// </remarks>
        private void Synthesize()
        {
            float scale = 1f;
            foreach (var pair in _scales) scale *= pair.Value;

            if (Mathf.Approximately(scale, _currentScale)) return;

            _currentScale = scale;
            UnityEngine.Time.timeScale = scale;     // 有引擎全局载体的直接写引擎（架构 D13）
            OnTimeScaleChanged?.Invoke(scale);
        }

        // ==================================================================
        //  暂停 / 时间机制开关
        // ==================================================================

        public void SetPaused(bool paused)
        {
            if (_paused == paused) return;

            _paused = paused;

            // 策划案 §3.1：暂停 = 停物理步进，绝不碰 Time.timeScale。
            // 代价：Time.deltaTime 在暂停期间照常流逝，规则时限等必须读 DeltaTime。
            Physics2D.simulationMode = paused
                ? SimulationMode2D.Script
                : SimulationMode2D.FixedUpdate;

            OnPausedChanged?.Invoke(paused);
        }

        public void SetTimeMechanicsEnabled(bool enabled)
        {
            if (_mechanicsEnabled == enabled) return;

            _mechanicsEnabled = enabled;
            OnTimeMechanicsChanged?.Invoke(enabled);
        }

        // ==================================================================
        //  事件
        // ==================================================================

        public event Action<float> OnTimeScaleChanged;
        public event Action<bool>  OnPausedChanged;
        public event Action<bool>  OnTimeMechanicsChanged;
    }
}
