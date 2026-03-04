using NUnit.Framework;
using UnityEngine;
using BilliardPhysics;
using BilliardPhysics.AniHelp;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="PocketDropAniHelper"/>.
    /// </summary>
    public class PocketDropAniHelperTests
    {
        private const float Eps = 1e-4f;

        // ── Helpers ───────────────────────────────────────────────────────────

        private static PocketDropAniHelper MakeHelper() => new PocketDropAniHelper();

        private static PocketDropRequest SimpleRequest(
            Vector3 startPos  = default,
            Vector3 pocketPos = default,
            float   duration  = 0.5f)
            => new PocketDropRequest
            {
                startPos  = startPos,
                pocketPos = pocketPos,
                duration  = duration,
            };

        // ── Basic lifecycle ───────────────────────────────────────────────────

        [Test]
        public void StartDrop_IsRunning_True()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest());
            Assert.IsTrue(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Update_FullDuration_ReturnsFinished()
        {
            var h   = MakeHelper();
            var req = SimpleRequest(duration: 0.5f);
            h.StartDrop(in req);

            PocketDropState state = h.Update(0.5f);

            Assert.AreEqual(PocketDropPhase.Finished, state.phase);
            Assert.AreEqual(1f, state.normalizedTime, Eps);
            Assert.IsFalse(h.IsRunning);
            Assert.IsTrue(h.IsFinished);
        }

        [Test]
        public void Evaluate_AtStart_PhaseIsAttract()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest(
                startPos:  new Vector3(0f, 0f, 0f),
                pocketPos: new Vector3(1f, 0f, 0f)));

            PocketDropState state = h.Evaluate(0f);

            Assert.AreEqual(PocketDropPhase.Attract, state.phase);
            Assert.AreEqual(0f, state.normalizedTime, Eps);
        }

        [Test]
        public void Evaluate_AtOne_PhaseIsFinished()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest());

            PocketDropState state = h.Evaluate(1f);

            Assert.AreEqual(PocketDropPhase.Finished, state.phase);
            Assert.AreEqual(0f, state.scale, Eps);
            Assert.AreEqual(0f, state.alpha, Eps);
        }

        [Test]
        public void Reset_ClearsState()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest());
            h.Update(0.3f);
            h.Reset();

            Assert.IsFalse(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Stop_InterruptsAnimation()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest());
            h.Update(0.1f);
            h.Stop();

            Assert.IsFalse(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        // ── Ball parameter – position override ────────────────────────────────

        [Test]
        public void BallProvided_StartPos_UsedFromBallPosition()
        {
            // Ball.Position is (3, 4); startPos.z = 2.
            // The helper should use (3, 4, 2) as the internal start position.
            var ball = new Ball(1);
            ball.Position = new FixVec2(Fix64.FromFloat(3f, 6), Fix64.FromFloat(4f, 6));

            var req = new PocketDropRequest
            {
                ball      = ball,
                startPos  = new Vector3(0f, 0f, 2f),   // XY ignored; Z kept
                pocketPos = new Vector3(3f, 4f, 2f),    // pocket at same XY as ball
                duration  = 1f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            // At t=0 the ball should be at ball.Position (XY) with Z from startPos.
            PocketDropState state = h.Evaluate(0f);
            Assert.AreEqual(3f, state.position.x, 1e-3f, "X should come from Ball.Position");
            Assert.AreEqual(4f, state.position.y, 1e-3f, "Y should come from Ball.Position");
            Assert.AreEqual(2f, state.position.z, Eps,   "Z should come from startPos.z");
        }

        [Test]
        public void BallNull_StartPos_UsedFromRequest()
        {
            // When ball is null, startPos should be used as-is.
            var req = new PocketDropRequest
            {
                ball      = null,
                startPos  = new Vector3(5f, 6f, 1f),
                pocketPos = new Vector3(10f, 6f, 1f),
                duration  = 1f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            PocketDropState state = h.Evaluate(0f);
            Assert.AreEqual(5f, state.position.x, 1e-3f);
            Assert.AreEqual(6f, state.position.y, 1e-3f);
            Assert.AreEqual(1f, state.position.z, Eps);
        }

        // ── Ball parameter – angular velocity ─────────────────────────────────

        [Test]
        public void BallProvided_AngularVelocity_AtStart_MatchesBall()
        {
            // Ball angular velocity (1, 2, 3) should be output at t=0 of Attract phase.
            var ball = new Ball(1);
            ball.AngularVelocity = new FixVec3(
                Fix64.FromFloat(1f, 6),
                Fix64.FromFloat(2f, 6),
                Fix64.FromFloat(3f, 6));

            var req = new PocketDropRequest
            {
                ball      = ball,
                startPos  = new Vector3(0f, 0f, 0f),
                pocketPos = new Vector3(5f, 0f, 0f),
                duration  = 1f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            // t=0: eased=0, so angularVelocity == _startAngularVelocity
            PocketDropState state = h.Evaluate(0f);
            Assert.AreEqual(PocketDropPhase.Attract, state.phase);
            Assert.AreEqual(1f, state.angularVelocity.x, 1e-3f);
            Assert.AreEqual(2f, state.angularVelocity.y, 1e-3f);
            Assert.AreEqual(3f, state.angularVelocity.z, 1e-3f);
        }

        [Test]
        public void BallNull_AngularVelocity_IsZeroThroughout()
        {
            // Without a ball, angular velocity should always be zero.
            var req = SimpleRequest(
                startPos:  new Vector3(0f, 0f, 0f),
                pocketPos: new Vector3(5f, 0f, 0f));

            var h = MakeHelper();
            h.StartDrop(in req);

            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                PocketDropState state = h.Evaluate(t);
                Assert.AreEqual(Vector3.zero, state.angularVelocity,
                    $"angularVelocity must be zero at t={t} when no ball is supplied");
            }
        }

        [Test]
        public void AngularVelocity_DecaysToZero_AtAttractEnd()
        {
            // Angular velocity must reach zero at the end of the Attract phase.
            var ball = new Ball(1);
            ball.AngularVelocity = new FixVec3(
                Fix64.FromFloat(5f, 6),
                Fix64.FromFloat(0f, 6),
                Fix64.FromFloat(0f, 6));

            var req = new PocketDropRequest
            {
                ball         = ball,
                startPos     = new Vector3(0f, 0f, 0f),
                pocketPos    = new Vector3(5f, 0f, 0f),
                duration     = 1f,
                attractRatio = 0.3f,
                sinkRatio    = 0.5f,
                vanishRatio  = 0.2f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            // Evaluate at the exact end of the Attract phase (t == attractRatio after normalisation)
            PocketDropState state = h.Evaluate(0.3f);
            Assert.AreEqual(PocketDropPhase.Sink, state.phase,
                "t=0.3 should be in Sink phase (Attract ends at 0.3)");
            Assert.AreEqual(0f, state.angularVelocity.x, Eps,
                "Angular velocity must be zero in Sink phase");
        }

        [Test]
        public void AngularVelocity_IsZero_InSinkPhase()
        {
            var ball = new Ball(1);
            ball.AngularVelocity = new FixVec3(
                Fix64.FromFloat(10f, 6), Fix64.Zero, Fix64.Zero);

            var req = new PocketDropRequest
            {
                ball         = ball,
                startPos     = new Vector3(0f, 0f, 0f),
                pocketPos    = new Vector3(5f, 0f, 0f),
                duration     = 1f,
                attractRatio = 0.30f,
                sinkRatio    = 0.50f,
                vanishRatio  = 0.20f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            // Midpoint of Sink phase
            PocketDropState state = h.Evaluate(0.55f);
            Assert.AreEqual(PocketDropPhase.Sink, state.phase);
            Assert.AreEqual(0f, state.angularVelocity.x, Eps);
            Assert.AreEqual(0f, state.angularVelocity.y, Eps);
            Assert.AreEqual(0f, state.angularVelocity.z, Eps);
        }

        [Test]
        public void AngularVelocity_IsZero_InVanishPhase()
        {
            var ball = new Ball(1);
            ball.AngularVelocity = new FixVec3(
                Fix64.FromFloat(10f, 6), Fix64.Zero, Fix64.Zero);

            var req = new PocketDropRequest
            {
                ball         = ball,
                startPos     = new Vector3(0f, 0f, 0f),
                pocketPos    = new Vector3(5f, 0f, 0f),
                duration     = 1f,
                attractRatio = 0.30f,
                sinkRatio    = 0.50f,
                vanishRatio  = 0.20f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            PocketDropState state = h.Evaluate(0.95f);
            Assert.AreEqual(PocketDropPhase.Vanish, state.phase);
            Assert.AreEqual(Vector3.zero, state.angularVelocity);
        }

        [Test]
        public void AngularVelocity_IsZero_WhenFinished()
        {
            var ball = new Ball(1);
            ball.AngularVelocity = new FixVec3(
                Fix64.FromFloat(10f, 6), Fix64.Zero, Fix64.Zero);

            var req = new PocketDropRequest
            {
                ball      = ball,
                startPos  = new Vector3(0f, 0f, 0f),
                pocketPos = new Vector3(5f, 0f, 0f),
                duration  = 1f,
            };

            var h = MakeHelper();
            h.StartDrop(in req);

            PocketDropState state = h.Update(2f); // overshoot → Finished
            Assert.AreEqual(PocketDropPhase.Finished, state.phase);
            Assert.AreEqual(Vector3.zero, state.angularVelocity);
        }

        // ── Scale and alpha ───────────────────────────────────────────────────

        [Test]
        public void Scale_IsOne_InAttractAndSinkPhases()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest(pocketPos: new Vector3(5f, 0f, 0f), duration: 1f));

            // Sample Attract and Sink phases
            foreach (float t in new[] { 0.1f, 0.2f, 0.4f, 0.6f })
            {
                var state = h.Evaluate(t);
                if (state.phase == PocketDropPhase.Attract || state.phase == PocketDropPhase.Sink)
                    Assert.AreEqual(1f, state.scale, Eps, $"Scale must be 1 in {state.phase} at t={t}");
            }
        }

        [Test]
        public void Scale_DecreasesToZero_InVanishPhase()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest(pocketPos: new Vector3(5f, 0f, 0f), duration: 1f));

            PocketDropState state = h.Evaluate(1f);
            Assert.AreEqual(0f, state.scale, Eps);
        }

        // ── Reuse ─────────────────────────────────────────────────────────────

        [Test]
        public void Reuse_StartCalledTwice_SecondRunIndependentOfFirst()
        {
            var h = MakeHelper();
            h.StartDrop(SimpleRequest(duration: 0.5f));
            h.Update(0.5f); // finish first run

            // Second run with different params
            h.StartDrop(new PocketDropRequest
            {
                startPos  = new Vector3(1f, 2f, 0f),
                pocketPos = new Vector3(3f, 2f, 0f),
                duration  = 1f,
            });

            Assert.IsTrue(h.IsRunning);
            PocketDropState state = h.Evaluate(0f);
            Assert.AreEqual(1f, state.position.x, Eps);
        }
    }
}
