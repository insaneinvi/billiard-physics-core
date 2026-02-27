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

            Fix64 disc = bCoeff * bCoeff - Fix64.From(4) * aCoeff * cCoeff;
            if (disc < Fix64.Zero) return false;

            Fix64 sqrtDisc = Fix64.Sqrt(disc);
            Fix64 t        = (-bCoeff - sqrtDisc) / (Fix64.From(2) * aCoeff);

            if (t < Fix64.Zero || t > dt) return false;

            toi = t;
            return true;
        }

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
            foreach (FixVec2 pt in points)
            {
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

        // ── Find earliest collision ───────────────────────────────────────────────

        public static TOIResult FindEarliestCollision(
            List<Ball>    balls,
            List<Segment> segments,
            List<Pocket>  pockets,
            Fix64         dt)
        {
            TOIResult best = new TOIResult { Hit = false, TOI = dt };

            // Ball–ball pairs.
            for (int i = 0; i < balls.Count; i++)
            {
                if (balls[i].IsPocketed) continue;
                for (int j = i + 1; j < balls.Count; j++)
                {
                    if (balls[j].IsPocketed) continue;

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

                foreach (Segment seg in segments)
                {
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

                // Pocket rim segments (only when ball is near the pocket).
                foreach (Pocket pocket in pockets)
                {
                    Fix64 distToPocket = FixVec2.Distance(ball.Position, pocket.Center);
                    if (distToPocket > pocket.Radius) continue;

                    foreach (Segment rimSeg in pocket.RimSegments)
                    {
                        Fix64   toi;
                        FixVec2 hitNormal;
                        if (SweptCircleSegment(ball, rimSeg, dt, out toi, out hitNormal))
                        {
                            if (!best.Hit || toi < best.TOI ||
                                (toi == best.TOI && ball.Id < best.BallA))
                            {
                                best = new TOIResult
                                {
                                    Hit        = true,
                                    TOI        = toi,
                                    BallA      = ball.Id,
                                    Segment    = rimSeg,
                                    HitNormal  = hitNormal,
                                    IsBallBall = false
                                };
                            }
                        }
                    }
                }
            }

            return best;
        }
    }
}
