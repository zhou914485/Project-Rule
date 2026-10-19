using RuleGame.Presentation.Audio;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    [CustomEditor(typeof(MusicRegion))]
    public sealed class MusicRegionEditor : UnityEditor.Editor
    {
        private readonly UnityEditor.IMGUI.Controls.BoxBoundsHandle _handle =
            new UnityEditor.IMGUI.Controls.BoxBoundsHandle();

        private void OnSceneGUI()
        {
            var region = (MusicRegion)target;
            using (new Handles.DrawingScope(region.transform.localToWorldMatrix))
            {
                _handle.center = Vector3.zero;
                _handle.size = new Vector3(region.Size.x, region.Size.y, 0f);
                _handle.axes = UnityEditor.IMGUI.Controls.PrimitiveBoundsHandle.Axes.X
                    | UnityEditor.IMGUI.Controls.PrimitiveBoundsHandle.Axes.Y;
                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (!EditorGUI.EndChangeCheck()) return;
                Undo.RecordObject(region, "调整音乐区域");
                serializedObject.Update();
                serializedObject.FindProperty("_size").vector2Value = new Vector2(_handle.size.x, _handle.size.y);
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}
