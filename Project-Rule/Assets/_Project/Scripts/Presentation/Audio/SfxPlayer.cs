using System;
using System.Collections.Generic;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    /// <summary>固定声源池：世界六槽、界面三槽，界面不被世界暂停或池占用影响。</summary>
    [DisallowMultipleComponent]
    public sealed class SfxPlayer : MonoBehaviour
    {
        [SerializeField] private SfxBank _bank;
        [Range(0f, 1f)] [SerializeField] private float _volume = 0.85f;
        [SerializeField, Min(1f)] private float _hearingDistance = 24f;
        public static SfxPlayer Active { get; private set; }
        private Transform _listener;
        private readonly Dictionary<(SfxCue, EntityId), float> _sourceIntervals = new Dictionary<(SfxCue, EntityId), float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => Active = null;

        private const int WorldVoices = 6;
        private const int InterfaceVoices = 3;
        private readonly Voice[] _voices = new Voice[WorldVoices + InterfaceVoices];
        private readonly float[] _nextAllowed = new float[Enum.GetValues(typeof(SfxCue)).Length];
        private readonly int[] _lastVariant = new int[Enum.GetValues(typeof(SfxCue)).Length];
        private ITimeManager _time;
        private ISnapshotSystem _snapshots;
        private GameObject _pool;
        private bool _paused;
        private readonly System.Random _random = new System.Random();

        public event Action<SfxCue> OnPlayed;
        public SfxBank Bank => _bank;

        private sealed class Voice
        {
            public AudioSource Source;
            public SfxCue Cue;
            public float Started;
            public bool Paused;
            public bool Occupied => Source.clip != null && (Source.isPlaying || Paused);
        }

        private void OnEnable()
        {
            if (Active != null && Active != this) { enabled = false; return; }
            Active = this;
            AudioListener listener = GetComponentInChildren<AudioListener>(true);
            _listener = listener != null ? listener.transform : null;
            _pool = new GameObject("PlayerWheel_SfxVoices");
            _pool.transform.SetParent(transform, false);
            for (int i = 0; i < _voices.Length; i++)
            {
                AudioSource source = _pool.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = i >= WorldVoices;
                _voices[i] = new Voice { Source = source };
            }
            _sourceIntervals.Clear();
            Array.Clear(_nextAllowed, 0, _nextAllowed.Length);
            for (int i = 0; i < _lastVariant.Length; i++) _lastVariant[i] = -1;
            _paused = false;
            BindServices();
        }

        private void Update() => BindServices();

        private void BindServices()
        {
            if (!ReferenceEquals(_time, TimeManager.Instance))
            {
                if (_time != null) _time.OnPausedChanged -= SetPaused;
                _time = TimeManager.Instance;
                if (_time != null) _time.OnPausedChanged += SetPaused;
                SetPaused(_time?.IsPaused == true);
            }
            if (ReferenceEquals(_snapshots, SnapshotSystem.Instance)) return;
            if (_snapshots != null) _snapshots.OnRestored -= ClearWorld;
            _snapshots = SnapshotSystem.Instance;
            if (_snapshots != null) _snapshots.OnRestored += ClearWorld;
        }

        public bool Play(SfxCue cue) => PlayInternal(cue, EntityId.None, 1f, 0f);

        /// <summary>2D 世界声音按监听位置衰减；每个发声物体独立限频，不占用 UI 通道。</summary>
        public bool PlayAt(SfxCue cue, Vector3 position, Component source)
        {
            if (_listener == null)
            {
                AudioListener listener = FindAnyObjectByType<AudioListener>();
                if (listener == null) return false;
                _listener = listener.transform;
            }
            Vector2 offset = position - _listener.position;
            float gain = Mathf.Clamp01(1f - Mathf.Max(0f, offset.magnitude - 3f) / Mathf.Max(1f, _hearingDistance - 3f));
            if (gain <= 0f) return false;
            return PlayInternal(cue, source != null ? source.GetEntityId() : EntityId.None, gain * gain,
                Mathf.Clamp(offset.x / 18f, -0.65f, 0.65f));
        }

        private bool PlayInternal(SfxCue cue, EntityId emitter, float gain, float pan)
        {
            if (!isActiveAndEnabled || _pool == null || _bank == null) return false;
            BindServices();
            bool ui = SfxBank.IsInterface(cue);
            if (!ui && _paused) return false;
            SfxBank.Entry entry = _bank.Find(cue);
            int key = (int)cue;
            if (entry == null || entry.Clips == null || entry.Clips.Length == 0
                || key < 0 || key >= _nextAllowed.Length || entry.Volume <= 0f || _volume <= 0f
                || (emitter == EntityId.None && Time.unscaledTime < _nextAllowed[key])) return false;
            if (emitter != EntityId.None && _sourceIntervals.TryGetValue((cue, emitter), out float next)
                && Time.unscaledTime < next) return false;

            int start = ui ? WorldVoices : 0;
            int end = ui ? _voices.Length : WorldVoices;
            int same = 0;
            Voice free = null, replace = null;
            for (int i = start; i < end; i++)
            {
                Voice voice = _voices[i];
                if (!voice.Occupied) { if (free == null) free = voice; continue; }
                if (voice.Cue == cue) same++;
                if (voice.Source.priority >= entry.Priority && (replace == null
                    || voice.Source.priority > replace.Source.priority
                    || (voice.Source.priority == replace.Source.priority && voice.Started < replace.Started))) replace = voice;
            }
            if (same >= Mathf.Max(1, entry.MaxVoices)) return false;
            Voice slot = free ?? replace;
            if (slot == null) return false;

            int variant = _random.Next(entry.Clips.Length);
            if (entry.Clips.Length > 1 && variant == _lastVariant[key]) variant = (variant + 1) % entry.Clips.Length;
            AudioClip clip = entry.Clips[variant];
            if (clip == null || clip.loadState == AudioDataLoadState.Failed) return false;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded) return false;
            slot.Source.Stop();
            slot.Cue = cue;
            slot.Paused = false;
            slot.Started = Time.unscaledTime;
            slot.Source.clip = clip;
            slot.Source.volume = Mathf.Clamp01(entry.Volume * _volume * gain);
            slot.Source.pitch = Mathf.Lerp(Mathf.Clamp(entry.Pitch.x, 0.1f, 3f),
                Mathf.Clamp(Mathf.Max(entry.Pitch.x, entry.Pitch.y), 0.1f, 3f), (float)_random.NextDouble());
            slot.Source.priority = entry.Priority;
            slot.Source.panStereo = pan;
            slot.Source.Play();
            _lastVariant[key] = variant;
            float allowed = Time.unscaledTime + Mathf.Max(0f, entry.MinInterval);
            if (emitter == EntityId.None) _nextAllowed[key] = allowed;
            else
            {
                // 不保留实体引用；无限生成的投射物也不会让限频表无限增长。
                if (_sourceIntervals.Count >= 256) _sourceIntervals.Clear();
                _sourceIntervals[(cue, emitter)] = allowed;
            }
            OnPlayed?.Invoke(cue);
            return true;
        }

        private void SetPaused(bool paused)
        {
            _paused = paused;
            for (int i = 0; i < WorldVoices; i++)
            {
                Voice voice = _voices[i];
                if (voice == null) continue;
                if (paused && voice.Source.isPlaying)
                {
                    voice.Source.Pause();
                    voice.Paused = true;
                }
                else if (!paused && voice.Paused)
                {
                    voice.Source.UnPause();
                    voice.Paused = false;
                }
            }
        }

        public void ClearWorld()
        {
            if (_pool == null) return;
            _sourceIntervals.Clear();
            for (int i = 0; i < WorldVoices; i++)
            {
                _voices[i].Source.Stop();
                _voices[i].Source.clip = null;
                _voices[i].Paused = false;
            }
            for (int i = 0; i < _nextAllowed.Length; i++)
                if (!SfxBank.IsInterface((SfxCue)i)) _nextAllowed[i] = 0f;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            if (_time != null) _time.OnPausedChanged -= SetPaused;
            if (_snapshots != null) _snapshots.OnRestored -= ClearWorld;
            _time = null; _snapshots = null;
            for (int i = 0; i < _voices.Length; i++) _voices[i]?.Source.Stop();
            if (_pool != null) Destroy(_pool);
            _pool = null;
        }
    }
}
