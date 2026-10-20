using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="PhasePlatformGroup"/> 的 Inspector：一键从子物体收集平台，
    /// 并检查「按钮到底接没接、是不是点按式」。
    /// </summary>
    /// <remarks>
    /// <para>虚实平台用**点按式**按钮：<c>Toggle = false</c>（按一次由平台翻转一次，按完平台把按钮复位），
    /// 且不开 <c>PressByPlayerProximity</c>。勾了 <c>Toggle</c> 的按钮会保持按下状态，
    /// 和其他按钮混在一起时会让「按了没反应」。</para>
    /// </remarks>
    [CustomEditor(typeof(PhasePlatformGroup))]
    public sealed class PhasePlatformGroupInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var group = (PhasePlatformGroup)target;
            var so = new SerializedObject(group);
            so.Update();
            var platforms = so.FindProperty("_platforms");

            int filled = CountFilled(platforms);

            if (GUILayout.Button("从子物体收集平台"))
            {
                PhasePlatform[] found = group.GetComponentsInChildren<PhasePlatform>(true);

                Undo.RecordObject(group, "收集虚实平台");
                platforms.arraySize = found.Length;
                for (int i = 0; i < found.Length; i++)
                {
                    platforms.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(group);

                filled = CountFilled(platforms);
            }

            EditorGUILayout.LabelField("平台", $"{filled} / {platforms.arraySize} 块已接");

            int wired = 0;
            bool anyToggle = false;
            bool anyProximity = false;
            CitySwitch[] switches = Object.FindObjectsByType<CitySwitch>(FindObjectsInactive.Include);

            for (int i = 0; i < switches.Length; i++)
            {
                CitySwitch trigger = switches[i];
                if (trigger == null || !Targets(trigger, group)) continue;

                wired++;
                if (trigger.Toggle) anyToggle = true;
                if (trigger.PressByPlayerProximity) anyProximity = true;
            }

            if (wired == 0)
            {
                EditorGUILayout.HelpBox(
                    "还没有按钮接在这个组上：把本组件（或本物体）拖到 CityButton / CityLever 的 FollowMotor 上。\n" +
                    "推荐摆法：每块平台顶面各放一个 CityButton，都接到同一个组 —— 按任意一个翻全部。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("已接按钮", wired.ToString());
            }

            if (anyToggle)
            {
                EditorGUILayout.HelpBox(
                    "有按钮是切换型（Toggle 勾着）：它会保持自己的按下状态，和其他按钮混在一起时" +
                    "会出现「按了没反应」。虚实平台用**点按式** —— 取消勾选 Toggle（菜单 ⑯a 会统一改）。",
                    MessageType.Warning);
            }

            if (anyProximity)
            {
                EditorGUILayout.HelpBox(
                    "有按钮开了 PressByPlayerProximity：踩上去就按的按钮不会被复位，会一直锁在按下状态。" +
                    "虚实平台请用 E / 攻击触发。", MessageType.Warning);
            }

            if (filled == 0)
            {
                EditorGUILayout.HelpBox("一块平台都没接 —— 按钮按了什么都不会发生。", MessageType.Warning);
            }
        }

        /// <summary>按钮是不是接在本组上（拖 GameObject 时 Unity 可能赋的是同物体上的别的组件）。</summary>
        private static bool Targets(CitySwitch trigger, PhasePlatformGroup group)
        {
            MonoBehaviour follow = trigger.FollowMotor;
            if (follow == null) return false;
            if (follow == group) return true;

            return follow.GetComponent<PhasePlatformGroup>() == group;
        }

        private static int CountFilled(SerializedProperty platforms)
        {
            int filled = 0;
            for (int i = 0; i < platforms.arraySize; i++)
            {
                if (platforms.GetArrayElementAtIndex(i).objectReferenceValue != null) filled++;
            }
            return filled;
        }
    }
}
