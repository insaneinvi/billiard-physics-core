using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Assigns a numerical score to a simulated shot.  Higher scores are better.
    ///
    /// Scoring breakdown:
    /// <list type="bullet">
    ///   <item><term>+<see cref="PotBonus"/></term>
    ///     <description>per legally pocketed object ball.</description></item>
    ///   <item><term><see cref="FoulPenalty"/></term>
    ///     <description>when the shot is a foul (as determined by the rule adapter).</description></item>
    ///   <item><term><see cref="WrongBallPenalty"/></term>
    ///     <description>for each illegally pocketed object ball.</description></item>
    ///   <item><term><see cref="ScratchPenalty"/></term>
    ///     <description>extra penalty when the cue ball is pocketed (scratch).</description></item>
    ///   <item><term>Small negative position term</term>
    ///     <description>proportional to the distance from the cue ball to the nearest
    ///     remaining legal target ball; rewards good leave position.</description></item>
    /// </list>
    /// </summary>
    public sealed class ShotEvaluator
    {
        public static readonly Fix64 PotBonus       =  Fix64.From(100);
        public static readonly Fix64 FoulPenalty    = -Fix64.From(200);
        public static readonly Fix64 WrongBallPenalty = -Fix64.From(200);
        public static readonly Fix64 ScratchPenalty = -Fix64.From(300);

        /// <summary>
        /// Computes a score for the given shot and its simulated outcome.
        /// </summary>
        /// <param name="shot">The shot that was played.</param>
        /// <param name="result">The simulated outcome of the shot.</param>
        /// <param name="stateBefore">Table state before the shot was played.</param>
        /// <param name="rules">Rule adapter used for foul and legality queries.</param>
        /// <param name="currentPlayer">Current player index.</param>
        public Fix64 Evaluate(Shot shot, ShotResult result, TableState stateBefore,
                               RuleAdapter rules, int currentPlayer)
        {
            Fix64 score = Fix64.Zero;

            // Foul check (includes scratch in many rule sets).
            if (rules.IsFoul(shot, result, stateBefore))
                score += FoulPenalty;

            // Extra penalty for a scratch (cue ball pocketed).
            if (result.CueBallPocketed)
                score += ScratchPenalty;

            // Bonus for each legally pocketed object ball; penalty for illegal ones.
            IReadOnlyList<int> legalTargets = rules.GetLegalTargetBalls(stateBefore, currentPlayer);
            foreach (int potted in result.PocketedBallIds)
            {
                if (potted == stateBefore.CueBallId) continue;  // scratch already penalised above
                if (IsLegal(potted, legalTargets))
                    score += PotBonus;
                else
                    score += WrongBallPenalty;
            }

            // Position score: reward leaving the cue ball close to a remaining legal target.
            if (!result.CueBallPocketed)
            {
                BallState cueBallAfter = result.StateAfter.CueBall;
                if (cueBallAfter != null)
                {
                    Fix64 minDist = Fix64.MaxValue;
                    foreach (BallState b in result.StateAfter.Balls)
                    {
                        if (b.Id == stateBefore.CueBallId) continue;
                        if (b.IsPocketed)                  continue;
                        if (!IsLegal(b.Id, legalTargets))  continue;
                        Fix64 dist = FixVec2.Distance(cueBallAfter.Position, b.Position);
                        if (dist < minDist) minDist = dist;
                    }

                    if (minDist < Fix64.MaxValue)
                    {
                        // Scale so the position term does not dominate potting bonuses.
                        score -= minDist / Fix64.From(10);
                    }
                }
            }

            return score;
        }

        private static bool IsLegal(int ballId, IReadOnlyList<int> legalTargets)
        {
            foreach (int id in legalTargets)
                if (id == ballId) return true;
            return false;
        }
    }
}
