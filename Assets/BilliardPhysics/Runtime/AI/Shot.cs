namespace BilliardPhysics.AI
{
    /// <summary>Classifies a shot by its tactical intent.</summary>
    public enum ShotKind
    {
        Unknown,
        DirectPot,
        BankShot,
        Safety,
    }

    /// <summary>
    /// Parameters for a single cue-ball strike.
    /// All numeric values use fixed-point arithmetic; no floating point is permitted.
    /// </summary>
    public sealed class Shot
    {
        /// <summary>Normalized XY direction of the cue-stick strike.</summary>
        public FixVec2  Direction { get; }

        /// <summary>
        /// Strike force magnitude (linear impulse) in the same units as
        /// <see cref="BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMin"/>.
        /// </summary>
        public Fix64    Strength  { get; }

        /// <summary>Lateral (side-spin) offset from ball centre along the perpendicular axis.</summary>
        public Fix64    SpinX     { get; }

        /// <summary>Vertical (top/back-spin) offset from ball centre; positive = top-spin.</summary>
        public Fix64    SpinY     { get; }

        /// <summary>Tactical category of the shot, used by the evaluator for context.</summary>
        public ShotKind Kind      { get; }

        public Shot(FixVec2 direction, Fix64 strength, Fix64 spinX, Fix64 spinY,
                    ShotKind kind = ShotKind.Unknown)
        {
            Direction = direction;
            Strength  = strength;
            SpinX     = spinX;
            SpinY     = spinY;
            Kind      = kind;
        }
    }
}
