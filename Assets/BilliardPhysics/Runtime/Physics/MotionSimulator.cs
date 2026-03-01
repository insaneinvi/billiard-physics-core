namespace BilliardPhysics
{
    /// <summary>
    /// Advances ball linear and angular motion under friction, without resolving collisions.
    /// Coordinate convention: Z-down (+Z points toward the table; table normal n = (0,0,-1)).
    /// Ball center-to-contact-point vector: r = (0, 0, +Radius) (points in +Z, i.e. downward).
    /// Use <see cref="BilliardPhysics.Runtime.ViewTool.PhysicsToView.IntegrateRotation"/> to
    /// convert AngularVelocity into a Unity transform.rotation each frame.
    /// </summary>
    public static class MotionSimulator
    {
        /// <summary>Gravitational deceleration used to scale friction forces (units/s²).</summary>
        public static readonly Fix64 Gravity = Fix64.From(9);

        // Velocity / angular-velocity magnitude below which we treat the quantity as zero.
        private static readonly Fix64 Epsilon = Fix64.From(1) / Fix64.From(1000);

        public static void Step(Ball ball, Fix64 dt)
            => Step(ball, dt, Fix64.Zero);

        /// <summary>
        /// Advances ball state by <paramref name="dt"/> seconds, applying table-surface
        /// friction in addition to per-ball friction coefficients.
        /// </summary>
        /// <param name="ball">The ball to advance.</param>
        /// <param name="dt">Time step in seconds.</param>
        /// <param name="tableFriction">
        /// Additional rolling-resistance coefficient contributed by the table surface
        /// (see <see cref="PhysicsWorld2D.TableFriction"/>).  Default = 0 (no extra friction).
        /// During the rolling phase this is added to <see cref="Ball.RollingFriction"/> to
        /// increase linear-velocity decay; the rolling-contact constraint
        /// (<c>ω.Y = −Lv.X / R</c>) then automatically couples the linear deceleration
        /// into a proportional change of the Y-axis angular velocity.
        /// During the sliding phase this is added to <see cref="Ball.SlidingFriction"/>
        /// so that the translation-to-rotation impulse is also proportionally larger.
        /// </param>
        public static void Step(Ball ball, Fix64 dt, Fix64 tableFriction)
        {
            if (ball.IsPocketed) return;

            Fix64 speed    = ball.LinearVelocity.Magnitude;
            Fix64 omegaSqr = ball.AngularVelocity.SqrMagnitude;

            bool linearStopped  = speed    < Epsilon;
            bool angularStopped = omegaSqr < Epsilon * Epsilon;  // |ω| < Epsilon

            if (linearStopped && angularStopped)
            {
                ball.IsMotionless = true;
                return;
            }

            ball.IsMotionless = false;

            Fix64 R          = ball.Radius;
            Fix64 invMass    = Fix64.One / ball.Mass;
            Fix64 invInertia = Fix64.One / ball.Inertia;

            // ── Ball-table friction coupling (sliding → rolling) ───────────────────
            // r = (0, 0, R)  [contact point from ball center; +Z is down in Z-down frame]
            // ω × r = (ω.Y*R, -ω.X*R, 0)
            // Tangential slip at contact: v_t = (Lv.X + ω.Y*R,  Lv.Y - ω.X*R)
            Fix64 vtX = ball.LinearVelocity.X + ball.AngularVelocity.Y * R;
            Fix64 vtY = ball.LinearVelocity.Y - ball.AngularVelocity.X * R;
            Fix64 slip = Fix64.Sqrt(vtX * vtX + vtY * vtY);

            if (slip > Epsilon)
            {
                // Effective contact mass for a solid sphere rolling without slip:
                //   m_eff = m * I / (m * R² + I)  →  2m/7 for I = 2/5 m R²
                Fix64 mEff = ball.Mass * ball.Inertia /
                             (ball.Mass * R * R + ball.Inertia);

                // Coulomb limit: μ * m * g * dt; cap so we never over-correct.
                // tableFriction adds to the effective sliding coefficient so that table
                // surface friction contributes to the translation-to-rotation coupling.
                Fix64 jZero = slip * mEff;
                Fix64 jMax  = (ball.SlidingFriction + tableFriction) * ball.Mass * Gravity * dt;
                Fix64 jMag  = Fix64.Min(jZero, jMax);

                // Friction impulse direction: opposing the slip.
                Fix64 invSlip = Fix64.One / slip;
                Fix64 jx = -vtX * invSlip * jMag;
                Fix64 jy = -vtY * invSlip * jMag;

                // Update linear velocity (XY only).
                ball.LinearVelocity = new FixVec2(
                    ball.LinearVelocity.X + jx * invMass,
                    ball.LinearVelocity.Y + jy * invMass);

                // Update angular velocity: Δω = I⁻¹ · (r × J)
                // r × J = (0,0,R) × (jx,jy,0) = (-R·jy, R·jx, 0)
                ball.AngularVelocity.X += -R * jy * invInertia;
                ball.AngularVelocity.Y +=  R * jx * invInertia;
                // ω.Z is unaffected by table-normal friction (torque has no Z component).
            }

            // ── Spin friction: decay the Z (side-spin) component ──────────────────
            Fix64 spinDecay  = ball.SpinFriction * Gravity * dt;
            Fix64 omegaZAbs  = Fix64.Abs(ball.AngularVelocity.Z);
            if (omegaZAbs <= spinDecay)
                ball.AngularVelocity.Z = Fix64.Zero;
            else
                ball.AngularVelocity.Z -= Fix64.Sign(ball.AngularVelocity.Z) * spinDecay;

            // ── Rolling friction: decelerate once contact slip ≈ 0 ────────────────
            // Recompute slip with the updated angular velocity.
            Fix64 vtX2   = ball.LinearVelocity.X + ball.AngularVelocity.Y * R;
            Fix64 vtY2   = ball.LinearVelocity.Y - ball.AngularVelocity.X * R;
            Fix64 newSlip = Fix64.Sqrt(vtX2 * vtX2 + vtY2 * vtY2);
            speed         = ball.LinearVelocity.Magnitude;

            if (newSlip < Epsilon)
            {
                if (speed > Epsilon)
                {
                    // Pure rolling: apply rolling resistance to linear velocity.
                    // tableFriction is added to ball.RollingFriction here; the rolling
                    // constraint (ω.Y = −Lv.X / R) couples this deceleration into a
                    // proportional change of the Y-axis angular velocity automatically.
                    Fix64 rollingDecel = (ball.RollingFriction + tableFriction) * Gravity;
                    Fix64 decelDt      = rollingDecel * dt;

                    if (speed <= decelDt)
                    {
                        ball.LinearVelocity  = FixVec2.Zero;
                        ball.AngularVelocity.X = Fix64.Zero;
                        ball.AngularVelocity.Y = Fix64.Zero;
                    }
                    else
                    {
                        FixVec2 dir = ball.LinearVelocity.Normalized;
                        ball.LinearVelocity -= dir * decelDt;

                        // Keep XY angular components in sync with linear velocity.
                        // Rolling condition: Lv.X = -ω.Y*R, Lv.Y = ω.X*R
                        Fix64 invR = Fix64.One / R;
                        ball.AngularVelocity.Y = -ball.LinearVelocity.X * invR;
                        ball.AngularVelocity.X =  ball.LinearVelocity.Y * invR;
                    }
                }
                else
                {
                    // Linear stopped and slip is zero: zero out XY spin.
                    ball.AngularVelocity.X = Fix64.Zero;
                    ball.AngularVelocity.Y = Fix64.Zero;
                }
            }

            // ── Clamp tiny residuals ──────────────────────────────────────────────
            if (ball.LinearVelocity.Magnitude < Epsilon)
                ball.LinearVelocity = FixVec2.Zero;
            if (Fix64.Abs(ball.AngularVelocity.Z) < Epsilon)
                ball.AngularVelocity.Z = Fix64.Zero;

            // ── Integrate position ────────────────────────────────────────────────
            ball.Position += ball.LinearVelocity * dt;
        }
    }
}
