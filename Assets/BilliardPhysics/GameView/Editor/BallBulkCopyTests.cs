using System;
using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode tests for the bulk-copy helpers added to <see cref="PhysicsWorld2D"/>:
    ///   • <c>CopyBallsTo(Ball[])</c>        – Array.Copy path
    ///   • <c>CopyBallsTo(Span&lt;Ball&gt;)</c> – Span.CopyTo path
    ///   • <c>SnapshotBalls()</c>             – allocates + copies
    ///   • <c>UnsafeCopyBallsTo(Ball*)</c>    – unsafe Unsafe.CopyBlock path
    /// </summary>
    public class BallBulkCopyTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Builds a PhysicsWorld2D populated with <paramref name="count"/> balls.</summary>
        private static PhysicsWorld2D MakeWorld(int count)
        {
            var world = new PhysicsWorld2D();
            for (int i = 0; i < count; i++)
            {
                var ball = new Ball(i);
                // Give each ball a distinct position so copies can be verified.
                ball.Position = new FixVec2(Fix64.From(i * 10), Fix64.From(i));
                world.AddBall(ball);
            }
            return world;
        }

        // ── CopyBallsTo(Ball[]) ───────────────────────────────────────────────────

        /// <summary>
        /// Array.Copy path: destination array receives all active balls and their
        /// field values match those in the source world.
        /// </summary>
        [Test]
        public void CopyBallsTo_Array_CopiesAllBalls()
        {
            // Optimization target: Array.Copy → single native block copy for blittable structs.
            var world = MakeWorld(4);
            var dst = new Ball[world.BallCount];

            world.CopyBallsTo(dst);

            Assert.AreEqual(world.BallCount, dst.Length);
            for (int i = 0; i < world.BallCount; i++)
            {
                // Verify position field (representative of all blittable fields).
                Assert.AreEqual(world.Balls[i].Position.X, dst[i].Position.X,
                    $"Position.X mismatch at index {i}");
                Assert.AreEqual(world.Balls[i].Position.Y, dst[i].Position.Y,
                    $"Position.Y mismatch at index {i}");
                Assert.AreEqual(world.Balls[i].Id, dst[i].Id,
                    $"Id mismatch at index {i}");
            }
        }

        /// <summary>
        /// The copy must be independent: mutating the destination does not affect the world.
        /// </summary>
        [Test]
        public void CopyBallsTo_Array_ProducesIndependentCopy()
        {
            var world = MakeWorld(3);
            var dst = new Ball[world.BallCount];
            world.CopyBallsTo(dst);

            // Mutate the copy.
            dst[0].Position = new FixVec2(Fix64.From(9999), Fix64.From(9999));

            // Source world must be unchanged.
            Assert.AreEqual(Fix64.From(0), world.Balls[0].Position.X,
                "Mutating the destination array must not affect the source world.");
        }

        // ── CopyBallsTo(Span<Ball>) ───────────────────────────────────────────────

        /// <summary>
        /// Span.CopyTo path: managed array slice receives all active balls.
        /// Optimization: Span.CopyTo is JIT-lowered to a memcpy intrinsic; no per-element overhead.
        /// </summary>
        [Test]
        public void CopyBallsTo_Span_CopiesAllBalls()
        {
            var world = MakeWorld(5);
            var pool = new Ball[world.BallCount];
            // Ball[] → Span<Ball> (array-backed span).
            world.CopyBallsTo(new Span<Ball>(pool));

            for (int i = 0; i < world.BallCount; i++)
            {
                Assert.AreEqual(world.Balls[i].Id, pool[i].Id,
                    $"Id mismatch at index {i}");
                Assert.AreEqual(world.Balls[i].Position.X, pool[i].Position.X,
                    $"Position.X mismatch at index {i}");
            }
        }

        /// <summary>
        /// stackalloc path: copy into a stack-allocated Span (zero heap allocation).
        /// This is the recommended pattern for temporary per-frame snapshots with a
        /// known small bound (e.g. ≤ 16 balls for a full billiard rack).
        /// </summary>
        [Test]
        public void CopyBallsTo_StackAllocSpan_CopiesAllBalls()
        {
            const int maxBalls = 16;
            var world = MakeWorld(4);

            // stackalloc: zero heap allocation; safe for small fixed-size buffers.
            unsafe
            {
                // Use a fixed-size stack buffer via a helper to avoid variable-length
                // stackalloc in a method with other try/catch frames (Unity limitation).
                Ball* stackBuf = stackalloc Ball[maxBalls];
                Span<Ball> span = new Span<Ball>(stackBuf, maxBalls);
                world.CopyBallsTo(span.Slice(0, world.BallCount));

                for (int i = 0; i < world.BallCount; i++)
                {
                    Assert.AreEqual(world.Balls[i].Id, span[i].Id,
                        $"Id mismatch at index {i}");
                }
            }
        }

        // ── SnapshotBalls() ───────────────────────────────────────────────────────

        /// <summary>
        /// SnapshotBalls allocates a new Ball[] of exactly BallCount length and
        /// fills it via Array.Copy (one allocation, no per-element overhead).
        /// </summary>
        [Test]
        public void SnapshotBalls_ReturnsCorrectLengthAndValues()
        {
            var world = MakeWorld(6);
            Ball[] snapshot = world.SnapshotBalls();

            // Length must equal BallCount (not _balls.Length which may be larger).
            Assert.AreEqual(world.BallCount, snapshot.Length,
                "Snapshot array length must equal BallCount.");

            for (int i = 0; i < world.BallCount; i++)
            {
                Assert.AreEqual(world.Balls[i].Id, snapshot[i].Id,
                    $"Id mismatch at index {i}");
                Assert.AreEqual(world.Balls[i].Position.X, snapshot[i].Position.X,
                    $"Position.X mismatch at index {i}");
            }
        }

        /// <summary>
        /// The snapshot must be independent: mutating it does not affect the world.
        /// </summary>
        [Test]
        public void SnapshotBalls_ProducesIndependentArray()
        {
            var world = MakeWorld(3);
            Ball[] snapshot = world.SnapshotBalls();

            snapshot[1].Position = new FixVec2(Fix64.From(-1), Fix64.From(-1));

            // The world's ball at index 1 must be unchanged.
            Assert.AreEqual(Fix64.From(10), world.Balls[1].Position.X,
                "Mutating the snapshot must not affect the source world.");
        }

        // ── UnsafeCopyBallsTo(Ball*) ──────────────────────────────────────────────

        /// <summary>
        /// Unsafe.CopyBlock path: raw byte-level copy into a pinned managed buffer.
        /// Use when destination is unmanaged / native memory; fastest available path.
        /// </summary>
        [Test]
        public unsafe void UnsafeCopyBallsTo_CopiesAllBalls()
        {
            var world = MakeWorld(4);
            var dst = new Ball[world.BallCount];

            fixed (Ball* dstPtr = dst)
            {
                // Unsafe.CopyBlock: zero bounds checks, single native memcpy.
                world.UnsafeCopyBallsTo(dstPtr);
            }

            for (int i = 0; i < world.BallCount; i++)
            {
                Assert.AreEqual(world.Balls[i].Id, dst[i].Id,
                    $"Id mismatch at index {i}");
                Assert.AreEqual(world.Balls[i].Position.X, dst[i].Position.X,
                    $"Position.X mismatch at index {i}");
            }
        }

        /// <summary>
        /// UnsafeCopyBallsTo on an empty world must not throw or write anything.
        /// </summary>
        [Test]
        public unsafe void UnsafeCopyBallsTo_EmptyWorld_DoesNotThrow()
        {
            var world = new PhysicsWorld2D(); // zero balls
            var dst   = new Ball[1];          // harmless buffer

            Assert.DoesNotThrow(() =>
            {
                fixed (Ball* dstPtr = dst)
                    world.UnsafeCopyBallsTo(dstPtr);
            });
        }

        // ── Edge cases ────────────────────────────────────────────────────────────

        /// <summary>
        /// All copy helpers must produce identical data for the same source world,
        /// demonstrating that Array.Copy, Span.CopyTo, SnapshotBalls, and
        /// UnsafeCopyBallsTo are equivalent in result.
        /// </summary>
        [Test]
        public unsafe void AllCopyMethods_ProduceIdenticalResults()
        {
            var world = MakeWorld(4);

            var arrDst      = new Ball[world.BallCount];
            var spanDst     = new Ball[world.BallCount];
            Ball[] snapshot = world.SnapshotBalls();
            var unsafeDst   = new Ball[world.BallCount];

            world.CopyBallsTo(arrDst);
            world.CopyBallsTo(new Span<Ball>(spanDst));
            fixed (Ball* dstPtr = unsafeDst)
                world.UnsafeCopyBallsTo(dstPtr);

            for (int i = 0; i < world.BallCount; i++)
            {
                // All four methods must yield the same Id and Position.
                Assert.AreEqual(arrDst[i].Id, spanDst[i].Id,    $"Array vs Span Id mismatch [{i}]");
                Assert.AreEqual(arrDst[i].Id, snapshot[i].Id,   $"Array vs Snapshot Id mismatch [{i}]");
                Assert.AreEqual(arrDst[i].Id, unsafeDst[i].Id,  $"Array vs Unsafe Id mismatch [{i}]");

                Assert.AreEqual(arrDst[i].Position.X, spanDst[i].Position.X,   $"Array vs Span X mismatch [{i}]");
                Assert.AreEqual(arrDst[i].Position.X, snapshot[i].Position.X,  $"Array vs Snapshot X mismatch [{i}]");
                Assert.AreEqual(arrDst[i].Position.X, unsafeDst[i].Position.X, $"Array vs Unsafe X mismatch [{i}]");
            }
        }
    }
}
