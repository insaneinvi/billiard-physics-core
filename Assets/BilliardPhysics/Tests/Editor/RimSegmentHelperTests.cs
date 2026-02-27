using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="RimSegmentHelper"/>.
    /// Covers the Start/End promotion logic required by Goal 1.
    /// </summary>
    public class RimSegmentHelperTests
    {
        // ── Helpers ───────────────────────────────────────────────────────

        private static PocketConfig.SegmentData MakeSeg(
            Vector2 start, Vector2 end, params Vector2[] cps)
        {
            return new PocketConfig.SegmentData
            {
                Start            = start,
                End              = end,
                ConnectionPoints = new List<Vector2>(cps),
            };
        }

        // ── TryPromoteLastCPToEnd ─────────────────────────────────────────

        [Test]
        public void PromoteEnd_SingleCP_BecomesEnd()
        {
            var cp  = new Vector2(1f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(2f, 0f), cp);

            bool ok = RimSegmentHelper.TryPromoteLastCPToEnd(seg);

            Assert.IsTrue(ok, "Promotion should succeed when CP exists.");
            Assert.AreEqual(cp, seg.End, "Promoted point should become End.");
            Assert.AreEqual(0, seg.ConnectionPoints.Count, "CP list should be empty after promotion.");
        }

        [Test]
        public void PromoteEnd_MultipleCPs_LastBecomesEnd_OrderPreserved()
        {
            var a   = new Vector2(1f, 0f);
            var b   = new Vector2(2f, 0f);
            var c   = new Vector2(3f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(4f, 0f), a, b, c);

            bool ok = RimSegmentHelper.TryPromoteLastCPToEnd(seg);

            Assert.IsTrue(ok);
            Assert.AreEqual(c, seg.End, "Last CP (c) should become End.");
            Assert.AreEqual(2, seg.ConnectionPoints.Count);
            Assert.AreEqual(a, seg.ConnectionPoints[0], "First CP (a) must remain first.");
            Assert.AreEqual(b, seg.ConnectionPoints[1], "Second CP (b) must remain second.");
        }

        [Test]
        public void PromoteEnd_EmptyCPs_ReturnsFalseAndLeavesSegmentUnchanged()
        {
            var originalEnd = new Vector2(5f, 0f);
            var seg = MakeSeg(Vector2.zero, originalEnd);   // no CPs

            bool ok = RimSegmentHelper.TryPromoteLastCPToEnd(seg);

            Assert.IsFalse(ok, "Should return false when CPs are empty.");
            Assert.AreEqual(originalEnd, seg.End, "End must remain unchanged.");
        }

        // ── TryPromoteFirstCPToStart ──────────────────────────────────────

        [Test]
        public void PromoteStart_SingleCP_BecomesStart()
        {
            var cp  = new Vector2(1f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(2f, 0f), cp);

            bool ok = RimSegmentHelper.TryPromoteFirstCPToStart(seg);

            Assert.IsTrue(ok, "Promotion should succeed when CP exists.");
            Assert.AreEqual(cp, seg.Start, "Promoted point should become Start.");
            Assert.AreEqual(0, seg.ConnectionPoints.Count, "CP list should be empty after promotion.");
        }

        [Test]
        public void PromoteStart_MultipleCPs_FirstBecomesStart_OrderPreserved()
        {
            var a   = new Vector2(1f, 0f);
            var b   = new Vector2(2f, 0f);
            var c   = new Vector2(3f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(4f, 0f), a, b, c);

            bool ok = RimSegmentHelper.TryPromoteFirstCPToStart(seg);

            Assert.IsTrue(ok);
            Assert.AreEqual(a, seg.Start, "First CP (a) should become Start.");
            Assert.AreEqual(2, seg.ConnectionPoints.Count);
            Assert.AreEqual(b, seg.ConnectionPoints[0], "Second CP (b) must shift to index 0.");
            Assert.AreEqual(c, seg.ConnectionPoints[1], "Third CP (c) must shift to index 1.");
        }

        [Test]
        public void PromoteStart_EmptyCPs_ReturnsFalseAndLeavesSegmentUnchanged()
        {
            var originalStart = new Vector2(0f, 0f);
            var seg = MakeSeg(originalStart, new Vector2(5f, 0f));   // no CPs

            bool ok = RimSegmentHelper.TryPromoteFirstCPToStart(seg);

            Assert.IsFalse(ok, "Should return false when CPs are empty.");
            Assert.AreEqual(originalStart, seg.Start, "Start must remain unchanged.");
        }

        // ── Multi-operation consistency ───────────────────────────────────

        [Test]
        public void MultiplePromotes_DataRemainsConsistent()
        {
            // Polyline: S(0) → a(1) → b(2) → c(3) → E(4)
            var a   = new Vector2(1f, 0f);
            var b   = new Vector2(2f, 0f);
            var c   = new Vector2(3f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(4f, 0f), a, b, c);

            // Remove End → c becomes End
            Assert.IsTrue(RimSegmentHelper.TryPromoteLastCPToEnd(seg));
            Assert.AreEqual(c, seg.End);
            Assert.AreEqual(2, seg.ConnectionPoints.Count);

            // Remove Start → a becomes Start
            Assert.IsTrue(RimSegmentHelper.TryPromoteFirstCPToStart(seg));
            Assert.AreEqual(a, seg.Start);
            Assert.AreEqual(1, seg.ConnectionPoints.Count);

            // Only b remains in CPs
            Assert.AreEqual(b, seg.ConnectionPoints[0]);

            // No duplicates: Start and End are not in CPs
            Assert.IsFalse(seg.ConnectionPoints.Contains(seg.Start));
            Assert.IsFalse(seg.ConnectionPoints.Contains(seg.End));
        }

        [Test]
        public void MultipleEndPromotes_SequentiallyDrainCPs()
        {
            var a   = new Vector2(1f, 0f);
            var b   = new Vector2(2f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(3f, 0f), a, b);

            Assert.IsTrue(RimSegmentHelper.TryPromoteLastCPToEnd(seg));   // b→End
            Assert.AreEqual(b, seg.End);
            Assert.AreEqual(1, seg.ConnectionPoints.Count);

            Assert.IsTrue(RimSegmentHelper.TryPromoteLastCPToEnd(seg));   // a→End
            Assert.AreEqual(a, seg.End);
            Assert.AreEqual(0, seg.ConnectionPoints.Count);

            // CPs now empty — must block
            Assert.IsFalse(RimSegmentHelper.TryPromoteLastCPToEnd(seg));
            Assert.AreEqual(a, seg.End, "End must not change after blocked promotion.");
        }

        [Test]
        public void MultipleStartPromotes_SequentiallyDrainCPs()
        {
            var a   = new Vector2(1f, 0f);
            var b   = new Vector2(2f, 0f);
            var seg = MakeSeg(Vector2.zero, new Vector2(3f, 0f), a, b);

            Assert.IsTrue(RimSegmentHelper.TryPromoteFirstCPToStart(seg));  // a→Start
            Assert.AreEqual(a, seg.Start);
            Assert.AreEqual(1, seg.ConnectionPoints.Count);

            Assert.IsTrue(RimSegmentHelper.TryPromoteFirstCPToStart(seg));  // b→Start
            Assert.AreEqual(b, seg.Start);
            Assert.AreEqual(0, seg.ConnectionPoints.Count);

            // CPs now empty — must block
            Assert.IsFalse(RimSegmentHelper.TryPromoteFirstCPToStart(seg));
            Assert.AreEqual(b, seg.Start, "Start must not change after blocked promotion.");
        }
    }
}
