using UnityEngine;

namespace RuleGame.Presentation.Audio
{
    [CreateAssetMenu(fileName = "Area_MusicProfile", menuName = "RuleGame/音频/区域音乐")]
    public sealed class MusicProfile : ScriptableObject
    {
        [Tooltip("首次进入时播放一次；留空则直接进入循环")]
        [SerializeField] private AudioClip _intro;
        [Tooltip("完整循环段；必须先试听首尾接点")]
        [SerializeField] private AudioClip _loop;
        [Range(0f, 1f)] [SerializeField] private float _volume = 0.25f;
        [Min(0.05f)] [SerializeField] private float _crossfadeSeconds = 1.5f;

        public AudioClip Intro => _intro;
        public AudioClip Loop => _loop;
        public float Volume => Mathf.Clamp01(_volume);
        public float CrossfadeSeconds => Mathf.Max(0.05f, _crossfadeSeconds);
        public bool IsPlayable => _loop != null && _loop.samples > 0;
    }
}
