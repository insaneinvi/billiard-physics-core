namespace BilliardPhysics
{
    /// <summary>
    /// Applies a cue-stick strike impulse to a ball.
    /// </summary>
    public static class CueStrike
    {
        /// <summary>
        /// Applies a cue strike to a ball, computing a physically correct 3D angular
        /// velocity from the strike offset in the Z-down frame.
        /// </summary>
        /// <param name="ball">Target ball.</param>
        /// <param name="direction">Normalized strike direction (XY plane).</param>
        /// <param name="strength">Strike force magnitude (linear impulse = strength).</param>
        /// <param name="spinX">
        /// Lateral (left/right english) offset from ball centre along dir.Perp().
        /// Drives side-spin (ω.Z).
        /// </param>
        /// <param name="spinY">
        /// Vertical (top/back-spin) offset above ball centre (+Y = above = top-spin).
        /// In the Z-down frame this corresponds to a −Z offset, driving ω.X/ω.Y.
        /// </param>
        public static void Apply(Ball ball, FixVec2 direction, Fix64 strength, Fix64 spinX, Fix64 spinY)
        {
            // Normalize direction defensively.
            FixVec2 dir = direction.Normalized;

            // Linear impulse J = strength * dir → velocity change Δv = J / m.
            ball.LinearVelocity = dir * strength / ball.Mass;

            // 3D angular velocity from strike offset:
            //   offset = spinX * perp + spinY * (-Z)   (perp = dir.Perp(); -Z = above in Z-down)
            //          = (spinX*perp.X, spinX*perp.Y, -spinY)
            //   J3     = (strength*dir.X, strength*dir.Y, 0)
            //   Δω     = I⁻¹ · (offset × J3)
            //
            // Expanded (with perp = (-dir.Y, dir.X) and |dir|=1):
            //   Δω.X = +spinY * strength * dir.Y / I
            //   Δω.Y = −spinY * strength * dir.X / I
            //   Δω.Z = −spinX * strength          / I
            Fix64 invI = Fix64.One / ball.Inertia;
            ball.AngularVelocity = new FixVec3(
                 spinY * strength * dir.Y * invI,
                -spinY * strength * dir.X * invI,
                -spinX * strength          * invI);
        }
    }
}
