using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Presentation.Audio
{
    /// <summary>
    /// 常驻表现组件：两个播放组各含前奏/循环声源，DSP 排程负责衔接。
    /// 不写规则或游戏时间；暂停仅压低音乐音量，音乐不进入世界快照。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MusicDirector : MonoBehaviour
    {
        [SerializeField] private MusicProfile _defaultProfile;
        [SerializeField] private Transform _focus;
        [Min(0f)] [SerializeField] private float _boundaryHysteresis = 1f;
        [Min(0f)] [SerializeField] private float _regionDelay = 0.25f;
        [Range(0f, 1f)] [SerializeField] private float _pausedVolume = 0.35f;
        [Min(0.01f)] [SerializeField] private float _duckSeconds = 0.2f;
        [Tooltip("流式音频提前排程的缓冲时间（秒）")]
        [Min(0.1f)] [SerializeField] private float _scheduleAhead = 0.5f;

        private static MusicDirector _active;
        private readonly HashSet<MusicProfile> _heard = new HashSet<MusicProfile>();
        private readonly HashSet<MusicProfile> _failed = new HashSet<MusicProfile>();
        private readonly Deck[] _decks = new Deck[2];
        private readonly float[] _fadeFrom = new float[2];
        private ITimeManager _time;
        private MusicRegion _region;
        private MusicProfile _candidate, _desired;
        private float _candidateTime, _duckGain = 1f, _focusRetry;
        private double _fadeStart, _fadeDuration;
        private int _target = -1;
        private bool _fading;
        private float _pauseTarget = 1f;

        public MusicProfile CurrentProfile => _target >= 0 ? _decks[_target]?.Profile : null;
        public MusicRegion CurrentRegion => _region;
        public float PauseGain => _duckGain;
        public bool IsTransitioning => _fading;
        public double ScheduledLoopStart => _target >= 0 ? _decks[_target].LoopStart : 0d;
        public double ScheduledStart => _target >= 0 ? _decks[_target].Start : 0d;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => _active = null;

        private void OnEnable()
        {
            if (_active != null && _active != this)
            {
                Debug.LogWarning("[MusicDirector] 已有常驻音乐组件，停用重复组件。", this);
                enabled = false;
                return;
            }
            _active = this;
            _decks[0] = new Deck(transform, "Music_A");
            _decks[1] = new Deck(transform, "Music_B");
            SceneManager.sceneLoaded += OnSceneLoaded;
            BindTime();
        }

        private void Start() => RefreshRegion(true);

        public void BindFocus(Transform focus)
        {
            _focus = focus;
            _region = null;
            RefreshRegion(true);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            _focusRetry = 0f;
            // 加载只允许重新找玩家，不把预载场景当成玩家进入了该区域。
        }

        private void BindTime()
        {
            if (ReferenceEquals(_time, TimeManager.Instance)) return;
            if (_time != null) _time.OnPausedChanged -= OnPaused;
            _time = TimeManager.Instance;
            if (_time != null) _time.OnPausedChanged += OnPaused;
            OnPaused(_time?.IsPaused == true);
        }

        private void OnPaused(bool paused)
        {
            _pauseTarget = paused ? _pausedVolume : 1f;
        }

        private void Update()
        {
            BindTime();
            float dt = Time.unscaledDeltaTime;
            if (_focus == null)
            {
                _focusRetry -= dt;
                if (_focusRetry <= 0f)
                {
                    PlayerController player = FindAnyObjectByType<PlayerController>();
                    if (player != null) _focus = player.transform;
                    _focusRetry = 0.5f;
                }
            }
            RefreshRegion();
            _duckGain = Mathf.MoveTowards(_duckGain,
                _pauseTarget, dt / Mathf.Max(0.01f, _duckSeconds));

            double now = AudioSettings.dspTime;
            for (int i = 0; i < 2; i++)
            {
                Deck deck = _decks[i];
                if (deck.Profile != null && now >= deck.Start) _heard.Add(deck.Profile);
            }
            TickFade(now);
            TryTransition(now);
            for (int i = 0; i < 2; i++) _decks[i].ApplyVolume(_duckGain);
        }

        public void RefreshRegion(bool immediate = false)
        {
            if (_focus != null) _region = MusicRegion.Resolve(_focus.position, _region, _boundaryHysteresis);
            else _region = null;
            MusicProfile next = _region != null ? _region.Profile : _defaultProfile;
            if (next != _candidate)
            {
                _candidate = next;
                _candidateTime = 0f;
            }
            else _candidateTime += Time.unscaledDeltaTime;
            if (immediate || _candidateTime >= _regionDelay) _desired = next;
        }

        private void TryTransition(double now)
        {
            if (_desired == CurrentProfile) return;
            if (_desired != null && (!_desired.IsPlayable || _failed.Contains(_desired))) return;
            int existing = -1;
            for (int i = 0; i < 2; i++)
                if (_desired != null && _decks[i].Profile == _desired) existing = i;

            // 两组都在发声时，第三首等待本次淡化结束；不会截断仍可听见的声源。
            if (_fading && existing < 0) return;
            if (_desired != null && !Prepare(_desired)) return;

            int next = existing >= 0 ? existing : (_target == 0 ? 1 : 0);
            if (_desired != null && existing < 0)
                _decks[next].Schedule(_desired, !_heard.Contains(_desired), now + _scheduleAhead);
            for (int i = 0; i < 2; i++) _fadeFrom[i] = _decks[i].Weight;
            _fadeStart = _desired == null || existing >= 0 ? now : _decks[next].Start;
            _fadeDuration = _desired != null ? _desired.CrossfadeSeconds : 1.5d;
            _target = _desired != null ? next : -1;
            _fading = true;
        }

        private bool Prepare(MusicProfile profile)
        {
            bool ready = PrepareClip(profile.Loop);
            if (profile.Intro != null && !_heard.Contains(profile)) ready &= PrepareClip(profile.Intro);
            return ready;
        }

        private bool PrepareClip(AudioClip clip)
        {
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                if (_failed.Add(_desired)) Debug.LogError($"[MusicDirector] 音频加载失败：{clip.name}", this);
                return false;
            }
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            return clip.loadState == AudioDataLoadState.Loaded;
        }

        private void TickFade(double now)
        {
            if (!_fading) return;
            float t = Mathf.Clamp01((float)((now - _fadeStart) / _fadeDuration));
            float eased = t * t * (3f - 2f * t);
            for (int i = 0; i < 2; i++)
                _decks[i].Weight = Mathf.Lerp(_fadeFrom[i], i == _target ? 1f : 0f, eased);
            if (t < 1f) return;
            for (int i = 0; i < 2; i++) if (i != _target) _decks[i].Stop();
            _fading = false;
        }

        private void OnDisable()
        {
            if (_active != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_time != null) _time.OnPausedChanged -= OnPaused;
            _time = null;
            for (int i = 0; i < 2; i++) _decks[i]?.Dispose();
            _target = -1;
            _fading = false;
            _candidate = _desired = null;
            _region = null;
            _active = null;
        }

        private sealed class Deck
        {
            private readonly GameObject _host;
            private readonly AudioSource _intro, _loop;
            public MusicProfile Profile { get; private set; }
            public double Start { get; private set; }
            public double LoopStart { get; private set; }
            public float Weight;

            public Deck(Transform parent, string name)
            {
                _host = new GameObject(name);
                _host.transform.SetParent(parent, false);
                _intro = CreateSource();
                _loop = CreateSource();
            }

            private AudioSource CreateSource()
            {
                AudioSource source = _host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.pitch = 1f;
                source.priority = 0;
                return source;
            }

            public void Schedule(MusicProfile profile, bool firstVisit, double start)
            {
                Stop();
                Profile = profile;
                Start = start;
                LoopStart = start;
                if (firstVisit && profile.Intro != null)
                {
                    _intro.clip = profile.Intro;
                    _intro.PlayScheduled(start);
                    // 用采样数求精确时长，避免按帧等待造成前奏与循环之间的空隙。
                    LoopStart += (double)profile.Intro.samples / profile.Intro.frequency;
                    _intro.SetScheduledEndTime(LoopStart);
                }
                _loop.clip = profile.Loop;
                _loop.loop = true;
                _loop.PlayScheduled(LoopStart);
            }

            public void ApplyVolume(float duck)
            {
                float volume = Profile != null ? Profile.Volume * Weight * duck : 0f;
                _intro.volume = _loop.volume = volume;
            }

            public void Stop()
            {
                _intro.Stop(); _loop.Stop();
                _intro.clip = _loop.clip = null;
                Profile = null;
                Weight = 0f;
            }

            public void Dispose()
            {
                Stop();
                UnityEngine.Object.Destroy(_host);
            }
        }
    }
}
