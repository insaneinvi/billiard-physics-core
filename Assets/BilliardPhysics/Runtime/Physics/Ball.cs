namespace BilliardPhysics
{
    public class Ball
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
            SlidingFriction = Fix64.From(25)   / Fix64.From(100);   // 0.2
            RollingFriction = Fix64.From(2)   / Fix64.From(100);  // 0.01
            SpinFriction    = Fix64.From(10)   / Fix64.From(100);  // 0.05
        }

        /// <summary>Convenience constructor using standard radius and mass.</summary>
        public Ball(int id) : this(id, StandardRadius, StandardMass) { }

        /// <summary>Zeros velocity and angular velocity; marks ball as active.</summary>
        public void Reset()
        {
            LinearVelocity  = FixVec2.Zero;
            AngularVelocity = FixVec3.Zero;
            IsMotionless    = true;
            IsPocketed      = false;
        }
    }
}
