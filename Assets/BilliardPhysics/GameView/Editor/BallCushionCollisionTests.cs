using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode regression tests for ball–cushion collision with pre-collision pure-rolling state.
    ///
    /// Bug: after a cushion bounce the residual AngularVelocity.X/Y (which was aligned with
    /// the pre-bounce velocity) is now misaligned with the reflected LinearVelocity.  The
    /// resulting slip drives sliding-friction torque over the next several simulation steps,
    /// bending the post-bounce trajectory ("walking arc").
    ///
    /// Fix: when the ball is in pure-rolling state immediately before the cushion collision
    /// (slip ≤ MotionSimulator.Epsilon), PhysicsWorld2D.Step re-projects AngularVelocity.X/Y
    /// after impulse resolution:
    ///     ω.Y =  v.X / R
    ///     ω.X = −v.Y / R
    /// This restores the rolling constraint instantly, eliminating the post-bounce arc.
    /// </summary>
    public class BallCushionCollisionTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the contact-point tangential slip magnitude for the given ball
        /// under the Z-up rolling convention used by MotionSimulator.
        /// slip = |(v.X − ω.Y·R,  v.Y + ω.X·R)|
        /// </summary>
        private static float ComputeSlip(Ball ball)
        {
            Fix64 R   = ball.Radius;
            Fix64 vtX = ball.LinearVelocity.X - ball.AngularVelocity.Y * R;
            Fix64 vtY = ball.LinearVelocity.Y + ball.AngularVelocity.X * R;
            return Fix64.Sqrt(vtX * vtX + vtY * vtY).ToFloat();
        }

        /// <summary>
        /// Creates a vertical cushion segment at x = <paramref name="wallX"/> with outward
        /// normal (−1, 0) — a right-side wall.  The segment runs from y = −500 to y = +500.
        /// </summary>
        private static Segment MakeRightWall(float wallX)
        {
            return new Segment(
                new FixVec2(Fix64.FromFloat(wallX), Fix64.From(-500)),
                new FixVec2(Fix64.FromFloat(wallX), Fix64.From( 500)));
        }

        /// <summary>
        /// Creates a ball in pure rolling state moving to the right at
        /// <paramref name="speedX"/> mm/s.
        /// </summary>
        private static Ball MakePureRollingBall(float speedX)
        {
            var ball = new Ball(0);
            Fix64 v = Fix64.FromFloat(speedX);
            ball.LinearVelocity    = new FixVec2(v, Fix64.Zero);
            // Rolling constraint: ω.Y = v.X / R,  ω.X = −v.Y / R
            ball.AngularVelocity.Y =  v / ball.Radius;
            ball.AngularVelocity.X = Fix64.Zero;
            ball.AngularVelocity.Z = Fix64.Zero;
            return ball;
        }

        // ── Pure-rolling before collision ─────────────────────────────────────────

        /// <summary>
        /// A ball in pure-rolling state that bounces off a cushion must have near-zero
        /// contact-point slip immediately after the bounce (the rolling constraint must
        /// be satisfied for the new reflected velocity).
        ///
        /// Without the fix, ω.Y retains its pre-bounce sign while v.X reverses, so
        /// slip ≈ 2·|v.X| / R after the bounce — far above Epsilon.
        /// </summary>
        [Test]
        public void Step_PureRollingBallHitsCushion_SlipNearZeroAfterBounce()
        {
            var world = new PhysicsWorld2D();

            Ball ball = MakePureRollingBall(400f);
            Fix64 R   = ball.Radius;

            // Place ball at origin; wall just 5 mm ahead (well within one step at 400 mm/s).
            ball.Position = FixVec2.Zero;
            Fix64 wallX   = R + Fix64.From(5);
            world.AddBall(ball);
            world.AddSegment(MakeRightWall(wallX.ToFloat()));

            // Verify pre-collision pure rolling.
            float slipBefore = ComputeSlip(ball);
            Assert.IsTrue(slipBefore <= MotionSimulator.Epsilon.ToFloat() + 1e-4f,
                $"Pre-collision slip must be near zero, was {slipBefore:F6}");

            world.Step();

            // After the bounce the ball must be moving left (reflected).
            Assert.IsTrue(ball.LinearVelocity.X.ToFloat() < 0f,
                "Ball must rebound (negative X velocity) after hitting the right wall.");

            // Slip must remain near zero (rolling constraint re-projected).
            float slipAfter = ComputeSlip(ball);
            Assert.IsTrue(slipAfter <= MotionSimulator.Epsilon.ToFloat() + 1e-3f,
                $"Post-bounce slip must be near zero (rolling constraint), was {slipAfter:F6}");
        }

        /// <summary>
        /// After a pure-rolling cushion bounce, running several MotionSimulator.Step calls
        /// must NOT bend the trajectory (post-bounce direction must remain constant).
        ///
        /// Without the fix the misaligned ω.X/Y drives a sliding-friction arc; the velocity
        /// direction drifts away from the initial reflected direction within ~10 steps.
        /// </summary>
        [Test]
        public void Step_PureRollingBallAfterBounce_DirectionStableAcrossMultipleSteps()
        {
            var world = new PhysicsWorld2D();

            Ball ball = MakePureRollingBall(600f);
            Fix64 R   = ball.Radius;

            ball.Position = FixVec2.Zero;
            Fix64 wallX   = R + Fix64.From(5);
            world.AddBall(ball);
            world.AddSegment(MakeRightWall(wallX.ToFloat()));

            // Bounce.
            world.Step();

            Assert.IsTrue(ball.LinearVelocity.X.ToFloat() < 0f,
                "Ball must rebound before direction-stability check.");

            // Record direction immediately after bounce.
            float dx0 = ball.LinearVelocity.X.ToFloat();
            float dy0 = ball.LinearVelocity.Y.ToFloat();
            float mag0 = (float)System.Math.Sqrt(dx0 * dx0 + dy0 * dy0);

            if (mag0 < 1f) return;  // ball almost stopped – direction test not meaningful

            Fix64 dt = Fix64.One / Fix64.From(60);
            for (int i = 0; i < 20; i++)
                MotionSimulator.Step(ball, dt);

            float dx1 = ball.LinearVelocity.X.ToFloat();
            float dy1 = ball.LinearVelocity.Y.ToFloat();
            float mag1 = (float)System.Math.Sqrt(dx1 * dx1 + dy1 * dy1);

            if (mag1 < 1f) return;  // ball stopped during deceleration – OK

            // Cosine of angle between pre- and post-step velocity vectors must be close to 1
            // (direction must not have drifted).
            float cosAngle = (dx0 * dx1 + dy0 * dy1) / (mag0 * mag1);
            Assert.IsTrue(cosAngle > 0.999f,
                $"Post-bounce direction must not bend; cosine of drift angle = {cosAngle:F6} (expected > 0.999)");
        }

        // ── Sliding before collision: existing behaviour must be preserved ─────────

        /// <summary>
        /// When the ball is sliding (slip > Epsilon) before the cushion collision the
        /// projection must NOT be applied.  The ball's pre-bounce ω.X/Y must be
        /// forwarded to ResolveBallCushion unchanged (existing behaviour).
        ///
        /// We verify this by confirming that the post-bounce slip is non-zero when the
        /// ball was clearly sliding before impact.
        /// </summary>
        [Test]
        public void Step_SlidingBallHitsCushion_AngularVelocityNotProjected()
        {
            var world = new PhysicsWorld2D();

            var ball = new Ball(0);
            Fix64 R  = ball.Radius;

            // Ball moving right but with zero angular velocity → large slip.
            ball.LinearVelocity    = new FixVec2(Fix64.From(400), Fix64.Zero);
            ball.AngularVelocity.Y = Fix64.Zero;   // ← mismatched: slip = v.X = 400
            ball.AngularVelocity.X = Fix64.Zero;
            ball.AngularVelocity.Z = Fix64.Zero;
            ball.Position          = FixVec2.Zero;

            // Confirm pre-collision sliding state.
            float slipBefore = ComputeSlip(ball);
            Assert.IsTrue(slipBefore > MotionSimulator.Epsilon.ToFloat(),
                $"Pre-collision slip must be above Epsilon for this test to be valid, was {slipBefore:F4}");

            Fix64 wallX = R + Fix64.From(5);
            world.AddBall(ball);
            world.AddSegment(MakeRightWall(wallX.ToFloat()));

            // Snapshot pre-bounce angular velocity to compare after step.
            float omegaYBefore = ball.AngularVelocity.Y.ToFloat();

            world.Step();

            // Ball must have bounced.
            Assert.IsTrue(ball.LinearVelocity.X.ToFloat() < 0f,
                "Sliding ball must still rebound off the cushion.");

            // Post-bounce ω.Y must NOT be equal to v.X_new / R (the projection was not applied).
            // Instead it should reflect the impulse-modified value (not the pure-rolling target).
            float vxAfter     = ball.LinearVelocity.X.ToFloat();
            float omegaYAfter = ball.AngularVelocity.Y.ToFloat();
            float pureRollingTarget = vxAfter / R.ToFloat();

            // The projected value would equal pureRollingTarget; we expect them to differ
            // because the projection must not have been applied.
            Assert.IsFalse(
                System.Math.Abs(omegaYAfter - pureRollingTarget) < 0.01f,
                $"Sliding ball: ω.Y must not be projected to pure-rolling target " +
                $"(ω.Y = {omegaYAfter:F4}, target = {pureRollingTarget:F4})");
        }
    }
}
