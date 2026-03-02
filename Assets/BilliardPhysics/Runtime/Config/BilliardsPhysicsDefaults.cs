namespace BilliardPhysics
{
    /// <summary>
    /// Default values for realistic billiards simulation parameters.
    /// All length values are in metres (m), mass values in kilograms (kg).
    /// </summary>
    public static class BilliardsPhysicsDefaults
    {
        /// <summary>
        /// Standard billiard ball radius in metres (m).
        /// Matches the World Pool-Billiard Association spec: diameter = 57.15 mm = 0.05715 m.
        /// </summary>
        public static readonly Fix64 Ball_Radius = Fix64.FromFloat(0.028575f, 6);

        /// <summary>
        /// Standard billiard ball mass in kilograms (kg).
        /// </summary>
        public static readonly Fix64 Ball_Mass = Fix64.FromFloat(0.17f, 4);

        /// <summary>
        /// Coefficient of restitution for a cushion segment (0–1).
        /// A value of 0.75 represents a typical rubber-cushion billiard rail.
        /// Combined with the ball's own restitution via the minimum of both values.
        /// </summary>
        public static readonly Fix64 Segment_Restitution = Fix64.FromFloat(0.75f, 4);
        
        /// <summary>
        /// Minimum cue-strike strength (linear impulse, in kg·m/s).
        /// For <see cref="Ball_Mass"/> = 0.17 kg, this corresponds to Δv ≈ 0.5 m/s:
        /// J_min = 0.5 m/s × 0.17 kg = 0.085 kg·m/s.
        /// </summary>
        public static readonly Fix64 ApplyCueStrike_StrengthMin = Fix64.FromFloat(0.085f, 4);

        /// <summary>
        /// Maximum cue-strike strength (linear impulse, in kg·m/s).
        /// For <see cref="Ball_Mass"/> = 0.17 kg, this corresponds to Δv ≈ 8 m/s:
        /// J_max = 8 m/s × 0.17 kg = 1.36 kg·m/s.
        /// </summary>
        public static readonly Fix64 ApplyCueStrike_StrengthMax = Fix64.FromFloat(1.36f, 4);

        /// <summary>
        /// Additional rolling-resistance coefficient contributed by the table cloth surface
        /// (dimensionless).  Added to each ball's own <see cref="Ball.RollingFriction"/> and
        /// <see cref="Ball.SlidingFriction"/> inside <see cref="MotionSimulator.Step"/>.
        /// Zero means no extra table-cloth drag beyond the ball's own friction values.
        /// </summary>
        public static readonly Fix64 PhysicsWorld2D_TableFriction = Fix64.Zero;

        /// <summary>
        /// Ball speed threshold (m/s) below which a ball whose centre is inside
        /// a pocket's capture radius is considered pocketed (sunk).
        /// Balls faster than this value pass through the pocket mouth without being
        /// captured immediately; they must slow down (e.g. via rim collisions) first.
        /// Default: 0.5 m/s — the minimum cue-strike speed, so only truly slow/rolling
        /// balls are captured without first bouncing off the pocket rim.
        /// </summary>
        public static readonly Fix64 PocketSinkSpeedThreshold = Fix64.FromFloat(0.5f, 4);

        /// <summary>
        /// Coefficient of restitution applied to a pocket's <see cref="Pocket.RimSegment"/>
        /// (0–1).  A very low value (≈ 0.1) causes the ball to lose most of its normal
        /// velocity on rim contact, simulating the energy-absorbing leather/rubber of a
        /// real pocket jaw and slowing the ball enough to be captured on the next
        /// <see cref="PhysicsWorld2D.CheckPocketCaptures"/> call.
        /// </summary>
        public static readonly Fix64 PocketRimRestitution = Fix64.FromFloat(0.1f, 4);
    }
}
