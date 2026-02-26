namespace BilliardPhysics
{
    public struct FixVec2
    {
        public Fix64 X;
        public Fix64 Y;

        public FixVec2(Fix64 x, Fix64 y) { X = x; Y = y; }

        // ── Constants ────────────────────────────────────────────────────────────
        public static FixVec2 Zero => new FixVec2(Fix64.Zero, Fix64.Zero);
        public static FixVec2 One  => new FixVec2(Fix64.One,  Fix64.One);

        // ── Arithmetic operators ──────────────────────────────────────────────────
        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new FixVec2(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new FixVec2(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator -(FixVec2 a)            => new FixVec2(-a.X, -a.Y);
        public static FixVec2 operator *(FixVec2 v, Fix64 s)   => new FixVec2(v.X * s, v.Y * s);
        public static FixVec2 operator *(Fix64 s, FixVec2 v)   => new FixVec2(v.X * s, v.Y * s);
        public static FixVec2 operator /(FixVec2 v, Fix64 s)   => new FixVec2(v.X / s, v.Y / s);

        public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

        // ── Dot product ───────────────────────────────────────────────────────────
        public static Fix64 Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

        // ── Properties ───────────────────────────────────────────────────────────
        public Fix64 SqrMagnitude => X * X + Y * Y;
        public Fix64 Magnitude    => Fix64.Sqrt(SqrMagnitude);

        public FixVec2 Normalized
        {
            get
            {
                Fix64 mag = Magnitude;
                if (mag == Fix64.Zero) return Zero;
                return this / mag;
            }
        }

        /// <summary>Returns the left-hand perpendicular: (-Y, X).</summary>
        public FixVec2 Perp() => new FixVec2(-Y, X);

        // ── Distance ─────────────────────────────────────────────────────────────
        public static Fix64 Distance(FixVec2 a, FixVec2 b) => (a - b).Magnitude;

        // ── Object overrides ──────────────────────────────────────────────────────
        public override bool Equals(object obj) => obj is FixVec2 v && this == v;
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 16);
        public override string ToString() => $"({X}, {Y})";
    }
}
