using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    public class PhysicsWorld2D
    {
        // ── Physics configuration ─────────────────────────────────────────────────

        /// <summary>
        /// Additional rolling-resistance coefficient for the table surface (dimensionless).
        /// This is added to each ball's own <see cref="Ball.RollingFriction"/> and
        /// <see cref="Ball.SlidingFriction"/> inside <see cref="MotionSimulator.Step"/>,
        /// producing faster linear-velocity decay and a proportionally larger
        /// translation-to-rotation coupling on <c>AngularVelocity.Y</c> (and X).
        /// <para/>
        /// Simplified model: the table friction is treated as an isotropic Coulomb
        /// friction coefficient on top of per-ball values.  During pure rolling the
        /// rolling constraint <c>ω.Y = +Lv.X / R</c> automatically couples any extra
        /// linear deceleration into a matching change of Y-axis angular velocity.
        /// <para/>
        /// Default = 0 (no extra friction, preserves existing per-ball behaviour).
        /// Typical useful range: 0.005–0.05.
        /// </summary>
        public Fix64 TableFriction = Fix64.Zero;

        // ── Internal state ────────────────────────────────────────────────────────
        private readonly List<Ball>    _balls          = new List<Ball>();
        private readonly List<Segment> _tableSegments  = new List<Segment>();
        private readonly List<Pocket>  _pockets        = new List<Pocket>();

        // Spatial grid for segment broadphase; rebuilt when segments change.
        private SegmentGrid _segmentGrid;
        private bool        _segmentGridDirty = true;

        private Fix64 _fixedDt   = Fix64.One / Fix64.From(60);
        private int   _nextBallId;

        private const int MaxSubSteps = 20;

        private static readonly Fix64 CaptureRadiusFactor = Fix64.One;
        // A factor of 1 means the ball is captured as soon as its centre enters
        // the full pocket radius.  Formerly 0.5 (half radius), which was too restrictive.

        // Small epsilon added to remaining time after a collision to avoid re-triggering
        // the same contact in the next sub-step.  The value (1e-5 s) is large enough to
        // push the ball just past the contact surface in fixed-point arithmetic, yet small
        // enough that it does not visibly affect the simulation at 60 Hz.
        private static readonly Fix64 CollisionEpsilon = Fix64.From(1) / Fix64.From(100000);

        // ── Performance stats (updated each Step call) ────────────────────────────

        /// <summary>
        /// Number of ball–segment narrow-phase tests performed during the last
        /// <see cref="Step"/> call.  Compare with <c>Balls.Count × TableSegments.Count</c>
        /// to gauge how much the spatial grid reduces unnecessary work.
        /// </summary>
        public int LastStepNarrowPhaseSegmentCalls { get; private set; }

        /// <summary>
        /// Wall-clock milliseconds spent inside <see cref="CCDSystem.FindEarliestCollision"/>
        /// (all substeps combined) during the last <see cref="Step"/> call.
        /// </summary>
        public float LastStepFindCollisionMs { get; private set; }

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

        public void AddSegment(Segment seg)
        {
            _tableSegments.Add(seg);
            _segmentGridDirty = true;
        }
        public void AddPocket(Pocket pocket)  => _pockets.Add(pocket);

        public void SetTableSegments(IEnumerable<Segment> segs)
        {
            _tableSegments.Clear();
            _tableSegments.AddRange(segs);
            _segmentGridDirty = true;
        }

        // ── Simulation step ───────────────────────────────────────────────────────

        public void Step()
        {
            // (Re)build the segment grid if segments have changed since last step.
            if (_segmentGridDirty)
            {
                _segmentGrid      = _tableSegments.Count > 0 ? new SegmentGrid(_tableSegments) : null;
                _segmentGridDirty = false;
            }

            CCDSystem.ResetStats();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            Fix64 remainingTime = _fixedDt;
            int   subSteps      = 0;

            while (remainingTime > Fix64.Zero && subSteps < MaxSubSteps)
            {
                CCDSystem.TOIResult result = CCDSystem.FindEarliestCollision(
                    _balls, _tableSegments, _pockets, remainingTime, _segmentGrid);

                if (!result.Hit)
                {
                    // No collision: advance all balls for the full remaining time.
                    foreach (Ball ball in _balls)
                        MotionSimulator.Step(ball, remainingTime, TableFriction);
                    CheckPocketCaptures();
                    break;
                }

                // Advance to just before the collision.
                Fix64 advanceTime = result.TOI;
                if (advanceTime > Fix64.Zero)
                {
                    foreach (Ball ball in _balls)
                        MotionSimulator.Step(ball, advanceTime, TableFriction);
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
                        ImpulseResolver.ResolveBallCushion(ball, result.HitNormal,
                            result.Segment != null ? result.Segment.Restitution : Fix64.One);
                }

                // Consume advance time plus a small safety margin.
                remainingTime -= advanceTime + CollisionEpsilon;

                CheckPocketCaptures();
                subSteps++;
            }

            stopwatch.Stop();
            LastStepNarrowPhaseSegmentCalls = CCDSystem.NarrowPhaseSegmentCalls;
            LastStepFindCollisionMs         = (float)stopwatch.Elapsed.TotalMilliseconds;
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
                    if (dist < pocket.Radius * CaptureRadiusFactor)
                    {
                        ball.IsPocketed       = true;
                        ball.LinearVelocity   = FixVec2.Zero;
                        ball.AngularVelocity  = FixVec3.Zero;
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

        // ── Debug draw ────────────────────────────────────────────────────────────

        private PhysicsWorld2DDebug _debugVisualiser;

        /// <summary>
        /// Enables or disables runtime debug drawing in the Game view.
        /// When enabled, table boundary segments and pocket geometry are visualised
        /// each frame via <see cref="Camera.onPostRender"/>.
        /// Call <c>SetDebug(false)</c> when the world is no longer in use to unsubscribe
        /// from the render callback.
        /// </summary>
        /// <remarks>
        /// This method is retained for backwards compatibility.
        /// Prefer constructing a <see cref="PhysicsWorld2DDebug"/> directly for finer
        /// control over what is visualised.
        /// </remarks>
        [System.Obsolete("Use PhysicsWorld2DDebug directly for debug visualisation.")]
        public void SetDebug(bool enable)
        {
            if (enable)
            {
                if (_debugVisualiser == null)
                    _debugVisualiser = new PhysicsWorld2DDebug();

                _debugVisualiser.SetTableGeometry(_tableSegments, _pockets);
                _debugVisualiser.SetBalls(_balls);
                _debugVisualiser.SetDebug(true);
            }
            else
            {
                _debugVisualiser?.SetDebug(false);
            }
        }
    }
}
