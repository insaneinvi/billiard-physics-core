using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Generates candidate shots for the AI to evaluate.
    /// Produces three categories:
    /// <list type="bullet">
    ///   <item><term>Direct pot</term>
    ///     <description>Ghost-ball aim toward a pocket via a legal target ball.</description></item>
    ///   <item><term>Bank shot</term>
    ///     <description>Rail-first route computed by reflecting the target across the rail line.</description></item>
    ///   <item><term>Safety</term>
    ///     <description>Low-power shot that contacts a legal ball without trying to pot it.</description></item>
    /// </list>
    /// All arithmetic uses fixed-point math; no floating point is used.
    /// </summary>
    public sealed class ShotGenerator
    {
        /// <summary>Default impulse magnitude for direct-pot attempts.</summary>
        public static readonly Fix64 DirectPotStrength = Fix64.From(15);

        /// <summary>Default impulse magnitude for bank shots (needs extra power to reach via rail).</summary>
        public static readonly Fix64 BankShotStrength  = Fix64.From(20);

        /// <summary>Default impulse magnitude for safety shots (minimum force).</summary>
        public static readonly Fix64 SafetyStrength    = BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMin;

        /// <summary>
        /// Returns all candidate shots for the current game situation.
        /// </summary>
        /// <param name="state">Current table state (ball positions).</param>
        /// <param name="pockets">Pocket list from the physics world.</param>
        /// <param name="segments">Table rail segments from the physics world.</param>
        /// <param name="rules">Rule adapter used to determine legal target balls.</param>
        /// <param name="currentPlayer">Current player index (passed to the rule adapter).</param>
        public IReadOnlyList<Shot> GenerateShots(
            TableState             state,
            IReadOnlyList<Pocket>  pockets,
            IReadOnlyList<Segment> segments,
            RuleAdapter            rules,
            int                    currentPlayer)
        {
            var shots = new List<Shot>();

            BallState cueBall = state.CueBall;
            if (cueBall == null || cueBall.IsPocketed)
                return shots;

            IReadOnlyList<int> legalTargets = rules.GetLegalTargetBalls(state, currentPlayer);
            Fix64 twoRadii = Fix64.Two * Ball.StandardRadius;

            foreach (int targetId in legalTargets)
            {
                BallState target = state.GetBall(targetId);
                if (target == null || target.IsPocketed) continue;

                // ── Direct pot shots (ghost-ball method) ──────────────────────────────
                foreach (Pocket pocket in pockets)
                {
                    FixVec2 toBall = target.Position - pocket.Center;
                    Fix64   dist   = toBall.Magnitude;
                    if (dist == Fix64.Zero) continue;

                    // Ghost ball centre = target position + unit(toBall) * twoRadii.
                    // Aiming at the ghost ball pushes the target toward the pocket.
                    FixVec2 ghostPos = target.Position + (toBall / dist) * twoRadii;
                    FixVec2 aimVec   = ghostPos - cueBall.Position;
                    Fix64   aimLen   = aimVec.Magnitude;
                    if (aimLen == Fix64.Zero) continue;

                    shots.Add(new Shot(aimVec / aimLen, DirectPotStrength,
                                       Fix64.Zero, Fix64.Zero, ShotKind.DirectPot));
                }

                // ── Bank shots (reflect target across each rail segment) ───────────────
                foreach (Segment seg in segments)
                {
                    FixVec2 lineVec    = seg.End - seg.Start;
                    Fix64   lineVecMag = lineVec.Magnitude;
                    if (lineVecMag == Fix64.Zero) continue;
                    FixVec2 lineDir = lineVec / lineVecMag;

                    // Aiming toward the mirror image of the target behind the rail
                    // produces the correct bank angle.
                    FixVec2 reflected = ReflectPoint(target.Position, seg.Start, lineDir);
                    FixVec2 aimVec    = reflected - cueBall.Position;
                    Fix64   aimLen    = aimVec.Magnitude;
                    if (aimLen == Fix64.Zero) continue;

                    shots.Add(new Shot(aimVec / aimLen, BankShotStrength,
                                       Fix64.Zero, Fix64.Zero, ShotKind.BankShot));
                }

                // ── Safety shot (contact the legal ball with minimum force) ────────────
                {
                    FixVec2 aimVec = target.Position - cueBall.Position;
                    Fix64   aimLen = aimVec.Magnitude;
                    if (aimLen != Fix64.Zero)
                        shots.Add(new Shot(aimVec / aimLen, SafetyStrength,
                                           Fix64.Zero, Fix64.Zero, ShotKind.Safety));
                }
            }

            return shots;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Reflects <paramref name="point"/> across the infinite line that passes through
        /// <paramref name="linePoint"/> in direction <paramref name="lineDir"/> (must be normalised).
        /// </summary>
        private static FixVec2 ReflectPoint(FixVec2 point, FixVec2 linePoint, FixVec2 lineDir)
        {
            FixVec2 ap   = point - linePoint;
            Fix64   proj = FixVec2.Dot(ap, lineDir);
            FixVec2 foot = linePoint + lineDir * proj;
            return Fix64.Two * foot - point;
        }
    }
}
