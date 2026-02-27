using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BilliardPhysics;
using BilliardPhysics.Editor;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="ImportFixedBinaryHelper"/>.
    /// Covers <see cref="ImportFixedBinaryHelper.BuildTableConfig"/> (collapse logic)
    /// and <see cref="ImportFixedBinaryHelper.BuildPocketConfigs"/> (rim reconstruction).
    /// </summary>
    public class ImportFixedBinaryHelperTests
    {
        // ── Fix64ToFloat ──────────────────────────────────────────────────

        [Test]
        public void Fix64ToFloat_Zero_ReturnsZero()
        {
            Assert.AreEqual(0f, ImportFixedBinaryHelper.Fix64ToFloat(Fix64.Zero));
        }

        [Test]
        public void Fix64ToFloat_One_ReturnsOne()
        {
            Assert.AreApproximatelyEqual(1f,
                ImportFixedBinaryHelper.Fix64ToFloat(Fix64.One), 1e-5f);
        }

        [Test]
        public void Fix64ToFloat_RoundTrip_FloatToFix64AndBack()
        {
            float original = 1.5f;
            Fix64 fixed64  = Fix64.FromFloat(original);
            float result   = ImportFixedBinaryHelper.Fix64ToFloat(fixed64);
            Assert.AreApproximatelyEqual(original, result, 1e-4f,
                "Round-trip float→Fix64→float should preserve value within fixed-point precision.");
        }

        // ── BuildTableConfig: empty input ─────────────────────────────────

        [Test]
        public void BuildTableConfig_EmptySegments_ReturnsEmptyConfig()
        {
            var config = ImportFixedBinaryHelper.BuildTableConfig(new List<Segment>());
            Assert.AreEqual(0, config.Segments.Count,
                "Empty segment list should produce an empty TableConfig.");
        }

        // ── BuildTableConfig: single sub-segment (no ConnectionPoints) ────

        [Test]
        public void BuildTableConfig_SingleSegment_OneSegmentDataNoConnectionPoints()
        {
            Fix64 ax = Fix64.FromFloat(0f), ay = Fix64.FromFloat(0f);
            Fix64 bx = Fix64.FromFloat(1f), by = Fix64.FromFloat(0f);
            var segs = new List<Segment>
            {
                new Segment(new FixVec2(ax, ay), new FixVec2(bx, by))
            };

            TableConfig config = ImportFixedBinaryHelper.BuildTableConfig(segs);

            Assert.AreEqual(1, config.Segments.Count,
                "One flat sub-segment should map to one SegmentData.");
            Assert.AreEqual(0, config.Segments[0].ConnectionPoints.Count,
                "No ConnectionPoints expected for a single flat sub-segment.");

            Assert.AreApproximatelyEqual(0f, config.Segments[0].Start.x, 1e-4f);
            Assert.AreApproximatelyEqual(0f, config.Segments[0].Start.y, 1e-4f);
            Assert.AreApproximatelyEqual(1f, config.Segments[0].End.x, 1e-4f);
            Assert.AreApproximatelyEqual(0f, config.Segments[0].End.y, 1e-4f);
        }

        // ── BuildTableConfig: two DISCONNECTED sub-segments ───────────────

        [Test]
        public void BuildTableConfig_TwoDisconnectedSegments_TwoSeparateSegmentData()
        {
            // Wall 1: (0,0)→(1,0)  — no shared endpoint with Wall 2
            // Wall 2: (2,0)→(3,0)  — gap at x=1..2
            var segs = new List<Segment>
            {
                new Segment(new FixVec2(Fix64.FromFloat(0f), Fix64.FromFloat(0f)),
                            new FixVec2(Fix64.FromFloat(1f), Fix64.FromFloat(0f))),
                new Segment(new FixVec2(Fix64.FromFloat(2f), Fix64.FromFloat(0f)),
                            new FixVec2(Fix64.FromFloat(3f), Fix64.FromFloat(0f))),
            };

            TableConfig config = ImportFixedBinaryHelper.BuildTableConfig(segs);

            Assert.AreEqual(2, config.Segments.Count,
                "Two disconnected sub-segments should each become a separate SegmentData.");
            Assert.AreEqual(0, config.Segments[0].ConnectionPoints.Count);
            Assert.AreEqual(0, config.Segments[1].ConnectionPoints.Count);
        }

        // ── BuildTableConfig: two CONNECTED sub-segments (1 CP) ──────────

        [Test]
        public void BuildTableConfig_TwoConnectedSubSegments_OneSegmentDataWithOneCP()
        {
            // Simulates SegmentData(Start=A, CP=[B], End=C) expanded to [A→B, B→C].
            Fix64 ax = Fix64.FromFloat(0f), ay = Fix64.FromFloat(0f);
            Fix64 bx = Fix64.FromFloat(1f), by = Fix64.FromFloat(0f);
            Fix64 cx = Fix64.FromFloat(2f), cy = Fix64.FromFloat(0f);

            var segs = new List<Segment>
            {
                new Segment(new FixVec2(ax, ay), new FixVec2(bx, by)), // A→B
                new Segment(new FixVec2(bx, by), new FixVec2(cx, cy)), // B→C
            };

            TableConfig config = ImportFixedBinaryHelper.BuildTableConfig(segs);

            Assert.AreEqual(1, config.Segments.Count,
                "Two connected sub-segments should collapse to one SegmentData.");

            SegmentData sd = config.Segments[0];
            Assert.AreEqual(1, sd.ConnectionPoints.Count,
                "Exactly one ConnectionPoint (the shared junction B) expected.");

            Assert.AreApproximatelyEqual(0f, sd.Start.x, 1e-4f, "Start.x should be A.x");
            Assert.AreApproximatelyEqual(0f, sd.Start.y, 1e-4f, "Start.y should be A.y");
            Assert.AreApproximatelyEqual(1f, sd.ConnectionPoints[0].x, 1e-4f, "CP[0].x should be B.x");
            Assert.AreApproximatelyEqual(0f, sd.ConnectionPoints[0].y, 1e-4f, "CP[0].y should be B.y");
            Assert.AreApproximatelyEqual(2f, sd.End.x, 1e-4f, "End.x should be C.x");
            Assert.AreApproximatelyEqual(0f, sd.End.y, 1e-4f, "End.y should be C.y");
        }

        // ── BuildTableConfig: three CONNECTED sub-segments (2 CPs) ───────

        /// <summary>
        /// Regression test: 3 connected sub-segments A→B, B→C, C→D must collapse
        /// to a single SegmentData with Start=A, CP=[B,C], End=D.
        /// Before the fix, BuildTableConfig would produce 3 separate SegmentData
        /// (one per sub-segment), losing the polyline structure.
        /// </summary>
        [Test]
        public void BuildTableConfig_ThreeConnectedSubSegments_OneSegmentDataWithTwoCPs()
        {
            Fix64 ax = Fix64.FromFloat(0f), ay = Fix64.FromFloat(0f);
            Fix64 bx = Fix64.FromFloat(1f), by = Fix64.FromFloat(0f);
            Fix64 cx = Fix64.FromFloat(2f), cy = Fix64.FromFloat(0f);
            Fix64 dx = Fix64.FromFloat(3f), dy = Fix64.FromFloat(0f);

            var segs = new List<Segment>
            {
                new Segment(new FixVec2(ax, ay), new FixVec2(bx, by)), // A→B
                new Segment(new FixVec2(bx, by), new FixVec2(cx, cy)), // B→C
                new Segment(new FixVec2(cx, cy), new FixVec2(dx, dy)), // C→D
            };

            TableConfig config = ImportFixedBinaryHelper.BuildTableConfig(segs);

            Assert.AreEqual(1, config.Segments.Count,
                "Three connected sub-segments must collapse to exactly one SegmentData.");

            SegmentData sd = config.Segments[0];
            Assert.AreEqual(2, sd.ConnectionPoints.Count,
                "Exactly two ConnectionPoints (junctions B and C) expected.");

            Assert.AreApproximatelyEqual(0f, sd.Start.x,               1e-4f, "Start.x should be A.x");
            Assert.AreApproximatelyEqual(1f, sd.ConnectionPoints[0].x, 1e-4f, "CP[0].x should be B.x");
            Assert.AreApproximatelyEqual(2f, sd.ConnectionPoints[1].x, 1e-4f, "CP[1].x should be C.x");
            Assert.AreApproximatelyEqual(3f, sd.End.x,                 1e-4f, "End.x should be D.x");
        }

        // ── BuildTableConfig: mixed connected and disconnected ────────────

        [Test]
        public void BuildTableConfig_MixedConnectedAndDisconnected_CorrectGrouping()
        {
            // Two connected walls: A→B, B→C (one polyline wall)
            // One standalone wall: D→E (disconnected)
            Fix64 ax = Fix64.FromFloat(0f), ay = Fix64.FromFloat(0f);
            Fix64 bx = Fix64.FromFloat(1f), by = Fix64.FromFloat(0f);
            Fix64 cx = Fix64.FromFloat(2f), cy = Fix64.FromFloat(0f);
            Fix64 dx = Fix64.FromFloat(5f), dy = Fix64.FromFloat(1f); // gap
            Fix64 ex = Fix64.FromFloat(6f), ey = Fix64.FromFloat(1f);

            var segs = new List<Segment>
            {
                new Segment(new FixVec2(ax, ay), new FixVec2(bx, by)), // A→B (connected with next)
                new Segment(new FixVec2(bx, by), new FixVec2(cx, cy)), // B→C
                new Segment(new FixVec2(dx, dy), new FixVec2(ex, ey)), // D→E (disconnected)
            };

            TableConfig config = ImportFixedBinaryHelper.BuildTableConfig(segs);

            Assert.AreEqual(2, config.Segments.Count,
                "Connected pair + standalone = 2 SegmentData.");
            Assert.AreEqual(1, config.Segments[0].ConnectionPoints.Count,
                "First SegmentData should have 1 CP (junction B).");
            Assert.AreEqual(0, config.Segments[1].ConnectionPoints.Count,
                "Standalone sub-segment should have 0 CPs.");
        }

        // ── BuildTableConfig: backward compat (old buggy export) ─────────

        /// <summary>
        /// Backward-compatibility test: if previously buggy code had split a
        /// polyline into individual SegmentData objects (one per sub-segment),
        /// each exported without CPs, loading back still produces the correct
        /// collapsed SegmentData because the sub-segments remain connected in
        /// the binary.
        /// </summary>
        [Test]
        public void BuildTableConfig_OldFormatConnectedPoints_MigratesCorrectly()
        {
            // Old buggy behavior would have stored the data as 3 disconnected SegmentData
            // but exported them as 3 sub-segments [A→B, B→C, C→D].
            // Since the sub-segments ARE connected, the new loader should collapse them.
            Fix64 ax = Fix64.FromFloat(0.5f), ay = Fix64.FromFloat(0.5f);
            Fix64 bx = Fix64.FromFloat(1.0f), by = Fix64.FromFloat(0.0f);
            Fix64 cx = Fix64.FromFloat(1.5f), cy = Fix64.FromFloat(-0.5f);
            Fix64 dx = Fix64.FromFloat(2.0f), dy = Fix64.FromFloat(-1.0f);

            var segs = new List<Segment>
            {
                new Segment(new FixVec2(ax, ay), new FixVec2(bx, by)),
                new Segment(new FixVec2(bx, by), new FixVec2(cx, cy)),
                new Segment(new FixVec2(cx, cy), new FixVec2(dx, dy)),
            };

            TableConfig config = ImportFixedBinaryHelper.BuildTableConfig(segs);

            // Expect 1 SegmentData with 2 CPs, NOT 3 SegmentData with 0 CPs.
            Assert.AreEqual(1, config.Segments.Count,
                "Old-format connected sub-segments must be migrated to one SegmentData.");
            Assert.AreEqual(2, config.Segments[0].ConnectionPoints.Count,
                "Migrated SegmentData must have exactly 2 ConnectionPoints.");
        }

        // ── BuildPocketConfigs: empty pocket list ─────────────────────────

        [Test]
        public void BuildPocketConfigs_EmptyPockets_ReturnsEmptyList()
        {
            var result = ImportFixedBinaryHelper.BuildPocketConfigs(new List<Pocket>());
            Assert.AreEqual(0, result.Count,
                "Empty pocket list should produce an empty PocketConfig list.");
        }

        // ── BuildPocketConfigs: pocket with no rim segments ───────────────

        [Test]
        public void BuildPocketConfigs_PocketWithNoRimSegments_EmptyRimSegmentData()
        {
            var pocket = new Pocket(0,
                new FixVec2(Fix64.FromFloat(0.5f), Fix64.FromFloat(-0.5f)),
                Fix64.FromFloat(0.1f));
            // No rim segments added.

            var result = ImportFixedBinaryHelper.BuildPocketConfigs(
                new List<Pocket> { pocket });

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0, result[0].RimSegments.ConnectionPoints.Count,
                "Pocket with no rim segments should have empty RimSegments.ConnectionPoints.");
        }

        // ── BuildPocketConfigs: pocket with multiple rim sub-segments ─────

        /// <summary>
        /// Regression test for pocket rim reconstruction:
        /// 3 flat rim sub-segments A→B, B→C, C→D must collapse to a single
        /// SegmentData with Start=A, CP=[B,C], End=D.
        /// </summary>
        [Test]
        public void BuildPocketConfigs_PocketWithThreeRimSubSegments_OneRimSegmentDataWithTwoCPs()
        {
            Fix64 ax = Fix64.FromFloat(0f), ay = Fix64.FromFloat(0f);
            Fix64 bx = Fix64.FromFloat(1f), by = Fix64.FromFloat(0f);
            Fix64 cx = Fix64.FromFloat(2f), cy = Fix64.FromFloat(0f);
            Fix64 dx = Fix64.FromFloat(3f), dy = Fix64.FromFloat(0f);

            var pocket = new Pocket(0,
                new FixVec2(Fix64.FromFloat(1.5f), Fix64.FromFloat(0f)),
                Fix64.FromFloat(0.5f));
            pocket.RimSegments.Add(new Segment(new FixVec2(ax, ay), new FixVec2(bx, by)));
            pocket.RimSegments.Add(new Segment(new FixVec2(bx, by), new FixVec2(cx, cy)));
            pocket.RimSegments.Add(new Segment(new FixVec2(cx, cy), new FixVec2(dx, dy)));

            var result = ImportFixedBinaryHelper.BuildPocketConfigs(
                new List<Pocket> { pocket });

            Assert.AreEqual(1, result.Count, "One pocket expected.");
            SegmentData rim = result[0].RimSegments;

            Assert.AreEqual(2, rim.ConnectionPoints.Count,
                "Exactly two ConnectionPoints (B and C) expected.");

            Assert.AreApproximatelyEqual(0f, rim.Start.x,               1e-4f, "Start.x should be A.x");
            Assert.AreApproximatelyEqual(1f, rim.ConnectionPoints[0].x, 1e-4f, "CP[0].x should be B.x");
            Assert.AreApproximatelyEqual(2f, rim.ConnectionPoints[1].x, 1e-4f, "CP[1].x should be C.x");
            Assert.AreApproximatelyEqual(3f, rim.End.x,                 1e-4f, "End.x should be D.x");
        }

        // ── BuildPocketConfigs: metadata round-trip ───────────────────────

        [Test]
        public void BuildPocketConfigs_MetadataPreserved()
        {
            float cx  = 0.5f, cy = -0.5f, r = 0.1f, rvt = 2f;
            var pocket = new Pocket(0,
                new FixVec2(Fix64.FromFloat(cx), Fix64.FromFloat(cy)),
                Fix64.FromFloat(r))
            {
                ReboundVelocityThreshold = Fix64.FromFloat(rvt),
            };

            var result = ImportFixedBinaryHelper.BuildPocketConfigs(
                new List<Pocket> { pocket });

            Assert.AreEqual(1, result.Count);
            PocketConfig pc = result[0];
            Assert.AreApproximatelyEqual(cx,  pc.Center.x,                   1e-4f, "Center.x");
            Assert.AreApproximatelyEqual(cy,  pc.Center.y,                   1e-4f, "Center.y");
            Assert.AreApproximatelyEqual(r,   pc.Radius,                     1e-4f, "Radius");
            Assert.AreApproximatelyEqual(rvt, pc.ReboundVelocityThreshold,   1e-4f, "ReboundVelocityThreshold");
        }
    }
}
