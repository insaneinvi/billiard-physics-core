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

        private static Ball MakeBall(float x = 1f, float y = 2f, float vx = 0f, float vy = 0f, float spinZ = 0f)
        {
            var b = new Ball(1);
            b.Position       = new FixVec2(Fix64.FromFloat(x, 6), Fix64.FromFloat(y, 6));
            b.LinearVelocity = new FixVec2(Fix64.FromFloat(vx, 6), Fix64.FromFloat(vy, 6));
            b.AngularVelocity = new FixVec3(Fix64.Zero, Fix64.Zero, Fix64.FromFloat(spinZ, 6));
            return b;
        }

        // ── Legacy path: no Ball (backward-compatible) ────────────────────────

        [Test]
        public void StartDrop_Noball_IsRunning()
        {
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos       = Vector3.zero,
                pocketPos      = new Vector3(1f, 0f, 0f),
                duration       = 0.5f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });
            Assert.IsTrue(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Update_FullDuration_ReturnsFinished()
        {
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos       = Vector3.zero,
                pocketPos      = new Vector3(1f, 0f, 0f),
                duration       = 1f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });

            PocketDropState state = h.Update(1.0f);

            Assert.AreEqual(PocketDropPhase.Finished, state.phase);
            Assert.AreEqual(1f, state.normalizedTime, Eps);
            Assert.IsFalse(h.IsRunning);
            Assert.IsTrue(h.IsFinished);
        }

        // ── Ball integration ──────────────────────────────────────────────────

        [Test]
        public void StartDrop_WithBall_UsesPhysicsPositionAsStartPos()
        {
            float ballX = 3f, ballY = 4f, tableZ = 0.5f;
            var ball = MakeBall(ballX, ballY);

            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                ball           = ball,
                tableZ         = tableZ,
                pocketPos      = new Vector3(ballX, ballY, tableZ),
                duration       = 0.5f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });

            // At t=0 the position must equal the derived start position.
            PocketDropState state = h.Evaluate(0f);

            Assert.AreEqual(ballX,   state.position.x, 1e-3f, "X should match ball.Position.X");
            Assert.AreEqual(ballY,   state.position.y, 1e-3f, "Y should match ball.Position.Y");
            Assert.AreEqual(tableZ,  state.position.z, 1e-3f, "Z should equal tableZ");
        }

        [Test]
        public void StartDrop_WithBall_IsRunning()
        {
            var ball = MakeBall(1f, 2f);
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                ball           = ball,
                tableZ         = 0f,
                pocketPos      = new Vector3(0f, 0f, 0f),
                duration       = 0.5f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });
            Assert.IsTrue(h.IsRunning);
        }

        [Test]
        public void StartDrop_WithBall_FinishesCorrectly()
        {
            var ball = MakeBall(1f, 2f, vx: 3f, vy: 0f);
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                ball           = ball,
                tableZ         = 0f,
                pocketPos      = new Vector3(0f, 0f, 0f),
                duration       = 0.3f,
                sinkDepth      = 0.15f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });

            PocketDropState state = h.Update(1f);

            Assert.AreEqual(PocketDropPhase.Finished, state.phase);
            Assert.AreEqual(0f, state.scale, Eps);
            Assert.AreEqual(0f, state.alpha, Eps);
        }

        [Test]
        public void StartDrop_BallOverridesStartPos_WhenBothSet()
        {
            // Even if startPos is set, ball.Position takes precedence.
            float ballX = 5f, ballY = 6f;
            var ball = MakeBall(ballX, ballY);

            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                ball           = ball,
                tableZ         = 0f,
                startPos       = new Vector3(99f, 99f, 99f), // should be ignored
                pocketPos      = new Vector3(ballX, ballY, 0f),
                duration       = 0.5f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });

            PocketDropState state = h.Evaluate(0f);

            Assert.AreEqual(ballX, state.position.x, 1e-3f, "Ball position X should override startPos.x");
            Assert.AreEqual(ballY, state.position.y, 1e-3f, "Ball position Y should override startPos.y");
        }

        // ── Conical-pocket spiral (sinkSpin) ──────────────────────────────────

        [Test]
        public void SinkPhase_WithSinkSpin_HasLateralOffset()
        {
            // With sinkSpin > 0 the position during Sink must deviate from the
            // straight vertical drop axis.
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos        = new Vector3(0f, 0f, 0f),
                pocketPos       = new Vector3(0f, 0f, 0f),
                duration        = 1f,
                sinkDepth       = 1f,
                attractRatio    = 0.01f,
                sinkRatio       = 0.98f,
                vanishRatio     = 0.01f,
                attractStrength = 0.01f,
                sinkSpin        = 0.5f,
            });

            // Sample mid-sink (normalizedTime ~ 0.5, well inside the Sink phase)
            PocketDropState state = h.Evaluate(0.5f);

            Assert.AreEqual(PocketDropPhase.Sink, state.phase);
            float lateralOffset = Mathf.Sqrt(state.position.x * state.position.x
                                           + state.position.y * state.position.y);
            Assert.Greater(lateralOffset, 0f, "sinkSpin > 0 should produce a non-zero lateral offset.");
        }

        [Test]
        public void SinkPhase_ZeroSinkSpin_NoLateralOffset()
        {
            // With sinkSpin = 0 and no ball, the position during Sink must lie on
            // the straight axis (X and Y at start values, only Z varies).
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos        = new Vector3(0f, 0f, 0f),
                pocketPos       = new Vector3(0f, 0f, 0f),
                duration        = 1f,
                sinkDepth       = 1f,
                attractRatio    = 0.01f,
                sinkRatio       = 0.98f,
                vanishRatio     = 0.01f,
                attractStrength = 0.01f,
                sinkSpin        = 0f,
            });

            PocketDropState state = h.Evaluate(0.5f);

            Assert.AreEqual(PocketDropPhase.Sink, state.phase);
            Assert.AreEqual(0f, state.position.x, Eps, "X should be zero (no spiral)");
            Assert.AreEqual(0f, state.position.y, Eps, "Y should be zero (no spiral)");
        }

        [Test]
        public void SinkSpin_DerivedFromBallAngularVelocity_WhenSinkSpinIsZero()
        {
            // When sinkSpin == 0 and ball.AngularVelocity.Z != 0, an automatic
            // spiral should be derived, producing a non-zero lateral offset.
            var ball = MakeBall(0f, 0f, spinZ: 20f);

            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                ball            = ball,
                tableZ          = 0f,
                pocketPos       = new Vector3(0f, 0f, 0f),
                duration        = 1f,
                sinkDepth       = 1f,
                attractRatio    = 0.01f,
                sinkRatio       = 0.98f,
                vanishRatio     = 0.01f,
                attractStrength = 0.01f,
                sinkSpin        = 0f, // rely on auto-derivation
            });

            PocketDropState state = h.Evaluate(0.5f);

            Assert.AreEqual(PocketDropPhase.Sink, state.phase);
            float lateralOffset = Mathf.Sqrt(state.position.x * state.position.x
                                           + state.position.y * state.position.y);
            Assert.Greater(lateralOffset, 0f,
                "Large AngularVelocity.Z should produce a lateral spiral offset.");
        }

        // ── Phase structure ───────────────────────────────────────────────────

        [Test]
        public void Evaluate_AtZero_IsAttractPhase()
        {
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos       = new Vector3(0f, 0f, 0f),
                pocketPos      = new Vector3(1f, 0f, 0f),
                duration       = 1f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });

            PocketDropState state = h.Evaluate(0f);
            Assert.AreEqual(PocketDropPhase.Attract, state.phase);
        }

        [Test]
        public void Evaluate_AtOne_IsFinished_ScaleAndAlphaZero()
        {
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos       = Vector3.zero,
                pocketPos      = Vector3.zero,
                duration       = 1f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.1f,
            });

            PocketDropState state = h.Evaluate(1f);

            Assert.AreEqual(PocketDropPhase.Finished, state.phase);
            Assert.AreEqual(0f, state.scale, Eps);
            Assert.AreEqual(0f, state.alpha, Eps);
        }

        // ── Reset ─────────────────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsState()
        {
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos       = Vector3.zero,
                pocketPos      = Vector3.zero,
                duration       = 1f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });
            h.Update(0.5f);
            h.Reset();

            Assert.IsFalse(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }

        [Test]
        public void Stop_InterruptsAnimation()
        {
            var h = MakeHelper();
            h.StartDrop(new PocketDropRequest
            {
                startPos       = Vector3.zero,
                pocketPos      = Vector3.zero,
                duration       = 1f,
                sinkDepth      = 0.1f,
                attractRatio   = 0.3f,
                sinkRatio      = 0.5f,
                vanishRatio    = 0.2f,
                attractStrength = 0.25f,
            });
            h.Update(0.3f);
            h.Stop();

            Assert.IsFalse(h.IsRunning);
            Assert.IsFalse(h.IsFinished);
        }
    }
}
