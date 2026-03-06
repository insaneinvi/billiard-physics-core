using System;
using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// Evaluates a <see cref="Shot"/> by running it through the deterministic
    /// fixed-point physics engine, then restoring the world to its original state.
    /// This allows the AI to evaluate many candidate shots without permanently
    /// modifying the live game state.
    /// </summary>
    public sealed class ShotSimulator
    {
        /// <summary>
        /// Maximum number of 60 Hz physics steps before simulation is cut short.
        /// 600 steps ≈ 10 simulated seconds, far longer than any real billiards shot.
        /// </summary>
        public const int MaxSimSteps = 600;

        /// <summary>
        /// Simulates <paramref name="shot"/> starting from <paramref name="state"/> and
        /// returns the outcome without permanently modifying <paramref name="world"/>.
        /// </summary>
        /// <param name="world">Physics world that holds table geometry and live balls.</param>
        /// <param name="state">Snapshot of ball states to simulate from.</param>
        /// <param name="shot">The shot to apply to the cue ball.</param>
        /// <param name="cueBallId">ID of the cue ball inside <paramref name="world"/>.</param>
        public ShotResult Simulate(PhysicsWorld2D world, TableState state, Shot shot, int cueBallId)
        {
            // 1. Save the current world state so we can restore it after simulation.
            SavedBall[] saved = SaveBalls(world.Balls);

            // 2. Apply the snapshot values to the world's balls.
            RestoreFromSnapshot(world.Balls, state);

            // 3. Subscribe to pocket events to record which balls are pocketed.
            var pocketedIds = new List<int>();
            Action<int, int> handler = (pocketId, ballId) => pocketedIds.Add(ballId);
            world.OnBallPocketed += handler;

            // 4. Apply the cue strike to the cue ball.
            Ball cueBall = FindBall(world.Balls, cueBallId);
            if (cueBall != null)
            {
                world.ApplyCueStrike(cueBall, shot.Direction, shot.Strength, shot.SpinX, shot.SpinY);
                // Explicitly flag the cue ball as moving so the motionless check
                // fires correctly on the very first pass through the loop below.
                cueBall.IsMotionless = false;
            }

            // 5. Step until all balls are still or the step limit is reached.
            int steps = 0;
            while (!AreAllBallsMotionless(world.Balls) && steps < MaxSimSteps)
            {
                world.Step();
                steps++;
            }

            // 6. Capture the result before restoring state.
            world.OnBallPocketed -= handler;
            TableState stateAfter    = TableState.FromWorld(world.Balls, cueBallId);
            bool       cueBallPocketed = pocketedIds.Contains(cueBallId);

            // 7. Restore the world to its original state.
            RestoreFromSaved(world.Balls, saved);

            return new ShotResult(stateAfter, pocketedIds.AsReadOnly(), cueBallPocketed, steps);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private struct SavedBall
        {
            public int     Id;
            public FixVec2 Position;
            public FixVec2 LinearVelocity;
            public FixVec3 AngularVelocity;
            /// <summary>
            /// Captured when a ball is pocketed so the drop animation can apply
            /// natural spin.  Must be preserved so that any ball pocketed during a
            /// simulation trial is fully restored to its pre-trial state.
            /// </summary>
            public FixVec3 LastAngularVelocity;
            public bool    IsMotionless;
            public bool    IsPocketed;
        }

        private static SavedBall[] SaveBalls(IReadOnlyList<Ball> balls)
        {
            var saved = new SavedBall[balls.Count];
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                saved[i] = new SavedBall
                {
                    Id                  = b.Id,
                    Position            = b.Position,
                    LinearVelocity      = b.LinearVelocity,
                    AngularVelocity     = b.AngularVelocity,
                    LastAngularVelocity = b.LastAngularVelocity,
                    IsMotionless        = b.IsMotionless,
                    IsPocketed          = b.IsPocketed,
                };
            }
            return saved;
        }

        private static void RestoreFromSaved(IReadOnlyList<Ball> balls, SavedBall[] saved)
        {
            foreach (SavedBall s in saved)
            {
                Ball b = FindBall(balls, s.Id);
                if (b == null) continue;
                b.Position            = s.Position;
                b.LinearVelocity      = s.LinearVelocity;
                b.AngularVelocity     = s.AngularVelocity;
                b.LastAngularVelocity = s.LastAngularVelocity;
                b.IsMotionless        = s.IsMotionless;
                b.IsPocketed          = s.IsPocketed;
            }
        }

        private static void RestoreFromSnapshot(IReadOnlyList<Ball> balls, TableState state)
        {
            foreach (BallState bs in state.Balls)
            {
                Ball b = FindBall(balls, bs.Id);
                if (b == null) continue;
                b.Position        = bs.Position;
                b.LinearVelocity  = bs.Velocity;
                b.AngularVelocity = bs.Angular;
                b.IsMotionless    = bs.IsMotionless;
                b.IsPocketed      = bs.IsPocketed;
            }
        }

        private static bool AreAllBallsMotionless(IReadOnlyList<Ball> balls)
        {
            Fix64 eps2 = MotionSimulator.Epsilon * MotionSimulator.Epsilon;
            foreach (Ball b in balls)
            {
                if (b.IsPocketed) continue;
                if (b.LinearVelocity.SqrMagnitude  >= eps2) return false;
                if (b.AngularVelocity.SqrMagnitude >= eps2) return false;
            }
            return true;
        }

        private static Ball FindBall(IReadOnlyList<Ball> balls, int id)
        {
            foreach (Ball b in balls)
                if (b.Id == id) return b;
            return null;
        }
    }
}
