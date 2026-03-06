using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Wraps an <see cref="IBilliardsRule"/> implementation and provides a uniform
    /// interface for all AI components (<see cref="ShotGenerator"/>,
    /// <see cref="ShotEvaluator"/>, <see cref="ShotSearch"/>).
    /// Centralising rule queries here makes it straightforward to add caching,
    /// logging, or fallback behaviour without modifying the rule implementation.
    /// </summary>
    public sealed class RuleAdapter
    {
        private readonly IBilliardsRule _rule;

        public RuleAdapter(IBilliardsRule rule)
        {
            _rule = rule;
        }

        /// <summary>Returns the IDs of balls legally targetable by <paramref name="currentPlayer"/>.</summary>
        public IReadOnlyList<int> GetLegalTargetBalls(TableState state, int currentPlayer)
            => _rule.GetLegalTargetBalls(state, currentPlayer);

        /// <summary>Returns <c>true</c> if the shot result is a foul.</summary>
        public bool IsFoul(Shot shot, ShotResult result, TableState stateBefore)
            => _rule.IsFoul(shot, result, stateBefore);

        /// <summary>Returns the points scored for this shot.</summary>
        public int GetScore(Shot shot, ShotResult result, TableState stateBefore)
            => _rule.GetScore(shot, result, stateBefore);

        /// <summary>Returns <c>true</c> when the game has ended.</summary>
        public bool IsGameOver(TableState state)
            => _rule.IsGameOver(state);
    }
}
