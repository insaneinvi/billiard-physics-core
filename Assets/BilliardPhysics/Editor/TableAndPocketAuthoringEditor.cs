#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace BilliardPhysics.Editor
{
    [CustomEditor(typeof(TableAndPocketAuthoring))]
    public class TableAndPocketAuthoringEditor : UnityEditor.Editor
    {
        // ── Colors ────────────────────────────────────────────────────────
        private static readonly Color s_tableSegColor     = Color.green;
        private static readonly Color s_tableSelColor     = Color.red;
        private static readonly Color s_normalColor       = new Color(0f, 0.4f, 1f, 0.8f);
        private static readonly Color s_pocketRadColor    = Color.cyan;
        private static readonly Color s_pocketRimColor    = Color.yellow;
        private static readonly Color s_pocketRimSelColor = Color.red;
        private static readonly Color s_handleColor       = Color.white;

        // ── Selection ─────────────────────────────────────────────────────
        private enum SelectionKind { None, TableSegment, PocketRimSegment }
        private SelectionKind _selKind    = SelectionKind.None;
        private int           _selPrimary = -1; // table segment idx or pocket idx
        private int           _selRim     = -1; // rim segment idx inside pocket

        // Sub-element selection within a rim segment (Start or End point).
        // When set, the Delete key promotes from ConnectionPoints instead of
        // deleting the whole segment (see TryDeleteSelected / RimSegmentHelper).
        private enum SelectionPoint { None, SegStart, SegEnd }
        private SelectionPoint _selPointKind = SelectionPoint.None;

        // ── Length Constraint Mode (editor-only, never serialized) ────────
        private bool    _lengthConstraintEnabled = false;
        private float[] _segmentLengths          = System.Array.Empty<float>();

        // ── Rim Generation Settings (per-pocket, editor-only) ─────────────
        private int[] _rimGenPointCount = System.Array.Empty<int>();

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

        private void OnDisable()
        {
            SceneView.RepaintAll();
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
                EditorGUILayout.LabelField("Generate Rim From Circle:", EditorStyles.boldLabel);
                SyncRimGenArrays();
                for (int i = 0; i < _pocketsProp.arraySize; i++)
                {
                    EditorGUILayout.LabelField("Pocket " + i, EditorStyles.miniBoldLabel);
                    _rimGenPointCount[i] = EditorGUILayout.IntField("  Point Count", _rimGenPointCount[i]);

                    bool invalid = _rimGenPointCount[i] < 3;
                    if (invalid)
                    {
                        EditorGUILayout.HelpBox(
                            "Point count must be >= 3.", MessageType.Warning);
                    }

                    EditorGUI.BeginDisabledGroup(invalid);
                    if (GUILayout.Button("Pocket " + i + ": Generate Rim From Circle"))
                    {
                        GenerateRimFromCircle(i);
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }

            // ── Rim Segment Operations ────────────────────────────────────
            // Deletion of Start/End promotes the first/last ConnectionPoint.
            // Blocked (with warning) when ConnectionPoints is empty.
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Rim Segment Operations", EditorStyles.boldLabel);
            if (_pocketsProp != null)
            {
                var auth2 = (TableAndPocketAuthoring)target;
                for (int i = 0; i < _pocketsProp.arraySize; i++)
                {
                    if (i >= auth2.Pockets.Count) continue;
                    var segData  = auth2.Pockets[i].RimSegments;
                    bool emptyCPs = segData.ConnectionPoints == null ||
                                    segData.ConnectionPoints.Count == 0;

                    EditorGUILayout.LabelField("Pocket " + i + " Rim", EditorStyles.miniBoldLabel);

                    if (emptyCPs)
                    {
                        EditorGUILayout.HelpBox(
                            "ConnectionPoints is empty. Cannot remove Start or End.",
                            MessageType.Warning);
                    }

                    EditorGUI.BeginDisabledGroup(emptyCPs);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Remove End"))
                    {
                        Undo.RecordObject(target, "Remove Rim Segment End");
                        bool ok = RimSegmentHelper.TryPromoteLastCPToEnd(segData);
                        if (ok)
                        {
                            serializedObject.Update();
                            EditorUtility.SetDirty(target);
                            SceneView.RepaintAll();
                        }
                        else
                        {
                            Debug.LogWarning(
                                "[BilliardPhysics] Cannot remove End of Pocket " + i +
                                " Rim: ConnectionPoints is empty.");
                        }
                    }
                    if (GUILayout.Button("Remove Start"))
                    {
                        Undo.RecordObject(target, "Remove Rim Segment Start");
                        bool ok = RimSegmentHelper.TryPromoteFirstCPToStart(segData);
                        if (ok)
                        {
                            serializedObject.Update();
                            EditorUtility.SetDirty(target);
                            SceneView.RepaintAll();
                        }
                        else
                        {
                            Debug.LogWarning(
                                "[BilliardPhysics] Cannot remove Start of Pocket " + i +
                                " Rim: ConnectionPoints is empty.");
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.EndDisabledGroup();
                }
            }

            // ── Coordinate Transform ──────────────────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Coordinate Transform", EditorStyles.boldLabel);
            if (GUILayout.Button("Rotate CCW 90\u00b0 + Translate X=1270"))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Apply Coordinate Transform",
                    "This will rotate all table/pocket points 90\u00b0 counter-clockwise and translate X by +1270.\n\nThis operation is applied in-place and can be undone.",
                    "Apply",
                    "Cancel");
                if (confirmed)
                {
                    var auth = (TableAndPocketAuthoring)target;
                    Undo.RecordObject(target, "Apply Table/Pocket Transform");
                    TransformTableConfig(auth.Table);
                    if (auth.Pockets != null)
                        foreach (var pocket in auth.Pockets)
                            TransformPocketConfig(pocket);
                    EditorUtility.SetDirty(target);
                    SceneView.RepaintAll();
                }
            }

            // ── Fixed-Point Binary Export / Import ────────────────────────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Export / Import", EditorStyles.boldLabel);
            if (GUILayout.Button("Export Fixed Binary (.bytes)"))
            {
                ExportFixedBinary();
            }
            if (GUILayout.Button("Load Export Fixed Binary..."))
            {
                LoadFixedBinary();
            }
        }

        // ── Coordinate Transform Helpers ──────────────────────────────────
        // Rotate left (CCW) 90° about origin then translate X by offsetX.
        // Formula: newX = -oldY + offsetX, newY = oldX
        private static Vector2 RotateLeft90TranslateX(Vector2 p, float offsetX = 1270f)
            => new Vector2(-p.y + offsetX, p.x);

        private static void TransformSegmentData(SegmentData seg)
        {
            if (seg == null) return;
            seg.Start = RotateLeft90TranslateX(seg.Start);
            seg.End   = RotateLeft90TranslateX(seg.End);
            if (seg.ConnectionPoints != null)
                for (int i = 0; i < seg.ConnectionPoints.Count; i++)
                    seg.ConnectionPoints[i] = RotateLeft90TranslateX(seg.ConnectionPoints[i]);
        }

        private static void TransformTableConfig(TableConfig table)
        {
            if (table?.Segments == null) return;
            foreach (var seg in table.Segments)
                TransformSegmentData(seg);
        }

        private static void TransformPocketConfig(PocketConfig pocket)
        {
            if (pocket == null) return;
            pocket.Center = RotateLeft90TranslateX(pocket.Center);
            TransformSegmentData(pocket.RimSegments);
        }

        // ── Fixed-Point Binary Export ──────────────────────────────────────
        // 0x59485042 is the uint whose little-endian bytes are 0x42,0x50,0x48,0x59 = 'B','P','H','Y'.
        private const uint   k_exportMagic   = 0x59485042u;
        private const ushort k_exportVersion = 2;

        private void ExportFixedBinary()
        {
            var auth = (TableAndPocketAuthoring)target;

            // Build a sanitized default name from the GameObject name.
            string defaultName = string.Concat(
                auth.gameObject.name.Split(System.IO.Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(defaultName)) defaultName = "table";

            // ── Step 1: Let the user choose the export folder and file name ──
            string path = EditorUtility.SaveFilePanel(
                "Export Fixed Binary",
                "",           // default directory (OS remembers last used)
                defaultName,  // default file name (without extension)
                "bytes");     // extension filter

            if (string.IsNullOrEmpty(path))
                return; // user cancelled

            // ── Step 2: Normalize the extension to .bytes ────────────────────
            string dir      = System.IO.Path.GetDirectoryName(path);
            string fileName = ExportFixedBinaryHelper.NormalizeExtension(
                                  System.IO.Path.GetFileName(path));
            path = System.IO.Path.Combine(dir, fileName);

            // ── Step 3: Validate the file name ───────────────────────────────
            if (!ExportFixedBinaryHelper.ValidateFileName(fileName, out string validationError))
            {
                EditorUtility.DisplayDialog(
                    "Export Fixed Binary \u2014 Invalid File Name",
                    validationError,
                    "OK");
                return;
            }

            // ── Step 4: Overwrite confirmation ───────────────────────────────
            if (System.IO.File.Exists(path))
            {
                bool overwrite = EditorUtility.DisplayDialog(
                    "Export Fixed Binary \u2014 File Exists",
                    $"The following file already exists:\n{path}\n\nDo you want to overwrite it?",
                    "Overwrite",
                    "Cancel");
                if (!overwrite) return;
            }

            // ── Step 5: Serialise and write ───────────────────────────────────
            try
            {
                using (var writer = new System.IO.BinaryWriter(
                           System.IO.File.Open(path, System.IO.FileMode.Create)))
                {
                    WriteFixedBinaryBody(writer, auth);
                }
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Export Fixed Binary \u2014 Error",
                    $"Could not write file:\n{path}\n\nReason: {ex.Message}",
                    "OK");
                return;
            }

            // Refresh AssetDatabase so the file appears in the Project view when
            // the export target is inside the project folder.
            AssetDatabase.Refresh();
            Debug.Log($"[BilliardPhysics] Exported fixed-point binary to {path}");

            // ── Step 6: Success – offer to reveal the file ───────────────────
            bool reveal = EditorUtility.DisplayDialog(
                "Export Fixed Binary \u2014 Done",
                $"Exported successfully to:\n{path}",
                "Reveal in Finder/Explorer",
                "OK");
            if (reveal)
                EditorUtility.RevealInFinder(path);
        }

        // ── Serialisation (version 2 format) ──────────────────────────
        // Writes the fixed-point binary body (header + table segments + pockets)
        // to <paramref name="writer"/>.  Separated from the UI flow so it can be
        // called independently and tested without any dialog interaction.
        //
        // Version 2 layout per segment:  Start(x,y) + End(x,y) + CPCount + CP[0..n-1]
        // This preserves ConnectionPoints; the runtime Segment is created 1-to-1.
        private static void WriteFixedBinaryBody(
            System.IO.BinaryWriter writer, TableAndPocketAuthoring auth)
        {
            // Header
            writer.Write(k_exportMagic);
            writer.Write(k_exportVersion);

            // Table segments – one entry per SegmentData, preserving ConnectionPoints.
            var segs = auth.Table.Segments;
            writer.Write(segs.Count);
            foreach (var seg in segs)
            {
                writer.Write(Fix64.FromFloat(seg.Start.x).RawValue);
                writer.Write(Fix64.FromFloat(seg.Start.y).RawValue);
                writer.Write(Fix64.FromFloat(seg.End.x).RawValue);
                writer.Write(Fix64.FromFloat(seg.End.y).RawValue);
                WriteConnectionPoints(writer, seg.ConnectionPoints);
            }

            // Pockets – one rim entry per SegmentData, preserving ConnectionPoints.
            var pockets = auth.Pockets;
            writer.Write(pockets.Count);
            foreach (var pocket in pockets)
            {
                writer.Write(Fix64.FromFloat(pocket.Center.x).RawValue);
                writer.Write(Fix64.FromFloat(pocket.Center.y).RawValue);
                writer.Write(Fix64.FromFloat(pocket.Radius).RawValue);
                writer.Write(Fix64.FromFloat(pocket.ReboundVelocityThreshold).RawValue);

                // Rim: one entry for the single RimSegments SegmentData.
                writer.Write(1);  // rimSegCount = 1
                var rim = pocket.RimSegments;
                writer.Write(Fix64.FromFloat(rim.Start.x).RawValue);
                writer.Write(Fix64.FromFloat(rim.Start.y).RawValue);
                writer.Write(Fix64.FromFloat(rim.End.x).RawValue);
                writer.Write(Fix64.FromFloat(rim.End.y).RawValue);
                WriteConnectionPoints(writer, rim.ConnectionPoints);
            }
        }

        private static void WriteConnectionPoints(
            System.IO.BinaryWriter writer, System.Collections.Generic.List<Vector2> cps)
        {
            int count = (cps != null) ? cps.Count : 0;
            writer.Write(count);
            if (cps != null)
            {
                foreach (var cp in cps)
                {
                    writer.Write(Fix64.FromFloat(cp.x).RawValue);
                    writer.Write(Fix64.FromFloat(cp.y).RawValue);
                }
            }
        }

        // ── Fixed-Point Binary Import ──────────────────────────────────────
        private void LoadFixedBinary()
        {
            string path = EditorUtility.OpenFilePanel("Load Export Fixed Binary", "", "bytes");
            if (string.IsNullOrEmpty(path)) return; // user cancelled

            if (EditorUtility.IsDirty(target))
            {
                bool discard = EditorUtility.DisplayDialog(
                    "Load Export Fixed Binary",
                    "The current data has unsaved changes. Discard them and load the selected file?",
                    "Discard",
                    "Cancel");
                if (!discard) return;
            }

            byte[] bytes;
            try
            {
                bytes = System.IO.File.ReadAllBytes(path);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Load Export Fixed Binary \u2014 Error",
                    $"Could not read file:\n{path}\n\nReason: {ex.Message}",
                    "OK");
                return;
            }

            List<Segment> tableSegments;
            List<Pocket>  pockets;
            try
            {
                TableAndPocketBinaryLoader.Load(bytes, out tableSegments, out pockets);
            }
            catch (System.IO.InvalidDataException ex)
            {
                EditorUtility.DisplayDialog(
                    "Load Export Fixed Binary \u2014 Error",
                    $"File format error:\n{path}\n\nReason: {ex.Message}",
                    "OK");
                return;
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Load Export Fixed Binary \u2014 Error",
                    $"Unexpected error while loading:\n{path}\n\nReason: {ex.Message}",
                    "OK");
                return;
            }

            var auth = (TableAndPocketAuthoring)target;
            Undo.RecordObject(target, "Load Fixed Binary");
            auth.Table   = ImportFixedBinaryHelper.BuildTableConfig(tableSegments);
            auth.Pockets = ImportFixedBinaryHelper.BuildPocketConfigs(pockets);
            serializedObject.Update();
            EditorUtility.SetDirty(target);
            SyncSegmentLengths();
            SyncRimGenArrays();
            ClearSelection();
            SceneView.RepaintAll();
            Debug.Log($"[BilliardPhysics] Loaded fixed-point binary from {path}");
        }

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

            // ── Draw table segments ───────────────────────────────────────
            for (int i = 0; i < _tableSegsProp.arraySize; i++)
            {
                SerializedProperty seg       = _tableSegsProp.GetArrayElementAtIndex(i);
                SerializedProperty startProp = seg.FindPropertyRelative("Start");
                SerializedProperty endProp   = seg.FindPropertyRelative("End");
                SerializedProperty cpsProp   = seg.FindPropertyRelative("ConnectionPoints");
                if (startProp == null || endProp == null) continue;

                Vector2 sv = startProp.vector2Value;
                Vector2 ev = endProp.vector2Value;
                Vector3 s3 = new Vector3(sv.x, sv.y, 0f);
                Vector3 e3 = new Vector3(ev.x, ev.y, 0f);

                bool isSel = (_selKind == SelectionKind.TableSegment && _selPrimary == i);

                // Polyline: Start → CP[0] → … → End
                Handles.color = isSel ? s_tableSelColor : s_tableSegColor;
                DrawPolylineHandles(s3, e3, cpsProp);

                // Outward normal arrow from the overall Start→End midpoint
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

                // Connection point position handles
                if (cpsProp != null)
                {
                    Handles.color = s_handleColor;
                    for (int k = 0; k < cpsProp.arraySize; k++)
                    {
                        SerializedProperty cp  = cpsProp.GetArrayElementAtIndex(k);
                        Vector2            cpv = cp.vector2Value;
                        Vector3            cp3 = new Vector3(cpv.x, cpv.y, 0f);
                        EditorGUI.BeginChangeCheck();
                        Vector3 newCp = Handles.PositionHandle(cp3, Quaternion.identity);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(target, "Move Table Segment Connection Point");
                            cp.vector2Value = new Vector2(newCp.x, newCp.y);
                            _selKind    = SelectionKind.TableSegment;
                            _selPrimary = i;
                            changed     = true;
                        }
                    }
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

                // Rim segment (single SegmentData)
                if (rimsProp == null) continue;
                {
                    SerializedProperty rimStart = rimsProp.FindPropertyRelative("Start");
                    SerializedProperty rimEnd   = rimsProp.FindPropertyRelative("End");
                    SerializedProperty rimCps   = rimsProp.FindPropertyRelative("ConnectionPoints");
                    if (rimStart == null || rimEnd == null) continue;

                    Vector2 rsv = rimStart.vector2Value;
                    Vector2 rev = rimEnd.vector2Value;
                    Vector3 rs  = new Vector3(rsv.x, rsv.y, 0f);
                    Vector3 re  = new Vector3(rev.x, rev.y, 0f);

                    bool rimSel = (_selKind == SelectionKind.PocketRimSegment &&
                                   _selPrimary == i && _selRim == 0);

                    // Polyline: Start → CP[0] → … → End
                    Handles.color = rimSel ? s_pocketRimSelColor : s_pocketRimColor;
                    DrawPolylineHandles(rs, re, rimCps);

                    // Clickable midpoint selects the whole segment (clears point sub-selection).
                    Vector3 rimMid  = (rs + re) * 0.5f;
                    float   rimDot  = HandleUtility.GetHandleSize(rimMid) * 0.08f;
                    if (Handles.Button(rimMid, Quaternion.identity, rimDot, rimDot,
                                       Handles.DotHandleCap))
                    {
                        _selKind      = SelectionKind.PocketRimSegment;
                        _selPrimary   = i;
                        _selRim       = 0;
                        _selPointKind = SelectionPoint.None;
                    }

                    // Endpoint position handles.
                    // Small clickable dots allow selecting Start/End without dragging,
                    // enabling point-level deletion (Delete key → CP promotion).
                    Handles.color = s_handleColor;

                    float dotSizeS = HandleUtility.GetHandleSize(rs) * 0.06f;
                    if (Handles.Button(rs, Quaternion.identity, dotSizeS, dotSizeS,
                                       Handles.DotHandleCap))
                    {
                        _selKind      = SelectionKind.PocketRimSegment;
                        _selPrimary   = i;
                        _selRim       = 0;
                        _selPointKind = SelectionPoint.SegStart;
                    }

                    EditorGUI.BeginChangeCheck();
                    Vector3 newRs = Handles.PositionHandle(rs, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Move Rim Segment Start");
                        rimStart.vector2Value = new Vector2(newRs.x, newRs.y);
                        _selKind      = SelectionKind.PocketRimSegment;
                        _selPrimary   = i;
                        _selRim       = 0;
                        _selPointKind = SelectionPoint.SegStart;
                        changed       = true;
                    }

                    float dotSizeE = HandleUtility.GetHandleSize(re) * 0.06f;
                    if (Handles.Button(re, Quaternion.identity, dotSizeE, dotSizeE,
                                       Handles.DotHandleCap))
                    {
                        _selKind      = SelectionKind.PocketRimSegment;
                        _selPrimary   = i;
                        _selRim       = 0;
                        _selPointKind = SelectionPoint.SegEnd;
                    }

                    EditorGUI.BeginChangeCheck();
                    Vector3 newRe = Handles.PositionHandle(re, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(target, "Move Rim Segment End");
                        rimEnd.vector2Value = new Vector2(newRe.x, newRe.y);
                        _selKind      = SelectionKind.PocketRimSegment;
                        _selPrimary   = i;
                        _selRim       = 0;
                        _selPointKind = SelectionPoint.SegEnd;
                        changed       = true;
                    }

                    // Connection point position handles
                    if (rimCps != null)
                    {
                        Handles.color = s_handleColor;
                        for (int k = 0; k < rimCps.arraySize; k++)
                        {
                            SerializedProperty cp  = rimCps.GetArrayElementAtIndex(k);
                            Vector2            cpv = cp.vector2Value;
                            Vector3            cp3 = new Vector3(cpv.x, cpv.y, 0f);
                            EditorGUI.BeginChangeCheck();
                            Vector3 newCp = Handles.PositionHandle(cp3, Quaternion.identity);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(target, "Move Rim Segment Connection Point");
                                cp.vector2Value = new Vector2(newCp.x, newCp.y);
                                _selKind      = SelectionKind.PocketRimSegment;
                                _selPrimary   = i;
                                _selRim       = 0;
                                _selPointKind = SelectionPoint.None;
                                changed       = true;
                            }
                        }
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

        // Keep _rimGenPointCount array in sync with the pocket count.
        private void SyncRimGenArrays()
        {
            if (_pocketsProp == null) return;
            int count = _pocketsProp.arraySize;
            if (_rimGenPointCount.Length == count) return;

            int[] newCount = new int[count];
            for (int i = 0; i < count; i++)
                newCount[i] = i < _rimGenPointCount.Length ? _rimGenPointCount[i] : 8;
            _rimGenPointCount = newCount;
        }

        private void GenerateRimFromCircle(int pocketIdx)
        {
            if (_pocketsProp == null || pocketIdx < 0 || pocketIdx >= _pocketsProp.arraySize)
                return;

            var auth   = (TableAndPocketAuthoring)target;
            var pocket = auth.Pockets[pocketIdx];
            Vector2 center = pocket.Center;
            float   radius = Mathf.Max(0.01f, pocket.Radius);
            int     N      = _rimGenPointCount[pocketIdx];
            if (N < 3) return;

            // Generate N evenly distributed points on the circle.
            // Points are in counter-clockwise order (increasing angle).
            Vector2[] pts = new Vector2[N];
            for (int k = 0; k < N; k++)
            {
                float angle = k * 2f * Mathf.PI / N;
                pts[k] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            // Randomly select a single index; Start and End are the same point.
            // Invariant: Start == End, and ConnectionPoints holds all N-1 other points.
            // To use a deterministic seed, call UnityEngine.Random.InitState(seed) before this.
            int idx = UnityEngine.Random.Range(0, N);

            // Build ConnectionPoints in clockwise order (decreasing index with wrap):
            // idx-1, idx-2, …, idx+1 — all N-1 remaining points, no duplicates.
            // Points were generated in CCW order (increasing angle/index), so clockwise
            // traversal means decreasing index.
            var cpList = new System.Collections.Generic.List<Vector2>(N - 1);
            for (int i = 1; i < N; i++)
                cpList.Add(pts[(idx - i + N) % N]);

            Undo.RecordObject(target, "Generate Pocket Rim From Circle");
            pocket.RimSegments = new SegmentData
            {
                Start            = pts[idx],
                End              = pts[idx],
                ConnectionPoints = cpList,
            };
            serializedObject.Update();
            EditorUtility.SetDirty(target);

            _selKind    = SelectionKind.PocketRimSegment;
            _selPrimary = pocketIdx;
            _selRim     = 0;
            SceneView.RepaintAll();
        }

        private void ClearSelection()
        {
            _selKind      = SelectionKind.None;
            _selPrimary   = -1;
            _selRim       = -1;
            _selPointKind = SelectionPoint.None;
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
                var auth    = (TableAndPocketAuthoring)target;
                var segData = auth.Pockets[_selPrimary].RimSegments;

                // Point-level deletion: promote from ConnectionPoints instead of
                // resetting the whole segment.
                if (_selPointKind == SelectionPoint.SegEnd)
                {
                    Undo.RecordObject(target, "Remove Rim Segment End");
                    bool ok = RimSegmentHelper.TryPromoteLastCPToEnd(segData);
                    if (ok)
                    {
                        serializedObject.Update();
                        _selPointKind = SelectionPoint.None;
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[BilliardPhysics] Cannot remove End: ConnectionPoints is empty. " +
                            "Add intermediate points before removing End.");
                    }
                    return ok;
                }

                if (_selPointKind == SelectionPoint.SegStart)
                {
                    Undo.RecordObject(target, "Remove Rim Segment Start");
                    bool ok = RimSegmentHelper.TryPromoteFirstCPToStart(segData);
                    if (ok)
                    {
                        serializedObject.Update();
                        _selPointKind = SelectionPoint.None;
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[BilliardPhysics] Cannot remove Start: ConnectionPoints is empty. " +
                            "Add intermediate points before removing Start.");
                    }
                    return ok;
                }

                // No sub-element selected: clear selection (single rim segment cannot be deleted).
                ClearSelection();
                return true;
            }

            return false;
        }

        // Draw a polyline Start → CP[0] → … → CP[n-1] → End using the current Handles.color.
        private static void DrawPolylineHandles(Vector3 start, Vector3 end,
                                                SerializedProperty cpsProp)
        {
            Vector3 prev = start;
            if (cpsProp != null)
            {
                for (int k = 0; k < cpsProp.arraySize; k++)
                {
                    Vector2 cpv  = cpsProp.GetArrayElementAtIndex(k).vector2Value;
                    Vector3 next = new Vector3(cpv.x, cpv.y, 0f);
                    Handles.DrawLine(prev, next);
                    prev = next;
                }
            }
            Handles.DrawLine(prev, end);
        }

        // Build the ordered point list [Start, CP[0], …, CP[n-1], End] for export/building.
        private static System.Collections.Generic.List<Vector2> BuildPolylinePoints(
            Vector2 start, Vector2 end,
            System.Collections.Generic.List<Vector2> connectionPoints)
        {
            var pts = new System.Collections.Generic.List<Vector2> { start };
            if (connectionPoints != null) pts.AddRange(connectionPoints);
            pts.Add(end);
            return pts;
        }
    }
}
#endif
