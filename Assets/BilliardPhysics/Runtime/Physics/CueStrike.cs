namespace BilliardPhysics
{
    /// <summary>
    /// Applies a cue-stick strike impulse to a ball.
    /// </summary>
    public static class CueStrike
    {
        /// <summary>
        /// Applies a cue strike to a ball.
        /// </summary>
        /// <param name="ball">Target ball.</param>
        /// <param name="direction">Normalized strike direction.</param>
        /// <param name="strength">Strike force magnitude.</param>
        /// <param name="spinX">Lateral (left/right english) offset from ball centre.</param>
        /// <param name="spinY">Vertical (top/back spin) offset from ball centre.</param>
        public static void Apply(Ball ball, FixVec2 direction, Fix64 strength, Fix64 spinX, Fix64 spinY)
        {
            // Normalize direction defensively.
            FixVec2 dir = direction.Normalized;

            // Linear impulse → velocity change.
            ball.LinearVelocity = dir * strength / ball.Mass;

            // Angular velocity from vertical (top/back) and lateral english.
            // spinY drives top-spin / back-spin; spinX drives side-spin (english).
            ball.AngularVelocity = (spinY + spinX) * strength / ball.Inertia;
        }
    }
}
