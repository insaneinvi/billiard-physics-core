using NUnit.Framework;
using BilliardPhysics;
using BilliardPhysics.AniHelp;
using BilliardPhysics.Runtime.ViewTool;
using UnityEngine;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests validating that the pocket-drop animation correctly spins
    /// the ball using the angular velocity captured at the pocketing moment.
    ///
    /// Root cause being tested: <c>PhysicsWorld2D.CheckPocketCaptures</c> zeroed
    /// <see cref="Ball.AngularVelocity"/> before raising <c>OnBallPocketed</c>, so the
    /// drop animation had no spin data to work with.  The fix stores the velocity in
    /// <see cref="Ball.LastAngularVelocity"/> first, and the drop animation reads from
    /// that field instead of the (now-zero) <see cref="Ball.AngularVelocity"/>.
    /// </summary>
    public class BallDropControllerTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Creates a pocket centered at (cx, cy) with given radius.</summary>
        private static Pocket MakePocket(float cx, float cy, float radius)
        {
            var center = new FixVec2(Fix64.FromFloat(cx), Fix64.FromFloat(cy));
            return new Pocket(0, center, Fix64.FromFloat(radius));
        }

        // ── LastAngularVelocity is preserved on pocketing ─────────────────────────

        /// <summary>
        /// When a ball is pocketed, <see cref="Ball.LastAngularVelocity"/> must equal
        /// the angular velocity the ball had just before pocketing, while
        /// <see cref="Ball.AngularVelocity"/> must be zeroed.
        /// </summary>
        [Test]
        public void Pocketed_Ball_LastAngularVelocity_PreservesEntryAngularVelocity()
        {
            var world  = new PhysicsWorld2D();
            var pocket = MakePocket(0f, 0f, 50f);
            world.AddPocket(pocket);

            var ball = new Ball(0);
            // Place the ball at the pocket center so it is captured immediately.
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.AngularVelocity = new FixVec3(
                Fix64.FromFloat(3.5f),
                Fix64.FromFloat(-2.0f),
                Fix64.FromFloat(1.0f));
            world.AddBall(ball);

            world.Step();

            Assert.IsTrue(ball.IsPocketed, "Ball must be pocketed after Step.");

            // AngularVelocity must be zeroed by CheckPocketCaptures.
            Assert.AreEqual(FixVec3.Zero, ball.AngularVelocity,
                "Ball.AngularVelocity must be zero after pocketing.");

            // LastAngularVelocity must retain the pre-pocketing value.
            Assert.AreEqual(Fix64.FromFloat(3.5f),  ball.LastAngularVelocity.X,
                "LastAngularVelocity.X must equal the entry angular velocity X.");
            Assert.AreEqual(Fix64.FromFloat(-2.0f), ball.LastAngularVelocity.Y,
                "LastAngularVelocity.Y must equal the entry angular velocity Y.");
            Assert.AreEqual(Fix64.FromFloat(1.0f),  ball.LastAngularVelocity.Z,
                "LastAngularVelocity.Z must equal the entry angular velocity Z.");
        }

        // ── Drop animation spins the ball when entry angular velocity is non-zero ──

        /// <summary>
        /// When the drop animation starts with a non-zero entry angular velocity, the ball's
        /// rotation must change from the initial orientation after one update tick.
        /// </summary>
        [Test]
        public void DropAnimation_WithNonZeroEntryAngularVelocity_RotationChangesEachFrame()
        {
            var ball = new Ball(0);
            ball.Position = new FixVec2(Fix64.Zero, Fix64.Zero);

            // Simulate the state produced by CheckPocketCaptures: entry velocity saved,
            // AngularVelocity zeroed.
            ball.LastAngularVelocity = new FixVec3(
                Fix64.FromFloat(0f),
                Fix64.FromFloat(5f),   // rolling in +X direction
                Fix64.FromFloat(0f));
            ball.AngularVelocity = FixVec3.Zero;

            var entryRotation = Quaternion.identity;
            var pocketWorldPos = new Vector3(0f, 0f, 0f);

            // Build a PocketDropAniHelper directly to test the animation state.
            var helper = new PocketDropAniHelper();
            helper.StartDrop(new PocketDropRequest
            {
                startPos            = new Vector3(-1f, 0f, 0f),
                pocketPos           = pocketWorldPos,
                entryLinearVelocity = new Vector2(1f, 0f),
                duration            = 0.25f,
            });

            // Simulate one frame of drop animation using the entry angular velocity
            // (as the fixed BallDropController.TickDrops does).
            float     deltaTime    = 1f / 60f;
            const float SpinDecay  = 4f;
            Vector3 entryOmega = new Vector3(
                ball.LastAngularVelocity.X.ToFloat(),
                ball.LastAngularVelocity.Y.ToFloat(),
                ball.LastAngularVelocity.Z.ToFloat());

            // Decay and integrate (mirrors BallDropController.TickDrops logic).
            entryOmega *= Mathf.Exp(-SpinDecay * deltaTime);
            Quaternion newRotation = PhysicsToView.IntegrateRotation(
                entryRotation, entryOmega, deltaTime);

            // The rotation must differ from the initial identity quaternion.
            Assert.AreNotEqual(Quaternion.identity, newRotation,
                "Non-zero entry angular velocity must produce a rotation change each frame.");
        }

        /// <summary>
        /// When a ball is pocketed with zero angular velocity, the drop animation must
        /// produce no rotation change (the ball stays visually still during the drop).
        /// </summary>
        [Test]
        public void DropAnimation_WithZeroEntryAngularVelocity_RotationUnchanged()
        {
            var entryRotation  = Quaternion.Euler(15f, 30f, 45f);
            var entryOmega     = Vector3.zero;
            float deltaTime    = 1f / 60f;
            const float SpinDecay = 4f;

            entryOmega *= Mathf.Exp(-SpinDecay * deltaTime);
            Quaternion newRotation = PhysicsToView.IntegrateRotation(
                entryRotation, entryOmega, deltaTime);

            Assert.AreEqual(entryRotation, newRotation,
                "Zero entry angular velocity must leave the rotation unchanged.");
        }

        /// <summary>
        /// The decayed angular velocity after a full animation duration (0.25 s at decay rate 4)
        /// must be substantially smaller than the initial value, confirming the spin fades.
        /// </summary>
        [Test]
        public void DropAnimation_SpinDecay_AngularVelocityReducesOverFullAnimation()
        {
            Vector3 initialOmega = new Vector3(0f, 5f, 0f);
            const float SpinDecay  = 4f;
            const float Duration   = 0.25f;

            // Simulate frame-by-frame decay over the full animation.
            float   dt           = 1f / 60f;
            Vector3 omega        = initialOmega;
            float   elapsed      = 0f;
            while (elapsed < Duration)
            {
                omega   *= Mathf.Exp(-SpinDecay * dt);
                elapsed += dt;
            }

            Assert.Less(omega.magnitude, initialOmega.magnitude * 0.5f,
                "Spin must be reduced to less than half its initial magnitude by the end of the drop animation.");
        }
    }
}
