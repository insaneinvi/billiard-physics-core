using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode tests covering the polyline Segment (Scheme 1):
    ///   1) No ConnectionPoints – degenerate single sub-segment
    ///   2) One ConnectionPoint – two sub-segments (bent / corner segment)
    ///   3) Multiple ConnectionPoints – many sub-segments
    ///   4) Binary v1 backward-compatibility (flat sub-segments, no CPs)
    ///   5) Binary v2 round-trip  (Start/End + CPCount + CPs)
    ///   6) CCDSystem.SweptCircleSegment against a polyline segment
    /// </summary>
    public class SegmentPolylineTests
    {
        // ── 1. Single sub-segment (no ConnectionPoints) ───────────────────

        [Test]
        public void Segment_NoCPs_SingleSubSegment_PropertiesCorrect()
        {
            // Horizontal segment (0,0) → (3,0)
            FixVec2 start = new FixVec2(Fix64.Zero, Fix64.Zero);
            FixVec2 end   = new FixVec2(Fix64.From(3), Fix64.Zero);

            var seg = new Segment(start, end);

            Assert.AreEqual(0, seg.ConnectionPoints.Count, "No CPs expected.");
            Assert.AreEqual(1, seg.Normal.Length,          "One normal for one sub-segment.");
            Assert.AreEqual(1, seg.Direction.Length,       "One direction for one sub-segment.");

            // Direction of (3,0) is (1,0).
            Assert.AreEqual(Fix64.One,  seg.Direction[0].X, "Direction[0].X should be 1.");
            Assert.AreEqual(Fix64.Zero, seg.Direction[0].Y, "Direction[0].Y should be 0.");

            // Perp of (1,0) = (-0, 1) = (0, 1) using left-hand convention.
            Assert.AreEqual(Fix64.Zero, seg.Normal[0].X, "Normal[0].X should be 0.");
            Assert.AreEqual(Fix64.One,  seg.Normal[0].Y, "Normal[0].Y should be 1.");

            // Total length = 3.
            Assert.AreEqual(Fix64.From(3), seg.Length, "Length should be 3.");

            // Points = [Start, End].
            IReadOnlyList<FixVec2> pts = seg.Points;
            Assert.AreEqual(2, pts.Count, "Two points for a degenerate (single-sub-segment) polyline.");
            Assert.AreEqual(start, pts[0], "pts[0] must be Start.");
            Assert.AreEqual(end,   pts[1], "pts[1] must be End.");
        }

        [Test]
        public void Segment_NullConnectionPoints_TreatedAsEmpty()
        {
            var seg = new Segment(
                new FixVec2(Fix64.Zero, Fix64.Zero),
                new FixVec2(Fix64.One,  Fix64.Zero),
                null);

            Assert.IsNotNull(seg.ConnectionPoints, "ConnectionPoints must never be null.");
            Assert.AreEqual(0, seg.ConnectionPoints.Count, "Null CPs treated as empty.");
            Assert.AreEqual(1, seg.Normal.Length,    "One sub-segment.");
            Assert.AreEqual(1, seg.Direction.Length, "One sub-segment.");
        }

        // ── 2. One ConnectionPoint – two sub-segments ─────────────────────

        [Test]
        public void Segment_OneCP_TwoSubSegments_PropertiesCorrect()
        {
            // L-shape: A(0,0) → B(1,0) → C(1,1)
            FixVec2 a = new FixVec2(Fix64.Zero, Fix64.Zero);
            FixVec2 b = new FixVec2(Fix64.One,  Fix64.Zero);
            FixVec2 c = new FixVec2(Fix64.One,  Fix64.One);

            var cps = new List<FixVec2> { b };
            var seg = new Segment(a, c, cps);

            Assert.AreEqual(1, seg.ConnectionPoints.Count, "One CP expected.");
            Assert.AreEqual(2, seg.Normal.Length,          "Two normals for two sub-segments.");
            Assert.AreEqual(2, seg.Direction.Length,       "Two directions for two sub-segments.");

            // Sub-segment 0: A→B direction = (1,0); normal = (0,1).
            Assert.AreEqual(Fix64.One,  seg.Direction[0].X, "Dir[0].X = 1 (rightward).");
            Assert.AreEqual(Fix64.Zero, seg.Direction[0].Y, "Dir[0].Y = 0.");
            Assert.AreEqual(Fix64.Zero, seg.Normal[0].X,    "Normal[0].X = 0.");
            Assert.AreEqual(Fix64.One,  seg.Normal[0].Y,    "Normal[0].Y = 1.");

            // Sub-segment 1: B→C direction = (0,1); normal = perp(0,1) = (-1, 0).
            Assert.AreEqual(Fix64.Zero,   seg.Direction[1].X, "Dir[1].X = 0 (upward).");
            Assert.AreEqual(Fix64.One,    seg.Direction[1].Y, "Dir[1].Y = 1.");
            Assert.AreEqual(Fix64.NegOne, seg.Normal[1].X,    "Normal[1].X = -1.");
            Assert.AreEqual(Fix64.Zero,   seg.Normal[1].Y,    "Normal[1].Y = 0.");

            // Total length = |AB| + |BC| = 1 + 1 = 2.
            Assert.AreEqual(Fix64.From(2), seg.Length, "Total polyline length = 2.");

            // Points = [A, B, C].
            IReadOnlyList<FixVec2> pts = seg.Points;
            Assert.AreEqual(3, pts.Count, "Three vertices.");
            Assert.AreEqual(a, pts[0], "pts[0] = A.");
            Assert.AreEqual(b, pts[1], "pts[1] = B (CP).");
            Assert.AreEqual(c, pts[2], "pts[2] = C (End).");
        }

        // ── 3. Multiple ConnectionPoints ─────────────────────────────────

        [Test]
        public void Segment_MultipleCPs_PointsOrderPreserved()
        {
            // P0(0,0) → P1(1,0) → P2(2,0) → P3(3,0)  (collinear, three sub-segments)
            FixVec2 p0 = new FixVec2(Fix64.Zero,    Fix64.Zero);
            FixVec2 p1 = new FixVec2(Fix64.One,     Fix64.Zero);
            FixVec2 p2 = new FixVec2(Fix64.From(2), Fix64.Zero);
            FixVec2 p3 = new FixVec2(Fix64.From(3), Fix64.Zero);

            var cps = new List<FixVec2> { p1, p2 };
            var seg = new Segment(p0, p3, cps);

            Assert.AreEqual(2, seg.ConnectionPoints.Count, "Two CPs.");
            Assert.AreEqual(3, seg.Normal.Length,    "Three normals for three sub-segments.");
            Assert.AreEqual(3, seg.Direction.Length, "Three directions.");

            // All sub-segments horizontal → direction=(1,0), normal=(0,1).
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(Fix64.One,  seg.Direction[i].X, $"Direction[{i}].X = 1.");
                Assert.AreEqual(Fix64.Zero, seg.Direction[i].Y, $"Direction[{i}].Y = 0.");
                Assert.AreEqual(Fix64.Zero, seg.Normal[i].X,    $"Normal[{i}].X = 0.");
                Assert.AreEqual(Fix64.One,  seg.Normal[i].Y,    $"Normal[{i}].Y = 1.");
            }

            // Total length = 3.
            Assert.AreEqual(Fix64.From(3), seg.Length, "Total length = 3.");

            // Points = [P0, P1, P2, P3].
            IReadOnlyList<FixVec2> pts = seg.Points;
            Assert.AreEqual(4, pts.Count, "Four vertices.");
            Assert.AreEqual(p0, pts[0]);
            Assert.AreEqual(p1, pts[1]);
            Assert.AreEqual(p2, pts[2]);
            Assert.AreEqual(p3, pts[3]);
        }

        // ── 4. Binary v1 backward-compatibility ──────────────────────────

        private const uint   k_magic    = 0x59485042u;
        private const ushort k_version1 = 1;
        private const ushort k_version2 = 2;

        /// <summary>Builds a version-1 (flat, no CPs) binary payload.</summary>
        private static byte[] BuildV1Binary(
            List<(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by)> segs,
            List<(Fix64 cx, Fix64 cy, Fix64 r, Fix64 rvt,
                  List<(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by)> rims)> pockets)
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);

            w.Write(k_magic);
            w.Write(k_version1);

            w.Write(segs.Count);
            foreach (var s in segs)
            {
                w.Write(s.ax.RawValue); w.Write(s.ay.RawValue);
                w.Write(s.bx.RawValue); w.Write(s.by.RawValue);
            }

            w.Write(pockets.Count);
            foreach (var p in pockets)
            {
                w.Write(p.cx.RawValue); w.Write(p.cy.RawValue);
                w.Write(p.r.RawValue);
                w.Write(p.rvt.RawValue);
                w.Write(p.rims.Count);
                foreach (var rim in p.rims)
                {
                    w.Write(rim.ax.RawValue); w.Write(rim.ay.RawValue);
                    w.Write(rim.bx.RawValue); w.Write(rim.by.RawValue);
                }
            }

            return ms.ToArray();
        }

        [Test]
        public void BinaryV1_SingleFlatSegment_LoadedWithoutCPs()
        {
            Fix64 ax = Fix64.Zero, ay = Fix64.Zero;
            Fix64 bx = Fix64.One,  by = Fix64.Zero;

            byte[] bytes = BuildV1Binary(
                new List<(Fix64, Fix64, Fix64, Fix64)> { (ax, ay, bx, by) },
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>());

            TableAndPocketBinaryLoader.Load(bytes, out var segs, out _);

            Assert.AreEqual(1, segs.Count, "One segment from v1 binary.");
            Assert.AreEqual(ax, segs[0].Start.X, "Start.X");
            Assert.AreEqual(ay, segs[0].Start.Y, "Start.Y");
            Assert.AreEqual(bx, segs[0].End.X,   "End.X");
            Assert.AreEqual(by, segs[0].End.Y,   "End.Y");
            Assert.AreEqual(0, segs[0].ConnectionPoints.Count,
                "v1 format: ConnectionPoints must be empty (no CPs stored).");
        }

        [Test]
        public void BinaryV1_ThreeFlatRimSubSegments_AllLoadedNoCPs()
        {
            // Three connected flat rim sub-segments: A→B, B→C, C→D.
            Fix64 ax = Fix64.Zero,    ay = Fix64.Zero;
            Fix64 bx = Fix64.One,     by = Fix64.Zero;
            Fix64 cx = Fix64.From(2), cy = Fix64.Zero;
            Fix64 dx = Fix64.From(3), dy = Fix64.Zero;

            var rims = new List<(Fix64, Fix64, Fix64, Fix64)>
            {
                (ax, ay, bx, by),
                (bx, by, cx, cy),
                (cx, cy, dx, dy),
            };
            var pocketList = new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>
            {
                (Fix64.Zero, Fix64.Zero, Fix64.Half, Fix64.One, rims)
            };
            byte[] bytes = BuildV1Binary(
                new List<(Fix64, Fix64, Fix64, Fix64)>(), pocketList);

            TableAndPocketBinaryLoader.Load(bytes, out _, out var pockets);

            Assert.AreEqual(1, pockets.Count);
            Assert.AreEqual(3, pockets[0].RimSegments.Count,
                "v1: three flat rim sub-segments must all be present as separate Segments.");
            foreach (var rim in pockets[0].RimSegments)
                Assert.AreEqual(0, rim.ConnectionPoints.Count,
                    "v1 rim Segments must have no ConnectionPoints.");
        }

        // ── 5. Binary v2 round-trip ───────────────────────────────────────

        /// <summary>Builds a version-2 (with CP count + CPs) binary payload.</summary>
        private static byte[] BuildV2Binary(
            List<(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by, List<(Fix64 x, Fix64 y)> cps)> segs,
            List<(Fix64 cx, Fix64 cy, Fix64 r, Fix64 rvt,
                  List<(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by, List<(Fix64 x, Fix64 y)> cps)> rims)> pockets)
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);

            w.Write(k_magic);
            w.Write(k_version2);

            w.Write(segs.Count);
            foreach (var s in segs)
            {
                w.Write(s.ax.RawValue); w.Write(s.ay.RawValue);
                w.Write(s.bx.RawValue); w.Write(s.by.RawValue);
                WriteCPs(w, s.cps);
            }

            w.Write(pockets.Count);
            foreach (var p in pockets)
            {
                w.Write(p.cx.RawValue); w.Write(p.cy.RawValue);
                w.Write(p.r.RawValue);
                w.Write(p.rvt.RawValue);
                w.Write(p.rims.Count);
                foreach (var rim in p.rims)
                {
                    w.Write(rim.ax.RawValue); w.Write(rim.ay.RawValue);
                    w.Write(rim.bx.RawValue); w.Write(rim.by.RawValue);
                    WriteCPs(w, rim.cps);
                }
            }

            return ms.ToArray();
        }

        private static void WriteCPs(BinaryWriter w, List<(Fix64 x, Fix64 y)> cps)
        {
            int count = (cps != null) ? cps.Count : 0;
            w.Write(count);
            if (cps != null)
                foreach (var cp in cps)
                {
                    w.Write(cp.x.RawValue);
                    w.Write(cp.y.RawValue);
                }
        }

        [Test]
        public void BinaryV2_SegmentNoCPs_LoadedCorrectly()
        {
            Fix64 ax = Fix64.One,     ay = Fix64.From(2);
            Fix64 bx = Fix64.From(3), by = Fix64.From(4);

            byte[] bytes = BuildV2Binary(
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>
                    { (ax, ay, bx, by, null) },
                new List<(Fix64, Fix64, Fix64, Fix64,
                          List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>)>());

            TableAndPocketBinaryLoader.Load(bytes, out var segs, out _);

            Assert.AreEqual(1, segs.Count, "One v2 segment.");
            Assert.AreEqual(ax, segs[0].Start.X, "Start.X");
            Assert.AreEqual(ay, segs[0].Start.Y, "Start.Y");
            Assert.AreEqual(bx, segs[0].End.X,   "End.X");
            Assert.AreEqual(by, segs[0].End.Y,   "End.Y");
            Assert.AreEqual(0, segs[0].ConnectionPoints.Count,
                "v2 segment with CPCount=0 must have no ConnectionPoints.");
        }

        [Test]
        public void BinaryV2_SegmentWithTwoCPs_LoadedCorrectly()
        {
            // Polyline P0(0,0) → P1(1,0) → P2(2,0) → P3(3,0).
            Fix64 ax = Fix64.Zero,    ay = Fix64.Zero;
            Fix64 bx = Fix64.From(3), by = Fix64.Zero;
            Fix64 c1x = Fix64.One,    c1y = Fix64.Zero;
            Fix64 c2x = Fix64.From(2),c2y = Fix64.Zero;

            var cps = new List<(Fix64, Fix64)> { (c1x, c1y), (c2x, c2y) };
            byte[] bytes = BuildV2Binary(
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>
                    { (ax, ay, bx, by, cps) },
                new List<(Fix64, Fix64, Fix64, Fix64,
                          List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>)>());

            TableAndPocketBinaryLoader.Load(bytes, out var segs, out _);

            Assert.AreEqual(1, segs.Count, "One v2 segment with 2 CPs.");
            Assert.AreEqual(2, segs[0].ConnectionPoints.Count, "Two CPs expected.");
            Assert.AreEqual(c1x, segs[0].ConnectionPoints[0].X, "CP[0].X");
            Assert.AreEqual(c1y, segs[0].ConnectionPoints[0].Y, "CP[0].Y");
            Assert.AreEqual(c2x, segs[0].ConnectionPoints[1].X, "CP[1].X");
            Assert.AreEqual(c2y, segs[0].ConnectionPoints[1].Y, "CP[1].Y");

            // Runtime arrays must reflect three sub-segments.
            Assert.AreEqual(3, segs[0].Normal.Length,    "Three normals for 3 sub-segments.");
            Assert.AreEqual(3, segs[0].Direction.Length, "Three directions for 3 sub-segments.");
            Assert.AreEqual(Fix64.From(3), segs[0].Length, "Total length = 3.");
        }

        [Test]
        public void BinaryV2_PocketRimWithOneCP_PreservedRoundTrip()
        {
            // Bent rim: A(0,0) → mid(1,1) → B(2,0).
            Fix64 ax = Fix64.Zero, ay = Fix64.Zero;
            Fix64 bx = Fix64.From(2), by = Fix64.Zero;
            Fix64 cpx = Fix64.One, cpy = Fix64.One;

            var rimCps = new List<(Fix64, Fix64)> { (cpx, cpy) };
            var rims = new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>
                { (ax, ay, bx, by, rimCps) };
            var pocketList = new List<(Fix64, Fix64, Fix64, Fix64,
                List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>)>
            {
                (Fix64.Zero, Fix64.Zero, Fix64.Half, Fix64.One, rims)
            };
            byte[] bytes = BuildV2Binary(
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64)>)>(),
                pocketList);

            TableAndPocketBinaryLoader.Load(bytes, out _, out var pockets);

            Assert.AreEqual(1, pockets.Count);
            Assert.AreEqual(1, pockets[0].RimSegments.Count,
                "v2: one Segment per SegmentData (not expanded).");

            Segment rimSeg = pockets[0].RimSegments[0];
            Assert.AreEqual(ax,  rimSeg.Start.X, "Rim Start.X");
            Assert.AreEqual(ay,  rimSeg.Start.Y, "Rim Start.Y");
            Assert.AreEqual(bx,  rimSeg.End.X,   "Rim End.X");
            Assert.AreEqual(by,  rimSeg.End.Y,   "Rim End.Y");
            Assert.AreEqual(1,   rimSeg.ConnectionPoints.Count, "One CP preserved.");
            Assert.AreEqual(cpx, rimSeg.ConnectionPoints[0].X, "CP.X");
            Assert.AreEqual(cpy, rimSeg.ConnectionPoints[0].Y, "CP.Y");
            // Two sub-segments → two normals/directions.
            Assert.AreEqual(2, rimSeg.Normal.Length,    "Two normals.");
            Assert.AreEqual(2, rimSeg.Direction.Length, "Two directions.");
        }

        // ── 6. CCDSystem.SweptCircleSegment on a polyline ─────────────────

        /// <summary>
        /// Helper: creates a minimal Ball for CCD tests.
        /// Mass and Inertia do not matter for CCD (only Position, Velocity, Radius).
        /// </summary>
        private static Ball MakeBall(FixVec2 pos, FixVec2 vel, Fix64 radius)
        {
            var b = new Ball(0, radius, Fix64.One);
            b.Position       = pos;
            b.LinearVelocity = vel;
            return b;
        }

        [Test]
        public void SweptCircleSegment_SingleSegment_HitsHorizontalWall()
        {
            // Wall: (0,0)→(3,0) at y=0, normal = (0,1) (upward into field).
            // Ball at (1.5, 0.5), radius 0.1, moving downward (0,−2).
            // Expected: hit with hitNormal = (0,1).
            var seg = new Segment(
                new FixVec2(Fix64.Zero,    Fix64.Zero),
                new FixVec2(Fix64.From(3), Fix64.Zero));

            var ball = MakeBall(
                pos:    new FixVec2(Fix64.From(1) + Fix64.Half, Fix64.Half),
                vel:    new FixVec2(Fix64.Zero, Fix64.From(-2)),
                radius: Fix64.FromFloat(0.1f));

            Fix64   toi;
            FixVec2 hitNormal;
            bool hit = CCDSystem.SweptCircleSegment(ball, seg, Fix64.One, out toi, out hitNormal);

            Assert.IsTrue(hit, "Ball moving toward wall should hit.");
            Assert.AreEqual(Fix64.Zero, hitNormal.X, "HitNormal.X = 0.");
            Assert.AreEqual(Fix64.One,  hitNormal.Y, "HitNormal.Y = 1.");
        }

        [Test]
        public void SweptCircleSegment_Polyline_BallHitsFirstArm()
        {
            // L-shaped wall: A(0,0)→B(2,0)→C(2,2).
            // Sub-seg 0 A→B: direction=(1,0), normal=(0,1) upward.
            // Ball above first arm at (1, 0.5), moving down (0,−2).
            FixVec2 a = new FixVec2(Fix64.Zero,    Fix64.Zero);
            FixVec2 b = new FixVec2(Fix64.From(2), Fix64.Zero);
            FixVec2 c = new FixVec2(Fix64.From(2), Fix64.From(2));

            var seg = new Segment(a, c, new List<FixVec2> { b });

            var ball = MakeBall(
                pos:    new FixVec2(Fix64.One, Fix64.Half),
                vel:    new FixVec2(Fix64.Zero, Fix64.From(-2)),
                radius: Fix64.FromFloat(0.1f));

            Fix64   toi;
            FixVec2 hitNormal;
            bool hit = CCDSystem.SweptCircleSegment(ball, seg, Fix64.One, out toi, out hitNormal);

            Assert.IsTrue(hit, "Ball should hit horizontal arm A→B.");
            Assert.AreEqual(Fix64.Zero, hitNormal.X, "HitNormal.X = 0 (horizontal arm).");
            Assert.AreEqual(Fix64.One,  hitNormal.Y, "HitNormal.Y = 1 (upward normal).");
        }

        [Test]
        public void SweptCircleSegment_Polyline_BallHitsSecondArm()
        {
            // Same L-shaped wall: A(0,0)→B(2,0)→C(2,2).
            // Sub-seg 1 B→C: direction=(0,1), normal = perp(0,1) = (−1, 0) pointing LEFT.
            // Ball to the left of second arm: at (1.5, 1), moving right (2, 0).
            // dist = dot((1.5,1)−(2,0), (−1,0)) = dot((−0.5,1),(−1,0)) = 0.5 > 0 ✓.
            FixVec2 a = new FixVec2(Fix64.Zero,    Fix64.Zero);
            FixVec2 b = new FixVec2(Fix64.From(2), Fix64.Zero);
            FixVec2 c = new FixVec2(Fix64.From(2), Fix64.From(2));

            var seg = new Segment(a, c, new List<FixVec2> { b });

            // x=1.5 = One + Half is exact in Fix64.
            Fix64 x15 = Fix64.One + Fix64.Half;
            var ball = MakeBall(
                pos:    new FixVec2(x15, Fix64.One),
                vel:    new FixVec2(Fix64.From(2), Fix64.Zero),
                radius: Fix64.FromFloat(0.1f));

            Fix64   toi;
            FixVec2 hitNormal;
            bool hit = CCDSystem.SweptCircleSegment(ball, seg, Fix64.One, out toi, out hitNormal);

            Assert.IsTrue(hit, "Ball moving right should hit vertical arm B→C.");
            Assert.AreEqual(Fix64.NegOne, hitNormal.X, "HitNormal.X = −1 (left-facing normal).");
            Assert.AreEqual(Fix64.Zero,   hitNormal.Y, "HitNormal.Y = 0.");
        }

        [Test]
        public void SweptCircleSegment_BackwardCompat_SingleSegment_SameResultAsOriginal()
        {
            // Regression: a plain (no-CP) Segment must behave identically to the
            // old single-sub-segment implementation.
            var seg = new Segment(
                new FixVec2(Fix64.Zero, Fix64.Zero),
                new FixVec2(Fix64.From(4), Fix64.Zero));

            var ball = MakeBall(
                pos:    new FixVec2(Fix64.From(2), Fix64.One),
                vel:    new FixVec2(Fix64.Zero, Fix64.NegOne),
                radius: Fix64.FromFloat(0.1f));

            Fix64   toi;
            FixVec2 hitNormal;
            bool hit = CCDSystem.SweptCircleSegment(ball, seg, Fix64.From(2), out toi, out hitNormal);

            Assert.IsTrue(hit, "Ball heading toward wall must be detected.");
            // toi ≈ (1 − 0.1) / 1 = 0.9
            float toiF = (float)((double)toi.RawValue / (1L << 32));
            Assert.AreApproximatelyEqual(0.9f, toiF, 1e-3f, "TOI should be ≈ 0.9.");
        }
    }
}
