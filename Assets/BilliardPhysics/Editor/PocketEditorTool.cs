using UnityEngine;
using UnityEditor;
using BilliardPhysics;

namespace BilliardPhysics.Editor
{
    [CustomEditor(typeof(PocketDefinition))]
    public class PocketEditorTool : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PocketDefinition pocketDef = (PocketDefinition)target;
            SerializedProperty pocketsProp = serializedObject.FindProperty("Pockets");

            EditorGUILayout.Space();

            if (GUILayout.Button("Add Pocket"))
            {
                Undo.RecordObject(pocketDef, "Add Pocket");
                pocketsProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
            }

            if (GUILayout.Button("Remove Last Pocket"))
            {
                if (pocketsProp.arraySize > 0)
                {
                    Undo.RecordObject(pocketDef, "Remove Last Pocket");
                    pocketsProp.arraySize--;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private void OnSceneGUI()
        {
            PocketDefinition pocketDef = (PocketDefinition)target;
            if (pocketDef == null) return;

            SerializedProperty pocketsProp = serializedObject.FindProperty("Pockets");
            if (pocketsProp == null) return;

            bool changed = false;

            for (int i = 0; i < pocketsProp.arraySize; i++)
            {
                SerializedProperty pocket      = pocketsProp.GetArrayElementAtIndex(i);
                SerializedProperty centerProp  = pocket.FindPropertyRelative("Center");
                SerializedProperty radiusProp  = pocket.FindPropertyRelative("Radius");
                SerializedProperty rimSegsProp = pocket.FindPropertyRelative("RimSegments");

                if (centerProp == null || radiusProp == null) continue;

                Vector3 center = new Vector3(centerProp.vector2Value.x, centerProp.vector2Value.y, 0f);
                float   radius = radiusProp.floatValue;

                // Draw pocket as a yellow disc outline.
                Handles.color = Color.yellow;
                Handles.DrawWireDisc(center, Vector3.forward, radius);

                // Draw centre dot.
                Handles.DotHandleCap(0, center, Quaternion.identity, 0.05f, EventType.Repaint);

                // Draw rim segments in red.
                if (rimSegsProp != null)
                {
                    Handles.color = Color.red;
                    for (int j = 0; j < rimSegsProp.arraySize; j++)
                    {
                        SerializedProperty rim      = rimSegsProp.GetArrayElementAtIndex(j);
                        SerializedProperty rimStart = rim.FindPropertyRelative("Start");
                        SerializedProperty rimEnd   = rim.FindPropertyRelative("End");
                        if (rimStart == null || rimEnd == null) continue;

                        Vector3 rs = new Vector3(rimStart.vector2Value.x, rimStart.vector2Value.y, 0f);
                        Vector3 re = new Vector3(rimEnd.vector2Value.x,   rimEnd.vector2Value.y,   0f);
                        Handles.DrawLine(rs, re);
                    }
                }

                // Editable centre handle.
                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                Vector3 newCenter = Handles.FreeMoveHandle(center, 0.15f, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(pocketDef, "Move Pocket Center");
                    centerProp.vector2Value = new Vector2(newCenter.x, newCenter.y);
                    changed = true;
                }
            }

            if (changed)
                serializedObject.ApplyModifiedProperties();
        }
    }
}
