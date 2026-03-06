using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Outcome returned by <see cref="ShotSimulator"/> after simulating a <see cref="Shot"/>.
    /// </summary>
    public sealed class ShotResult
    {
        /// <summary>Table state after all balls have come to rest.</summary>
        public TableState         StateAfter      { get; }

        /// <summary>IDs of all balls pocketed during the simulated shot.</summary>
        public IReadOnlyList<int> PocketedBallIds { get; }

        /// <summary>True if the cue ball was pocketed (scratch / foul).</summary>
        public bool               CueBallPocketed { get; }

        /// <summary>Number of 60 Hz physics steps taken during the simulation.</summary>
        public int                StepsSimulated  { get; }

        public ShotResult(TableState stateAfter, IReadOnlyList<int> pocketedBallIds,
                          bool cueBallPocketed, int stepsSimulated)
        {
            StateAfter      = stateAfter;
            PocketedBallIds = pocketedBallIds;
            CueBallPocketed = cueBallPocketed;
            StepsSimulated  = stepsSimulated;
        }
    }
}
