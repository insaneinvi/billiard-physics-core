using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode regression tests for ball–ball oblique collision (ImpulseResolver.ResolveBallBall).
    ///
    /// Motivating bug: with a wrong approach-guard sign the normal impulse could be applied
    /// to separating balls, amplifying energy and producing an exaggerated reverse rebound on
    /// the cue ball.  The tests below verify the three key invariants for e ≤ 1:
    ///   1. Total kinetic energy must not increase after the collision.
    ///   2. The cue-ball post-collision speed must not exceed its pre-collision speed.
    ///   3. The post-collision relative normal velocity must indicate separation (not approach).
    ///
    /// These tests pass with the correct guard <c>if (v_rel_n &lt;= 0) return</c> and would
    /// fail with the inverted guard <c>if (v_rel_n &gt;= 0) return</c>.
    /// </summary>
    public class BallBallCollisionTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Sets up two touching, equal-mass balls: b stationary at the origin, a touching
        /// b on the left with the given linear velocity.  The collision normal is +X.
        /// </summary>
        private static (Ball a, Ball b) MakeTouchingPair(int vx, int vy)
        {
            var a = new Ball(0);  // id = 0 (cue ball)
            var b = new Ball(1);  // id = 1 (target ball)

            Fix64 twoR = a.Radius + b.Radius;
            a.Position       = new FixVec2(-twoR, Fix64.Zero);
            b.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            a.LinearVelocity = new FixVec2(Fix64.From(vx), Fix64.From(vy));
            b.LinearVelocity = FixVec2.Zero;
            return (a, b);
        }

        // ── Oblique collision: energy must not increase ────────────────────────────

        /// <summary>
        /// Oblique shot (e.g. 30° angle): after the impulse the total kinetic energy
        /// of both balls must not exceed the pre-collision value (e ≤ 1 → inelastic/elastic).
        /// </summary>
        [Test]
        public void ResolveBallBall_ObliqueShotEqualMass_TotalKineticEnergyDoesNotIncrease()
        {
            var (a, b) = MakeTouchingPair(vx: 300, vy: 200);  // clear normal component

            float m   = a.Mass.ToFloat();
            float ke0 = KE(m, a) + KE(m, b);

            ImpulseResolver.ResolveBallBall(a, b);

            float ke1 = KE(m, a) + KE(m, b);

            // Allow a tiny fixed-point rounding tolerance (0.1 % of initial KE).
            float eps = ke0 * 0.001f;
            Assert.IsTrue(ke1 <= ke0 + eps,
                $"KE must not increase: before={ke0:F1}, after={ke1:F1}");
        }

        /// <summary>
        /// Shallow oblique shot (mostly tangential, small normal component): the normal
        /// impulse is small but the energy constraint must still hold.
        /// </summary>
        [Test]
        public void ResolveBallBall_ShallowAngle_TotalKineticEnergyDoesNotIncrease()
        {
            var (a, b) = MakeTouchingPair(vx: 50, vy: 400);

            float m   = a.Mass.ToFloat();
            float ke0 = KE(m, a) + KE(m, b);

            ImpulseResolver.ResolveBallBall(a, b);

            float ke1 = KE(m, a) + KE(m, b);
            float eps = ke0 * 0.001f;
            Assert.IsTrue(ke1 <= ke0 + eps,
                $"Shallow-angle KE must not increase: before={ke0:F1}, after={ke1:F1}");
        }

        // ── Oblique collision: cue-ball speed must not increase ────────────────────

        /// <summary>
        /// After an oblique shot the cue ball must not exit with a higher speed than it
        /// had before (energy cannot be created).  A small tolerance covers fixed-point
        /// rounding in the sqrt approximation.
        /// </summary>
        [Test]
        public void ResolveBallBall_ObliqueShotEqualMass_CueBallSpeedDoesNotIncrease()
        {
            var (a, b) = MakeTouchingPair(vx: 300, vy: 200);

            float speed0 = a.LinearVelocity.Magnitude.ToFloat();

            ImpulseResolver.ResolveBallBall(a, b);

            float speed1 = a.LinearVelocity.Magnitude.ToFloat();

            // Allow 0.5 unit tolerance for fixed-point sqrt rounding.
            Assert.IsTrue(speed1 <= speed0 + 0.5f,
                $"Cue-ball speed must not increase: before={speed0:F3}, after={speed1:F3}");
        }

        /// <summary>
        /// 45-degree oblique shot: cue-ball speed must not increase.
        /// </summary>
        [Test]
        public void ResolveBallBall_FortyFiveDegreeShot_CueBallSpeedDoesNotIncrease()
        {
            var (a, b) = MakeTouchingPair(vx: 300, vy: 300);

            float speed0 = a.LinearVelocity.Magnitude.ToFloat();

            ImpulseResolver.ResolveBallBall(a, b);

            float speed1 = a.LinearVelocity.Magnitude.ToFloat();
            Assert.IsTrue(speed1 <= speed0 + 0.5f,
                $"Cue-ball speed must not increase at 45°: before={speed0:F3}, after={speed1:F3}");
        }

        // ── Post-collision separation condition ────────────────────────────────────

        /// <summary>
        /// After the impulse the relative normal velocity along n (from a to b) must be
        /// ≤ 0, i.e. the balls must be moving apart (or at most just touching), not still
        /// approaching.  A wrong approach-guard sign would leave the balls continuing to
        /// approach after impulse resolution.
        /// </summary>
        [Test]
        public void ResolveBallBall_ObliqueShotEqualMass_PostCollisionRelativeNormalVelocitySeparating()
        {
            var (a, b) = MakeTouchingPair(vx: 300, vy: 200);

            ImpulseResolver.ResolveBallBall(a, b);

            // n still points from a to b after positional correction.
            FixVec2 n      = (b.Position - a.Position).Normalized;
            FixVec2 v_rel  = a.LinearVelocity - b.LinearVelocity;
            float vRelN    = FixVec2.Dot(v_rel, n).ToFloat();

            // Allow a small tolerance for fixed-point rounding.
            Assert.IsTrue(vRelN <= 0.5f,
                $"Balls must be separating after impulse: v_rel·n = {vRelN:F4} (expected ≤ 0)");
        }

        // ── Separating balls: impulse must NOT be applied ─────────────────────────

        /// <summary>
        /// When ball a is already moving away from ball b (negative normal component),
        /// ResolveBallBall must apply no impulse and leave all velocities unchanged.
        /// This would fail with an inverted guard (<c>v_rel_n &gt;= 0</c> → would apply
        /// impulse and exaggerate the separation).
        /// </summary>
        [Test]
        public void ResolveBallBall_SeparatingBalls_VelocitiesUnchanged()
        {
            var (a, b) = MakeTouchingPair(vx: -300, vy: 200);  // moving AWAY from b

            float vax0 = a.LinearVelocity.X.ToFloat();
            float vay0 = a.LinearVelocity.Y.ToFloat();

            ImpulseResolver.ResolveBallBall(a, b);

            Assert.AreEqual(vax0, a.LinearVelocity.X.ToFloat(), 0.001f,
                "Separating ball-a Vx must be unchanged.");
            Assert.AreEqual(vay0, a.LinearVelocity.Y.ToFloat(), 0.001f,
                "Separating ball-a Vy must be unchanged.");
            Assert.AreEqual(0f, b.LinearVelocity.X.ToFloat(), 0.001f,
                "Separating ball-b must remain stationary.");
        }

        // ── Tangential velocity preservation ─────────────────────────────────────

        /// <summary>
        /// The tangential (perpendicular-to-normal) velocity of the cue ball must be
        /// largely preserved after collision.  With μ = 0.2 and a moderate normal
        /// component the Coulomb friction clamp removes at most ~20 % of the tangential
        /// impulse, but there is additional attenuation through the denominator and
        /// angular terms.  We conservatively require ≥ 75 % preservation.
        /// </summary>
        [Test]
        public void ResolveBallBall_ObliqueShotEqualMass_TangentialVelocityMostlyPreserved()
        {
            var (a, b) = MakeTouchingPair(vx: 200, vy: 300);

            // Collision normal is +X, so tangential direction is +Y.
            float vay0 = a.LinearVelocity.Y.ToFloat();  // initial tangential speed of a

            ImpulseResolver.ResolveBallBall(a, b);

            float vay1 = a.LinearVelocity.Y.ToFloat();

            // After collision ball-a's y-component should be at least 75 % of original.
            Assert.IsTrue(vay1 >= vay0 * 0.75f,
                $"Tangential velocity must be mostly preserved: before={vay0:F2}, after={vay1:F2}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static float KE(float mass, Ball ball)
        {
            float vx = ball.LinearVelocity.X.ToFloat();
            float vy = ball.LinearVelocity.Y.ToFloat();
            return 0.5f * mass * (vx * vx + vy * vy);
        }
    }
}
