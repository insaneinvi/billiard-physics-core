namespace BilliardPhysics
{
    /// <summary>
    /// 3-component fixed-point vector using Fix64 components.
    /// Coordinate convention: Z-down (table surface normal is n = (0,0,-1)).
    /// Ball center-to-contact-point vector: r = (0,0,+Radius)).
    /// </summary>
    public struct FixVec3
    {
        public Fix64 X;
        public Fix64 Y;
        public Fix64 Z;

        public FixVec3(Fix64 x, Fix64 y, Fix64 z) { X = x; Y = y; Z = z; }

        // ── Constants ────────────────────────────────────────────────────────────
        public static FixVec3 Zero => new FixVec3(Fix64.Zero, Fix64.Zero, Fix64.Zero);

        // ── Arithmetic operators ──────────────────────────────────────────────────
        public static FixVec3 operator +(FixVec3 a, FixVec3 b) => new FixVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static FixVec3 operator -(FixVec3 a, FixVec3 b) => new FixVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static FixVec3 operator -(FixVec3 a)            => new FixVec3(-a.X, -a.Y, -a.Z);
        public static FixVec3 operator *(FixVec3 v, Fix64 s)   => new FixVec3(v.X * s, v.Y * s, v.Z * s);
        public static FixVec3 operator *(Fix64 s, FixVec3 v)   => new FixVec3(v.X * s, v.Y * s, v.Z * s);
        public static FixVec3 operator /(FixVec3 v, Fix64 s)   => new FixVec3(v.X / s, v.Y / s, v.Z / s);

        public static bool operator ==(FixVec3 a, FixVec3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        public static bool operator !=(FixVec3 a, FixVec3 b) => !(a == b);

        // ── Dot product ───────────────────────────────────────────────────────────
        public static Fix64 Dot(FixVec3 a, FixVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        // ── Cross product ─────────────────────────────────────────────────────────
        /// <summary>Right-hand cross product a × b.</summary>
        public static FixVec3 Cross(FixVec3 a, FixVec3 b) =>
            new FixVec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        // ── Properties ───────────────────────────────────────────────────────────
        public Fix64 SqrMagnitude => X * X + Y * Y + Z * Z;
        public Fix64 Magnitude    => Fix64.Sqrt(SqrMagnitude);

        public FixVec3 Normalized
        {
            get
            {
                Fix64 mag = Magnitude;
                if (mag == Fix64.Zero) return Zero;
                return this / mag;
            }
        }

        // ── Object overrides ──────────────────────────────────────────────────────
        public override bool Equals(object obj) => obj is FixVec3 v && this == v;
        public override int  GetHashCode()      => X.GetHashCode() ^ (Y.GetHashCode() << 16) ^ (Z.GetHashCode() << 8);
        public override string ToString()       => $"({X}, {Y}, {Z})";
    }
}
