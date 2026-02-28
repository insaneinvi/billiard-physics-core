using System.Collections.Generic;

namespace BilliardPhysics
{
    public static class CCDSystem
    {
        public struct TOIResult
        {
            public bool    Hit;
            public Fix64   TOI;
            public int     BallA;
            public int     BallB;
            public Segment Segment;
            public bool    IsBallBall;
            /// <summary>
            /// Outward normal of the sub-segment (or endpoint) actually struck.
            /// Valid only when <see cref="IsBallBall"/> is <c>false</c>.
            /// </summary>
            public FixVec2 HitNormal;
        }

        // ── Swept circle vs circle ────────────────────────────────────────────────

        /// <summary>
        /// Returns the earliest time in [0, dt] at which balls a and b first touch.
        /// </summary>
        public static bool SweptCircleCircle(Ball a, Ball b, Fix64 dt, out Fix64 toi)
        {
            toi = Fix64.Zero;

            FixVec2 dp = a.Position       - b.Position;
            FixVec2 dv = a.LinearVelocity - b.LinearVelocity;

            Fix64 radSum = a.Radius + b.Radius;

            Fix64 aCoeff = FixVec2.Dot(dv, dv);
            Fix64 bCoeff = Fix64.From(2) * FixVec2.Dot(dp, dv);
            Fix64 cCoeff = FixVec2.Dot(dp, dp) - radSum * radSum;

            if (aCoeff == Fix64.Zero)
            {
                // Balls have the same velocity; no relative motion.
                return false;
            }

            // Balls are already overlapping: resolve immediately if they are approaching.
            if (cCoeff <= Fix64.Zero)
            {
                if (bCoeff >= Fix64.Zero) return false;  // already separating despite overlap
                toi = Fix64.Zero;
                return true;
            }

            Fix64 disc = bCoeff * bCoeff - Fix64.From(4) * aCoeff * cCoeff;
            if (disc < Fix64.Zero) return false;

            Fix64 sqrtDisc = Fix64.Sqrt(disc);
            Fix64 two_a    = Fix64.From(2) * aCoeff;
            Fix64 t        = (-bCoeff - sqrtDisc) / two_a;

            if (t < Fix64.Zero || t > dt) return false;

            toi = t;
            return true;
        }

        // ── Swept circle vs point (endpoint helper) ───────────────────────────────

        private static bool SweptCirclePoint(Ball ball, FixVec2 point, Fix64 dt, out Fix64 toi)
        {
            toi = Fix64.Zero;

            FixVec2 dp = ball.Position - point;
            FixVec2 dv = ball.LinearVelocity;

            Fix64 aCoeff = FixVec2.Dot(dv, dv);
            Fix64 bCoeff = Fix64.From(2) * FixVec2.Dot(dp, dv);
            Fix64 cCoeff = FixVec2.Dot(dp, dp) - ball.Radius * ball.Radius;

            if (aCoeff == Fix64.Zero) return false;

            // Already overlapping the vertex: resolve at t=0 only if approaching.
            if (cCoeff <= Fix64.Zero)
            {
                if (bCoeff >= Fix64.Zero) return false;  // already separating despite overlap
                toi = Fix64.Zero;
                return true;
            }

            Fix64 disc = bCoeff * bCoeff - Fix64.From(4) * aCoeff * cCoeff;
            if (disc < Fix64.Zero) return false;

            Fix64 sqrtDisc = Fix64.Sqrt(disc);
            Fix64 t        = (-bCoeff - sqrtDisc) / (Fix64.From(2) * aCoeff);

            if (t < Fix64.Zero || t > dt) return false;

            toi = t;
            return true;
        }

        // Small safety margin (in world/physics units) added to broadphase distance checks
        // so that objects just barely within reach are not incorrectly culled by integer
        // rounding.  Ball radii are ~28.6 units, so 1 unit is safely sub-radius.
        private static readonly Fix64 BroadphaseTolerance = Fix64.From(1);

        // ── Swept circle vs segment ───────────────────────────────────────────────

