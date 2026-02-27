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
        /// <para>All flat rim sub-segments stored in each pocket are collapsed back
        /// into a single <see cref="SegmentData"/> with
        /// <c>ConnectionPoints</c>, reversing the polyline expansion performed
        /// during export:
        /// <list type="bullet">
        ///   <item><c>rimSeg.Start</c>  = flat[0].Start</item>
        ///   <item><c>rimSeg.CP[k-1]</c> = flat[k].Start  (for k = 1..n-1)</item>
        ///   <item><c>rimSeg.End</c>    = flat[n-1].End</item>
        /// </list>
        /// </para>
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
                var rims   = pocket.RimSegments;
                if (rims.Count == 1 && rims[0].ConnectionPoints != null &&
                    rims[0].ConnectionPoints.Count > 0)
                {
                    // v2 format: single runtime Segment with embedded ConnectionPoints.
                    rimSeg.Start = new Vector2(
                        Fix64ToFloat(rims[0].Start.X),
                        Fix64ToFloat(rims[0].Start.Y));
                    rimSeg.End = new Vector2(
                        Fix64ToFloat(rims[0].End.X),
                        Fix64ToFloat(rims[0].End.Y));
                    foreach (FixVec2 cp in rims[0].ConnectionPoints)
                        rimSeg.ConnectionPoints.Add(new Vector2(Fix64ToFloat(cp.X), Fix64ToFloat(cp.Y)));
                }
                else if (rims.Count > 0)
                {
                    // v1 format (or single flat sub-segment): collapse all rim Segments.
                    rimSeg.Start = new Vector2(
                        Fix64ToFloat(rims[0].Start.X),
                        Fix64ToFloat(rims[0].Start.Y));
                    rimSeg.End = new Vector2(
                        Fix64ToFloat(rims[rims.Count - 1].End.X),
                        Fix64ToFloat(rims[rims.Count - 1].End.Y));
                    for (int k = 1; k < rims.Count; k++)
                        rimSeg.ConnectionPoints.Add(new Vector2(
                            Fix64ToFloat(rims[k].Start.X),
                            Fix64ToFloat(rims[k].Start.Y)));
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
