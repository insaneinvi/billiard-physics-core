using UnityEngine;

namespace BilliardPhysics
{
    public class Ball
    {
        // ── Identity ──────────────────────────────────────────────────────────────
        public int Id;

        // ── State ─────────────────────────────────────────────────────────────────
        public FixVec2    Position;
        public FixVec2    LinearVelocity;
        public Fix64      AngularVelocity;
        public bool       IsMotionless;
        public bool       IsPocketed;

        // ── Rendering ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 3D orientation for rendering. Updated each simulation step by integrating
        /// AngularVelocity (rad/s) around the +Z axis (Z-up, XY-plane motion).
        /// </summary>
        public Quaternion Rotation = Quaternion.identity;

        // ── Physical parameters ───────────────────────────────────────────────────
        public Fix64 Radius;
        public Fix64 Mass;
        public Fix64 Inertia;
        public Fix64 Restitution;
        public Fix64 SlidingFriction;
        public Fix64 RollingFriction;
        public Fix64 SpinFriction;

        // ── Standard defaults ─────────────────────────────────────────────────────
        public static readonly Fix64 StandardRadius = Fix64.FromFloat(28.575f);  // 0.5 units
        public static readonly Fix64 StandardMass   = Fix64.From(160);

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
            SlidingFriction = Fix64.From(2)   / Fix64.From(10);   // 0.2
            RollingFriction = Fix64.From(1)   / Fix64.From(100);  // 0.01
            SpinFriction    = Fix64.From(5)   / Fix64.From(100);  // 0.05
        }

        /// <summary>Convenience constructor using standard radius and mass.</summary>
        public Ball(int id) : this(id, StandardRadius, StandardMass) { }

        /// <summary>Zeros velocity and angular velocity; marks ball as active.</summary>
        public void Reset()
        {
            LinearVelocity  = FixVec2.Zero;
            AngularVelocity = Fix64.Zero;
            IsMotionless    = true;
            IsPocketed      = false;
            Rotation        = Quaternion.identity;
        }
    }
}
