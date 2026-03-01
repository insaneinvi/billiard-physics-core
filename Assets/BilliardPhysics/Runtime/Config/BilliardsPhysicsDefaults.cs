namespace BilliardPhysics
{
    /// <summary>
    /// Default values for realistic billiards simulation parameters.
    /// All length values are in millimetres (mm), mass values in grams (g).
    /// </summary>
    public static class BilliardsPhysicsDefaults
    {
        /// <summary>
        /// Additional rolling-resistance coefficient for the table surface (dimensionless).
        /// Represents a typical cloth-covered slate table.
        /// Typical useful range: 0.005–0.05.
        /// </summary>
        public static readonly Fix64 PhysicsWorld2D_TableFriction = Fix64.FromFloat(0.02f, 4);

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
        public static readonly Fix64 Segment_Restitution = Fix64.FromFloat(0.75f, 4);

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
    }
}
