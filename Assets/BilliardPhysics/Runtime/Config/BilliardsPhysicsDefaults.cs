namespace BilliardPhysics
{
    /// <summary>
    /// Default values for realistic billiards simulation parameters.
    /// All length values are in millimetres (mm), mass values in grams (g).
    /// </summary>
    public static class BilliardsPhysicsDefaults
    {
        /// <summary>
        /// Standard billiard ball radius in millimetres (mm).
        /// Matches the World Pool-Billiard Association spec: diameter = 57.15 mm.
        /// </summary>
        public static readonly Fix64 Ball_Radius = Fix64.FromFloat(28.575f, 4);

        /// <summary>
        /// Standard billiard ball mass in grams (g).
        /// </summary>
        public static readonly Fix64 Ball_Mass = Fix64.From(170);

        /// <summary>
        /// Coefficient of restitution for a cushion segment (0–1).
        /// A value of 0.75 represents a typical rubber-cushion billiard rail.
        /// Combined with the ball's own restitution via the minimum of both values.
        /// </summary>
        public static readonly Fix64 Segment_Restitution = Fix64.FromFloat(0.85f, 4);

        /// <summary>
        /// Minimum cue-strike strength (linear impulse, in g·mm/s).
        /// For <see cref="Ball_Mass"/> = 170 g, this corresponds to Δv ≈ 0.5 m/s (500 mm/s):
        /// J_min = 500 mm/s × 170 g = 85 000 g·mm/s.
        /// </summary>
        public static readonly Fix64 ApplyCueStrike_StrengthMin = Fix64.From(85000);

        /// <summary>
        /// Maximum cue-strike strength (linear impulse, in g·mm/s).
        /// For <see cref="Ball_Mass"/> = 170 g, this corresponds to Δv ≈ 8 m/s (8 000 mm/s):
        /// J_max = 8 000 mm/s × 170 g = 1 360 000 g·mm/s.
        /// </summary>
        public static readonly Fix64 ApplyCueStrike_StrengthMax = Fix64.From(1360000);

        /// <summary>
        /// Additional rolling-resistance coefficient contributed by the table cloth surface
        /// (dimensionless).  Added to each ball's own <see cref="Ball.RollingFriction"/> and
        /// <see cref="Ball.SlidingFriction"/> inside <see cref="MotionSimulator.Step"/>.
        /// Zero means no extra table-cloth drag beyond the ball's own friction values.
        /// </summary>
        public static readonly Fix64 PhysicsWorld2D_TableFriction = Fix64.Zero;

        /// <summary>
        /// Ball speed threshold (mm/s) below which a ball whose centre is inside
        /// a pocket's capture radius is considered pocketed (sunk).
        /// Balls faster than this value pass through the pocket mouth without being
        /// captured immediately; they must slow down (e.g. via rim collisions) first.
        /// Default: 500 mm/s — the minimum cue-strike speed, so only truly slow/rolling
        /// balls are captured without first bouncing off the pocket rim.
        /// </summary>
        public static readonly Fix64 PocketSinkSpeedThreshold = Fix64.From(500);

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
