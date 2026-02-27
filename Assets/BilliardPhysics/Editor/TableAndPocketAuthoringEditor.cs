#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace BilliardPhysics.Editor
{
    [CustomEditor(typeof(TableAndPocketAuthoring))]
    public class TableAndPocketAuthoringEditor : UnityEditor.Editor
    {
        // ── Colors ────────────────────────────────────────────────────────
        private static readonly Color s_tableSegColor    = Color.green;
        private static readonly Color s_tableSelColor    = Color.cyan;
        private static readonly Color s_normalColor      = new Color(0f, 0.4f, 1f, 0.8f);
        private static readonly Color s_pocketRadColor   = Color.cyan;
        private static readonly Color s_pocketRimColor   = Color.yellow;
        private static readonly Color s_pocketRimSelColor = new Color(1f, 0.5f, 0f, 1f);
        private static readonly Color s_handleColor      = Color.white;
        private static readonly Color s_addPreviewColor  = new Color(1f, 1f, 0f, 0.6f);

        // ── Selection ─────────────────────────────────────────────────────
        private enum SelectionKind { None, TableSegment, PocketRimSegment }
        private SelectionKind _selKind    = SelectionKind.None;
        private int           _selPrimary = -1; // table segment idx or pocket idx
        private int           _selRim     = -1; // rim segment idx inside pocket

        // ── Add-segment mode ──────────────────────────────────────────────
        private enum AddMode { None, TableSegment, PocketRimSegment }
        private AddMode _addMode       = AddMode.None;
        private int     _addPocketIdx  = -1;
        private bool    _waitingForEnd = false;
        private Vector2 _addStartPos;

        // ── Length Constraint Mode (editor-only, never serialized) ────────
        private bool    _lengthConstraintEnabled = false;
        private float[] _segmentLengths          = System.Array.Empty<float>();

        // ── Rim Generation Settings (per-pocket, editor-only) ─────────────
        private int[]   _rimGenSegCount      = System.Array.Empty<int>();
        private float[] _rimGenStartAngle    = System.Array.Empty<float>();
        private bool[]  _rimGenClearExisting = System.Array.Empty<bool>();

        // Threshold for treating a segment as degenerate (squared length).
        private const float k_minSegLenSq = 1e-6f;
        // Fallback direction used when a segment is degenerate.
        private static readonly Vector2 k_defaultSegDir = Vector2.right;

        // ── Cached serialized properties (set once in OnEnable) ───────────
        private SerializedProperty _tableSegsProp;
        private SerializedProperty _pocketsProp;

        private void OnEnable()
        {
            SerializedProperty tableProp = serializedObject.FindProperty("Table");
            _tableSegsProp = tableProp?.FindPropertyRelative("Segments");
            _pocketsProp   = serializedObject.FindProperty("Pockets");
            SyncSegmentLengths();
            SyncRimGenArrays();
        }

        // Keep _segmentLengths in sync with the number of table segments.
        // New entries are initialised from the current Start/End distance.
        private void SyncSegmentLengths()
        {
            if (_tableSegsProp == null) return;
            int count = _tableSegsProp.arraySize;
            if (_segmentLengths.Length == count) return;

            float[] next = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (i < _segmentLengths.Length)
                {
                    next[i] = _segmentLengths[i];
                }
                else
                {
                    SerializedProperty seg = _tableSegsProp.GetArrayElementAtIndex(i);
                    SerializedProperty sp  = seg.FindPropertyRelative("Start");
                    SerializedProperty ep  = seg.FindPropertyRelative("End");
                    next[i] = (sp != null && ep != null)
                        ? (ep.vector2Value - sp.vector2Value).magnitude
                        : 0f;
                }
            }
            _segmentLengths = next;
        }

        // ── Inspector GUI ─────────────────────────────────────────────────
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // Keep lengths array in sync after default inspector may have changed array size.
            SyncSegmentLengths();

            // ── Length Constraint Mode ────────────────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Length Constraint", EditorStyles.boldLabel);
            _lengthConstraintEnabled = EditorGUILayout.ToggleLeft(
                "Length Constraint Mode", _lengthConstraintEnabled);

            if (_lengthConstraintEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Move Start → End is repositioned to keep the segment length constant.\n" +
                    "Move End   → Start is fixed; End is projected onto the circle of the given radius.\n" +
                    "Edit Length below → End is recalculated so Start→End equals the new length.\n" +
                    "Length is editor-only and is never saved to the runtime data.",
                    MessageType.Info);

                if (_tableSegsProp != null)
                {
                    for (int i = 0; i < _tableSegsProp.arraySize; i++)
                    {
                        float prevLen = _segmentLengths[i];
                        float newLen  = EditorGUILayout.FloatField("Segment " + i + " Length", prevLen);

                        bool invalid = float.IsNaN(newLen) || float.IsInfinity(newLen) || newLen < 0f;
                        if (invalid)
                        {
                            EditorGUILayout.HelpBox(
                                "Segment " + i + ": Length must be a non-negative finite number.",
                                MessageType.Warning);
                        }
                        else if (!Mathf.Approximately(newLen, prevLen))
                        {
                            SerializedProperty seg = _tableSegsProp.GetArrayElementAtIndex(i);
                            SerializedProperty sp  = seg.FindPropertyRelative("Start");
                            SerializedProperty ep  = seg.FindPropertyRelative("End");
                            if (sp != null && ep != null)
                            {
                                Vector2 sv  = sp.vector2Value;
                                Vector2 dir = ep.vector2Value - sv;
                                Vector2 newEnd = dir.sqrMagnitude > k_minSegLenSq
                                    ? sv + dir.normalized * newLen
                                    : sv + k_defaultSegDir * newLen;

                                Undo.RecordObject(target, "Change Segment Length");
                                ep.vector2Value = newEnd;
                                serializedObject.ApplyModifiedProperties();
                                EditorUtility.SetDirty(target);
                                _segmentLengths[i] = newLen;
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Table Segments", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Table Segment"))
            {
                _addMode       = AddMode.TableSegment;
                _addPocketIdx  = -1;
                _waitingForEnd = false;
                SceneView.RepaintAll();
            }

            if (_tableSegsProp != null && _tableSegsProp.arraySize > 0 &&
                GUILayout.Button("Remove Last Table Segment"))
            {
                Undo.RecordObject(target, "Remove Last Table Segment");
                _tableSegsProp.arraySize--;
                if (_selKind == SelectionKind.TableSegment &&
                    _selPrimary >= _tableSegsProp.arraySize)
                    ClearSelection();
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pockets", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Pocket"))
            {
                Undo.RecordObject(target, "Add Pocket");
                _pocketsProp.arraySize++;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }

            if (_pocketsProp != null && _pocketsProp.arraySize > 0)
            {
                if (GUILayout.Button("Remove Last Pocket"))
                {
                    Undo.RecordObject(target, "Remove Last Pocket");
                    int removed = _pocketsProp.arraySize - 1;
                    _pocketsProp.arraySize--;
                    if (_selKind == SelectionKind.PocketRimSegment &&
                        _selPrimary == removed)
                        ClearSelection();
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Add Rim Segment to Pocket:");
                for (int i = 0; i < _pocketsProp.arraySize; i++)
                {
                    if (GUILayout.Button("Pocket " + i + ": Add Rim Segment"))
                    {
                        _addMode       = AddMode.PocketRimSegment;
                        _addPocketIdx  = i;
                        _waitingForEnd = false;
                        SceneView.RepaintAll();
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Generate Rim From Circle:", EditorStyles.boldLabel);
                SyncRimGenArrays();
                for (int i = 0; i < _pocketsProp.arraySize; i++)
                {
                    EditorGUILayout.LabelField("Pocket " + i, EditorStyles.miniBoldLabel);
                    _rimGenSegCount[i]      = EditorGUILayout.IntField("  Segment Count", _rimGenSegCount[i]);
                    _rimGenStartAngle[i]    = EditorGUILayout.FloatField("  Start Angle (deg)", _rimGenStartAngle[i]);
                    _rimGenClearExisting[i] = EditorGUILayout.Toggle("  Clear Existing", _rimGenClearExisting[i]);

                    bool invalid = _rimGenSegCount[i] < 3;
                    if (invalid)
                    {
                        EditorGUILayout.HelpBox(
                            "Segment count must be >= 3.", MessageType.Warning);
                    }

                    EditorGUI.BeginDisabledGroup(invalid);
                    if (GUILayout.Button("Pocket " + i + ": Generate Rim From Circle"))
                    {
                        GenerateRimFromCircle(i);
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }

            if (_addMode != AddMode.None)
            {
                EditorGUILayout.HelpBox(
                    _waitingForEnd
                        ? "Click in Scene to place the end point."
                        : "Click in Scene to place the start point.",
                    MessageType.Info);

                if (GUILayout.Button("Cancel"))
                {
                    _addMode       = AddMode.None;
                    _waitingForEnd = false;
                    SceneView.RepaintAll();
                }
            }
        }

        // ── Scene GUI ─────────────────────────────────────────────────────
        private void OnSceneGUI()
        {
            if (_tableSegsProp == null || _pocketsProp == null) return;

            serializedObject.Update();
            SyncSegmentLengths();

            Event e       = Event.current;
            bool  changed = false;

            // Delete key: remove selected element
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete)
            {
                if (TryDeleteSelected())
                {
                    changed = true;
                    e.Use();
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    return;
                }
            }

            // Add-segment mode: capture clicks to place start/end points
            if (_addMode != AddMode.None)
            {
                int ctrlId = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(ctrlId);

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    Vector2 worldPos = MouseToWorld(e.mousePosition);
                    if (!_waitingForEnd)
                    {
                        _addStartPos   = worldPos;
                        _waitingForEnd = true;
                    }
                    else
                    {
                        FinishAddSegment(worldPos);
                        changed        = true;
                        _waitingForEnd = false;
                        _addMode       = AddMode.None;
                    }
                    e.Use();
                }
                else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    _addMode       = AddMode.None;
                    _waitingForEnd = false;
                    e.Use();
                }

                // Draw preview line from first click to current mouse
                if (_waitingForEnd)
                {
                    Vector2 mw = MouseToWorld(e.mousePosition);
                    Handles.color = s_addPreviewColor;
                    Handles.DrawLine(
                        new Vector3(_addStartPos.x, _addStartPos.y, 0f),
                        new Vector3(mw.x,           mw.y,           0f));
                    Handles.DotHandleCap(0,
                        new Vector3(_addStartPos.x, _addStartPos.y, 0f),
                        Quaternion.identity,
                        HandleUtility.GetHandleSize(new Vector3(_addStartPos.x, _addStartPos.y, 0f)) * 0.08f,
                        EventType.Repaint);

                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                        SceneView.RepaintAll();
                }
            }

            // ── Draw table segments ───────────────────────────────────────
            for (int i = 0; i < _tableSegsProp.arraySize; i++)
            {
                SerializedProperty seg       = _tableSegsProp.GetArrayElementAtIndex(i);
                SerializedProperty startProp = seg.FindPropertyRelative("Start");
                SerializedProperty endProp   = seg.FindPropertyRelative("End");
                if (startProp == null || endProp == null) continue;

                Vector2 sv = startProp.vector2Value;
                Vector2 ev = endProp.vector2Value;
                Vector3 s3 = new Vector3(sv.x, sv.y, 0f);
                Vector3 e3 = new Vector3(ev.x, ev.y, 0f);

                bool isSel = (_selKind == SelectionKind.TableSegment && _selPrimary == i);

                // Segment line
                Handles.color = isSel ? s_tableSelColor : s_tableSegColor;
                Handles.DrawLine(s3, e3);

                // Outward normal arrow from midpoint
                Vector3 seg3 = e3 - s3;
                if (seg3.sqrMagnitude > 0.0001f)
                {
                    Vector3 mid    = (s3 + e3) * 0.5f;
                    Vector3 dirN   = seg3.normalized;
                    Vector3 normal = new Vector3(-dirN.y, dirN.x, 0f);
                    Handles.color = s_normalColor;
                    Handles.DrawLine(mid, mid + normal * 0.3f);
                    Handles.ArrowHandleCap(0,
                        mid + normal * 0.3f,
                        Quaternion.LookRotation(normal),
                        0.1f, EventType.Repaint);
                }

                // Clickable midpoint dot for selection
                Vector3 mid2    = (s3 + e3) * 0.5f;
                float   dotSize = HandleUtility.GetHandleSize(mid2) * 0.08f;
                Handles.color = isSel ? s_tableSelColor : s_tableSegColor;
                if (Handles.Button(mid2, Quaternion.identity, dotSize, dotSize,
                                   Handles.DotHandleCap))
                {
                    _selKind    = SelectionKind.TableSegment;
                    _selPrimary = i;
                    _selRim     = -1;
                }

                // Endpoint position handles
                Handles.color = s_handleColor;
                EditorGUI.BeginChangeCheck();
                Vector3 newS = Handles.PositionHandle(s3, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Move Table Segment Start");
                    Vector2 newStartV = new Vector2(newS.x, newS.y);
                    Vector2 newEndV   = ev; // default: end unchanged
                    if (_lengthConstraintEnabled && i < _segmentLengths.Length)
                    {
                        // Keep length: reposition End along the original direction from the new Start.
                        float   segLen = _segmentLengths[i];
                        Vector2 dir    = ev - sv;
                        newEndV = dir.sqrMagnitude > k_minSegLenSq
                            ? newStartV + dir.normalized * segLen
                            : newStartV + k_defaultSegDir * segLen;
                    }
                    startProp.vector2Value = newStartV;
                    endProp.vector2Value   = newEndV;
                    _selKind    = SelectionKind.TableSegment;
                    _selPrimary = i;
                    changed     = true;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 newE = Handles.PositionHandle(e3, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Move Table Segment End");
                    Vector2 newEndV = new Vector2(newE.x, newE.y);
                    if (_lengthConstraintEnabled && i < _segmentLengths.Length)
                    {
                        // Keep Start and length fixed; project End onto the circle of radius segLen.
                        float   segLen = _segmentLengths[i];
                        Vector2 drag   = new Vector2(newE.x, newE.y);
                        Vector2 dir    = drag - sv;
                        newEndV = dir.sqrMagnitude > k_minSegLenSq
                            ? sv + dir.normalized * segLen
                            : sv + k_defaultSegDir * segLen;
                    }
                    endProp.vector2Value = newEndV;
                    _selKind    = SelectionKind.TableSegment;
                    _selPrimary = i;
                    changed     = true;
                }
            }

            // ── Draw pockets ──────────────────────────────────────────────
            for (int i = 0; i < _pocketsProp.arraySize; i++)
            {
                SerializedProperty pocket     = _pocketsProp.GetArrayElementAtIndex(i);
                SerializedProperty centerProp = pocket.FindPropertyRelative("Center");
                SerializedProperty radiusProp = pocket.FindPropertyRelative("Radius");
                SerializedProperty rimsProp   = pocket.FindPropertyRelative("RimSegments");
                if (centerProp == null || radiusProp == null) continue;

                Vector2 cv     = centerProp.vector2Value;
                Vector3 center = new Vector3(cv.x, cv.y, 0f);
                float   radius = radiusProp.floatValue;

                // Pocket radius wire disc (cyan)
                Handles.color = s_pocketRadColor;
                Handles.DrawWireDisc(center, Vector3.forward, radius);

                // Center dot
                Handles.DotHandleCap(0, center, Quaternion.identity,
                    HandleUtility.GetHandleSize(center) * 0.08f, EventType.Repaint);

                // Radius drag handle – slider along +X from center
                Vector3 radiusPt = new Vector3(cv.x + radius, cv.y, 0f);
                float   rHSize   = HandleUtility.GetHandleSize(radiusPt) * 0.1f;
                EditorGUI.BeginChangeCheck();
                Vector3 newRPt = Handles.Slider(radiusPt, Vector3.right,
                                                rHSize, Handles.DotHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    float newRad = Mathf.Max(0.01f, newRPt.x - cv.x);
                    Undo.RecordObject(target, "Resize Pocket Radius");
                    radiusProp.floatValue = newRad;
                    changed = true;
                }

                // Center position handle
                Handles.color = s_handleColor;
                EditorGUI.BeginChangeCheck();
                Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Move Pocket Center");
                    centerProp.vector2Value = new Vector2(newCenter.x, newCenter.y);
                    changed = true;
                }

                // Rim segments
                if (rimsProp == null) continue;
                for (int j = 0; j < rimsProp.arraySize; j++)
                {
                    SerializedProperty rim      = rimsProp.GetArrayElementAtIndex(j);
                    SerializedProperty rimStart = rim.FindPropertyRelative("Start");
                    SerializedProperty rimEnd   = rim.FindPropertyRelative("End");
                    if (rimStart == null || rimEnd == null) continue;

                    Vector2 rsv = rimStart.vector2Value;
                    Vector2 rev = rimEnd.vector2Value;
                    Vector3 rs  = new Vector3(rsv.x, rsv.y, 0f);
                    Vector3 re  = new Vector3(rev.x, rev.y, 0f);

                    bool rimSel = (_selKind == SelectionKind.PocketRimSegment &&
                                   _selPrimary == i && _selRim == j);

                    // Rim segment line (yellow / orange when selected)
                    Handles.color = rimSel ? s_pocketRimSelColor : s_pocketRimColor;
                    Handles.DrawLine(rs, re);

                    // Clickable midpoint for selection
                    Vector3 rimMid  = (rs + re) * 0.5f;
                    float   rimDot  = HandleUtility.GetHandleSize(rimMid) * 0.08f;
                    if (Handles.Button(rimMid, Quaternion.identity, rimDot, rimDot,
                                       Handles.DotHandleCap))
                    {
                        _selKind    = SelectionKind.PocketRimSegment;
                        _selPrimary = i;
                        _selRim     = j;
                    }

                    // Endpoint position handles
                    Handles.color = s_handleColor;
                    EditorGUI.BeginChangeCheck();
                    Vector3 newRs = Handles.PositionHandle(rs, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Move Rim Segment Start");
                        rimStart.vector2Value = new Vector2(newRs.x, newRs.y);
                        _selKind    = SelectionKind.PocketRimSegment;
                        _selPrimary = i;
                        _selRim     = j;
                        changed     = true;
                    }

                    EditorGUI.BeginChangeCheck();
                    Vector3 newRe = Handles.PositionHandle(re, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Move Rim Segment End");
                        rimEnd.vector2Value = new Vector2(newRe.x, newRe.y);
                        _selKind    = SelectionKind.PocketRimSegment;
                        _selPrimary = i;
                        _selRim     = j;
                        changed     = true;
                    }
                }
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        // Keep _rimGenSegCount/Angle/Clear arrays in sync with the pocket count.
        private void SyncRimGenArrays()
        {
            if (_pocketsProp == null) return;
            int count = _pocketsProp.arraySize;
            if (_rimGenSegCount.Length == count) return;

            int[]   newCount = new int[count];
            float[] newAngle = new float[count];
            bool[]  newClear = new bool[count];
            for (int i = 0; i < count; i++)
            {
                newCount[i] = i < _rimGenSegCount.Length      ? _rimGenSegCount[i]      : 8;
                newAngle[i] = i < _rimGenStartAngle.Length    ? _rimGenStartAngle[i]    : 0f;
                newClear[i] = i < _rimGenClearExisting.Length ? _rimGenClearExisting[i] : true;
            }
            _rimGenSegCount      = newCount;
            _rimGenStartAngle    = newAngle;
            _rimGenClearExisting = newClear;
        }

        private void GenerateRimFromCircle(int pocketIdx)
        {
            if (_pocketsProp == null || pocketIdx < 0 || pocketIdx >= _pocketsProp.arraySize)
                return;

            SerializedProperty pocket     = _pocketsProp.GetArrayElementAtIndex(pocketIdx);
            SerializedProperty centerProp = pocket.FindPropertyRelative("Center");
            SerializedProperty radiusProp = pocket.FindPropertyRelative("Radius");
            SerializedProperty rimsProp   = pocket.FindPropertyRelative("RimSegments");
            if (centerProp == null || radiusProp == null || rimsProp == null) return;

            Vector2 center   = centerProp.vector2Value;
            float   radius   = Mathf.Max(0.01f, radiusProp.floatValue);
            int     A        = _rimGenSegCount[pocketIdx];
            float   startRad = _rimGenStartAngle[pocketIdx] * Mathf.Deg2Rad;
            bool    clear    = _rimGenClearExisting[pocketIdx];

            Undo.RecordObject(target, "Generate Pocket Rim Segments");

            if (clear)
                rimsProp.arraySize = 0;

            int baseIdx = rimsProp.arraySize;
            rimsProp.arraySize += A;

            for (int k = 0; k < A; k++)
            {
                float   thetaK  = startRad + k       * 2f * Mathf.PI / A;
                float   thetaK1 = startRad + (k + 1) * 2f * Mathf.PI / A;
                Vector2 pK  = center + new Vector2(Mathf.Cos(thetaK),  Mathf.Sin(thetaK))  * radius;
                Vector2 pK1 = center + new Vector2(Mathf.Cos(thetaK1), Mathf.Sin(thetaK1)) * radius;

                SerializedProperty seg    = rimsProp.GetArrayElementAtIndex(baseIdx + k);
                SerializedProperty startP = seg.FindPropertyRelative("Start");
                SerializedProperty endP   = seg.FindPropertyRelative("End");
                startP.vector2Value = pK;
                endP.vector2Value   = pK1;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);

            _selKind    = SelectionKind.PocketRimSegment;
            _selPrimary = pocketIdx;
            _selRim     = baseIdx;
            SceneView.RepaintAll();
        }

        private void ClearSelection()
        {
            _selKind    = SelectionKind.None;
            _selPrimary = -1;
            _selRim     = -1;
        }

        private bool TryDeleteSelected()
        {
            if (_selKind == SelectionKind.TableSegment &&
                _selPrimary >= 0 && _tableSegsProp != null &&
                _selPrimary < _tableSegsProp.arraySize)
            {
                Undo.RecordObject(target, "Delete Table Segment");
                _tableSegsProp.DeleteArrayElementAtIndex(_selPrimary);
                ClearSelection();
                return true;
            }

            if (_selKind == SelectionKind.PocketRimSegment &&
                _selPrimary >= 0 && _pocketsProp != null &&
                _selPrimary < _pocketsProp.arraySize)
            {
                SerializedProperty pocket  = _pocketsProp.GetArrayElementAtIndex(_selPrimary);
                SerializedProperty rimsProp = pocket.FindPropertyRelative("RimSegments");
                if (rimsProp != null && _selRim >= 0 && _selRim < rimsProp.arraySize)
                {
                    Undo.RecordObject(target, "Delete Pocket Rim Segment");
                    rimsProp.DeleteArrayElementAtIndex(_selRim);
                    ClearSelection();
                    return true;
                }
            }

            return false;
        }

        private void FinishAddSegment(Vector2 endPos)
        {
            if (_addMode == AddMode.TableSegment && _tableSegsProp != null)
            {
                Undo.RecordObject(target, "Add Table Segment");
                int idx = _tableSegsProp.arraySize;
                _tableSegsProp.arraySize++;
                SerializedProperty seg    = _tableSegsProp.GetArrayElementAtIndex(idx);
                SerializedProperty startP = seg.FindPropertyRelative("Start");
                SerializedProperty endP   = seg.FindPropertyRelative("End");
                startP.vector2Value = _addStartPos;
                endP.vector2Value   = endPos;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _selKind    = SelectionKind.TableSegment;
                _selPrimary = idx;
                _selRim     = -1;
            }
            else if (_addMode == AddMode.PocketRimSegment &&
                     _pocketsProp != null &&
                     _addPocketIdx >= 0 && _addPocketIdx < _pocketsProp.arraySize)
            {
                Undo.RecordObject(target, "Add Pocket Rim Segment");
                SerializedProperty pocket  = _pocketsProp.GetArrayElementAtIndex(_addPocketIdx);
                SerializedProperty rimsProp = pocket.FindPropertyRelative("RimSegments");
                int idx = rimsProp.arraySize;
                rimsProp.arraySize++;
                SerializedProperty rim    = rimsProp.GetArrayElementAtIndex(idx);
                SerializedProperty startP = rim.FindPropertyRelative("Start");
                SerializedProperty endP   = rim.FindPropertyRelative("End");
                startP.vector2Value = _addStartPos;
                endP.vector2Value   = endPos;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _selKind    = SelectionKind.PocketRimSegment;
                _selPrimary = _addPocketIdx;
                _selRim     = idx;
            }
        }

        // Project mouse screen position onto the z = 0 world plane.
        private static Vector2 MouseToWorld(Vector2 mousePos)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);
            if (Mathf.Abs(ray.direction.z) > 1e-6f)
            {
                float   t     = -ray.origin.z / ray.direction.z;
                Vector3 world = ray.origin + ray.direction * t;
                return new Vector2(world.x, world.y);
            }
            return new Vector2(ray.origin.x, ray.origin.y);
        }
    }
}
#endif
