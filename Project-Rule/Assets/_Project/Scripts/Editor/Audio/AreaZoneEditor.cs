using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Audio
{
    /// <summary>
    /// <see cref="AreaZone"/> 的 Inspector：**多段矩形取并集** + 和揭示区域一样的四角手柄。
    /// </summary>
    /// <remarks>
    /// <para><b>操作和 <c>RevealRegion</c> 一致</b>：每段四个角，拖任意一个角、对角固定，
    /// 不用去改 Transform 的 Scale；「吸附」把尺寸和位置对齐到 0.5 格。</para>
    /// <para><b>为什么不用 BoxBoundsHandle</b>：它给的是「边 + 中心」手柄，拖起来像改包围盒；
    /// 这里的区域是拿来对着房间描的，四角拖框更直接。</para>
    /// <para>旧数据（只有 <c>_size</c> 的单个矩形）照常显示；第一次拖角会自动拆成一段，
    /// 之后就能继续加矩形拼异形。</para>
    /// <para>背景 / 音乐仍然是**整块区域一份**：它们挂在 <c>BackdropZone</c> 上，
    /// 不是每段一份 —— 异形只是把「进入哪个区域」判定得更准。</para>
    /// </remarks>
    [CustomEditor(typeof(AreaZone), true)]
    public sealed class AreaZoneEditor : UnityEditor.Editor
    {
        /// <summary>与 Grid 的 Cell Size 一致（0.5 世界单位）。</summary>
        private const float CellSize = 0.5f;

        /// <summary>四角手柄的「对角」下标：左下 ↔ 右上，左上 ↔ 右下。</summary>
        private static readonly int[] Opposite = { 3, 2, 1, 0 };

        public override void OnInspectorGUI()
        {
            var zone = (AreaZone)target;

            serializedObject.Update();
            DrawFields(zone);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("形状（多段矩形取并集）", EditorStyles.boldLabel);

            if (!zone.HasParts)
            {
                EditorGUILayout.HelpBox(
                    "现在是旧版单矩形。可以直接拖 Scene 里的四个角（第一次拖会自动拆成一段），" +
                    "也可以点下面按钮先拆。",
                    MessageType.Info);

                if (GUILayout.Button("把单矩形拆成多段"))
                {
                    Migrate(zone);
                }

                serializedObject.ApplyModifiedProperties();
                return;
            }

            SerializedProperty parts = serializedObject.FindProperty("_parts");
            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty part = parts.GetArrayElementAtIndex(i);
                SerializedProperty center = part.FindPropertyRelative("Center");
                SerializedProperty size = part.FindPropertyRelative("Size");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("矩形 " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(58));

                    Vector2 cells = size.vector2Value / CellSize;
                    EditorGUI.BeginChangeCheck();
                    float cellsX = EditorGUILayout.FloatField(Mathf.Max(1f, cells.x), GUILayout.Width(46));
                    float cellsY = EditorGUILayout.FloatField(Mathf.Max(1f, cells.y), GUILayout.Width(46));
                    if (EditorGUI.EndChangeCheck())
                    {
                        size.vector2Value = new Vector2(Mathf.Max(1f, cellsX), Mathf.Max(1f, cellsY)) * CellSize;
                    }

                    EditorGUILayout.LabelField("格", GUILayout.Width(18));

                    if (GUILayout.Button("吸附", GUILayout.Width(44)))
                    {
                        Snap(zone, center, size);
                    }

                    if (GUILayout.Button("删", GUILayout.Width(30)))
                    {
                        parts.DeleteArrayElementAtIndex(i);
                        serializedObject.ApplyModifiedProperties();
                        SceneView.RepaintAll();
                        return;
                    }
                }

                Vector2 local = center.vector2Value;
                Vector3 world = zone.transform.TransformPoint(new Vector3(local.x, local.y, 0f));
                EditorGUI.BeginChangeCheck();
                Vector2 edited = EditorGUILayout.Vector2Field("世界中心", new Vector2(world.x, world.y));
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 back = zone.transform.InverseTransformPoint(new Vector3(edited.x, edited.y, 0f));
                    center.vector2Value = new Vector2(back.x, back.y);
                }

                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("＋ 添加矩形"))
            {
                AddPart(zone, parts);
            }

            Vector2 total = BoundsOf(parts);
            float area = AreaOf(parts);
            EditorGUILayout.HelpBox(
                "共 " + parts.arraySize + " 段，外接 " + total.x.ToString("0.0") + " × " + total.y.ToString("0.0") +
                " 米，总面积 " + area.ToString("0") + " ㎡。\n" +
                "Scene 里每段四个角都能拖（对角固定）；背景 / 音乐是整块区域一份配置。",
                MessageType.None);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>画默认字段，但把 <c>_parts</c> 交给上面的分段 UI；拆过段之后旧的 <c>_size</c> 也藏起来。</summary>
        private void DrawFields(AreaZone zone)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "_parts") continue;
                if (iterator.name == "_size" && zone.HasParts) continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        // ----------------------------------------------------------------
        //  Scene 手柄（和 RevealRegion 同一套：四角 FreeMove，对角固定）
        // ----------------------------------------------------------------

        private void OnSceneGUI()
        {
            var zone = (AreaZone)target;

            if (!zone.HasParts)
            {
                DrawLegacyCorners(zone);
                return;
            }

            for (int i = 0; i < zone.Parts.Count; i++)
            {
                DrawPartCorners(zone, i);
            }
        }

        private void DrawPartCorners(AreaZone zone, int index)
        {
            AreaZone.Part part = zone.Parts[index];
            Vector3 center = zone.transform.TransformPoint(new Vector3(part.Center.x, part.Center.y, 0f));
            float halfX = Mathf.Abs(part.Size.x) * 0.5f;
            float halfY = Mathf.Abs(part.Size.y) * 0.5f;

            // 第一段绿色、其余蓝色 —— 和揭示区域 / 铺装区域保持一致，一眼看出是同一种操作。
            Handles.color = index == 0 ? new Color(0.25f, 0.95f, 0.45f, 1f) : new Color(0.35f, 0.8f, 1f, 1f);
            Handles.DrawWireCube(center, new Vector3(part.Size.x, part.Size.y, 0f));

            Vector3[] corners =
            {
                center + new Vector3(-halfX, -halfY, 0f),
                center + new Vector3( halfX, -halfY, 0f),
                center + new Vector3(-halfX,  halfY, 0f),
                center + new Vector3( halfX,  halfY, 0f),
            };

            for (int c = 0; c < corners.Length; c++)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(corners[c], 0.15f, Vector3.zero, Handles.RectangleHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;

                ApplyDrag(zone, index, corners[Opposite[c]], moved);
                return;     // 一帧只处理一个角
            }
        }

        /// <summary>拖旧版单矩形的角：第一次拖就自动拆成一段，之后按分段逻辑处理。</summary>
        private void DrawLegacyCorners(AreaZone zone)
        {
            Vector3 center = zone.transform.TransformPoint(Vector3.zero);
            Vector2 size = zone.Size;
            float halfX = Mathf.Abs(size.x) * 0.5f;
            float halfY = Mathf.Abs(size.y) * 0.5f;

            Handles.color = new Color(0.25f, 0.95f, 0.45f, 1f);
            Handles.DrawWireCube(center, new Vector3(size.x, size.y, 0f));

            Vector3[] corners =
            {
                center + new Vector3(-halfX, -halfY, 0f),
                center + new Vector3( halfX, -halfY, 0f),
                center + new Vector3(-halfX,  halfY, 0f),
                center + new Vector3( halfX,  halfY, 0f),
            };

            for (int c = 0; c < corners.Length; c++)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(corners[c], 0.15f, Vector3.zero, Handles.RectangleHandleCap);
                if (!EditorGUI.EndChangeCheck()) continue;

                Migrate(zone);
                ApplyDrag(zone, 0, corners[Opposite[c]], moved);
                return;
            }
        }

        /// <summary>把「对角固定」的拖动写回第 <paramref name="index"/> 段。</summary>
        private void ApplyDrag(AreaZone zone, int index, Vector3 anchor, Vector3 moved)
        {
            Vector3 min = Vector3.Min(moved, anchor);
            Vector3 max = Vector3.Max(moved, anchor);

            Vector3 localMin = zone.transform.InverseTransformPoint(min);
            Vector3 localMax = zone.transform.InverseTransformPoint(max);
            Vector2 draggedSize = new Vector2(
                Mathf.Max(CellSize, Mathf.Abs(localMax.x - localMin.x)),
                Mathf.Max(CellSize, Mathf.Abs(localMax.y - localMin.y)));
            Vector2 draggedCenter = new Vector2(
                (localMin.x + localMax.x) * 0.5f,
                (localMin.y + localMax.y) * 0.5f);

            Undo.RecordObject(zone, "拖动区域手柄");
            serializedObject.Update();

            SerializedProperty parts = serializedObject.FindProperty("_parts");
            SerializedProperty element = parts.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("Center").vector2Value = draggedCenter;
            element.FindPropertyRelative("Size").vector2Value = draggedSize;

            serializedObject.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        // ----------------------------------------------------------------
        //  数据操作
        // ----------------------------------------------------------------

        /// <summary>把旧的单矩形变成第一段。</summary>
        private void Migrate(AreaZone zone)
        {
            Undo.RecordObject(zone, "拆成多段区域");
            serializedObject.Update();

            Vector2 size = serializedObject.FindProperty("_size").vector2Value;
            SerializedProperty parts = serializedObject.FindProperty("_parts");
            if (parts.arraySize == 0) parts.arraySize = 1;

            SerializedProperty first = parts.GetArrayElementAtIndex(0);
            first.FindPropertyRelative("Center").vector2Value = Vector2.zero;
            first.FindPropertyRelative("Size").vector2Value = size;

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>加一段：接在最后一段右边；没有参考段就放在 Scene 视图中心。</summary>
        private void AddPart(AreaZone zone, SerializedProperty parts)
        {
            Vector2 centre = Vector2.zero;
            Vector2 size = new Vector2(8f, 8f);

            if (parts.arraySize > 0)
            {
                SerializedProperty last = parts.GetArrayElementAtIndex(parts.arraySize - 1);
                Vector2 lastCentre = last.FindPropertyRelative("Center").vector2Value;
                size = last.FindPropertyRelative("Size").vector2Value;
                centre = lastCentre + new Vector2(size.x, 0f);
            }
            else
            {
                SceneView view = SceneView.lastActiveSceneView;
                if (view != null)
                {
                    Vector3 local = zone.transform.InverseTransformPoint(
                        new Vector3(view.pivot.x, view.pivot.y, 0f));
                    centre = new Vector2(local.x, local.y);
                }
            }

            parts.arraySize++;
            SerializedProperty added = parts.GetArrayElementAtIndex(parts.arraySize - 1);
            added.FindPropertyRelative("Center").vector2Value = centre;
            added.FindPropertyRelative("Size").vector2Value = size;
            SceneView.RepaintAll();
        }

        /// <summary>中心与尺寸吸附到 0.5 格。</summary>
        private static void Snap(AreaZone zone, SerializedProperty center, SerializedProperty size)
        {
            Vector2 local = center.vector2Value;
            Vector3 world = zone.transform.TransformPoint(new Vector3(local.x, local.y, 0f));
            world.x = Mathf.Round(world.x / CellSize) * CellSize;
            world.y = Mathf.Round(world.y / CellSize) * CellSize;

            Vector3 back = zone.transform.InverseTransformPoint(world);
            center.vector2Value = new Vector2(back.x, back.y);

            Vector2 current = size.vector2Value;
            size.vector2Value = new Vector2(
                Mathf.Max(CellSize, Mathf.Round(current.x / CellSize) * CellSize),
                Mathf.Max(CellSize, Mathf.Round(current.y / CellSize) * CellSize));
        }

        /// <summary>从序列化数据算外接尺寸（避免读还没 Apply 的旧值）。</summary>
        private static Vector2 BoundsOf(SerializedProperty parts)
        {
            if (parts.arraySize == 0) return Vector2.zero;

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < parts.arraySize; i++)
            {
                SerializedProperty part = parts.GetArrayElementAtIndex(i);
                Vector2 center = part.FindPropertyRelative("Center").vector2Value;
                Vector2 size = part.FindPropertyRelative("Size").vector2Value;
                Vector2 half = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * 0.5f;
                min = Vector2.Min(min, center - half);
                max = Vector2.Max(max, center + half);
            }

            return max - min;
        }

        /// <summary>从序列化数据算总面积。</summary>
        private static float AreaOf(SerializedProperty parts)
        {
            float sum = 0f;
            for (int i = 0; i < parts.arraySize; i++)
            {
                Vector2 size = parts.GetArrayElementAtIndex(i).FindPropertyRelative("Size").vector2Value;
                sum += Mathf.Abs(size.x * size.y);
            }
            return sum;
        }
    }
}
