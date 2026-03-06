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
            SavedBall[] saved = SaveBalls(world.Balls, world.BallCount);

            // 2. Apply the snapshot values to the world's balls.
            RestoreFromSnapshot(world.Balls, world.BallCount, state);

            // 3. Subscribe to pocket events to record which balls are pocketed.
            var pocketedIds = new List<int>();
            Action<int, int> handler = (pocketId, ballId) => pocketedIds.Add(ballId);
            world.OnBallPocketed += handler;

            // 4. Apply the cue strike to the cue ball.
            int cueBallIdx = FindBallIndex(world.Balls, world.BallCount, cueBallId);
            if (cueBallIdx >= 0)
            {
                world.ApplyCueStrike(world.Balls[cueBallIdx], shot.Direction, shot.Strength, shot.SpinX, shot.SpinY);
                // Explicitly flag the cue ball as moving so the motionless check
                // fires correctly on the very first pass through the loop below.
                world.Balls[cueBallIdx].IsMotionless = false;
            }

            // 5. Step until all balls are still or the step limit is reached.
            int steps = 0;
            while (!AreAllBallsMotionless(world.Balls, world.BallCount) && steps < MaxSimSteps)
            {
                world.Step();
                steps++;
            }

            // 6. Capture the result before restoring state.
            world.OnBallPocketed -= handler;
            TableState stateAfter    = TableState.FromWorld(world.Balls, cueBallId);
            bool       cueBallPocketed = pocketedIds.Contains(cueBallId);

            // 7. Restore the world to its original state.
            RestoreFromSaved(world.Balls, world.BallCount, saved);

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

        private static SavedBall[] SaveBalls(Ball[] balls, int ballCount)
        {
            var saved = new SavedBall[ballCount];
            for (int i = 0; i < ballCount; i++)
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

        private static void RestoreFromSaved(Ball[] balls, int ballCount, SavedBall[] saved)
        {
            foreach (SavedBall s in saved)
            {
                int idx = FindBallIndex(balls, ballCount, s.Id);
                if (idx < 0) continue;
                balls[idx].Position            = s.Position;
                balls[idx].LinearVelocity      = s.LinearVelocity;
                balls[idx].AngularVelocity     = s.AngularVelocity;
                balls[idx].LastAngularVelocity = s.LastAngularVelocity;
                balls[idx].IsMotionless        = s.IsMotionless;
                balls[idx].IsPocketed          = s.IsPocketed;
            }
        }

        private static void RestoreFromSnapshot(Ball[] balls, int ballCount, TableState state)
        {
            foreach (BallState bs in state.Balls)
            {
                int idx = FindBallIndex(balls, ballCount, bs.Id);
                if (idx < 0) continue;
                balls[idx].Position        = bs.Position;
                balls[idx].LinearVelocity  = bs.Velocity;
                balls[idx].AngularVelocity = bs.Angular;
                balls[idx].IsMotionless    = bs.IsMotionless;
                balls[idx].IsPocketed      = bs.IsPocketed;
            }
        }

        private static bool AreAllBallsMotionless(Ball[] balls, int ballCount)
        {
            Fix64 eps2 = MotionSimulator.Epsilon * MotionSimulator.Epsilon;
            for (int i = 0; i < ballCount; i++)
            {
                if (balls[i].IsPocketed) continue;
                if (balls[i].LinearVelocity.SqrMagnitude  >= eps2) return false;
                if (balls[i].AngularVelocity.SqrMagnitude >= eps2) return false;
            }
            return true;
        }

        private static int FindBallIndex(Ball[] balls, int ballCount, int id)
        {
            for (int i = 0; i < ballCount; i++)
                if (balls[i].Id == id) return i;
            return -1;
        }
    }
}
