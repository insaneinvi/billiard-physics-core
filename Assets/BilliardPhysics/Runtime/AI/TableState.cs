using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Immutable snapshot of all balls on the table at a given moment.
    /// Table geometry (segments, pockets) is stored in the physics world and is
    /// intentionally excluded here, as it does not change during a game.
    /// </summary>
    public sealed class TableState
    {
        public IReadOnlyList<BallState> Balls     { get; }
        public int                      CueBallId { get; }

        public TableState(IReadOnlyList<BallState> balls, int cueBallId)
        {
            Balls     = balls;
            CueBallId = cueBallId;
        }

        /// <summary>Returns the state of the ball with the given id, or <c>null</c> if not found.</summary>
        public BallState GetBall(int id)
        {
            foreach (BallState b in Balls)
                if (b.Id == id) return b;
            return null;
        }

        /// <summary>Returns the cue ball state, or <c>null</c> if the cue ball is not present.</summary>
        public BallState CueBall => GetBall(CueBallId);

        /// <summary>
        /// Captures a snapshot of all balls currently in the physics world.
        /// </summary>
        public static TableState FromWorld(IReadOnlyList<Ball> balls, int cueBallId)
        {
            var states = new BallState[balls.Count];
            for (int i = 0; i < balls.Count; i++)
                states[i] = BallState.FromBall(balls[i]);
            return new TableState(states, cueBallId);
        }
    }
}
