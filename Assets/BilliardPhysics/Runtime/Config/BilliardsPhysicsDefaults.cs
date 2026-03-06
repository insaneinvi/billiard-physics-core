namespace BilliardPhysics
{
    /// <summary>
    /// Default values for realistic billiards simulation parameters.
    /// All length values are mm/100, mass values are g/100.
    /// </summary>
    public static class BilliardsPhysicsDefaults
    {
        /// <summary>
        /// Standard billiard ball radius (mm / 100).
        /// Matches the World Pool-Billiard Association spec: diameter = 57.15 mm → radius = 28.575 mm → 0.28575.
        /// </summary>
        public static readonly Fix64 Ball_Radius = Fix64.FromFloat(0.28575f, 6);

        /// <summary>
        /// Standard billiard ball mass (g / 100).
        /// </summary>
        public static readonly Fix64 Ball_Mass = Fix64.FromFloat(1.7f, 4);

        /// <summary>
        /// Coefficient of restitution for a cushion segment (0–1).
        /// A value of 0.75 represents a typical rubber-cushion billiard rail.
        /// Combined with the ball's own restitution via the minimum of both values.
        /// </summary>
        public static readonly Fix64 Segment_Restitution = Fix64.FromFloat(0.75f, 4);
        
        /// <summary>
        /// Minimum cue-strike strength (linear impulse, original 85000 g·mm/s scaled by 1/100 for g and 1/100 for mm = 8.5).
        /// For <see cref="Ball_Mass"/> = 1.7, this corresponds to Δv ≈ 5:
        /// J_min = 5 × 1.7 = 8.5.
        /// </summary>
        public static readonly Fix64 ApplyCueStrike_StrengthMin = Fix64.FromFloat(8.5f, 4);

        /// <summary>
        /// Maximum cue-strike strength (linear impulse, original 1360000 g·mm/s scaled by 1/100 for g and 1/100 for mm = 136).
        /// For <see cref="Ball_Mass"/> = 1.7, this corresponds to Δv ≈ 80:
        /// J_max = 80 × 1.7 = 136.
        /// </summary>
        public static readonly Fix64 ApplyCueStrike_StrengthMax = Fix64.From(186);

        /// <summary>
        /// Ball speed threshold below which a ball whose centre is inside
        /// a pocket's capture radius is considered pocketed (sunk).
        /// Balls faster than this value pass through the pocket mouth without being
        /// captured immediately; they must slow down (e.g. via rim collisions) first.
        /// Default: 5 (500 mm/s scaled by 1/100) — the minimum cue-strike speed, so only truly slow/rolling
        /// balls are captured without first bouncing off the pocket rim.
        /// </summary>
        public static readonly Fix64 PocketSinkSpeedThreshold = Fix64.From(5);

        /// <summary>
        /// Coefficient of restitution applied to a pocket's <see cref="Pocket.RimSegment"/>
        /// (0–1).  A very low value (≈ 0.1) causes the ball to lose most of its normal
        /// velocity on rim contact, simulating the energy-absorbing leather/rubber of a
        /// real pocket jaw and slowing the ball enough to be captured on the next
        /// <see cref="PhysicsWorld2D.CheckPocketCaptures"/> call.
        /// </summary>
        public static readonly Fix64 PocketRimRestitution = Fix64.FromFloat(0.02f, 4);

        public static readonly Fix64 SpinParam = Fix64.FromFloat(0.01f);
    }
}
