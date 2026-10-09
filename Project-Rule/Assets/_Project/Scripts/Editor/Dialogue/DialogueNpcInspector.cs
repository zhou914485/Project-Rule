using System.Linq;
using RuleGame.Gameplay.Dialogue;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Dialogue
{
    /// <summary>从内容资产选择 NPC id，避免在 Inspector 手输字符串；仍通过序列化属性支持 Undo。</summary>
    [CustomEditor(typeof(DialogueNpc)), CanEditMultipleObjects]
    public sealed class DialogueNpcInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "_speakerId");
            SerializedProperty id = serializedObject.FindProperty("_speakerId");
            SerializedProperty hostField = serializedObject.FindProperty("_host");
            DialogueHost host = hostField.objectReferenceValue as DialogueHost;
            DialogueSpeaker[] speakers = host != null && host.Content != null
                ? host.Content.Speakers.Where(speaker => speaker != null && !string.IsNullOrWhiteSpace(speaker.Id)).ToArray()
                : System.Array.Empty<DialogueSpeaker>();
            if (!hostField.hasMultipleDifferentValues && speakers.Length > 0)
            {
                string[] ids = speakers.Select(speaker => speaker.Id).ToArray();
                string[] names = speakers.Select(speaker => speaker.DisplayName + " / " + speaker.Id).ToArray();
                int selected = System.Array.IndexOf(ids, id.stringValue);
                EditorGUI.showMixedValue = id.hasMultipleDifferentValues;
                EditorGUI.BeginChangeCheck();
                int picked = EditorGUILayout.Popup("台词入口", selected, names);
                if (EditorGUI.EndChangeCheck() && picked >= 0) id.stringValue = ids[picked];
                EditorGUI.showMixedValue = false;
                if (selected < 0) EditorGUILayout.HelpBox("请选择台词入口。", MessageType.Info);
            }
            // 不同 Host 的多选对象不共用一张下拉列表，避免把第一个 Host 的入口写给其他对象。
            else EditorGUILayout.PropertyField(id);
            serializedObject.ApplyModifiedProperties();
            using (new EditorGUI.DisabledScope(!Application.isPlaying || targets.Length != 1))
                if (GUILayout.Button("试播当前 NPC")) ((DialogueNpc)target).Speak();
        }
    }
}
