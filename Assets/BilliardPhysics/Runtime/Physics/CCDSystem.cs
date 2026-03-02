using System;
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
            /// <summary>
            /// Closest point on the struck cushion surface in world space (contact point).
            /// For face hits this is the foot of the perpendicular from the ball centre to
            /// the sub-segment at TOI; for vertex hits it is the vertex position.
            /// Valid only when <see cref="IsBallBall"/> is <c>false</c>.
            /// Used by <see cref="PhysicsWorld2D"/> for positional push-out to prevent tunnelling.
            /// </summary>
            public FixVec2 HitPoint;
        }

        // ── Swept circle vs circle ────────────────────────────────────────────────

        // Scale factor for converting Fix64 raw values to doubles (2^32 = 4 294 967 296).
        private const double Fix64Scale = (double)(1L << 32);

        // Fix64 multiplication wraps when the integer part of the result exceeds 2^31.
        // For squaring: overflow when |value| > sqrt(2^31) ≈ 46340.95.
        // Store the threshold as a raw Fix64 value (46341 * 2^32).
        private static readonly long BallBallOverflowThreshold = 46341L << 32;

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

            Fix64 t;

            // bCoeff*bCoeff wraps (overflows Fix64) when |bCoeff| > sqrt(2^31) ≈ 46341.
            // At high shot strengths this makes the discriminant appear negative (false
            // negative — missed collision).  Fall back to double-precision arithmetic in
            // that regime to avoid both false negatives and false positives.
            long bRaw = bCoeff.RawValue;
            if (bRaw > BallBallOverflowThreshold || bRaw < -BallBallOverflowThreshold)
            {
                double da   = (double)aCoeff.RawValue / Fix64Scale;
                double db   = (double)bCoeff.RawValue / Fix64Scale;
                double dc   = (double)cCoeff.RawValue / Fix64Scale;
                double disc = db * db - 4.0 * da * dc;
                if (disc < 0.0) return false;

                double dtD = (double)dt.RawValue / Fix64Scale;
                double tD  = (-db - Math.Sqrt(disc)) / (2.0 * da);
                if (tD < 0.0 || tD > dtD) return false;

                t = new Fix64 { RawValue = (long)(tD * Fix64Scale) };
            }
            else
            {
                Fix64 disc = bCoeff * bCoeff - Fix64.From(4) * aCoeff * cCoeff;
                if (disc < Fix64.Zero) return false;

                Fix64 sqrtDisc = Fix64.Sqrt(disc);
                Fix64 two_a    = Fix64.From(2) * aCoeff;
                t              = (-bCoeff - sqrtDisc) / two_a;

                if (t < Fix64.Zero || t > dt) return false;
            }

            // Validate: confirm the balls genuinely touch at the candidate TOI.
            // This guards against false positives from Fix64 overflow in the normal path
            // and catches any numerical imprecision in the double-precision path.
            FixVec2 posA = a.Position + a.LinearVelocity * t;
            FixVec2 posB = b.Position + b.LinearVelocity * t;
            if (FixVec2.Distance(posA, posB) > radSum)
                return false;

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
        /// outward <paramref name="hitNormal"/> and cushion-surface contact point
        /// <paramref name="hitPoint"/>.
        /// </summary>
        public static bool SweptCircleSegment(Ball ball, Segment seg, Fix64 dt,
                                              out Fix64 toi, out FixVec2 hitNormal, out FixVec2 hitPoint)
        {
            toi       = Fix64.Zero;
            hitNormal = FixVec2.Zero;
            hitPoint  = FixVec2.Zero;

            bool    bestHit      = false;
            Fix64   bestToi      = dt;
            FixVec2 bestNormal   = FixVec2.Zero;
            FixVec2 bestHitPoint = FixVec2.Zero;

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
                        bestHit      = true;
                        bestToi      = t;
                        bestNormal   = n;
                        // Contact point on the cushion face: perpendicular foot from ball centre.
                        bestHitPoint = hitPos - n * ball.Radius;
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
                        // Guard against degenerate case where ball centre coincides with vertex.
                        FixVec2 normal;
                        if (dp.SqrMagnitude > Fix64.Zero)
                            normal = dp.Normalized;
                        else if (ball.LinearVelocity.SqrMagnitude > Fix64.Zero)
                            normal = -(ball.LinearVelocity.Normalized);
                        else
                            normal = new FixVec2(Fix64.One, Fix64.Zero);
                        bestHit      = true;
                        bestToi      = ptToi;
                        bestNormal   = normal;
                        bestHitPoint = pt;
                    }
                }
            }

            if (!bestHit) return false;

            toi       = bestToi;
            hitNormal = bestNormal;
            hitPoint  = bestHitPoint;
            return true;
        }

        /// <summary>
        /// Overload kept for source compatibility; <paramref name="hitPoint"/> is discarded.
        /// </summary>
        public static bool SweptCircleSegment(Ball ball, Segment seg, Fix64 dt,
                                              out Fix64 toi, out FixVec2 hitNormal)
        {
            FixVec2 dummy;
            return SweptCircleSegment(ball, seg, dt, out toi, out hitNormal, out dummy);
        }

        /// <summary>
        /// Overload kept for source compatibility; <paramref name="hitNormal"/> and
        /// <paramref name="hitPoint"/> are discarded.
        /// </summary>
        public static bool SweptCircleSegment(Ball ball, Segment seg, Fix64 dt, out Fix64 toi)
        {
            FixVec2 dummyNormal, dummyHitPoint;
            return SweptCircleSegment(ball, seg, dt, out toi, out dummyNormal, out dummyHitPoint);
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
                    FixVec2 hitPoint;
                    if (SweptCircleSegment(ball, seg, dt, out toi, out hitNormal, out hitPoint))
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
                                HitPoint   = hitPoint,
                                IsBallBall = false
                            };
                        }
                    }
                }

                // Requirement 2: when the ball is inside a pocket's trigger radius, also
                // test the pocket's RimSegment as a solid obstacle.  The rim is only
                // activated inside the pocket area so that it never interferes with normal
                // cushion reflections on the rest of the table.  Its Restitution is set to
                // a low value (PocketRimRestitution) so that the ball loses most of its
                // normal velocity on contact, slowing it below PocketSinkSpeedThreshold so
                // that CheckPocketCaptures can subsequently capture it.
                for (int pi = 0; pi < pockets.Count; pi++)
                {
                    Pocket pocket = pockets[pi];
                    if (pocket.RimSegment == null) continue;

                    Fix64 distToPocket = FixVec2.Distance(ball.Position, pocket.Center);
                    if (distToPocket >= pocket.Radius) continue;  // only active inside pocket area

                    NarrowPhaseSegmentCalls++;
                    Fix64   toi;
                    FixVec2 hitNormal;
                    FixVec2 hitPoint;
                    if (SweptCircleSegment(ball, pocket.RimSegment, dt, out toi, out hitNormal, out hitPoint))
                    {
                        if (!best.Hit || toi < best.TOI ||
                            (toi == best.TOI && ball.Id < best.BallA))
                        {
                            best = new TOIResult
                            {
                                Hit        = true,
                                TOI        = toi,
                                BallA      = ball.Id,
                                Segment    = pocket.RimSegment,
                                HitNormal  = hitNormal,
                                HitPoint   = hitPoint,
                                IsBallBall = false
                            };
                        }
                    }
                }
            }

            return best;
        }
    }
}
