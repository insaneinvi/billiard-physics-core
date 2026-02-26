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

        public static bool SweptCircleSegment(Ball ball, Segment seg, Fix64 dt, out Fix64 toi)
        {
            toi = Fix64.Zero;

            FixVec2 n   = seg.Normal;
            FixVec2 dir = seg.Direction;
            Fix64   len = seg.Length;

            Fix64 vn = FixVec2.Dot(ball.LinearVelocity, n);
            if (vn >= Fix64.Zero) return false;  // moving away from or parallel to segment

            Fix64 dist = FixVec2.Dot(ball.Position - seg.Start, n);
            if (dist < Fix64.Zero) return false;  // ball on wrong side

            Fix64 t = (dist - ball.Radius) / (-vn);
            if (t < Fix64.Zero || t > dt) return false;

            // Check if hit point falls within segment extents.
            FixVec2 hitPos = ball.Position + ball.LinearVelocity * t;
            Fix64   proj   = FixVec2.Dot(hitPos - seg.Start, dir);

            if (proj >= Fix64.Zero && proj <= len)
            {
                toi = t;
                return true;
            }

            // Test endpoints as circles of radius 0.
            Fix64 toiA, toiB;
            bool  hitA = SweptCirclePoint(ball, seg.Start, dt, out toiA);
            bool  hitB = SweptCirclePoint(ball, seg.End,   dt, out toiB);

            if (!hitA && !hitB) return false;

            if (hitA && hitB)
                toi = Fix64.Min(toiA, toiB);
            else
                toi = hitA ? toiA : toiB;

            return true;
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
                    Fix64 toi;
                    if (SweptCircleSegment(ball, seg, dt, out toi))
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
                        Fix64 toi;
                        if (SweptCircleSegment(ball, rimSeg, dt, out toi))
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
