using System;
using UnityEngine;
using Yarn.Unity;

namespace RuleGame.Gameplay.Dialogue
{
    [Serializable]
    public sealed class DialogueSpeaker
    {
        public string Id;
        public string DisplayName;
        public string EntryNode;
        public string FallbackNode;
    }

    /// <summary>策划可编辑的内容入口；正文始终在 .yarn 文件中，不写死在播放器代码里。</summary>
    [CreateAssetMenu(menuName = "RuleGame/Dialogue/Content", fileName = "DialogueContent")]
    public sealed class DialogueContent : ScriptableObject
    {
        [SerializeField] private YarnProject _project;
        [SerializeField, Tooltip("项目包含的全部 .yarn 文件，用于启动期引用与兜底校验。")]
        private TextAsset[] _scripts = Array.Empty<TextAsset>();
        [SerializeField] private DialogueSpeaker[] _speakers = Array.Empty<DialogueSpeaker>();
        public YarnProject Project => _project;
        public TextAsset[] Scripts => _scripts;
        public DialogueSpeaker[] Speakers => _speakers;
        public DialogueSpeaker FindSpeaker(string id) => Array.Find(_speakers, speaker => speaker != null && speaker.Id == id);
    }
}
