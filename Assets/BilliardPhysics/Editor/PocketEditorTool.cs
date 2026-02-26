#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using BilliardPhysics;

namespace BilliardPhysics.Editor
{
    [CustomEditor(typeof(PocketDefinition))]
    public class PocketEditorTool : UnityEditor.Editor
    {
        private SerializedProperty _pocketsProp;

        private static readonly Color s_pocketColor    = Color.yellow;
        private static readonly Color s_rimColor       = Color.red;
        private static readonly Color s_handleColor    = Color.white;

        private void OnEnable()
        {
            _pocketsProp = serializedObject.FindProperty("Pockets");
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            PocketDefinition pocketDef = (PocketDefinition)target;
            serializedObject.Update();

            EditorGUILayout.Space();

            if (GUILayout.Button("Add Pocket"))
            {
                Undo.RecordObject(pocketDef, "Add Pocket");
                _pocketsProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
            }

            if (GUILayout.Button("Remove Last Pocket"))
            {
                if (_pocketsProp.arraySize > 0)
                {
                    Undo.RecordObject(pocketDef, "Remove Last Pocket");
                    _pocketsProp.arraySize--;
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private void OnSceneGUI()
        {
            PocketDefinition pocketDef = (PocketDefinition)target;
            if (pocketDef == null) return;

            serializedObject.Update();

            if (_pocketsProp == null) return;

            bool changed = false;

            for (int i = 0; i < _pocketsProp.arraySize; i++)
            {
                SerializedProperty pocket      = _pocketsProp.GetArrayElementAtIndex(i);
                SerializedProperty centerProp  = pocket.FindPropertyRelative("Center");
                SerializedProperty radiusProp  = pocket.FindPropertyRelative("Radius");
                SerializedProperty rimSegsProp = pocket.FindPropertyRelative("RimSegments");

                if (centerProp == null || radiusProp == null) continue;

                Vector2 cv     = centerProp.vector2Value;
                Vector3 center = new Vector3(cv.x, cv.y, 0f);
                float   radius = radiusProp.floatValue;

                // Draw pocket outline and centre dot.
                Handles.color = s_pocketColor;
                Handles.DrawWireDisc(center, Vector3.forward, radius);
                Handles.DotHandleCap(0, center, Quaternion.identity, 0.05f, EventType.Repaint);

                // Draw and edit rim segments.
                if (rimSegsProp != null)
                {
                    for (int j = 0; j < rimSegsProp.arraySize; j++)
                    {
                        SerializedProperty rim      = rimSegsProp.GetArrayElementAtIndex(j);
                        SerializedProperty rimStart = rim.FindPropertyRelative("Start");
                        SerializedProperty rimEnd   = rim.FindPropertyRelative("End");
                        if (rimStart == null || rimEnd == null) continue;

                        Vector2 rsv = rimStart.vector2Value;
                        Vector2 rev = rimEnd.vector2Value;
                        Vector3 rs  = new Vector3(rsv.x, rsv.y, 0f);
                        Vector3 re  = new Vector3(rev.x, rev.y, 0f);

                        Handles.color = s_rimColor;
                        Handles.DrawLine(rs, re);

                        // Editable rim start endpoint.
                        Handles.color = s_handleColor;
                        EditorGUI.BeginChangeCheck();
                        Vector3 newRs = Handles.PositionHandle(rs, Quaternion.identity);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(pocketDef, "Move Rim Segment Start");
                            rimStart.vector2Value = new Vector2(newRs.x, newRs.y);
                            changed = true;
                        }

                        // Editable rim end endpoint.
                        EditorGUI.BeginChangeCheck();
                        Vector3 newRe = Handles.PositionHandle(re, Quaternion.identity);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(pocketDef, "Move Rim Segment End");
                            rimEnd.vector2Value = new Vector2(newRe.x, newRe.y);
                            changed = true;
                        }
                    }
                }

                // Editable pocket centre handle.
                Handles.color = s_handleColor;
                EditorGUI.BeginChangeCheck();
                Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(pocketDef, "Move Pocket Center");
                    centerProp.vector2Value = new Vector2(newCenter.x, newCenter.y);
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
