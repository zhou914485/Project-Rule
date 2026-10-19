using System;
using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    [CreateAssetMenu(fileName = "PlayerWheel_SfxBank", menuName = "RuleGame/音频/玩家与轮盘音效")]
    public sealed class SfxBank : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public SfxCue Cue;
            public AudioClip[] Clips = Array.Empty<AudioClip>();
            [Range(0f, 1f)] public float Volume = 0.6f;
            public Vector2 Pitch = new Vector2(0.96f, 1.04f);
            [Min(0f)] public float MinInterval = 0.06f;
            [Min(1)] public int MaxVoices = 2;
            [Range(0, 256)] public int Priority = 96;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public Entry Find(SfxCue cue)
        {
            for (int i = 0; i < _entries.Length; i++)
                if (_entries[i] != null && _entries[i].Cue == cue) return _entries[i];
            return null;
        }

        public static bool IsInterface(SfxCue cue) => cue == SfxCue.WheelOpen || cue == SfxCue.WheelPreview
            || cue == SfxCue.WheelConfirm || cue == SfxCue.WheelCancel || cue == SfxCue.WheelBack || cue == SfxCue.WheelError
            || cue == SfxCue.MenuClick || cue == SfxCue.MenuBack || cue == SfxCue.MenuConfirm;
    }
}
