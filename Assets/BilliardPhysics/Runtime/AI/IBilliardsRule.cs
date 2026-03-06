using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Game-rule contract that the AI uses to determine legal play.
    /// Implementations cover specific rule sets (8-ball, 9-ball, snooker, …).
    /// The AI never implements rules directly; it always queries through this interface.
    /// </summary>
    public interface IBilliardsRule
    {
        /// <summary>
        /// Returns the IDs of balls that <paramref name="currentPlayer"/> may legally target
        /// given the current <paramref name="state"/>.
        /// </summary>
        IReadOnlyList<int> GetLegalTargetBalls(TableState state, int currentPlayer);

        /// <summary>
        /// Returns <c>true</c> if <paramref name="result"/> constitutes a foul under
        /// the current rule set.
        /// </summary>
        bool IsFoul(Shot shot, ShotResult result, TableState stateBefore);

        /// <summary>
        /// Returns the number of points awarded for the shot.
        /// Returns 0 for non-scoring rule sets (e.g. standard 8-ball).
        /// </summary>
        int GetScore(Shot shot, ShotResult result, TableState stateBefore);

        /// <summary>Returns <c>true</c> when the game has ended.</summary>
        bool IsGameOver(TableState state);
    }
}
