namespace BilliardPhysics.AI
{
    /// <summary>
    /// Immutable snapshot of a single ball's physics state.
    /// Used to capture and restore the table configuration before and after simulation.
    /// </summary>
    public sealed class BallState
    {
        public int     Id           { get; }
        public FixVec2 Position     { get; }
        public FixVec2 Velocity     { get; }
        public FixVec3 Angular      { get; }
        public bool    IsPocketed   { get; }
        public bool    IsMotionless { get; }

        public BallState(int id, FixVec2 position, FixVec2 velocity, FixVec3 angular,
                         bool isPocketed, bool isMotionless)
        {
            Id           = id;
            Position     = position;
            Velocity     = velocity;
            Angular      = angular;
            IsPocketed   = isPocketed;
            IsMotionless = isMotionless;
        }

        /// <summary>Creates a snapshot from a live <see cref="Ball"/>.</summary>
        public static BallState FromBall(Ball ball)
            => new BallState(ball.Id, ball.Position, ball.LinearVelocity,
                             ball.AngularVelocity, ball.IsPocketed, ball.IsMotionless);
    }
}
