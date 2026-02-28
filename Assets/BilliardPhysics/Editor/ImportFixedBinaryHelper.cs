#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics.Editor
{
    /// <summary>
    /// Pure-logic helpers for the Fixed Binary import flow used by
    /// <see cref="TableAndPocketAuthoringEditor"/>.  Kept in a separate file so
    /// that the reconstruction logic can be exercised by unit tests without
    /// needing a running Unity Editor session.
    /// </summary>
    public static class ImportFixedBinaryHelper
    {
        /// <summary>
        /// Converts a Fix64 raw value back to float (32.32 fixed-point).
        /// </summary>
        public static float Fix64ToFloat(Fix64 value)
        {
            return (float)((double)value.RawValue / (1L << 32));
        }

        /// <summary>
        /// Converts a flat list of runtime <see cref="Segment"/>s (as produced by
        /// <see cref="TableAndPocketBinaryLoader.Load"/>) back to a <see cref="TableConfig"/>,
        /// collapsing consecutive connected sub-segments into polyline
        /// <see cref="SegmentData"/> objects with <c>ConnectionPoints</c>.
        ///
        /// <para>Two sub-segments are considered connected when the <c>End</c> of the
        /// first equals the <c>Start</c> of the second (exact fixed-point equality).
        /// All consecutive connected sub-segments are merged into a single
        /// <see cref="SegmentData"/>:
        /// <list type="bullet">
        ///   <item><see cref="SegmentData.Start"/> = first sub-segment's Start.</item>
        ///   <item><see cref="SegmentData.ConnectionPoints"/> = the shared junction
        ///     points between consecutive sub-segments.</item>
        ///   <item><see cref="SegmentData.End"/> = last sub-segment's End.</item>
        /// </list>
        /// </para>
        ///
        /// <para>Sub-segments that do not connect to their neighbours are each
        /// represented as a standalone <see cref="SegmentData"/> with an empty
        /// <c>ConnectionPoints</c> list.</para>
        ///
        /// <para>If <paramref name="segments"/> is empty an empty
        /// <see cref="TableConfig"/> is returned.</para>
        /// </summary>
        /// <param name="segments">
        /// Flat list of runtime segments loaded from the fixed binary asset.
        /// </param>
        /// <returns>
        /// A <see cref="TableConfig"/> whose <c>Segments</c> list contains one
        /// entry per connected polyline chain found in <paramref name="segments"/>.
        /// </returns>
        public static TableConfig BuildTableConfig(List<Segment> segments)
        {
            var segsData = new List<SegmentData>();
            if (segments == null || segments.Count == 0)
                return new TableConfig { Segments = segsData };

            int i = 0;
            while (i < segments.Count)
            {
                // If the runtime Segment already carries ConnectionPoints (v2 format),
                // map it directly to a SegmentData – no collapsing needed.
                if (segments[i].ConnectionPoints != null && segments[i].ConnectionPoints.Count > 0)
                {
                    var sd = new SegmentData();
                    sd.Start = new Vector2(
                        Fix64ToFloat(segments[i].Start.X),
                        Fix64ToFloat(segments[i].Start.Y));
                    sd.End = new Vector2(
                        Fix64ToFloat(segments[i].End.X),
                        Fix64ToFloat(segments[i].End.Y));
                    foreach (FixVec2 cp in segments[i].ConnectionPoints)
                        sd.ConnectionPoints.Add(new Vector2(Fix64ToFloat(cp.X), Fix64ToFloat(cp.Y)));
                    segsData.Add(sd);
                    i++;
                }
                else
                {
                    // v1 flat sub-segment: greedily collapse consecutive connected ones.
                    var sd = new SegmentData();
                    sd.Start = new Vector2(
                        Fix64ToFloat(segments[i].Start.X),
                        Fix64ToFloat(segments[i].Start.Y));

                    while (i + 1 < segments.Count &&
                           segments[i].End == segments[i + 1].Start &&
                           (segments[i + 1].ConnectionPoints == null ||
                            segments[i + 1].ConnectionPoints.Count == 0))
                    {
                        sd.ConnectionPoints.Add(new Vector2(
                            Fix64ToFloat(segments[i].End.X),
                            Fix64ToFloat(segments[i].End.Y)));
                        i++;
                    }

                    sd.End = new Vector2(
                        Fix64ToFloat(segments[i].End.X),
                        Fix64ToFloat(segments[i].End.Y));

                    segsData.Add(sd);
                    i++;
                }
            }

            return new TableConfig { Segments = segsData };
        }

        /// <summary>
        /// Converts a list of runtime <see cref="Pocket"/>s back to
        /// <see cref="PocketConfig"/> objects.
        ///
        /// <para>The single runtime <see cref="Segment"/> stored in each pocket's
        /// <c>RimSegment</c> is mapped directly to a <see cref="SegmentData"/>,
        /// preserving any <c>ConnectionPoints</c>.</para>
        /// </summary>
        /// <param name="pockets">
        /// Runtime pockets loaded from the fixed binary asset.
        /// </param>
        /// <returns>
        /// A list of <see cref="PocketConfig"/> objects, one per input pocket.
        /// </returns>
        public static List<PocketConfig> BuildPocketConfigs(List<Pocket> pockets)
        {
            var result = new List<PocketConfig>(pockets.Count);
            foreach (var pocket in pockets)
            {
                var rimSeg = new SegmentData();
                var rim    = pocket.RimSegment;
                if (rim != null)
                {
                    rimSeg.Start = new Vector2(Fix64ToFloat(rim.Start.X), Fix64ToFloat(rim.Start.Y));
                    rimSeg.End   = new Vector2(Fix64ToFloat(rim.End.X),   Fix64ToFloat(rim.End.Y));
                    if (rim.ConnectionPoints != null)
                        foreach (FixVec2 cp in rim.ConnectionPoints)
                            rimSeg.ConnectionPoints.Add(new Vector2(Fix64ToFloat(cp.X), Fix64ToFloat(cp.Y)));
                }

                result.Add(new PocketConfig
                {
                    Center                   = new Vector2(
                        Fix64ToFloat(pocket.Center.X),
                        Fix64ToFloat(pocket.Center.Y)),
                    Radius                   = Fix64ToFloat(pocket.Radius),
                    ReboundVelocityThreshold = Fix64ToFloat(pocket.ReboundVelocityThreshold),
                    RimSegments              = rimSeg,
                });
            }
            return result;
        }
    }
}
#endif
