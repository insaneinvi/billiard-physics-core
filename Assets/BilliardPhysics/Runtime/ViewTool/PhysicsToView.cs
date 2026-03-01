using UnityEngine;

namespace BilliardPhysics.Runtime.ViewTool
{
    /// <summary>
    /// Utility for converting physics-simulation data into Unity-ready view values.
    /// </summary>
    public static class PhysicsToView
    {
        /// <summary>
        /// Integrates a physics angular velocity into a Unity transform rotation.
        ///
        /// Unit convention: <paramref name="angularVelocity"/> is in rad/s, expressed in
        /// Unity world space (+Y up, +Z forward).  The caller is responsible for converting
        /// from the physics coordinate system (Z-down: +Z toward table) to Unity space
        /// before calling this method, e.g.:
        ///   var omegaUnity = new Vector3(omega.X, omega.Y, -omega.Z);
        ///
        /// Integration method: axis-angle.
        ///   angle (rad) = |ω| × deltaTime
        ///   axis         = ω / |ω|
        /// The incremental rotation is pre-multiplied onto <paramref name="currentRotation"/>
        /// (world-space convention: R_new = ΔR × R_current).
        /// </summary>
        /// <param name="currentRotation">The ball's current Unity transform rotation.</param>
        /// <param name="angularVelocity">Angular velocity ω in rad/s (Unity world space).</param>
        /// <param name="deltaTime">Time step in seconds.</param>
        /// <returns>The new Unity transform rotation after integrating ω over deltaTime.</returns>
        public static Quaternion IntegrateRotation(
            Quaternion currentRotation,
            Vector3    angularVelocity,
            float      deltaTime)
        {
            float omegaMag = angularVelocity.magnitude;
            if (omegaMag < 1e-6f)
                return currentRotation;

            float   angleDeg = omegaMag * deltaTime * Mathf.Rad2Deg;
            Vector3 axis     = angularVelocity / omegaMag;
            return (Quaternion.AngleAxis(angleDeg, axis) * currentRotation).normalized;
        }
    }
}
