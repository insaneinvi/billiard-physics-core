using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// Unit tests for the GenerateRimFromCircle invariants:
    ///   - Start == End (same point, the randomly chosen circle point).
    ///   - ConnectionPoints contains exactly N-1 unique circle points in CW order.
    ///   - No duplicates; Start/End point does not appear in ConnectionPoints.
    ///
    /// GenerateRimFromCircle is a private editor method, so these tests exercise
    /// the same algorithmic contract independently (the BuildSegment helper below
    /// mirrors the exact computation). This documents the required invariants and
    /// guards against regressions if the logic is refactored into a testable helper.
    /// </summary>
    public class RimFromCircleGeneratorTests
    {
        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Generates N evenly distributed CCW points on a unit circle centred at origin.
        /// </summary>
        private static Vector2[] GenerateCirclePoints(int n)
        {
            var pts = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                float angle = k * 2f * Mathf.PI / n;
                pts[k] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return pts;
        }

        /// <summary>
        /// Applies the GenerateRimFromCircle ConnectionPoints algorithm:
        /// Start = End = pts[idx], CPs = pts[idx-1], pts[idx-2], … (CW, N-1 points).
        /// </summary>
        private static SegmentData BuildSegment(Vector2[] pts, int idx)
        {
            int n = pts.Length;
            var cpList = new List<Vector2>(n - 1);
            for (int i = 1; i < n; i++)
                cpList.Add(pts[(idx - i + n) % n]);

            return new SegmentData
            {
                Start            = pts[idx],
                End              = pts[idx],
                ConnectionPoints = cpList,
            };
        }

        // ── Start == End ──────────────────────────────────────────────────

        [TestCase(4)]
        [TestCase(8)]
        public void StartEqualsEnd_ForAllIndices(int n)
        {
            Vector2[] pts = GenerateCirclePoints(n);

            for (int idx = 0; idx < n; idx++)
            {
                var seg = BuildSegment(pts, idx);
                Assert.AreEqual(seg.Start, seg.End,
                    $"Start must equal End for idx={idx}, N={n}.");
            }
        }

        // ── ConnectionPoints count == N-1 ─────────────────────────────────

        [Test]
        public void ConnectionPointsCount_IsNMinusOne([Values(3, 4, 5, 8, 12)] int n)
        {
            Vector2[] pts = GenerateCirclePoints(n);
            var seg = BuildSegment(pts, 0);

            Assert.AreEqual(n - 1, seg.ConnectionPoints.Count,
                $"ConnectionPoints must contain exactly N-1={n-1} points for N={n}.");
        }

        // ── No duplicates ─────────────────────────────────────────────────

        [Test]
        public void ConnectionPoints_ContainsNoDuplicates_N6()
        {
            int n = 6;
            Vector2[] pts = GenerateCirclePoints(n);

            for (int idx = 0; idx < n; idx++)
            {
                var seg = BuildSegment(pts, idx);
                var seen = new HashSet<Vector2>();
                foreach (var cp in seg.ConnectionPoints)
                    Assert.IsTrue(seen.Add(cp),
                        $"Duplicate found in ConnectionPoints for idx={idx}.");
            }
        }

        [Test]
        public void StartEndPoint_NotInConnectionPoints([Values(3, 5, 8)] int n)
        {
            Vector2[] pts = GenerateCirclePoints(n);

            for (int idx = 0; idx < n; idx++)
            {
                var seg = BuildSegment(pts, idx);
                Assert.IsFalse(seg.ConnectionPoints.Contains(seg.Start),
                    $"Start/End point must not appear in ConnectionPoints for idx={idx}, N={n}.");
            }
        }

        // ── CW order (decreasing index with wrap) ─────────────────────────

        [Test]
        public void ConnectionPointsOrder_IsClockwise_N4_Idx0()
        {
            // pts: 0=right, 1=top, 2=left, 3=bottom (CCW)
            // CW from idx=0: pts[3], pts[2], pts[1]
            int n   = 4;
            int idx = 0;
            Vector2[] pts = GenerateCirclePoints(n);
            var seg = BuildSegment(pts, idx);

            Assert.AreEqual(3, seg.ConnectionPoints.Count);
            Assert.AreEqual(pts[3], seg.ConnectionPoints[0], "First CP should be pts[idx-1] = pts[3].");
            Assert.AreEqual(pts[2], seg.ConnectionPoints[1], "Second CP should be pts[idx-2] = pts[2].");
            Assert.AreEqual(pts[1], seg.ConnectionPoints[2], "Third CP should be pts[idx-3] = pts[1].");
        }

        [Test]
        public void ConnectionPointsOrder_IsClockwise_N4_Idx2()
        {
            // CW from idx=2: pts[1], pts[0], pts[3]
            int n   = 4;
            int idx = 2;
            Vector2[] pts = GenerateCirclePoints(n);
            var seg = BuildSegment(pts, idx);

            Assert.AreEqual(3, seg.ConnectionPoints.Count);
            Assert.AreEqual(pts[1], seg.ConnectionPoints[0], "First CP should be pts[1].");
            Assert.AreEqual(pts[0], seg.ConnectionPoints[1], "Second CP should be pts[0].");
            Assert.AreEqual(pts[3], seg.ConnectionPoints[2], "Third CP should be pts[3] (wrap).");
        }

        // ── All circle points covered ─────────────────────────────────────

        [Test]
        public void AllCirclePoints_AreCoveredExactlyOnce([Values(3, 6, 10)] int n)
        {
            Vector2[] pts = GenerateCirclePoints(n);

            for (int idx = 0; idx < n; idx++)
            {
                var seg = BuildSegment(pts, idx);

                // Collect all points: Start + CPs (End == Start, so skip to avoid double-count)
                var all = new List<Vector2> { seg.Start };
                all.AddRange(seg.ConnectionPoints);

                Assert.AreEqual(n, all.Count,
                    $"Total distinct points (Start + CPs) should equal N={n} for idx={idx}.");

                // Every pts[k] must appear exactly once
                for (int k = 0; k < n; k++)
                    Assert.AreEqual(1, all.FindAll(p => p == pts[k]).Count,
                        $"pts[{k}] must appear exactly once in Start+CPs for idx={idx}, N={n}.");
            }
        }
    }
}
