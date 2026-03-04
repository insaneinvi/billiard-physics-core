using NUnit.Framework;
using UnityEngine;
using BilliardPhysics;
using BilliardPhysics.AniHelp;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="PocketPostRollAniHelper"/>.
    /// </summary>
    public class PocketPostRollAniHelperTests
    {
        private const float Eps = 1e-4f;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static PocketPostRollAniHelper MakeHelper() => new PocketPostRollAniHelper();

        private static PocketPostRollRequest SimpleRequest(
            float duration       = 1f,
            float ballRadius     = 0.5f,
            bool  isCueBall      = false,
            StoppedBallInfo[] stopped = null)
            => new PocketPostRollRequest
            {
                pathPoints   = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration     = duration,
                ballRadius   = ballRadius,
                isCueBall    = isCueBall,
                stoppedBalls = stopped,
            };

        // ── Basic rolling ─────────────────────────────────────────────────────

        [Test]
        public void Start_IsRunning_True()
        {
            var h = MakeHelper();
            h.Start(SimpleRequest());
            Assert.IsTrue(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Update_FullDuration_ReturnsFinished()
        {
            var h   = MakeHelper();
            var req = SimpleRequest(duration: 1f);
            h.Start(in req);

            PocketPostRollState state = h.Update(1.0f);

            Assert.AreEqual(PocketPostRollPhase.Finished, state.phase);
            Assert.AreEqual(1f, state.normalizedTime, Eps);
            Assert.IsFalse(h.IsRunning);
            Assert.IsTrue(h.IsFinished);
        }

        [Test]
        public void Update_AtEnd_PositionEqualsPathEnd()
        {
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(4f, 0f, 0f) },
                duration   = 1f,
                ballRadius = 0.1f,
            });

            PocketPostRollState state = h.Update(1.0f);

            Assert.AreEqual(4f, state.position.x, Eps);
            Assert.AreEqual(0f, state.position.y, Eps);
        }

        [Test]
        public void Evaluate_Midpoint_PositionIsHalfway()
        {
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration   = 2f,
                ballRadius = 0.1f,
            });

            PocketPostRollState state = h.Evaluate(0.5f);

            Assert.AreEqual(5f, state.position.x, Eps);
            Assert.AreEqual(PocketPostRollPhase.Rolling, state.phase);
        }

        // ── Multi-segment path ────────────────────────────────────────────────

        [Test]
        public void MultiSegmentPath_PositionAtEnd_IsLastWaypoint()
        {
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(5f, 0f, 0f),
                    new Vector3(5f, 5f, 0f),
                },
                duration   = 2f,
                ballRadius = 0.1f,
            });

            PocketPostRollState state = h.Update(2.0f);

            Assert.AreEqual(5f, state.position.x, Eps);
            Assert.AreEqual(5f, state.position.y, Eps);
            Assert.AreEqual(PocketPostRollPhase.Finished, state.phase);
        }

        // ── Early stop: blocked by stopped ball ───────────────────────────────

        [Test]
        public void StoppedBall_OnPath_BallStopsAtContactPoint()
        {
            // Path: (0,0) → (10,0). Stopped ball center at (6,0), radius 0.5.
            // Rolling ball radius 0.5. Contact distance = 1.0.
            // Contact occurs when rolling center is at x = 6 - 1 = 5.
            var stopped = new[]
            {
                new StoppedBallInfo
                {
                    ballId   = 99,
                    position = new Vector3(6f, 0f, 0f),
                    radius   = 0.5f,
                }
            };

            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints   = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration     = 1f,
                ballRadius   = 0.5f,
                stoppedBalls = stopped,
            });

            // Animate to completion
            PocketPostRollState state = h.Update(10f);

            Assert.AreEqual(PocketPostRollPhase.Finished, state.phase);
            // Ball should stop at contact point x ≈ 5.0
            Assert.AreEqual(5f, state.position.x, Eps);
        }

        [Test]
        public void StoppedBall_MultipleStopped_StopsAtNearest()
        {
            // Two stopped balls; one at x=8, one at x=4. Rolling ball r=0.5, stopped r=0.5.
            // Contact distances: 4-1=3, 8-1=7. Nearest = 3.
            var stopped = new[]
            {
                new StoppedBallInfo { ballId = 1, position = new Vector3(8f, 0f, 0f), radius = 0.5f },
                new StoppedBallInfo { ballId = 2, position = new Vector3(4f, 0f, 0f), radius = 0.5f },
            };

            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints   = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration     = 1f,
                ballRadius   = 0.5f,
                stoppedBalls = stopped,
            });

            PocketPostRollState state = h.Update(10f);

            Assert.AreEqual(3f, state.position.x, Eps, "Should stop at nearest blocked ball.");
        }

        [Test]
        public void StoppedBall_OffPath_DoesNotBlock()
        {
            // Stopped ball far off to the side – should not block.
            var stopped = new[]
            {
                new StoppedBallInfo { ballId = 5, position = new Vector3(5f, 100f, 0f), radius = 0.5f },
            };

            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints   = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration     = 1f,
                ballRadius   = 0.5f,
                stoppedBalls = stopped,
            });

            PocketPostRollState state = h.Update(10f);

            Assert.AreEqual(10f, state.position.x, Eps, "Should reach the end.");
        }

        // ── Cue ball callback ─────────────────────────────────────────────────

        [Test]
        public void CueBall_OnFinish_OnCueBallRetrievedFired()
        {
            bool cueBallFired = false;

            var h = MakeHelper();
            h.OnCueBallRetrieved = () => cueBallFired = true;
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(5f, 0f, 0f) },
                duration   = 0.5f,
                ballRadius = 0.1f,
                isCueBall  = true,
            });

            h.Update(1.0f); // past end

            Assert.IsTrue(cueBallFired, "OnCueBallRetrieved must fire for the cue ball.");
        }

        [Test]
        public void NonCueBall_OnFinish_OnCueBallRetrievedNotFired()
        {
            bool cueBallFired = false;

            var h = MakeHelper();
            h.OnCueBallRetrieved = () => cueBallFired = true;
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(5f, 0f, 0f) },
                duration   = 0.5f,
                ballRadius = 0.1f,
                isCueBall  = false,
            });

            h.Update(1.0f);

            Assert.IsFalse(cueBallFired, "OnCueBallRetrieved must NOT fire for non-cue balls.");
        }

        [Test]
        public void OnStop_FiredWithFinalPosition()
        {
            Vector3 stoppedAt = Vector3.negativeInfinity;

            var h = MakeHelper();
            h.OnStop = pos => stoppedAt = pos;
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(3f, 0f, 0f) },
                duration   = 1f,
                ballRadius = 0.1f,
            });

            h.Update(2f);

            Assert.AreEqual(3f, stoppedAt.x, Eps);
        }

        // ── Z-axis preservation ───────────────────────────────────────────────

        [Test]
        public void ZAxis_RemainsConstant_WhenWaypointsHaveSameZ()
        {
            // All waypoints at Z = 2. Ball should stay at Z = 2 throughout.
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { new Vector3(0f, 0f, 2f), new Vector3(10f, 0f, 2f) },
                duration   = 1f,
                ballRadius = 0.5f,
            });

            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                PocketPostRollState state = h.Evaluate(t);
                Assert.AreEqual(2f, state.position.z, Eps,
                    $"Z should be constant at 2.0 at normalizedTime={t}");
            }
        }

        [Test]
        public void ZAxis_ClampedToStartZ_EvenIfWaypointsVaryZ()
        {
            // Waypoints with different Z values; Z must be locked to the first waypoint's Z.
            float startZ = 5f;
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[]
                {
                    new Vector3(0f, 0f, startZ),
                    new Vector3(5f, 0f, 3f),      // different Z
                    new Vector3(10f, 0f, 7f),     // different Z
                },
                duration   = 1f,
                ballRadius = 0.5f,
            });

            for (float t = 0f; t <= 1f; t += 0.25f)
            {
                PocketPostRollState state = h.Evaluate(t);
                Assert.AreEqual(startZ, state.position.z, Eps,
                    $"Z must stay locked to startZ={startZ} at normalizedTime={t}");
            }
        }

        [Test]
        public void ZAxis_FinalPosition_MatchesStartZ()
        {
            float startZ = 3f;
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { new Vector3(0f, 0f, startZ), new Vector3(8f, 0f, startZ) },
                duration   = 1f,
                ballRadius = 0.5f,
            });

            PocketPostRollState state = h.Update(2f); // overshoot → Finished

            Assert.AreEqual(startZ, state.position.z, Eps, "Final position Z must equal startZ.");
        }

        // ── Angular velocity ──────────────────────────────────────────────────

        [Test]
        public void AngularVelocity_RollingAlongX_CorrectYComponent()
        {
            // Path along +X, length=10, duration=2, radius=0.5
            // speed = 10/2 = 5 m/s
            // dir = (1,0,0); ω = Cross(forward, dir*speed) / r
            // = Cross((0,0,1),(5,0,0)) / 0.5 = (0,5,0) / 0.5 = (0,10,0)
            float radius = 0.5f;
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration   = 2f,
                ballRadius = radius,
            });

            PocketPostRollState state = h.Evaluate(0.5f);

            Assert.AreEqual(PocketPostRollPhase.Rolling, state.phase);
            Assert.AreEqual(0f,  state.angularVelocity.x, Eps);
            Assert.AreEqual(10f, state.angularVelocity.y, Eps);
            Assert.AreEqual(0f,  state.angularVelocity.z, Eps);
        }

        [Test]
        public void AngularVelocity_RollingAlongY_CorrectXComponent()
        {
            // Path along +Y, length=10, duration=2, radius=0.5
            // speed = 5; dir = (0,1,0)
            // ω = Cross((0,0,1),(0,5,0)) / 0.5 = (-5,0,0) / 0.5 = (-10,0,0)
            float radius = 0.5f;
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(0f, 10f, 0f) },
                duration   = 2f,
                ballRadius = radius,
            });

            PocketPostRollState state = h.Evaluate(0.5f);

            Assert.AreEqual(PocketPostRollPhase.Rolling, state.phase);
            Assert.AreEqual(-10f, state.angularVelocity.x, Eps);
            Assert.AreEqual(0f,   state.angularVelocity.y, Eps);
            Assert.AreEqual(0f,   state.angularVelocity.z, Eps);
        }

        [Test]
        public void AngularVelocity_IsZero_WhenFinished()
        {
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(5f, 0f, 0f) },
                duration   = 1f,
                ballRadius = 0.5f,
            });

            PocketPostRollState state = h.Update(2f); // overshoot → Finished

            Assert.AreEqual(PocketPostRollPhase.Finished, state.phase);
            Assert.AreEqual(Vector3.zero, state.angularVelocity,
                "Angular velocity must be zero after the animation finishes.");
        }

        // ── Edge cases ────────────────────────────────────────────────────────

        [Test]
        public void NullPathPoints_FinishesImmediately()
        {
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest { pathPoints = null, duration = 1f });

            Assert.IsFalse(h.IsRunning);
            Assert.IsTrue(h.IsFinished);
        }

        [Test]
        public void SingleWaypoint_FinishesImmediately()
        {
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { new Vector3(1f, 2f, 3f) },
                duration   = 1f,
            });

            Assert.IsFalse(h.IsRunning);
            Assert.IsTrue(h.IsFinished);
        }

        [Test]
        public void Reset_ClearsState()
        {
            var h = MakeHelper();
            h.Start(SimpleRequest());
            h.Update(0.5f);
            h.Reset();

            Assert.IsFalse(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Stop_InterruptsAnimation()
        {
            var h = MakeHelper();
            h.Start(SimpleRequest());
            h.Update(0.3f);
            h.Stop();

            Assert.IsFalse(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Reuse_StartCalledTwice_SecondRunIndependentOfFirst()
        {
            var h = MakeHelper();
            h.Start(SimpleRequest(duration: 1f));
            h.Update(1.0f); // finish first run

            // Second run with a different path
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 7f, 0f) },
                duration   = 2f,
                ballRadius = 0.1f,
            });

            Assert.IsTrue(h.IsRunning);
            PocketPostRollState mid = h.Evaluate(0.5f);
            Assert.AreEqual(3.5f, mid.position.y, Eps);
        }

        // ── Ball instance integration ─────────────────────────────────────────

        [Test]
        public void BallInstance_OverridesBallRadius()
        {
            // Ball with radius 0.3; ballRadius = 0.5 in request should be overridden.
            var ball = new Ball(7);
            // Ball(id) uses StandardRadius ≈ 0.28575
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration   = 2f,
                ballRadius = 0.5f,  // should be overridden by ball.Radius
                ball       = ball,
            });

            PocketPostRollState state = h.Evaluate(0.5f);

            // The angular velocity must be computed using ball.Radius, not 0.5.
            // ω = Cross((0,0,1), (speed, 0, 0)) / radius
            // speed = 10/2 = 5; radius = 0.28575
            float expectedRadius = ball.Radius.ToFloat();
            float expectedOmegaY = 5f / expectedRadius;
            Assert.AreEqual(expectedOmegaY, state.angularVelocity.y, 1e-2f,
                "Ball.Radius should override ballRadius in angularVelocity computation.");
        }

        // ── SegmentData rollPath ──────────────────────────────────────────────

        [Test]
        public void RollPath_SegmentData_TwoPoints_PositionAtEnd()
        {
            var seg = new SegmentData
            {
                Start = new Vector2(0f, 0f),
                End   = new Vector2(6f, 0f),
            };

            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                rollPath   = seg,
                tableZ     = 1f,
                duration   = 1f,
                ballRadius = 0.1f,
            });

            PocketPostRollState state = h.Update(1.0f);

            Assert.AreEqual(PocketPostRollPhase.Finished, state.phase);
            Assert.AreEqual(6f, state.position.x, Eps, "End X should match SegmentData.End.x");
            Assert.AreEqual(0f, state.position.y, Eps, "End Y should match SegmentData.End.y");
            Assert.AreEqual(1f, state.position.z, Eps, "Z should equal tableZ");
        }

        [Test]
        public void RollPath_SegmentData_WithConnectionPoints_CorrectWaypoints()
        {
            // Start=(0,0) → CP=(3,3) → End=(6,0)
            var seg = new SegmentData
            {
                Start = new Vector2(0f, 0f),
                End   = new Vector2(6f, 0f),
            };
            seg.ConnectionPoints.Add(new Vector2(3f, 3f));

            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                rollPath   = seg,
                tableZ     = 0f,
                duration   = 2f,
                ballRadius = 0.1f,
            });

            Assert.IsTrue(h.IsRunning, "Should be running with a valid SegmentData path.");

            PocketPostRollState end = h.Update(2f);
            Assert.AreEqual(PocketPostRollPhase.Finished, end.phase);
            Assert.AreEqual(6f, end.position.x, Eps);
        }

        [Test]
        public void RollPath_OverridesPathPoints_WhenBothSet()
        {
            // rollPath leads to x=8; pathPoints leads to x=20.  rollPath wins.
            var seg = new SegmentData
            {
                Start = new Vector2(0f, 0f),
                End   = new Vector2(8f, 0f),
            };

            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                rollPath   = seg,
                tableZ     = 0f,
                pathPoints = new[] { Vector3.zero, new Vector3(20f, 0f, 0f) }, // should be ignored
                duration   = 1f,
                ballRadius = 0.1f,
            });

            PocketPostRollState state = h.Update(2f);

            Assert.AreEqual(8f, state.position.x, Eps, "rollPath should override pathPoints.");
        }

        // ── Rolling friction / energy loss ────────────────────────────────────

        [Test]
        public void RollingFriction_Zero_SpeedIsConstant()
        {
            // With rollingFriction = 0, angularVelocity should be the same at t=0.1 and t=0.9.
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints      = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration        = 2f,
                ballRadius      = 0.5f,
                rollingFriction = 0f,
            });

            PocketPostRollState s1 = h.Evaluate(0.1f);
            PocketPostRollState s2 = h.Evaluate(0.9f);

            Assert.AreEqual(s1.angularVelocity.y, s2.angularVelocity.y, Eps,
                "Zero friction should produce constant angular velocity.");
        }

        [Test]
        public void RollingFriction_Positive_SpeedDecaysAlongPath()
        {
            // With rollingFriction > 0, angular velocity at t=0.9 must be less than at t=0.1.
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints      = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration        = 2f,
                ballRadius      = 0.5f,
                rollingFriction = 2f,
            });

            PocketPostRollState s1 = h.Evaluate(0.1f);
            PocketPostRollState s2 = h.Evaluate(0.9f);

            Assert.Greater(s1.angularVelocity.y, s2.angularVelocity.y,
                "Positive friction should cause angular velocity to decay toward path end.");
        }

        [Test]
        public void RollingFriction_DoesNotAffectPosition()
        {
            // rollingFriction only affects speed (angular velocity), not the position path.
            var h = MakeHelper();
            h.Start(new PocketPostRollRequest
            {
                pathPoints      = new[] { Vector3.zero, new Vector3(10f, 0f, 0f) },
                duration        = 2f,
                ballRadius      = 0.5f,
                rollingFriction = 5f,
            });

            // At t=0.5 (midpoint), position should still be at x=5 regardless of friction.
            PocketPostRollState state = h.Evaluate(0.5f);
            Assert.AreEqual(5f, state.position.x, Eps,
                "rollingFriction must not alter position on the path.");
        }

        // ── Initial spin angle ────────────────────────────────────────────────

        [Test]
        public void InitialSpinAngle_IsInValidRange()
        {
            var h = MakeHelper();
            h.Start(SimpleRequest());

            PocketPostRollState state = h.Evaluate(0.5f);

            Assert.GreaterOrEqual(state.initialSpinAngle, 0f,
                "initialSpinAngle must be >= 0");
            Assert.Less(state.initialSpinAngle, Mathf.PI * 2f,
                "initialSpinAngle must be < 2π");
        }

        [Test]
        public void InitialSpinAngle_ConsistentAcrossEvaluateCalls()
        {
            // initialSpinAngle must be the same regardless of which normalizedTime is sampled.
            var h = MakeHelper();
            h.Start(SimpleRequest());

            float angle0 = h.Evaluate(0f).initialSpinAngle;
            float angle5 = h.Evaluate(0.5f).initialSpinAngle;
            float angle1 = h.Evaluate(1f).initialSpinAngle;

            Assert.AreEqual(angle0, angle5, Eps, "initialSpinAngle must be constant at t=0 vs t=0.5");
            Assert.AreEqual(angle0, angle1, Eps, "initialSpinAngle must be constant at t=0 vs t=1");
        }

        [Test]
        public void InitialSpinAngle_IsZeroAfterReset()
        {
            var h = MakeHelper();
            h.Start(SimpleRequest());
            h.Reset();

            // After reset, if Evaluate is called on an idle helper the returned
            // angle defaults to 0 (the field's zero value).
            PocketPostRollState state = h.Evaluate(0f);
            Assert.AreEqual(0f, state.initialSpinAngle, Eps);
        }
    }
}