        /// <summary>
        /// Tests a swept circle against a (possibly polyline) <see cref="Segment"/>.
        /// Iterates every sub-segment and all polyline vertices, returning the
        /// earliest <paramref name="toi"/> in [0, dt] and the corresponding
        /// outward <paramref name="hitNormal"/>.
        /// </summary>
        public static bool SweptCircleSegment(Ball ball, Segment seg, Fix64 dt,
                                              out Fix64 toi, out FixVec2 hitNormal)
        {
            toi       = Fix64.Zero;
            hitNormal = FixVec2.Zero;

            bool  bestHit    = false;
            Fix64 bestToi    = dt;
            FixVec2 bestNormal = FixVec2.Zero;

            IReadOnlyList<FixVec2> points = seg.Points;
            int subSegCount = points.Count - 1;

            // ── Test each sub-segment face ─────────────────────────────────────────
            for (int i = 0; i < subSegCount; i++)
            {
                FixVec2 segStart = points[i];
                FixVec2 n        = seg.Normal[i];
                FixVec2 dir      = seg.Direction[i];
                Fix64   len      = (points[i + 1] - segStart).Magnitude;

                Fix64 vn = FixVec2.Dot(ball.LinearVelocity, n);
                if (vn >= Fix64.Zero) continue;  // moving away from or parallel to sub-segment

                Fix64 dist = FixVec2.Dot(ball.Position - segStart, n);
                if (dist < Fix64.Zero) continue;  // ball on wrong side

                Fix64 t = (dist - ball.Radius) / (-vn);
                if (t < Fix64.Zero || t > dt) continue;

                // Check if hit point falls within sub-segment extents.
                FixVec2 hitPos = ball.Position + ball.LinearVelocity * t;
                Fix64   proj   = FixVec2.Dot(hitPos - segStart, dir);

                if (proj >= Fix64.Zero && proj <= len)
                {
                    if (!bestHit || t < bestToi)
                    {
                        bestHit    = true;
                        bestToi    = t;
                        bestNormal = n;
                    }
                }
            }

            // ── Test all polyline vertices (Start, every CP, End) as point circles ──
            // Use index-based for loop instead of foreach to avoid enumerator boxing
            // on the IReadOnlyList<FixVec2> interface, which would cause a GC alloc.
            Fix64 ballSpeed = ball.LinearVelocity.Magnitude;
            int   pointCount = subSegCount + 1;   // points.Count, already known
            for (int i = 0; i < pointCount; i++)
            {
                FixVec2 pt = points[i];
                // Broadphase: skip vertex if the ball cannot possibly reach it this step.
                Fix64 ptDist = FixVec2.Distance(ball.Position, pt);
                if (ptDist > ball.Radius + ballSpeed * dt + BroadphaseTolerance) continue;

                Fix64 ptToi;
                if (SweptCirclePoint(ball, pt, dt, out ptToi))
                {
                    if (!bestHit || ptToi < bestToi)
                    {
                        // Normal points from the vertex toward the ball centre at TOI.
                        FixVec2 ballAtToi = ball.Position + ball.LinearVelocity * ptToi;
                        FixVec2 dp        = ballAtToi - pt;
                        bestHit    = true;
                        bestToi    = ptToi;
                        bestNormal = dp.Normalized;
                    }
                }
            }

            if (!bestHit) return false;

            toi       = bestToi;
            hitNormal = bestNormal;
            return true;
        }

        /// <summary>
        /// Overload kept for source compatibility; <see cref="hitNormal"/> is discarded.
        /// </summary>
        public static bool SweptCircleSegment(Ball ball, Segment seg, Fix64 dt, out Fix64 toi)
        {
            FixVec2 dummy;
            return SweptCircleSegment(ball, seg, dt, out toi, out dummy);
        }

        // ── Performance counters ─────────────────────────────────────────────────

        /// <summary>
        /// Cumulative number of <see cref="SweptCircleSegment"/> calls made from
        /// <see cref="FindEarliestCollision"/> since the last <see cref="ResetStats"/>.
        /// Useful for comparing brute-force vs grid-accelerated broadphase.
        /// </summary>
        public static int NarrowPhaseSegmentCalls;

        /// <summary>Resets all performance counters to zero.</summary>
        public static void ResetStats() => NarrowPhaseSegmentCalls = 0;

