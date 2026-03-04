using System.Collections.Generic;
using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="SegmentGrid"/>.
    /// Verifies that the grid returns the correct candidate segments and that
    /// distant segments are culled from the query results.
    /// </summary>
    public class SegmentGridTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Segment MakeVertical(float x, float yMin, float yMax)
        {
            return new Segment(
                new FixVec2(Fix64.FromFloat(x), Fix64.FromFloat(yMin)),
                new FixVec2(Fix64.FromFloat(x), Fix64.FromFloat(yMax)));
        }

        private static Segment MakeHorizontal(float y, float xMin, float xMax)
        {
            return new Segment(
                new FixVec2(Fix64.FromFloat(xMin), Fix64.FromFloat(y)),
                new FixVec2(Fix64.FromFloat(xMax), Fix64.FromFloat(y)));
        }

        // ── Construction ──────────────────────────────────────────────────────────

        [Test]
        public void SegmentGrid_EmptySegmentList_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new SegmentGrid(new List<Segment>()));
        }

        [Test]
        public void SegmentGrid_EmptySegmentList_QueryReturnsEmpty()
        {
            var grid    = new SegmentGrid(new List<Segment>());
            var results = new List<Segment>();
            grid.Query(Fix64.From(-100), Fix64.From(-100), Fix64.From(100), Fix64.From(100), results);
            Assert.AreEqual(0, results.Count);
        }

        // ── Spatial culling ───────────────────────────────────────────────────────

        /// <summary>
        /// A segment near the query centre must appear in results;
        /// a segment far away must be culled.
        /// </summary>
        [Test]
        public void Query_NearbySegmentReturned_DistantSegmentCulled()
        {
            Segment near = MakeVertical(100f, -50f, 50f);   // x = 100
            Segment far  = MakeVertical(500f, -50f, 50f);   // x = 500

            var grid    = new SegmentGrid(new List<Segment> { near, far });
            var results = new List<Segment>();

            // Query AABB centred around x=100 with a width of ~120 units.
            grid.Query(Fix64.From(40), Fix64.From(-60), Fix64.From(160), Fix64.From(60), results);

            Assert.IsTrue(results.Contains(near), "Near segment should be in results.");
            Assert.IsFalse(results.Contains(far),  "Distant segment should be culled.");
        }

        /// <summary>
        /// A query entirely outside the world AABB must return no segments.
        /// </summary>
        [Test]
        public void Query_OutsideWorldBounds_ReturnsEmpty()
        {
            Segment seg  = MakeVertical(500f, -50f, 50f);
            var grid     = new SegmentGrid(new List<Segment> { seg });
            var results  = new List<Segment>();

            // Query far to the left of the single segment at x=500.
            grid.Query(Fix64.From(0), Fix64.From(-50), Fix64.From(50), Fix64.From(50), results);

            Assert.AreEqual(0, results.Count,
                "Query far from all segments must return no candidates.");
        }

        /// <summary>
        /// A query that spans the full world must return every segment.
        /// </summary>
        [Test]
        public void Query_FullWorldSpan_ReturnsAllSegments()
        {
            Segment s1 = MakeVertical(100f, -50f, 50f);
            Segment s2 = MakeVertical(500f, -50f, 50f);
            Segment s3 = MakeHorizontal(0f, 0f, 600f);

            var grid    = new SegmentGrid(new List<Segment> { s1, s2, s3 });
            var results = new List<Segment>();

            // Query covers the entire table area.
            grid.Query(Fix64.From(-100), Fix64.From(-200), Fix64.From(800), Fix64.From(200), results);

            Assert.IsTrue(results.Contains(s1), "s1 must be in full-span results.");
            Assert.IsTrue(results.Contains(s2), "s2 must be in full-span results.");
            Assert.IsTrue(results.Contains(s3), "s3 must be in full-span results.");
        }

        // ── De-duplication ────────────────────────────────────────────────────────

        /// <summary>
        /// A segment whose AABB spans multiple grid cells must appear exactly once
        /// in the query results even when the query overlaps all those cells.
        /// </summary>
        [Test]
        public void Query_LongSegmentAcrossManyCells_ReturnedOnlyOnce()
        {
            // Very long horizontal segment that will span all grid columns.
            Segment longSeg = MakeHorizontal(0f, -1000f, 1000f);

            var grid    = new SegmentGrid(new List<Segment> { longSeg });
            var results = new List<Segment>();

            grid.Query(Fix64.From(-200), Fix64.From(-50), Fix64.From(200), Fix64.From(50), results);

            int count = 0;
            foreach (Segment s in results)
                if (s == longSeg) count++;

            Assert.AreEqual(1, count,
                "A segment spanning multiple cells must appear exactly once in results.");
        }

        // ── Grid narrows candidates ───────────────────────────────────────────────

        /// <summary>
        /// When a ball is located near only a subset of segments, the grid query
        /// must return fewer candidates than the full segment list.
        /// </summary>
        [Test]
        public void Query_BallNearOneEdge_CandidatesFewerThanTotal()
        {
            // Build a 6-segment set: left/right cushions at x=±500, top/bottom at y=±300,
            // plus two extra cushions at x=±400 that a ball near the left wall won't touch.
            var segments = new List<Segment>
            {
                MakeVertical(-500f, -300f, 300f),   // left wall  (ball is near this)
                MakeVertical( 500f, -300f, 300f),   // right wall
                MakeHorizontal(-300f, -500f, 500f), // bottom rail
                MakeHorizontal( 300f, -500f, 500f), // top rail
                MakeVertical( 400f, -300f, 300f),   // inner right 1
                MakeVertical(-400f, -300f, 300f),   // inner left  (also near ball)
            };

            var grid = new SegmentGrid(segments, cols: 8, rows: 4);

            // Ball near the left wall (x ≈ -470), small query AABB (~60 units wide).
            var results = new List<Segment>();
            Fix64 qMargin = Fix64.From(30) + Ball.StandardRadius;
            grid.Query(
                Fix64.FromFloat(-500f) - qMargin,
                Fix64.FromFloat(-30f),
                Fix64.FromFloat(-500f) + qMargin,
                Fix64.FromFloat(30f),
                results);

            Assert.Less(results.Count, segments.Count,
                "Grid query near one edge should return fewer candidates than all segments.");
        }

        // ── Integration: NarrowPhaseSegmentCalls reduced by grid ─────────────────

        /// <summary>
        /// Calling FindEarliestCollision with a segment grid must produce fewer (or equal)
        /// narrow-phase segment calls than the brute-force path.
        /// </summary>
        [Test]
        public void FindEarliestCollision_WithGrid_NarrowPhaseCallsNotExceededBruteForce()
        {
            // 6 segments on a small table.
            var segments = new List<Segment>
            {
                MakeVertical(-500f, -300f, 300f),
                MakeVertical( 500f, -300f, 300f),
                MakeHorizontal(-300f, -500f, 500f),
                MakeHorizontal( 300f, -500f, 500f),
                MakeVertical( 400f, -100f, 100f),
                MakeVertical(-400f, -100f, 100f),
            };

            // Ball near the left wall.
            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.FromFloat(-450f), Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.FromFloat(-200f), Fix64.Zero);

            var balls   = new List<Ball>   { ball };
            var pockets = new List<Pocket>();
            Fix64 dt    = Fix64.One / Fix64.From(60);

            // Brute-force (no grid).
            CCDSystem.ResetStats();
            CCDSystem.FindEarliestCollision(balls, segments, pockets, dt);
            int bruteForce = CCDSystem.NarrowPhaseSegmentCalls;

            // With grid.
            var grid = new SegmentGrid(segments);
            CCDSystem.ResetStats();
            CCDSystem.FindEarliestCollision(balls, segments, pockets, dt, grid);
            int withGrid = CCDSystem.NarrowPhaseSegmentCalls;

            Assert.LessOrEqual(withGrid, bruteForce,
                $"Grid must not produce more narrow-phase calls than brute force " +
                $"(grid={withGrid}, brute={bruteForce}).");
        }

        /// <summary>
        /// Physics results must be identical with and without the grid — the grid is
        /// a broadphase optimisation only and must not change simulation outcome.
        /// </summary>
        [Test]
        public void Step_WithGrid_SameResultAsWithout()
        {
            var segs = new List<Segment>
            {
                MakeVertical(-500f, -300f, 300f),
                MakeVertical( 500f, -300f, 300f),
                MakeHorizontal(-300f, -500f, 500f),
                MakeHorizontal( 300f, -500f, 500f),
            };

            var w1 = MakeWorldWithSegments(segs, useSetTableSegments: true);
            var w2 = MakeWorldWithSegments(segs, useSetTableSegments: false);

            for (int i = 0; i < 30; i++)
            {
                w1.Step();
                w2.Step();
            }

            Ball b1 = w1.Balls[0];
            Ball b2 = w2.Balls[0];
            Assert.AreEqual(b1.Position,       b2.Position,       "Position must match");
            Assert.AreEqual(b1.LinearVelocity, b2.LinearVelocity, "Velocity must match");
        }

        private static PhysicsWorld2D MakeWorldWithSegments(List<Segment> segs, bool useSetTableSegments)
        {
            var world = new PhysicsWorld2D();
            if (useSetTableSegments)
                world.SetTableSegments(segs);
            else
                foreach (var s in segs) world.AddSegment(s);

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.FromFloat(-450f), Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.FromFloat(-300f), Fix64.Zero);
            world.AddBall(ball);
            return world;
        }
    }
}
