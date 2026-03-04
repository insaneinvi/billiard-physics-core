using NUnit.Framework;
using UnityEngine;
using BilliardPhysics.Runtime.ViewTool;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="PhysicsToView.IntegrateRotation"/>.
    ///
    /// Physics coordinate system: right-handed, Z-up (table normal = +Z).
    /// Rolling conditions (no-slip): ω.Y = +Lv.X / R,  ω.X = −Lv.Y / R.
    ///
    /// View coordinate transform: M = diag(1,1,−1), det(M) = −1.
    /// Correct axial-vector rule: ω_view = det(M)·M·ω = (−X, −Y, +Z).
    /// </summary>
    public class PhysicsToViewTests
    {
        private const float R      = 28.575f;   // standard ball radius (physics units)
        private const float Speed  = 100f;      // arbitrary linear speed (units/s)
        private const float Dt     = 1 / 60f;   // one 60 Hz frame
        private const float DegTol = 0.5f;      // acceptable angle error (degrees)

        // ── Helper ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the view-space axis direction that <see cref="PhysicsToView.IntegrateRotation"/>
        /// rotates around for a given physics angular velocity.
        /// A positive angle means the ball spins in the direction consistent with no-slip
        /// rolling along that axis.
        /// </summary>
        private static void AssertRotationAxis(
            Vector3 physicsOmega,
            Vector3 expectedViewAxis,
            string  message)
        {
            Quaternion q = PhysicsToView.IntegrateRotation(
                Quaternion.identity, physicsOmega, Dt);

            q.ToAngleAxis(out float angle, out Vector3 actualAxis);

            // Skip if effectively no rotation (angle ≈ 0; the axis is undefined).
            if (Mathf.Abs(angle) < 0.01f) return;

            // Normalise direction – ToAngleAxis may return the opposite axis with
            // a negative angle; unify by ensuring angle > 0.
            if (angle < 0f) { actualAxis = -actualAxis; }

            Assert.AreEqual(expectedViewAxis.x, actualAxis.x, DegTol, $"{message}: X axis");
            Assert.AreEqual(expectedViewAxis.y, actualAxis.y, DegTol, $"{message}: Y axis");
            Assert.AreEqual(expectedViewAxis.z, actualAxis.z, DegTol, $"{message}: Z axis");
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ball rolling in +X direction.
        /// Physics: ω = (0, +Speed/R, 0) [positive Y-axis, rolling contact drives +X].
        /// View:    ω_view = (0, −Speed/R, 0) → rotation about −Y axis.
        /// </summary>
        [Test]
        public void IntegrateRotation_RollingPlusX_CorrectViewAxis()
        {
            Vector3 physOmega = new Vector3(0f, Speed / R, 0f);

            // The integrated quaternion should represent a rotation about the −Y view axis
            // (axial-vector rule: ω_view.y = −ω_phys.y < 0, so rotation axis is −Y when angle > 0).
            AssertRotationAxis(physOmega, new Vector3(0f, -1f, 0f),
                "Rolling in +X: view rotation must be around −Y");
        }

        /// <summary>
        /// Ball rolling in +Y direction.
        /// Physics: ω = (−Speed/R, 0, 0) [negative X-axis, rolling contact drives +Y].
        /// View:    ω_view = (+Speed/R, 0, 0) → rotation about +X axis.
        /// </summary>
        [Test]
        public void IntegrateRotation_RollingPlusY_CorrectViewAxis()
        {
            Vector3 physOmega = new Vector3(-Speed / R, 0f, 0f);

            AssertRotationAxis(physOmega, new Vector3(1f, 0f, 0f),
                "Rolling in +Y: view rotation must be around +X");
        }

        /// <summary>
        /// Pure side-spin (Z-only angular velocity).
        /// Physics: ω = (0, 0, +Spin) [Z-axis side-spin].
        /// View:    ω_view = (0, 0, +Spin) — Z component is unchanged by the transform.
        /// </summary>
        [Test]
        public void IntegrateRotation_SideSpin_ZAxisUnchanged()
        {
            float spin = Speed / R;
            Vector3 physOmega = new Vector3(0f, 0f, spin);

            AssertRotationAxis(physOmega, new Vector3(0f, 0f, 1f),
                "Side-spin: view rotation must be around +Z");
        }

        /// <summary>
        /// Zero angular velocity must return the input rotation unchanged.
        /// </summary>
        [Test]
        public void IntegrateRotation_ZeroOmega_ReturnsCurrent()
        {
            Quaternion initial = Quaternion.Euler(30f, 45f, 60f);
            Quaternion result  = PhysicsToView.IntegrateRotation(
                initial, Vector3.zero, Dt);

            Assert.AreEqual(initial, result, "Zero ω must leave rotation unchanged.");
        }

        /// <summary>
        /// Diagonal rolling (+X and +Y simultaneously).
        /// Physics: ω = (−Speed/R, +Speed/R, 0) [combination of both axes].
        /// View:    ω_view = (+Speed/R, −Speed/R, 0) — magnitude and axes individually correct.
        /// The resulting quaternion must not be identity and should differ from either
        /// pure-X or pure-Y rolling alone, confirming no cross-axis contamination.
        /// </summary>
        [Test]
        public void IntegrateRotation_DiagonalRolling_NonIdentityAndNotSingleAxis()
        {
            Vector3 physOmega = new Vector3(-Speed / R, Speed / R, 0f);

            Quaternion q = PhysicsToView.IntegrateRotation(
                Quaternion.identity, physOmega, Dt);

            // Must not be identity.
            Assert.AreNotEqual(Quaternion.identity, q,
                "Diagonal rolling must produce a non-identity rotation.");

            // Rotation axis must have positive X and negative Y (fourth quadrant of XY plane).
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle < 0f) axis = -axis;
            Assert.Greater(axis.x,  0f, "Diagonal: view axis X must be positive (+X rolling component).");
            Assert.Less   (axis.y,  0f, "Diagonal: view axis Y must be negative (+X rolling component).");
        }
    }
}
