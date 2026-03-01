using UnityEngine;

namespace BilliardPhysics.Runtime.ViewTool
{
    /// <summary>
    /// Utility for converting physics-simulation data into Unity-ready view values.
    /// </summary>
    public static class PhysicsToView
    {
        /// <summary>
        /// Integrates a physics angular velocity into a Unity transform rotation,
        /// correctly accounting for the physics-to-view coordinate transformation.
        ///
        /// <b>Coordinate systems:</b>
        /// <list type="bullet">
        ///   <item>Physics: right-handed, Z-up (+Z away from table, table normal = (0,0,+1)).</item>
        ///   <item>View: physics coordinates reflected about the XY plane,
        ///     i.e. (x, y, z)_phys → (x, y, −z)_view.
        ///     Transformation matrix M = diag(1, 1, −1); det(M) = −1.</item>
        /// </list>
        ///
        /// <b>Why the sign correction is needed:</b>
        /// ω is an axial (pseudo-)vector (cross-product of two polar vectors).
        /// Under a reflection M with det(M) = −1, axial vectors transform as:
        /// <code>
        ///   ω_view = det(M) · M · ω_phys
        ///          = −1 · (ω.X, ω.Y, −ω.Z)
        ///          = (−ω.X, −ω.Y, +ω.Z)
        /// </code>
        /// Applying only the polar-vector rule (M · ω = (X, Y, −Z)) would reverse the
        /// X and Y rotation axes, making the ball appear to spin in the wrong direction.
        ///
        /// <b>Minimal verification cases:</b>
        /// <list type="bullet">
        ///   <item>+X rolling: ω_phys=(0,+v/R,0) → ω_view=(0,−v/R,0). Ball visually
        ///     rolls in the +X direction (top moves forward, no mirror flip).</item>
        ///   <item>+Y rolling: ω_phys=(−v/R,0,0) → ω_view=(+v/R,0,0). Ball visually
        ///     rolls in the +Y direction.</item>
        ///   <item>Diagonal (+X+Y) rolling: both axes follow from the above, no cross-axis
        ///     contamination.</item>
        ///   <item>Side-spin only: ω_phys=(0,0,+s) → ω_view=(0,0,+s). Spin about
        ///     the table-normal axis is unaffected (Z component is preserved).</item>
        /// </list>
        ///
        /// Integration method: axis-angle.
        ///   angle = |ω_view| × deltaTime
        ///   axis  = ω_view / |ω_view|
        /// The incremental rotation is pre-multiplied (R_new = ΔR × R_current).
        /// </summary>
        /// <param name="currentRotation">The ball's current Unity transform rotation.</param>
        /// <param name="physicsAngularVelocity">
        /// Angular velocity ω in rad/s in the <b>physics</b> coordinate system
        /// (right-handed, Z-up).  The view-space axis mapping is applied internally.
        /// </param>
        /// <param name="deltaTime">Time step in seconds.</param>
        /// <returns>The new Unity transform rotation after integrating ω over deltaTime.</returns>
        public static Quaternion IntegrateRotation(
            Quaternion currentRotation,
            Vector3    physicsAngularVelocity,
            float      deltaTime)
        {
            // Physics → View: M = diag(1, 1, −1), det(M) = −1.
            // ω is an axial vector → ω_view = det(M) · M · ω = (−X, −Y, +Z).
            Vector3 omega = new Vector3(
                -physicsAngularVelocity.x,
                -physicsAngularVelocity.y,
                 physicsAngularVelocity.z);

            float omegaMag = omega.magnitude;
            if (omegaMag < 1e-6f)
                return currentRotation;

            float   angleDeg = omegaMag * deltaTime * Mathf.Rad2Deg;
            Vector3 axis     = omega / omegaMag;
            return (Quaternion.AngleAxis(angleDeg, axis) * currentRotation).normalized;
        }
    }
}
