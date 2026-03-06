namespace BilliardPhysics
{
    // ── Design rationale ─────────────────────────────────────────────────────────
    // Ball is a value type (struct) so that:
    //   • A contiguous Ball[] array can be passed to Span<Ball> / MemoryMarshal for
    //     zero-copy bulk reads by AI inference or physics simulation pipelines.
    //   • Per-ball cache locality is preserved: all fields sit in one cache line.
    //
    // Blittability: all fields are unmanaged value types (int, bool, Fix64 = long,
    // FixVec2 / FixVec3 = struct of Fix64), so Ball is fully blittable / unmanaged.
    // MemoryMarshal.Cast<Ball, byte>() and similar operations are safe.
    //
    // Value-semantics hazard: because Ball is a struct, callers must be careful
    // not to work with stale copies.  Physics methods (MotionSimulator.Step,
    // ImpulseResolver, CueStrike.Apply) take 'ref Ball' to mutate in place.
    // After PhysicsWorld2D.Step(), always read ball state from PhysicsWorld2D.Balls[i]
    // rather than from a Ball value that was captured before the step.
    // ──────────────────────────────────────────────────────────────────────────────
    public struct Ball
    {
        // ── Identity ──────────────────────────────────────────────────────────────
        public int Id;

        // ── State ─────────────────────────────────────────────────────────────────
        public FixVec2    Position;
        public FixVec2    LinearVelocity;
        /// <summary>
        /// 3D angular velocity (rad/s) in the physics coordinate system (Z-up:
        /// +Z points away from the table; table normal = (0,0,+1)).
        /// X/Y components drive rolling (coupled to linear motion via table friction).
        /// Z component is side-spin (english); decays via SpinFriction.
        /// </summary>
        public FixVec3    AngularVelocity;
        /// <summary>
        /// Angular velocity (rad/s) captured at the moment the ball was pocketed,
        /// before <see cref="AngularVelocity"/> is zeroed.
        /// Read by the drop animation to continue spinning the ball naturally.
        /// </summary>
        public FixVec3    LastAngularVelocity;
        public bool       IsMotionless;
        public bool       IsPocketed;

        // ── Physical parameters ───────────────────────────────────────────────────
        public Fix64 Radius;
        public Fix64 Mass;
        public Fix64 Inertia;
        public Fix64 Restitution;
        public Fix64 SlidingFriction;
        public Fix64 RollingFriction;
        public Fix64 SpinFriction;

        // ── Standard defaults ─────────────────────────────────────────────────────
        public static readonly Fix64 StandardRadius = Fix64.FromFloat(0.28575f, 6);  // 28.575 mm scaled by 1/100
        public static readonly Fix64 StandardMass   = Fix64.FromFloat(1.6f, 4);      // 160 g scaled by 1/100

        /// <param name="id">Unique ball identifier.</param>
        /// <param name="radius">Ball radius in physics units.</param>
        /// <param name="mass">Ball mass.</param>
        public Ball(int id, Fix64 radius, Fix64 mass)
        {
            Id     = id;
            Radius = radius;
            Mass   = mass;

            // Moment of inertia for a solid sphere: I = (2/5) * m * r^2
            Fix64 two   = Fix64.From(2);
            Fix64 five  = Fix64.From(5);
            Inertia = two / five * mass * radius * radius;

            Restitution     = Fix64.From(95)  / Fix64.From(100);  // 0.95
            SlidingFriction = Fix64.From(25)  / Fix64.From(100);  // 0.25
            RollingFriction = Fix64.From(2)   / Fix64.From(100);  // 0.02
            SpinFriction    = Fix64.From(10)  / Fix64.From(100);  // 0.10

            // Zero-initialise remaining state fields (required for struct constructors
            // that do not use a base-class initialiser).
            Position            = FixVec2.Zero;
            LinearVelocity      = FixVec2.Zero;
            AngularVelocity     = FixVec3.Zero;
            LastAngularVelocity = FixVec3.Zero;
            IsMotionless        = false;
            IsPocketed          = false;
        }

        /// <summary>Convenience constructor using standard radius and mass.</summary>
        public Ball(int id) : this(id, StandardRadius, StandardMass) { }

        /// <summary>Zeros velocity and angular velocity; marks ball as active.</summary>
        // NOTE: Because Ball is a struct, calling this method on an array element
        // (e.g. _balls[i].Reset()) modifies the element in place, which is correct.
        // Calling it on a local copy will NOT affect any array or world state.
        public void Reset()
        {
            LinearVelocity  = FixVec2.Zero;
            AngularVelocity = FixVec3.Zero;
            IsMotionless    = true;
            IsPocketed      = false;
        }
    }
}