        // Scratch buffer for candidate segments returned by the spatial grid.
        // Single-threaded use only (physics loop is always single-threaded).
        private static readonly List<Segment> _candidateSegments = new List<Segment>();

        // ── Find earliest collision ───────────────────────────────────────────────

        /// <summary>
        /// Finds the earliest collision within [0, dt].
        /// When <paramref name="segmentGrid"/> is supplied the ball–cushion broadphase
        /// uses the grid to skip distant segments; otherwise every segment is tested
        /// (original brute-force behaviour, preserved for backwards compatibility).
        /// </summary>
        public static TOIResult FindEarliestCollision(
            List<Ball>    balls,
            List<Segment> segments,
            List<Pocket>  pockets,
            Fix64         dt,
            SegmentGrid   segmentGrid = null)
        {
            TOIResult best = new TOIResult { Hit = false, TOI = dt };

            // Ball–ball pairs.
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i].IsPocketed) continue;
                for (int j = i + 1; j < balls.Count; j++)
                {
                    if (balls[j].IsPocketed) continue;

                    // Broadphase: skip if balls cannot possibly reach each other this step.
                    Fix64 dist       = FixVec2.Distance(balls[i].Position, balls[j].Position);
                    Fix64 radSum     = balls[i].Radius + balls[j].Radius;
                    Fix64 maxApproach = (balls[i].LinearVelocity.Magnitude +
                                         balls[j].LinearVelocity.Magnitude) * dt;
                    if (dist > radSum + maxApproach + BroadphaseTolerance) continue;

                    Fix64 toi;
                    if (SweptCircleCircle(balls[i], balls[j], dt, out toi))
                    {
                        if (!best.Hit || toi < best.TOI ||
                            (toi == best.TOI && balls[i].Id < best.BallA))
                        {
                            best = new TOIResult
                            {
                                Hit       = true,
                                TOI       = toi,
                                BallA     = balls[i].Id,
                                BallB     = balls[j].Id,
                                IsBallBall = true
                            };
                        }
                    }
                }
            }

            // Ball–cushion pairs.
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i].IsPocketed) continue;
                Ball ball = balls[i];

                // Determine candidate segments via grid broadphase (or all segments).
                IList<Segment> candidates;
                if (segmentGrid != null)
                {
                    // Swept AABB of the ball over this substep (directional, not circular).
                    Fix64 velocityDisplacementX = ball.LinearVelocity.X * dt;
                    Fix64 velocityDisplacementY = ball.LinearVelocity.Y * dt;
                    Fix64 r   = ball.Radius + BroadphaseTolerance;
                    _candidateSegments.Clear();
                    segmentGrid.Query(
                        ball.Position.X - r + Fix64.Min(velocityDisplacementX, Fix64.Zero),
                        ball.Position.Y - r + Fix64.Min(velocityDisplacementY, Fix64.Zero),
                        ball.Position.X + r + Fix64.Max(velocityDisplacementX, Fix64.Zero),
                        ball.Position.Y + r + Fix64.Max(velocityDisplacementY, Fix64.Zero),
                        _candidateSegments);
                    candidates = _candidateSegments;
                }
                else
                {
                    candidates = segments;
                }

                for (int si = 0; si < candidates.Count; si++)
                {
                    Segment seg = candidates[si];
                    NarrowPhaseSegmentCalls++;
                    Fix64   toi;
                    FixVec2 hitNormal;
                    if (SweptCircleSegment(ball, seg, dt, out toi, out hitNormal))
                    {
                        if (!best.Hit || toi < best.TOI ||
                            (toi == best.TOI && ball.Id < best.BallA))
                        {
                            best = new TOIResult
                            {
                                Hit        = true,
                                TOI        = toi,
                                BallA      = ball.Id,
                                Segment    = seg,
                                HitNormal  = hitNormal,
                                IsBallBall = false
                            };
                        }
                    }
                }

                // Pockets are trigger volumes, not solid walls; rim segments are not
                // tested for collision here.  Capture is handled by CheckPocketCaptures.
            }

            return best;
        }
    }
}
