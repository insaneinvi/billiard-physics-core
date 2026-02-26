using System.Collections.Generic;

namespace BilliardPhysics
{
    public class PhysicsWorld2D
    {
        // ── Internal state ────────────────────────────────────────────────────────
        private readonly List<Ball>    _balls          = new List<Ball>();
        private readonly List<Segment> _tableSegments  = new List<Segment>();
        private readonly List<Pocket>  _pockets        = new List<Pocket>();

        private Fix64 _fixedDt   = Fix64.One / Fix64.From(60);
        private int   _nextBallId;

        private const int MaxSubSteps = 20;

        /// <summary>Fraction of pocket radius within which a slow ball is captured.</summary>
        private static readonly Fix64 CaptureRadiusFactor = Fix64.Half;

        // Small epsilon added to remaining time after a collision to avoid re-triggering
        // the same contact in the next sub-step.  The value (1e-5 s) is large enough to
        // push the ball just past the contact surface in fixed-point arithmetic, yet small
        // enough that it does not visibly affect the simulation at 60 Hz.
        private static readonly Fix64 CollisionEpsilon = Fix64.From(1) / Fix64.From(100000);

        // ── Public read-only views ────────────────────────────────────────────────
        public IReadOnlyList<Ball>    Balls         => _balls;
        public IReadOnlyList<Segment> TableSegments => _tableSegments;
        public IReadOnlyList<Pocket>  Pockets       => _pockets;

        // ── Mutation helpers ──────────────────────────────────────────────────────

        public void AddBall(Ball ball)
        {
            ball.Id = _nextBallId++;
            _balls.Add(ball);
        }

        public void AddSegment(Segment seg)   => _tableSegments.Add(seg);
        public void AddPocket(Pocket pocket)  => _pockets.Add(pocket);

        public void SetTableSegments(IEnumerable<Segment> segs)
        {
            _tableSegments.Clear();
            _tableSegments.AddRange(segs);
        }

        // ── Simulation step ───────────────────────────────────────────────────────

        public void Step()
        {
            Fix64 remainingTime = _fixedDt;
            int   subSteps      = 0;

            while (remainingTime > Fix64.Zero && subSteps < MaxSubSteps)
            {
                CCDSystem.TOIResult result = CCDSystem.FindEarliestCollision(
                    _balls, _tableSegments, _pockets, remainingTime);

                if (!result.Hit)
                {
                    // No collision: advance all balls for the full remaining time.
                    foreach (Ball ball in _balls)
                        MotionSimulator.Step(ball, remainingTime);
                    break;
                }

                // Advance to just before the collision.
                Fix64 advanceTime = result.TOI;
                if (advanceTime > Fix64.Zero)
                {
                    foreach (Ball ball in _balls)
                        MotionSimulator.Step(ball, advanceTime);
                }

                // Resolve the collision.
                if (result.IsBallBall)
                {
                    Ball ballA = FindBallById(result.BallA);
                    Ball ballB = FindBallById(result.BallB);
                    if (ballA != null && ballB != null)
                        ImpulseResolver.ResolveBallBall(ballA, ballB);
                }
                else
                {
                    Ball ball = FindBallById(result.BallA);
                    if (ball != null)
                        ImpulseResolver.ResolveBallCushion(ball, result.Segment);
                }

                // Consume advance time plus a small safety margin.
                remainingTime -= advanceTime + CollisionEpsilon;

                CheckPocketCaptures();
                subSteps++;
            }
        }

        // ── Cue strike ────────────────────────────────────────────────────────────

        public void ApplyCueStrike(Ball ball, FixVec2 direction, Fix64 strength, Fix64 spinX, Fix64 spinY)
        {
            CueStrike.Apply(ball, direction, strength, spinX, spinY);
        }

        // ── Pocket capture ────────────────────────────────────────────────────────

        private void CheckPocketCaptures()
        {
            foreach (Ball ball in _balls)
            {
                if (ball.IsPocketed) continue;

                foreach (Pocket pocket in _pockets)
                {
                    Fix64 dist = FixVec2.Distance(ball.Position, pocket.Center);
                    if (dist < pocket.Radius * CaptureRadiusFactor &&
                        ball.LinearVelocity.Magnitude < pocket.ReboundVelocityThreshold)
                    {
                        ball.IsPocketed       = true;
                        ball.LinearVelocity   = FixVec2.Zero;
                        ball.AngularVelocity  = Fix64.Zero;
                        break;
                    }
                }
            }
        }

        // ── Reset ─────────────────────────────────────────────────────────────────

        public void Reset()
        {
            foreach (Ball ball in _balls)
                ball.Reset();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private Ball FindBallById(int id)
        {
            foreach (Ball ball in _balls)
                if (ball.Id == id) return ball;
            return null;
        }
    }
}
