namespace BilliardPhysics
{
    /// <summary>
    /// Advances ball linear and angular motion under friction, without resolving collisions.
    /// </summary>
    public static class MotionSimulator
    {
        /// <summary>Gravitational deceleration used to scale friction forces (units/s²).</summary>
        public static readonly Fix64 Gravity = Fix64.From(9);

        // Velocity below which we treat the ball as stopped.
        private static readonly Fix64 Epsilon = Fix64.From(1) / Fix64.From(1000);

        public static void Step(Ball ball, Fix64 dt)
        {
            if (ball.IsPocketed) return;

            Fix64 speed = ball.LinearVelocity.Magnitude;
            Fix64 angAbs = Fix64.Abs(ball.AngularVelocity);

            bool linearStopped  = speed  < Epsilon;
            bool angularStopped = angAbs < Epsilon;

            if (linearStopped && angularStopped) return;

            // Rolling condition: |v| ≈ |ω| * r
            Fix64 rollingSpeed = angAbs * ball.Radius;
            bool  isRolling    = Fix64.Abs(speed - rollingSpeed) < Epsilon;

            if (isRolling || linearStopped)
            {
                // ── Rolling phase ─────────────────────────────────────────────────
                Fix64 rollingDecel = ball.RollingFriction * Gravity;
                Fix64 decelDt      = rollingDecel * dt;

                if (!linearStopped)
                {
                    if (speed <= decelDt)
                    {
                        ball.LinearVelocity  = FixVec2.Zero;
                        ball.AngularVelocity = Fix64.Zero;
                    }
                    else
                    {
                        FixVec2 dir = ball.LinearVelocity.Normalized;
                        ball.LinearVelocity  -= dir * decelDt;
                        // Keep angular in sync with linear speed.
                        Fix64 newSpeed = ball.LinearVelocity.Magnitude;
                        if (ball.Radius > Fix64.Zero)
                            ball.AngularVelocity = Fix64.Sign(ball.AngularVelocity) * newSpeed / ball.Radius;
                    }
                }
                else
                {
                    // Pure spin coming to rest.
                    Fix64 spinDecay = ball.SpinFriction * Gravity * dt;
                    if (angAbs <= spinDecay)
                        ball.AngularVelocity = Fix64.Zero;
                    else
                        ball.AngularVelocity -= Fix64.Sign(ball.AngularVelocity) * spinDecay;
                }
            }
            else
            {
                // ── Sliding phase ──────────────────────────────────────────────────
                Fix64 slidingDecel = ball.SlidingFriction * Gravity;
                Fix64 decelDt      = slidingDecel * dt;

                // Decelerate linear velocity.
                if (!linearStopped)
                {
                    if (speed <= decelDt)
                        ball.LinearVelocity = FixVec2.Zero;
                    else
                    {
                        FixVec2 dir = ball.LinearVelocity.Normalized;
                        ball.LinearVelocity -= dir * decelDt;
                    }
                }

                // Angular velocity spins toward rolling: friction torque accelerates ω.
                if (ball.Radius > Fix64.Zero)
                {
                    Fix64 angularAccel = slidingDecel / ball.Radius;
                    Fix64 targetAngular = (linearStopped ? Fix64.Zero : ball.LinearVelocity.Magnitude) / ball.Radius;

                    if (ball.AngularVelocity < targetAngular)
                    {
                        ball.AngularVelocity += angularAccel * dt;
                        if (ball.AngularVelocity > targetAngular)
                            ball.AngularVelocity = targetAngular;
                    }
                    else if (ball.AngularVelocity > targetAngular)
                    {
                        ball.AngularVelocity -= angularAccel * dt;
                        if (ball.AngularVelocity < targetAngular)
                            ball.AngularVelocity = targetAngular;
                    }
                }

                // Extra spin friction decay.
                Fix64 spinDecay = ball.SpinFriction * Gravity * dt;
                Fix64 newAngAbs = Fix64.Abs(ball.AngularVelocity);
                if (newAngAbs <= spinDecay)
                    ball.AngularVelocity = Fix64.Zero;
                else
                    ball.AngularVelocity -= Fix64.Sign(ball.AngularVelocity) * spinDecay;
            }

            // Clamp tiny residuals.
            if (ball.LinearVelocity.Magnitude < Epsilon)
                ball.LinearVelocity = FixVec2.Zero;
            if (Fix64.Abs(ball.AngularVelocity) < Epsilon)
                ball.AngularVelocity = Fix64.Zero;

            // Integrate position.
            ball.Position += ball.LinearVelocity * dt;
        }
    }
}
