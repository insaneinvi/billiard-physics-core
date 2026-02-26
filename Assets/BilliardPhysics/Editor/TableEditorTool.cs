using UnityEngine;
using UnityEditor;
using BilliardPhysics;

namespace BilliardPhysics.Editor
{
    [CustomEditor(typeof(TableDefinition))]
    public class TableEditorTool : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            TableDefinition table = (TableDefinition)target;
            SerializedProperty segsProp = serializedObject.FindProperty("Segments");

            EditorGUILayout.Space();

            if (GUILayout.Button("Add Segment"))
            {
                Undo.RecordObject(table, "Add Segment");
                segsProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
            }

            if (GUILayout.Button("Remove Last Segment"))
            {
                if (segsProp.arraySize > 0)
                {
                    Undo.RecordObject(table, "Remove Last Segment");
                    segsProp.arraySize--;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private void OnSceneGUI()
        {
            TableDefinition table = (TableDefinition)target;
            if (table == null) return;

            SerializedProperty segsProp = serializedObject.FindProperty("Segments");
            if (segsProp == null) return;

            bool changed = false;

            for (int i = 0; i < segsProp.arraySize; i++)
            {
                SerializedProperty element = segsProp.GetArrayElementAtIndex(i);
                SerializedProperty startProp = element.FindPropertyRelative("Start");
                SerializedProperty endProp   = element.FindPropertyRelative("End");

                if (startProp == null || endProp == null) continue;

                Vector3 start = new Vector3(startProp.vector2Value.x, startProp.vector2Value.y, 0f);
                Vector3 end   = new Vector3(endProp.vector2Value.x,   endProp.vector2Value.y,   0f);

                // Draw segment in green.
                Handles.color = Color.green;
                Handles.DrawLine(start, end);

                // Draw outward normal in blue from midpoint.
                Vector3 mid       = (start + end) * 0.5f;
                Vector3 direction = (end - start).normalized;
                Vector3 normal    = new Vector3(-direction.y, direction.x, 0f);
                Handles.color = Color.blue;
                Handles.DrawLine(mid, mid + normal * 0.3f);
                Handles.ArrowHandleCap(0, mid + normal * 0.3f, Quaternion.LookRotation(normal), 0.1f, EventType.Repaint);

                // Editable endpoint handles.
                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                Vector3 newStart = Handles.FreeMoveHandle(start, 0.1f, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(table, "Move Segment Start");
                    startProp.vector2Value = new Vector2(newStart.x, newStart.y);
                    changed = true;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newEnd = Handles.FreeMoveHandle(end, 0.1f, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(table, "Move Segment End");
                    endProp.vector2Value = new Vector2(newEnd.x, newEnd.y);
                    changed = true;
                }
            }

            if (changed)
                serializedObject.ApplyModifiedProperties();
        }
    }
}
