#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using BilliardPhysics;

namespace BilliardPhysics.Editor
{
    [CustomEditor(typeof(TableDefinition))]
    public class TableEditorTool : UnityEditor.Editor
    {
        private int _selectedSegment = -1;

        // Length Constraint Mode – editor-only, never saved to the ScriptableObject.
        private bool _lengthConstraintEnabled = false;

        private SerializedProperty _segsProp;

        private static readonly Color s_unselectedColor = Color.green;
        private static readonly Color s_selectedColor   = Color.cyan;
        private static readonly Color s_normalColor     = Color.blue;
        private static readonly Color s_handleColor     = Color.white;

        private static readonly GUIStyle s_labelStyle = new GUIStyle
        {
            normal = { textColor = Color.yellow }
        };

        private void OnEnable()
        {
            _segsProp = serializedObject.FindProperty("Segments");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            TableDefinition table = (TableDefinition)target;
            serializedObject.Update();

            EditorGUILayout.Space();

            // ── Length Constraint Mode toggle ────────────────────────────
            _lengthConstraintEnabled = EditorGUILayout.ToggleLeft(
                "Length Constraint Mode",
                _lengthConstraintEnabled);

            if (_lengthConstraintEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Dragging Start repositions End to maintain the segment length.\n" +
                    "Dragging End repositions Start to maintain the segment length.\n" +
                    "Length is calculated in the Editor only and is not saved.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Add Segment"))
            {
                Undo.RecordObject(table, "Add Segment");
                _segsProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
            }

            if (GUILayout.Button("Remove Last Segment"))
            {
                if (_segsProp.arraySize > 0)
                {
                    Undo.RecordObject(table, "Remove Last Segment");
                    _segsProp.arraySize--;
                    if (_selectedSegment >= _segsProp.arraySize)
                        _selectedSegment = -1;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private void OnSceneGUI()
        {
            TableDefinition table = (TableDefinition)target;
            if (table == null) return;

            serializedObject.Update();

            if (_segsProp == null) return;

            bool changed = false;

            for (int i = 0; i < _segsProp.arraySize; i++)
            {
                SerializedProperty element   = _segsProp.GetArrayElementAtIndex(i);
                SerializedProperty startProp = element.FindPropertyRelative("Start");
                SerializedProperty endProp   = element.FindPropertyRelative("End");

                if (startProp == null || endProp == null) continue;

                Vector2 sv = startProp.vector2Value;
                Vector2 ev = endProp.vector2Value;
                Vector3 start = new Vector3(sv.x, sv.y, 0f);
                Vector3 end   = new Vector3(ev.x, ev.y, 0f);

                bool isSelected = (i == _selectedSegment);

                // Draw segment, highlighted when selected.
                Handles.color = isSelected ? s_selectedColor : s_unselectedColor;
                Handles.DrawLine(start, end);

                // Draw outward normal in blue from midpoint.
                Vector3 mid       = (start + end) * 0.5f;
                Vector3 direction = (end - start).normalized;
                Vector3 normal    = new Vector3(-direction.y, direction.x, 0f);
                Handles.color = s_normalColor;
                Handles.DrawLine(mid, mid + normal * 0.3f);
                Handles.ArrowHandleCap(0, mid + normal * 0.3f, Quaternion.LookRotation(normal), 0.1f, EventType.Repaint);

                // Optional length and angle labels (only in Length Constraint Mode).
                if (_lengthConstraintEnabled)
                {
                    float   length = (end - start).magnitude;
                    float   angle  = Mathf.Atan2(ev.y - sv.y, ev.x - sv.x) * Mathf.Rad2Deg;
                    string  label  = string.Format("L:{0:F3}  A:{1:F1}°", length, angle);
                    Handles.Label(mid, label, s_labelStyle);
                }

                // Editable start endpoint.
                Handles.color = s_handleColor;
                EditorGUI.BeginChangeCheck();
                Vector3 newStart = Handles.PositionHandle(start, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(table, "Move Segment Start");

                    Vector2 newEndV = ev; // default: end unchanged
                    if (_lengthConstraintEnabled)
                    {
                        // Keep length: reposition end along the original direction from the new start.
                        float   length  = (end - start).magnitude;
                        Vector3 dir     = (end - start);
                        Vector3 newEnd3 = dir.sqrMagnitude > 1e-6f
                            ? newStart + dir.normalized * length
                            : newStart + Vector3.right * length;
                        newEndV = new Vector2(newEnd3.x, newEnd3.y);
                    }
                    startProp.vector2Value = new Vector2(newStart.x, newStart.y);
                    endProp.vector2Value   = newEndV;

                    _selectedSegment = i;
                    changed = true;
                }

                // Editable end endpoint.
                EditorGUI.BeginChangeCheck();
                Vector3 newEnd = Handles.PositionHandle(end, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(table, "Move Segment End");

                    Vector2 newStartV = sv; // default: start unchanged
                    if (_lengthConstraintEnabled)
                    {
                        // Keep length: reposition start along the direction from new end toward old start.
                        float   length    = (end - start).magnitude;
                        Vector3 newDir    = newEnd - start;
                        Vector3 newStart3 = newDir.sqrMagnitude > 1e-6f
                            ? newEnd - newDir.normalized * length
                            : newEnd - Vector3.right * length;
                        newStartV = new Vector2(newStart3.x, newStart3.y);
                    }
                    endProp.vector2Value   = new Vector2(newEnd.x, newEnd.y);
                    startProp.vector2Value = newStartV;

                    _selectedSegment = i;
                    changed = true;
                }
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif
