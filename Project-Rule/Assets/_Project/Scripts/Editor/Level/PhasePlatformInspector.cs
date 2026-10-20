using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="PhasePlatform"/> 的 Inspector：把「放错层 / 碰撞体是 Trigger / 没接外观」这三个
    /// 会让人以为「机关坏了」的配置直接报出来，并提供开局实化的快捷切换。
    /// </summary>
    /// <remarks>
    /// <para>玩法判定只看碰撞体和层，外观只是告诉玩家现在能不能站 ——
    /// 所以外观没接时平台照样工作，只是完全隐形（比坏掉更难查）。</para>
    /// </remarks>
    [CustomEditor(typeof(PhasePlatform))]
    public sealed class PhasePlatformInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var platform = (PhasePlatform)target;
            var box = platform.GetComponent<BoxCollider2D>();

            if (box == null)
            {
                EditorGUILayout.HelpBox("同物体上没有 BoxCollider2D —— 这块平台虚化 / 实化什么都改变不了。",
                                        MessageType.Error);
            }
            else if (box.isTrigger)
            {
                EditorGUILayout.HelpBox("碰撞体勾了 Is Trigger：它不是一块能站的地面。虚实平台需要实心碰撞体。",
                                        MessageType.Error);
            }

            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0 && platform.gameObject.layer != ground)
            {
                EditorGUILayout.HelpBox(
                    $"平台不在 Ground 层（层 {ground}）：玩家的 Slide 与接地探测只认这个层，" +
                    "放错层就是一块穿得过去的空气。", MessageType.Error);
            }

            var so = new SerializedObject(platform);
            so.Update();
            var artProperty = so.FindProperty("_art");
            var art = artProperty.objectReferenceValue as SpriteRenderer;
            if (art == null)
            {
                EditorGUILayout.HelpBox("没接外观（_art）：平台会完全隐形（判定仍然有效）。" +
                                        "接正式美术后指向那块 SpriteRenderer 即可。", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("开局", platform.StartsSolid ? "实化（能站）" : "虚化（可穿过）");
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("当前", platform.IsSolid ? "实化" : "虚化");
                if (platform.IsSwitchPending)
                    EditorGUILayout.LabelField("切换倒计时", $"{platform.PendingRemaining:0.00} 秒");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("开局 · 实化"))
                    SetStartsSolid(platform, box, art, true);

                if (GUILayout.Button("开局 · 虚化"))
                    SetStartsSolid(platform, box, art, false);
            }
        }

        private static void SetStartsSolid(PhasePlatform platform, BoxCollider2D box,
                                           SpriteRenderer art, bool solid)
        {
            string undo = solid ? "设为开局实化" : "设为开局虚化";

            Undo.RecordObject(platform, undo);
            if (box != null) Undo.RecordObject(box, undo);
            if (art != null) Undo.RecordObject(art, undo);

            platform.SetStartsSolid(solid);

            EditorUtility.SetDirty(platform);
            if (box != null) EditorUtility.SetDirty(box);
            if (art != null) EditorUtility.SetDirty(art);
        }
    }
}
