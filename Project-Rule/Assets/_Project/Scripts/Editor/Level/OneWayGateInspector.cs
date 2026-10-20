using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="OneWayGate"/> 的 Inspector：方向与白盒形状对不上时给出提示，并提供一键重设。
    /// </summary>
    /// <remarks>
    /// <para>形状和方向必须一致：水平门是竖板，竖直门是横板。判定本身与形状无关
    /// （全在通过方向那条轴上算），所以「竖板配竖直方向」不会报错 —— 它只是会让人
    /// 从板中间横着穿过去，看起来像门坏了。这个提示就是为了堵住这种情况。</para>
    /// <para>「重设」会同时改方向、碰撞体尺寸、美术缩放和标签位置，全部走 Undo。</para>
    /// </remarks>
    [CustomEditor(typeof(OneWayGate))]
    public sealed class OneWayGateInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var gate = (OneWayGate)target;
            var box = gate.GetComponent<BoxCollider2D>();
            if (box == null)
            {
                EditorGUILayout.HelpBox("同物体上没有 BoxCollider2D —— 这扇门挡不住任何人。",
                                        MessageType.Error);
                return;
            }

            var so = new SerializedObject(gate);
            var art = so.FindProperty("_indicator").objectReferenceValue as SpriteRenderer;
            var label = so.FindProperty("_label").objectReferenceValue as TextMesh;

            bool vertical = gate.PassesVertically;
            bool tall = box.size.y > box.size.x;
            bool wide = box.size.x > box.size.y;

            if ((vertical && tall) || (!vertical && wide))
            {
                EditorGUILayout.HelpBox(
                    vertical
                        ? "通过方向是竖直的，但碰撞体还是竖板。竖直门应该是横板 —— " +
                          "否则玩家会从板中间横着穿过去。点下面的「竖直通过」重设。"
                        : "通过方向是水平的，但碰撞体是横板。水平门应该是竖板 —— " +
                          "否则上下两侧都会变成通路。点下面的「水平通过」重设。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("白盒", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("碰撞体尺寸", $"{box.size.x:0.###} × {box.size.y:0.###}");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重设 · 水平通过（竖板）"))
                {
                    Apply(gate, box, art, label, HorizontalPass(gate.Pass), "重设单向门为水平通过");
                }
                if (GUILayout.Button("重设 · 竖直通过（横板）"))
                {
                    Apply(gate, box, art, label, VerticalPass(gate.Pass), "重设单向门为竖直通过");
                }
            }

            if (GUILayout.Button("反转通过方向"))
            {
                Apply(gate, box, art, label, Opposite(gate.Pass), "反转单向门方向");
            }
        }

        /// <summary>换成水平方向，但尽量保留原来朝左 / 朝右。</summary>
        private static OneWayGate.PassDirection HorizontalPass(OneWayGate.PassDirection current)
        {
            return current == OneWayGate.PassDirection.ToLeft
                ? OneWayGate.PassDirection.ToLeft
                : OneWayGate.PassDirection.ToRight;
        }

        /// <summary>换成竖直方向，但尽量保留原来朝上 / 朝下。</summary>
        private static OneWayGate.PassDirection VerticalPass(OneWayGate.PassDirection current)
        {
            return current == OneWayGate.PassDirection.ToDown
                ? OneWayGate.PassDirection.ToDown
                : OneWayGate.PassDirection.ToUp;
        }

        private static OneWayGate.PassDirection Opposite(OneWayGate.PassDirection current)
        {
            switch (current)
            {
                case OneWayGate.PassDirection.ToRight: return OneWayGate.PassDirection.ToLeft;
                case OneWayGate.PassDirection.ToLeft:  return OneWayGate.PassDirection.ToRight;
                case OneWayGate.PassDirection.ToUp:    return OneWayGate.PassDirection.ToDown;
                default:                               return OneWayGate.PassDirection.ToUp;
            }
        }

        /// <summary>方向、碰撞体、美术、标签一次性摆对。全部走 Undo。</summary>
        private static void Apply(OneWayGate gate, BoxCollider2D box,
                                  SpriteRenderer art, TextMesh label,
                                  OneWayGate.PassDirection pass, string undo)
        {
            Vector2 size = OneWayGateBuilder.SizeFor(pass);

            Undo.RecordObject(gate, undo);
            Undo.RecordObject(box, undo);
            if (art != null) Undo.RecordObject(art.transform, undo);
            if (label != null)
            {
                Undo.RecordObject(label.transform, undo);
                Undo.RecordObject(label, undo);
            }

            gate.SetPass(pass);
            box.size = size;
            box.offset = Vector2.zero;

            if (art != null) art.transform.localScale = new Vector3(size.x, size.y, 1f);
            if (label != null) label.transform.localPosition = OneWayGateBuilder.LabelOffset(pass, size);

            EditorUtility.SetDirty(gate);
            EditorUtility.SetDirty(box);
        }
    }
}
