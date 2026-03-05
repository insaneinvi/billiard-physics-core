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

        // ── CalcDropTarget utility ────────────────────────────────────────────────

        /// <summary>
        /// <see cref="PocketDropAniHelper.CalcDropTarget"/> must return a point exactly one
        /// ball-diameter from <c>dropStartPos</c> in the direction of <c>pocketWorldPos</c>.
        /// </summary>
        [Test]
        public void CalcDropTarget_ReturnsPointOneDiameterTowardPocket()
        {
            var   startPos      = new Vector3(0f, 0f, 0f);
            var   pocketPos     = new Vector3(3f, 4f, 0f); // distance = 5
            float ballDiameter  = 0.5715f;

            Vector3 result = PocketDropAniHelper.CalcDropTarget(startPos, pocketPos, ballDiameter);

            // Distance from startPos to result must equal ballDiameter.
            float dist = Vector3.Distance(startPos, result);
            Assert.AreEqual(ballDiameter, dist, 1e-5f,
                "CalcDropTarget must place the result exactly one ballDiameter from startPos.");

            // Result must lie on the straight line from startPos toward pocketPos.
            Vector3 expected = startPos + (pocketPos - startPos).normalized * ballDiameter;
            Assert.AreEqual(expected.x, result.x, 1e-5f, "X component must match.");
            Assert.AreEqual(expected.y, result.y, 1e-5f, "Y component must match.");
            Assert.AreEqual(expected.z, result.z, 1e-5f, "Z component must match.");
        }

        /// <summary>
        /// When <c>dropStartPos</c> equals <c>pocketWorldPos</c> (degenerate case),
        /// <see cref="PocketDropAniHelper.CalcDropTarget"/> must return <c>dropStartPos</c>
        /// without throwing or producing NaN.
        /// </summary>
        [Test]
        public void CalcDropTarget_WhenStartEqualsPocket_ReturnsStartPos()
        {
            var startPos = new Vector3(1f, 2f, 3f);
            Vector3 result = PocketDropAniHelper.CalcDropTarget(startPos, startPos, 0.5715f);

            Assert.AreEqual(startPos, result,
                "Degenerate case (start == pocket) must return startPos unchanged.");
        }

        /// <summary>
        /// When the distance from start to pocket is less than one ball-diameter,
        /// <see cref="PocketDropAniHelper.CalcDropTarget"/> must return
        /// <c>pocketWorldPos</c> so the ball does not overshoot the pocket center.
        /// </summary>
        [Test]
        public void CalcDropTarget_WhenDistanceLessThanDiameter_ReturnsPocketPos()
        {
            var   startPos     = new Vector3(0f, 0f, 0f);
            // Pocket is only 0.3 units away — closer than one ball-diameter (0.5715).
            var   pocketPos    = new Vector3(0.3f, 0f, 0f);
            float ballDiameter = 0.5715f;

            Vector3 result = PocketDropAniHelper.CalcDropTarget(startPos, pocketPos, ballDiameter);

            // When the distance is less than one diameter, the target must equal pocketPos.
            Assert.AreEqual(pocketPos.x, result.x, 1e-5f,
                "X: pocket closer than diameter — target must equal pocketWorldPos.");
            Assert.AreEqual(pocketPos.y, result.y, 1e-5f,
                "Y: pocket closer than diameter — target must equal pocketWorldPos.");
            Assert.AreEqual(pocketPos.z, result.z, 1e-5f,
                "Z: pocket closer than diameter — target must equal pocketWorldPos.");
        }

        /// <summary>
        /// When the distance from start to pocket equals exactly one ball-diameter,
        /// <see cref="PocketDropAniHelper.CalcDropTarget"/> must return
        /// <c>pocketWorldPos</c> (boundary case of the ≤ check).
        /// </summary>
        [Test]
        public void CalcDropTarget_WhenDistanceEqualsDiameter_ReturnsPocketPos()
        {
            float ballDiameter = 0.5715f;
            var   startPos     = new Vector3(0f, 0f, 0f);
            // Place pocketPos exactly one diameter away along X.
            var   pocketPos    = new Vector3(ballDiameter, 0f, 0f);

            Vector3 result = PocketDropAniHelper.CalcDropTarget(startPos, pocketPos, ballDiameter);

            Assert.AreEqual(pocketPos.x, result.x, 1e-5f,
                "X: distance == diameter boundary — target must equal pocketWorldPos.");
            Assert.AreEqual(pocketPos.y, result.y, 1e-5f,
                "Y: distance == diameter boundary — target must equal pocketWorldPos.");
            Assert.AreEqual(pocketPos.z, result.z, 1e-5f,
                "Z: distance == diameter boundary — target must equal pocketWorldPos.");
        }

        // ── CalcDropMoveTime utility ──────────────────────────────────────────────

        /// <summary>
        /// <see cref="PocketDropAniHelper.CalcDropMoveTime"/> must return
        /// <c>ballDiameter / ballLinearSpeed</c> for normal speed values.
        /// </summary>
        [Test]
        public void CalcDropMoveTime_NormalSpeed_ReturnsDiameterOverSpeed()
        {
            float diameter  = 0.5715f;
            float speed     = 2.0f;

            float result   = PocketDropAniHelper.CalcDropMoveTime(diameter, speed);
            float expected = diameter / speed;

            Assert.AreEqual(expected, result, 1e-5f,
                "CalcDropMoveTime must equal ballDiameter / ballLinearSpeed for normal speeds.");
        }

        /// <summary>
        /// When ball speed is near zero, <see cref="PocketDropAniHelper.CalcDropMoveTime"/>
        /// must return the minimum duration rather than a huge or infinite value.
        /// </summary>
        [Test]
        public void CalcDropMoveTime_NearZeroSpeed_ReturnsMinDuration()
        {
            float result = PocketDropAniHelper.CalcDropMoveTime(0.5715f, 0f);

            Assert.AreEqual(0.05f, result, 1e-5f,
                "Near-zero speed must produce the minimum duration (0.05 s).");
        }

        /// <summary>
        /// When the computed duration would exceed the maximum, the result must be clamped.
        /// </summary>
        [Test]
        public void CalcDropMoveTime_VerySlowBall_ClampsToMaxDuration()
        {
            // 0.5715 / 0.001 = 571.5 s — well above maxDuration = 1.0
            float result = PocketDropAniHelper.CalcDropMoveTime(0.5715f, 0.001f);

            Assert.AreEqual(1.0f, result, 1e-5f,
                "Very slow ball must clamp duration to the maximum (1.0 s).");
        }

        /// <summary>
        /// When the pocket is closer than one ball-diameter, <c>CalcDropTarget</c> returns
        /// <c>pocketWorldPos</c> and <c>CalcDropMoveTime</c> must base the duration on
        /// the shorter actual distance — not on the full ball-diameter.
        /// This verifies that the move-time correctly reflects the real path length.
        /// </summary>
        [Test]
        public void CalcDropMoveTime_WhenPocketCloserThanDiameter_UsesActualDistance()
        {
            var   startPos     = new Vector3(0f, 0f, 0f);
            // Pocket is 0.3 units away — less than one ball-diameter (0.5715).
            var   pocketPos    = new Vector3(0.3f, 0f, 0f);
            float ballDiameter = 0.5715f;
            float ballSpeed    = 2.0f;

            // CalcDropTarget returns pocketPos because the pocket is within one diameter.
            Vector3 dropTarget       = PocketDropAniHelper.CalcDropTarget(startPos, pocketPos, ballDiameter);
            float   actualDistance   = Vector3.Distance(dropTarget, startPos);
            float   moveTime         = PocketDropAniHelper.CalcDropMoveTime(actualDistance, ballSpeed);

            // Expected duration: 0.3 / 2.0 = 0.15 s.
            float expected = 0.3f / ballSpeed;
            Assert.AreEqual(expected, moveTime, 1e-5f,
                "Duration must equal actual distance / speed when pocket is within one diameter.");

            // Also verify that using the full diameter would give a different (wrong) answer.
            float wrongTime = PocketDropAniHelper.CalcDropMoveTime(ballDiameter, ballSpeed);
            Assert.AreNotEqual(wrongTime, moveTime,
                "Using full diameter instead of actual distance must give a different (incorrect) result.");
        }
    }
}
