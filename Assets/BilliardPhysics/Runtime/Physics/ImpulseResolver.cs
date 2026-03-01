namespace BilliardPhysics
{
    public static class ImpulseResolver
    {
        // ── Ball–ball collision ───────────────────────────────────────────────────

        public static void ResolveBallBall(Ball a, Ball b)
        {
            FixVec2 n = (b.Position - a.Position).Normalized;

            // Guard: skip if positions are coincident and normal is degenerate.
            if (n == FixVec2.Zero) return;

            // Vectors from centre to contact point.
            FixVec2 ra = n  * a.Radius;
            FixVec2 rb = -n * b.Radius;

            // Velocity at contact point (linear + angular contribution).
            // ω × r for an in-plane r = (rx, ry, 0) reduces to ω.Z * r.Perp()
            // regardless of Z convention (Z-up or Z-down), since only the Z
            // component of ω contributes to the XY contact velocity.
            FixVec2 va_contact = a.LinearVelocity + a.AngularVelocity.Z * ra.Perp();
            FixVec2 vb_contact = b.LinearVelocity + b.AngularVelocity.Z * rb.Perp();
            FixVec2 v_rel      = va_contact - vb_contact;

            Fix64 v_rel_n = FixVec2.Dot(v_rel, n);
            // n points from a to b; v_rel_n > 0 means a approaches b (collision).
            if (v_rel_n <= Fix64.Zero) return;  // already separating

            Fix64 e = Fix64.Min(a.Restitution, b.Restitution);

            FixVec2 raPerp = ra.Perp();
            FixVec2 rbPerp = rb.Perp();

            // Normal impulse denominator.
            Fix64 raPerpDotN = FixVec2.Dot(raPerp, n);
            Fix64 rbPerpDotN = FixVec2.Dot(rbPerp, n);
            Fix64 denom = Fix64.One / a.Mass + Fix64.One / b.Mass
                        + raPerpDotN * raPerpDotN / a.Inertia
                        + rbPerpDotN * rbPerpDotN / b.Inertia;

            Fix64 j = -(Fix64.One + e) * v_rel_n / denom;

            // Apply normal impulse.
            a.LinearVelocity    += n *  (j / a.Mass);
            b.LinearVelocity    -= n *  (j / b.Mass);
            a.AngularVelocity.Z += raPerpDotN * j / a.Inertia;
            b.AngularVelocity.Z -= rbPerpDotN * j / b.Inertia;

            // ── Tangential (friction) impulse ─────────────────────────────────────
            // Recalculate relative velocity with updated linear (angular unchanged here).
            FixVec2 tangent    = n.Perp();
            Fix64   v_rel_t    = FixVec2.Dot(v_rel, tangent);
            Fix64   raPerpDotT = FixVec2.Dot(raPerp, tangent);
            Fix64   rbPerpDotT = FixVec2.Dot(rbPerp, tangent);

            Fix64 denomT = Fix64.One / a.Mass + Fix64.One / b.Mass
                         + raPerpDotT * raPerpDotT / a.Inertia
                         + rbPerpDotT * rbPerpDotT / b.Inertia;

            Fix64 jt_raw   = -v_rel_t / denomT;
            Fix64 avgFrict = (a.SlidingFriction + b.SlidingFriction) * Fix64.Half;
            Fix64 maxFrict = Fix64.Abs(j) * avgFrict;
            Fix64 jt       = Clamp(jt_raw, -maxFrict, maxFrict);

            a.LinearVelocity    += tangent *  (jt / a.Mass);
            b.LinearVelocity    -= tangent *  (jt / b.Mass);
            a.AngularVelocity.Z += raPerpDotT * jt / a.Inertia;
            b.AngularVelocity.Z -= rbPerpDotT * jt / b.Inertia;
        }

        // ── Ball–cushion collision ────────────────────────────────────────────────

        /// <summary>
        /// Resolves a ball-cushion collision using the outward normal of the
        /// struck sub-segment (or vertex), as determined by CCD detection.
        /// Delegates to the overload with <paramref name="segmentRestitution"/> = 1
        /// (backwards-compatible; behaves identically to the original method).
        /// </summary>
        public static void ResolveBallCushion(Ball ball, FixVec2 hitNormal)
            => ResolveBallCushion(ball, hitNormal, Fix64.One);

        /// <summary>
        /// Resolves a ball-cushion collision, blending the ball's own restitution
        /// with the cushion segment's <paramref name="segmentRestitution"/>.
        /// The effective restitution is <c>Min(ball.Restitution, segmentRestitution)</c>.
        /// </summary>
        /// <param name="ball">The ball that struck the cushion.</param>
        /// <param name="hitNormal">Outward normal of the struck sub-segment or vertex.</param>
        /// <param name="segmentRestitution">
        /// Restitution of the struck <see cref="Segment"/> (0–1).
        /// Use <see cref="Segment.Restitution"/> from the TOI result.
        /// </param>
        public static void ResolveBallCushion(Ball ball, FixVec2 hitNormal, Fix64 segmentRestitution)
        {
            FixVec2 n = hitNormal;

            // Guard: skip if the hit normal is degenerate (zero-length or computed from
            // a vertex where the ball centre coincided with the contact point).
            if (n == FixVec2.Zero) return;

            // Vector from ball centre to contact point (on ball surface toward wall).
            FixVec2 r      = -n * ball.Radius;
            FixVec2 rPerp  = r.Perp();

            // Velocity at contact point (ω.Z drives the in-plane angular contribution).
            FixVec2 v_contact = ball.LinearVelocity + ball.AngularVelocity.Z * rPerp;
            Fix64   v_rel_n   = FixVec2.Dot(v_contact, n);
            if (v_rel_n >= Fix64.Zero) return;

            // Effective restitution: take the minimum of ball and cushion values so that
            // setting either to 0 yields a fully inelastic normal response.
            Fix64 e          = Fix64.Min(ball.Restitution, segmentRestitution);
            Fix64 rPerpDotN  = FixVec2.Dot(rPerp, n);
            Fix64 denomN     = Fix64.One / ball.Mass + rPerpDotN * rPerpDotN / ball.Inertia;
            Fix64 jn         = -(Fix64.One + e) * v_rel_n / denomN;

            ball.LinearVelocity    += n * (jn / ball.Mass);
            ball.AngularVelocity.Z += rPerpDotN * jn / ball.Inertia;

            // ── Tangential friction ───────────────────────────────────────────────
            FixVec2 tangent    = n.Perp();
            Fix64   v_rel_t    = FixVec2.Dot(v_contact, tangent);
            Fix64   rPerpDotT  = FixVec2.Dot(rPerp, tangent);
            Fix64   denomT     = Fix64.One / ball.Mass + rPerpDotT * rPerpDotT / ball.Inertia;
            Fix64   jt_raw     = -v_rel_t / denomT;
            Fix64   maxFrict   = Fix64.Abs(jn) * ball.SlidingFriction;
            Fix64   jt         = Clamp(jt_raw, -maxFrict, maxFrict);

            ball.LinearVelocity    += tangent * (jt / ball.Mass);
            ball.AngularVelocity.Z += rPerpDotT * jt / ball.Inertia;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
