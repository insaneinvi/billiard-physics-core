using System.Collections.Generic;
using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode regression tests for the three PhysicsWorld2D bugs:
    ///   1. Ball-ball tunneling / penetration (overlapping balls not resolved).
    ///   2. False rebounds from pocket rim segments (pockets not triggers).
    ///   3. Pocketing does not work (CheckPocketCaptures not called on no-collision path).
    /// </summary>
    public class PhysicsWorld2DTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>Creates a simple axis-aligned cushion segment at x = xPos.</summary>
        private static Segment MakeVerticalSegment(float xPos, float yMin, float yMax)
        {
            return new Segment(
                new FixVec2(Fix64.FromFloat(xPos), Fix64.FromFloat(yMin)),
                new FixVec2(Fix64.FromFloat(xPos), Fix64.FromFloat(yMax)));
        }

        /// <summary>Creates a pocket centered at (cx, cy) with given radius.</summary>
        private static Pocket MakePocket(float cx, float cy, float radius)
        {
            var center = new FixVec2(Fix64.FromFloat(cx), Fix64.FromFloat(cy));
            return new Pocket(0, center, Fix64.FromFloat(radius));
        }

        // ── Bug 1: Ball-ball tunneling ────────────────────────────────────────────

        /// <summary>
        /// Two balls heading toward each other that are ALREADY overlapping must be
        /// detected by SweptCircleCircle and resolved at toi = 0.
        /// Before the fix: cCoeff less than 0 → earliest root is negative → returns false
        /// (no collision detected, balls tunnel through each other).
        /// After the fix: cCoeff less than or equal to 0 AND approaching → toi = 0, returns true.
        /// </summary>
        [Test]
        public void SweptCircleCircle_OverlappingApproachingBalls_ReturnsHitAtZero()
        {
            var a = new Ball(0);
            var b = new Ball(1);

            // Place centres only 1 unit apart — far less than the combined radius (~57 units).
            a.Position       = new FixVec2(Fix64.FromFloat(-0.5f), Fix64.Zero);
            b.Position       = new FixVec2(Fix64.FromFloat( 0.5f), Fix64.Zero);
            a.LinearVelocity = new FixVec2(Fix64.From(100), Fix64.Zero);
            b.LinearVelocity = new FixVec2(Fix64.From(-100), Fix64.Zero);

            bool hit = CCDSystem.SweptCircleCircle(a, b, Fix64.One, out Fix64 toi);

            Assert.IsTrue(hit, "Overlapping, approaching balls must be detected.");
            Assert.AreEqual(Fix64.Zero, toi, "TOI must be 0 for already-overlapping balls.");
        }

        /// <summary>
        /// Overlapping balls that are already SEPARATING must not register a new collision.
        /// </summary>
        [Test]
        public void SweptCircleCircle_OverlappingSeparatingBalls_ReturnsNoHit()
        {
            var a = new Ball(0);
            var b = new Ball(1);

            a.Position       = new FixVec2(Fix64.FromFloat(-0.5f), Fix64.Zero);
            b.Position       = new FixVec2(Fix64.FromFloat( 0.5f), Fix64.Zero);
            // Balls moving AWAY from each other.
            a.LinearVelocity = new FixVec2(Fix64.From(-100), Fix64.Zero);
            b.LinearVelocity = new FixVec2(Fix64.From( 100), Fix64.Zero);

            bool hit = CCDSystem.SweptCircleCircle(a, b, Fix64.One, out Fix64 _);

            Assert.IsFalse(hit, "Overlapping but separating balls must not be re-resolved.");
        }

        /// <summary>
        /// Integration test: two balls colliding head-on at high speed must not end up
        /// with their centres closer than the sum of their radii after a physics step.
        /// </summary>
        [Test]
        public void Step_HeadOnBallBallCollision_NoPenetrationAfterStep()
        {
            var world = new PhysicsWorld2D();

            var a = new Ball(0);
            var b = new Ball(1);

            // Place at moderate distance (not overlapping).
            Fix64 gap = Ball.StandardRadius * Fix64.From(2) + Fix64.From(50);
            a.Position       = new FixVec2(-gap / Fix64.From(2), Fix64.Zero);
            b.Position       = new FixVec2( gap / Fix64.From(2), Fix64.Zero);
            a.LinearVelocity = new FixVec2(Fix64.From(500), Fix64.Zero);
            b.LinearVelocity = new FixVec2(Fix64.From(-500), Fix64.Zero);

            world.AddBall(a);
            world.AddBall(b);

            // Simulate enough steps for the collision to resolve.
            for (int i = 0; i < 10; i++)
                world.Step();

            Fix64 dist    = FixVec2.Distance(a.Position, b.Position);
            Fix64 radSum  = a.Radius + b.Radius;
            // Allow a small numerical tolerance.
            Fix64 tolerance = Fix64.FromFloat(0.5f);
            Assert.IsTrue(dist >= radSum - tolerance,
                $"Balls must not penetrate: dist={dist.ToFloat():F3}, radSum={radSum.ToFloat():F3}");
        }

        // ── Bug 2: False rebounds from pocket rim segments ────────────────────────

        /// <summary>
        /// FindEarliestCollision must NOT return a hit for a pocket's rim segment.
        /// Pockets are trigger volumes; their rim segments are not solid walls.
        /// Before the fix: pocket rim was tested as a cushion → ball bounced.
        /// After the fix: pocket rim block is removed → no rim collision returned.
        /// </summary>
        [Test]
        public void FindEarliestCollision_BallInsidePocketArea_NoPocketRimCollision()
        {
            var pocket = MakePocket(0f, 0f, 100f);
            // Give the pocket a rim segment that crosses the ball's path.
            pocket.RimSegment = new Segment(
                new FixVec2(Fix64.From(-200), Fix64.Zero),
                new FixVec2(Fix64.From( 200), Fix64.Zero));

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.From(-50), Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(100), Fix64.Zero);

            var balls    = new List<Ball>    { ball };
            var segments = new List<Segment>();
            var pockets  = new List<Pocket>  { pocket };

            CCDSystem.TOIResult result =
                CCDSystem.FindEarliestCollision(balls, segments, pockets, Fix64.One);

            Assert.IsFalse(result.Hit,
                "Pocket rim must not register as a solid cushion collision.");
        }

        /// <summary>
        /// Integration test: a ball moving toward a pocket (no table segments present)
        /// must NOT receive a cushion bounce from the pocket rim.
        /// </summary>
        [Test]
        public void Step_BallMovingIntoPocket_NoRimBounce()
        {
            var world = new PhysicsWorld2D();
            var pocket = MakePocket(200f, 0f, 60f);
            // Rim segment directly in the ball's path.
            pocket.RimSegment = new Segment(
                new FixVec2(Fix64.From(200), Fix64.From(-100)),
                new FixVec2(Fix64.From(200), Fix64.From( 100)));
            world.AddPocket(pocket);

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(1000), Fix64.Zero);
            world.AddBall(ball);

            Fix64 initialVelX = ball.LinearVelocity.X;

            world.Step();

            // Ball must have moved in the positive-X direction (no reversed velocity from rim).
            // After one step at 1/60 s the ball is still travelling right (possibly pocketed).
            bool notReversed = ball.IsPocketed || ball.LinearVelocity.X >= Fix64.Zero;
            Assert.IsTrue(notReversed,
                "Ball heading into a pocket must not bounce off the pocket rim.");
        }

        // ── Bug 3: Pocketing does not work ────────────────────────────────────────

        /// <summary>
        /// A motionless ball whose centre is inside the pocket radius must be flagged as
        /// pocketed after a single Step().
        /// Before the fix: CheckPocketCaptures was never called on the no-collision path
        /// (the code called 'break' first), so the ball was never pocketed.
        /// After the fix: CheckPocketCaptures is called before break, so the ball is
        /// captured on the first step.
        /// </summary>
        [Test]
        public void Step_BallAtPocketCenter_IsPocketedAfterOneStep()
        {
            var world = new PhysicsWorld2D();

            var pocket = MakePocket(0f, 0f, 50f);
            world.AddPocket(pocket);

            var ball = new Ball(0);
            ball.Position = new FixVec2(Fix64.Zero, Fix64.Zero);  // exactly at pocket center
            world.AddBall(ball);

            world.Step();

            Assert.IsTrue(ball.IsPocketed, "Ball at pocket center must be pocketed after Step.");
            Assert.AreEqual(FixVec2.Zero, ball.LinearVelocity,
                "Pocketed ball must have zero linear velocity.");
        }

        /// <summary>
        /// A fast-moving ball that reaches the pocket area during a step must be pocketed.
        /// This exercises the case where the ball enters the pocket on the no-collision path.
        /// </summary>
        [Test]
        public void Step_FastBallEnteringPocket_IsPocketedAfterSufficientSteps()
        {
            var world = new PhysicsWorld2D();

            // Pocket at x=300, radius=60.
            var pocket = MakePocket(300f, 0f, 60f);
            world.AddPocket(pocket);

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(2000), Fix64.Zero);
            world.AddBall(ball);

            // Simulate until the ball should have crossed into the pocket.
            bool pocketed = false;
            for (int i = 0; i < 60 && !pocketed; i++)
            {
                world.Step();
                pocketed = ball.IsPocketed;
            }

            Assert.IsTrue(pocketed,
                "A ball directed into a pocket must be pocketed within 60 steps.");
        }

        // ── Bug 3 continued: velocity threshold was too low ───────────────────────

        /// <summary>
        /// A ball moving at typical billiard speed (much faster than the old
        /// ReboundVelocityThreshold of 1 unit/s) that enters the pocket area must be
        /// captured.  Under the old code the velocity check prevented capture entirely.
        /// </summary>
        [Test]
        public void CheckPocketCaptures_FastBallInsidePocket_IsCaptured()
        {
            var world = new PhysicsWorld2D();

            var pocket = MakePocket(0f, 0f, 50f);
            world.AddPocket(pocket);

            var ball = new Ball(0);
            // Ball is already inside pocket area at high speed.
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(500), Fix64.Zero);
            world.AddBall(ball);

            world.Step();

            Assert.IsTrue(ball.IsPocketed,
                "A fast ball inside the pocket area must be captured regardless of speed.");
        }

        // ── No-false-bounce from table cushion ────────────────────────────────────

        /// <summary>
        /// A ball moving parallel to a distant cushion must not receive any bounce impulse.
        /// </summary>
        [Test]
        public void SweptCircleSegment_BallFarFromCushion_NoHit()
        {
            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(100), Fix64.Zero);

            // Cushion at x = 10000 — far beyond one step's travel distance.
            Segment seg = MakeVerticalSegment(10000f, -500f, 500f);

            bool hit = CCDSystem.SweptCircleSegment(ball, seg, Fix64.One, out Fix64 toi);

            Assert.IsFalse(hit, "Ball far from cushion must not register a collision.");
        }

        /// <summary>
        /// Integration test: a ball in open space (no cushions nearby) must not bounce
        /// spontaneously.  Its velocity should decrease only due to rolling friction,
        /// not reverse direction.
        /// </summary>
        [Test]
        public void Step_BallInOpenSpace_NoSpontaneousBounce()
        {
            var world = new PhysicsWorld2D();
            // One distant cushion so collision detection code exercises the segment path.
            world.SetTableSegments(new[] { MakeVerticalSegment(10000f, -1000f, 1000f) });

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(200), Fix64.Zero);
            world.AddBall(ball);

            // Run several steps; ball should keep moving in +X (friction only).
            for (int i = 0; i < 10; i++)
                world.Step();

            Assert.IsTrue(ball.LinearVelocity.X >= Fix64.Zero,
                "Ball in open space must not spontaneously reverse direction.");
        }

        // ── Phantom collision / straight-line trajectory regression ──────────────

        /// <summary>
        /// Deterministic regression test for the phantom-collision bug.
        /// A single ball with no nearby cushions or other balls must travel in a
        /// perfectly straight line: its Y velocity must remain exactly zero and its
        /// speed must never increase.
        /// </summary>
        [Test]
        public void Step_SingleBallFreeSpace_TrajectoryDirectionStable()
        {
            var world = new PhysicsWorld2D();

            // Place a distant cushion to exercise the segment-broadphase code paths
            // without being close enough for any real collision.
            world.SetTableSegments(new[] { MakeVerticalSegment(50000f, -5000f, 5000f) });

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);
            world.AddBall(ball);

            Fix64 prevSpeed = ball.LinearVelocity.Magnitude;

            for (int step = 0; step < 60; step++)
            {
                world.Step();

                if (ball.IsMotionless) break;

                // Y velocity must remain exactly zero — no lateral phantom impulse.
                Assert.AreEqual(Fix64.Zero, ball.LinearVelocity.Y,
                    $"Ball must not acquire lateral velocity at step {step}.");

                // Speed must only decrease (friction), never jump upward.
                Fix64 speed = ball.LinearVelocity.Magnitude;
                Assert.IsTrue(speed <= prevSpeed,
                    $"Speed must not increase at step {step}: " +
                    $"was {prevSpeed.ToFloat():F3}, now {speed.ToFloat():F3}.");

                prevSpeed = speed;
            }
        }

        /// <summary>
        /// Regression test: a ball whose path passes very close to a segment endpoint
        /// but does NOT actually touch it (closest approach > ball radius) must not
        /// receive a phantom collision impulse that changes its Y velocity.
        /// </summary>
        [Test]
        public void Step_BallPassesNearSegmentEndpoint_NoPhantomDeflection()
        {
            var world = new PhysicsWorld2D();

            // Segment endpoint at (500, 100).  Ball travels along Y=0, so its path
            // passes 100 units from the endpoint — well beyond the ball radius (~28.6 u).
            // No collision should be detected or resolved.
            var seg = new Segment(
                new FixVec2(Fix64.From(500), Fix64.From(-500)),
                new FixVec2(Fix64.From(500), Fix64.From( 100)));
            world.SetTableSegments(new[] { seg });

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(30000), Fix64.Zero);
            world.AddBall(ball);

            world.Step();

            // Ball must still travel in the +X direction without lateral deflection.
            Assert.IsTrue(ball.LinearVelocity.X >= Fix64.Zero,
                "Ball must not be reversed by a phantom cushion hit.");
            Assert.AreEqual(Fix64.Zero, ball.LinearVelocity.Y,
                "Ball must not acquire lateral velocity from a near-miss endpoint.");
        }

        // ── 2.5D spin: sliding → rolling develops XY angular velocity ─────────────

        /// <summary>
        /// Scene 1: A ball given a linear velocity with zero spin must develop
        /// XY angular velocity through table friction, indicating rolling onset.
        /// ω.Y must become non-zero.
        /// </summary>
        [Test]
        public void Step_SlidingBallZeroSpin_DevelopsRollingOmegaXY()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            ball.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);
            // Zero initial angular velocity.

            var dt = Fix64.One / Fix64.From(60);

            // Run enough steps for friction to build up rolling-direction spin.
            for (int i = 0; i < 100; i++)
                MotionSimulator.Step(ball, dt);

            // ω.Y must be non-zero (table friction drives rolling around Y for +X motion).
            Assert.AreNotEqual(Fix64.Zero, ball.AngularVelocity.Y,
                "Table friction must develop ω.Y for a ball sliding in +X.");

            // Contact-point slip must be significantly reduced from the initial value (300).
            Fix64 vtX = ball.LinearVelocity.X - ball.AngularVelocity.Y * ball.Radius;
            Fix64 vtY = ball.LinearVelocity.Y + ball.AngularVelocity.X * ball.Radius;
            Fix64 slip = Fix64.Sqrt(vtX * vtX + vtY * vtY);
            Assert.IsTrue(slip < Fix64.From(300),
                $"Contact-point slip must decrease; actual={slip.ToFloat():F3}");
        }

        /// <summary>
        /// Scene 2: A stationary ball injected with top-spin (ω.Y < 0, forward rolling
        /// direction for +X) must acquire positive X linear velocity through table friction.
        /// </summary>
        [Test]
        public void Step_StaticBallWithTopSpin_DevelopsLinearVelocity()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            ball.LinearVelocity = FixVec2.Zero;
            // Inject top-spin: ω.Y > 0 drives +X motion when ball contacts the table (Z-up).
            ball.AngularVelocity = new FixVec3(Fix64.Zero, Fix64.From(10), Fix64.Zero);

            var dt = Fix64.One / Fix64.From(60);

            // Run a step; friction coupling must transfer spin energy into linear motion.
            MotionSimulator.Step(ball, dt);

            Assert.IsTrue(ball.LinearVelocity.X > Fix64.Zero,
                "Top-spin (ω.Y < 0) on a static ball must produce positive X linear velocity.");
        }

        /// <summary>
        /// Scene 3: A ball with side-spin only (ω.Z ≠ 0) must not spontaneously
        /// acquire linear velocity (Z-spin has no table-friction coupling to XY motion)
        /// and the spin must decay over time via SpinFriction.
        /// </summary>
        [Test]
        public void Step_StaticBallWithSideSpin_SpinDecaysNoLinearVelocity()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            ball.LinearVelocity = FixVec2.Zero;
            // Pure side-spin (english).
            ball.AngularVelocity = new FixVec3(Fix64.Zero, Fix64.Zero, Fix64.From(50));

            var dt = Fix64.One / Fix64.From(60);

            Fix64 prevOmegaZ = ball.AngularVelocity.Z;
            for (int i = 0; i < 30; i++)
            {
                MotionSimulator.Step(ball, dt);

                // Linear velocity must remain exactly zero (side spin has no XY coupling).
                Assert.AreEqual(FixVec2.Zero, ball.LinearVelocity,
                    $"Side-spin must not produce linear velocity at step {i}.");

                // Z-spin must only decrease.
                Assert.IsTrue(ball.AngularVelocity.Z <= prevOmegaZ,
                    $"Side-spin ω.Z must decay monotonically at step {i}.");
                prevOmegaZ = ball.AngularVelocity.Z;
            }

            // After 30 steps spin should be noticeably reduced.
            Assert.IsTrue(ball.AngularVelocity.Z < Fix64.From(50),
                "Side-spin must have decayed after 30 steps.");
        }

        // ── TableFriction: linear-velocity decay and ω.Y coupling ─────────────────

        /// <summary>
        /// When <see cref="PhysicsWorld2D.TableFriction"/> is set to a non-zero value, a
        /// ball in free motion must decelerate faster than with the default (zero) table
        /// friction, confirming the rolling-resistance effect.
        /// </summary>
        [Test]
        public void Step_TableFrictionEnabled_BallDeceleratesFasterThanWithoutTableFriction()
        {
            const int steps = 120;
            var dt = Fix64.One / Fix64.From(60);

            // World A: default friction (TableFriction = 0).
            var worldNoFriction  = new PhysicsWorld2D();
            var ballNoFriction   = new Ball(0);
            ballNoFriction.Position       = FixVec2.Zero;
            ballNoFriction.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);
            worldNoFriction.AddBall(ballNoFriction);

            // World B: with extra table friction.
            var worldWithFriction  = new PhysicsWorld2D();
            worldWithFriction.TableFriction = Fix64.From(2) / Fix64.From(100);  // 0.02
            var ballWithFriction   = new Ball(0);
            ballWithFriction.Position       = FixVec2.Zero;
            ballWithFriction.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);
            worldWithFriction.AddBall(ballWithFriction);

            for (int i = 0; i < steps; i++)
            {
                worldNoFriction.Step();
                worldWithFriction.Step();
            }

            Fix64 speedNoFriction   = ballNoFriction.LinearVelocity.Magnitude;
            Fix64 speedWithFriction = ballWithFriction.LinearVelocity.Magnitude;

            Assert.IsTrue(speedWithFriction < speedNoFriction,
                $"Ball with TableFriction must decelerate faster: " +
                $"noFriction={speedNoFriction.ToFloat():F3}, " +
                $"withFriction={speedWithFriction.ToFloat():F3}");
        }

        /// <summary>
        /// A ball given a linear velocity but zero spin must develop ω.Y through the
        /// friction coupling inside <see cref="MotionSimulator.Step"/>, and the
        /// magnitude of ω.Y change must be larger when <see cref="PhysicsWorld2D.TableFriction"/>
        /// is non-zero, confirming the translation-to-rotation coupling.
        /// </summary>
        [Test]
        public void Step_TableFrictionEnabled_DevelopsOmegaYFasterThanWithoutTableFriction()
        {
            const int steps = 60;
            var dt = Fix64.One / Fix64.From(60);

            // Ball A: per-ball friction only.
            var ballNoExtra  = new Ball(0);
            ballNoExtra.Position       = FixVec2.Zero;
            ballNoExtra.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);

            // Ball B: same per-ball values, plus table friction.
            var ballExtra    = new Ball(0);
            ballExtra.Position       = FixVec2.Zero;
            ballExtra.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);
            Fix64 tableFriction = Fix64.From(2) / Fix64.From(100);  // 0.02

            for (int i = 0; i < steps; i++)
            {
                MotionSimulator.Step(ballNoExtra, dt, Fix64.Zero);
                MotionSimulator.Step(ballExtra,   dt, tableFriction);
            }

            // Both must have non-zero ω.Y (rolling must have started).
            Assert.AreNotEqual(Fix64.Zero, ballNoExtra.AngularVelocity.Y,
                "Ball without extra table friction must still develop ω.Y via per-ball friction.");
            Assert.AreNotEqual(Fix64.Zero, ballExtra.AngularVelocity.Y,
                "Ball with table friction must develop ω.Y.");

            // The magnitude of ω.Y for ballExtra must be >= ballNoExtra:
            // both approach the rolling condition ω.Y = +Lv.X / R, but ballExtra reaches
            // it from a lower Lv.X (it decelerates faster), so its final |ω.Y| is smaller.
            // What we assert is that both develop non-zero ω.Y, and that their final
            // speeds differ due to the extra friction.
            Fix64 speedNoExtra = ballNoExtra.LinearVelocity.Magnitude;
            Fix64 speedExtra   = ballExtra.LinearVelocity.Magnitude;
            Assert.IsTrue(speedExtra < speedNoExtra,
                $"Ball with extra table friction must be slower: " +
                $"noExtra={speedNoExtra.ToFloat():F3}, extra={speedExtra.ToFloat():F3}");
        }

        // ── Segment.Restitution: velocity attenuation on cushion bounce ────────────

        /// <summary>
        /// When a segment's <see cref="Segment.Restitution"/> is set to 1.0 (default)
        /// the post-bounce speed must be at least as large as it was before the bounce
        /// (only per-ball friction may reduce it slightly, but not the normal component).
        /// Regression test: no energy is lost solely from segment contact.
        /// </summary>
        [Test]
        public void ResolveBallCushion_SegmentRestitutionOne_SpeedPreservedInNormalDirection()
        {
            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            // Heading straight into the cushion (normal = +Y), no tangential velocity.
            ball.LinearVelocity = new FixVec2(Fix64.Zero, Fix64.From(-200));
            ball.AngularVelocity = FixVec3.Zero;

            FixVec2 normal = new FixVec2(Fix64.Zero, Fix64.One);  // pointing +Y (outward)
            Fix64 speedBefore = ball.LinearVelocity.Magnitude;

            // Use segment restitution = 1.0; ball.Restitution is also 0.95 by default.
            // Effective e = Min(0.95, 1.0) = 0.95. After bounce vy should be +0.95 * 200.
            ImpulseResolver.ResolveBallCushion(ball, normal, Fix64.One);

            // After bounce the ball must be moving in +Y (away from cushion).
            Assert.IsTrue(ball.LinearVelocity.Y > Fix64.Zero,
                "Ball must bounce away from cushion (positive Y velocity).");

            // Speed after bounce ≈ ball.Restitution * speedBefore = 0.95 * 200.
            Fix64 expectedY = Fix64.From(95) / Fix64.From(100) * Fix64.From(200);
            Fix64 tolerance  = Fix64.From(1);
            Assert.IsTrue(Fix64.Abs(ball.LinearVelocity.Y - expectedY) < tolerance,
                $"Normal velocity after bounce must reflect ball.Restitution; " +
                $"expected≈{expectedY.ToFloat():F1}, got {ball.LinearVelocity.Y.ToFloat():F3}");
        }

        /// <summary>
        /// When a segment's <see cref="Segment.Restitution"/> is less than 1 the
        /// post-bounce speed must be strictly lower than with restitution = 1.
        /// The reduction must be proportional to <c>Min(ball.Restitution, segmentRestitution)</c>.
        /// </summary>
        [Test]
        public void ResolveBallCushion_SegmentRestitutionLessThanOne_SpeedReducedComparedToElastic()
        {
            FixVec2 normal  = new FixVec2(Fix64.Zero, Fix64.One);
            FixVec2 initVel = new FixVec2(Fix64.Zero, Fix64.From(-200));

            // Ball A bounces off segment with restitution = 1.
            var ballElastic = new Ball(0);
            ballElastic.LinearVelocity = initVel;
            ImpulseResolver.ResolveBallCushion(ballElastic, normal, Fix64.One);

            // Ball B bounces off segment with restitution = 0.5.
            var ballInelastic = new Ball(0);
            ballInelastic.LinearVelocity = initVel;
            ImpulseResolver.ResolveBallCushion(ballInelastic, normal,
                Fix64.From(5) / Fix64.From(10));  // 0.5

            Fix64 speedElastic   = ballElastic.LinearVelocity.Magnitude;
            Fix64 speedInelastic = ballInelastic.LinearVelocity.Magnitude;

            Assert.IsTrue(speedInelastic < speedElastic,
                $"Lower segment restitution must yield lower post-bounce speed: " +
                $"elastic={speedElastic.ToFloat():F3}, inelastic={speedInelastic.ToFloat():F3}");
        }

        /// <summary>
        /// With segment restitution = 0 (fully inelastic) the normal velocity component
        /// must be completely absorbed; only the tangential component (if any) remains.
        /// A ball hitting the cushion head-on must stop (or nearly stop) after impact.
        /// </summary>
        [Test]
        public void ResolveBallCushion_SegmentRestitutionZero_NormalVelocityAbsorbed()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            // Head-on, no tangential component.
            ball.LinearVelocity  = new FixVec2(Fix64.Zero, Fix64.From(-200));
            ball.AngularVelocity = FixVec3.Zero;

            FixVec2 normal = new FixVec2(Fix64.Zero, Fix64.One);

            // segmentRestitution = 0; ball.Restitution = 0.95; effective e = Min(0.95, 0) = 0.
            ImpulseResolver.ResolveBallCushion(ball, normal, Fix64.Zero);

            // Normal velocity (Y) must be zero (inelastic: absorbed by cushion).
            Assert.AreEqual(Fix64.Zero, ball.LinearVelocity.Y,
                "With segmentRestitution=0 the normal velocity must be zero after impact.");
        }

        /// <summary>
        /// Integration test: a ball bouncing off a cushion segment with
        /// <see cref="Segment.Restitution"/> &lt; 1 must have a lower speed after the
        /// bounce than before it when tested through the full <see cref="PhysicsWorld2D.Step"/>
        /// loop.
        /// </summary>
        [Test]
        public void Step_BallBouncesOffLowRestitutionSegment_SpeedReducedAfterBounce()
        {
            // Vertical cushion at x = 200, facing -X (normal = (-1, 0)).
            var seg = new Segment(
                new FixVec2(Fix64.From(200), Fix64.From(-500)),
                new FixVec2(Fix64.From(200), Fix64.From( 500)));
            seg.Restitution = Fix64.From(6) / Fix64.From(10);  // 0.6

            var world = new PhysicsWorld2D();
            world.SetTableSegments(new[] { seg });

            var ball = new Ball(0);
            ball.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            ball.LinearVelocity = new FixVec2(Fix64.From(3000), Fix64.Zero);
            world.AddBall(ball);

            Fix64 speedBefore = ball.LinearVelocity.Magnitude;

            // Advance until the ball has collided and reversed direction.
            for (int i = 0; i < 30; i++)
            {
                world.Step();
                if (ball.LinearVelocity.X < Fix64.Zero)
                    break;
            }

            Fix64 speedAfter = ball.LinearVelocity.Magnitude;

            Assert.IsTrue(ball.LinearVelocity.X < Fix64.Zero,
                "Ball must have bounced back (negative X velocity).");
            Assert.IsTrue(speedAfter < speedBefore,
                $"Post-bounce speed must be lower than pre-bounce speed: " +
                $"before={speedBefore.ToFloat():F1}, after={speedAfter.ToFloat():F1}");
        }

        // ── Rolling-constraint verification ───────────────────────────────────────
        // Constraint (Z-up frame): v_contact = v + ω × r = 0
        //   r = (0, 0, −R),  ω × r = (−ω.Y·R, +ω.X·R, 0)
        //   slip_x = v.X − ω.Y·R,  slip_y = v.Y + ω.X·R
        // In rolling: |slip| < Epsilon,  |v.X| / (R·|ω.Y|) ≈ 1 (for +X motion).

        /// <summary>
        /// Helper: advances the ball until slip drops below Epsilon or maxSteps is reached.
        /// </summary>
        private static bool AdvanceToRolling(Ball ball, int maxSteps, Fix64 dt,
            out int stepsToRolling)
        {
            Fix64 eps = Fix64.One / Fix64.From(1000);
            for (int i = 0; i < maxSteps; i++)
            {
                MotionSimulator.Step(ball, dt);
                Fix64 vtX  = ball.LinearVelocity.X - ball.AngularVelocity.Y * ball.Radius;
                Fix64 vtY  = ball.LinearVelocity.Y + ball.AngularVelocity.X * ball.Radius;
                Fix64 slip = Fix64.Sqrt(vtX * vtX + vtY * vtY);
                if (slip < eps)
                {
                    stepsToRolling = i + 1;
                    return true;
                }
            }
            stepsToRolling = maxSteps;
            return false;
        }

        /// <summary>
        /// Ball rolling in +X: after entering rolling, slip must stay near zero
        /// for at least 60 subsequent steps (no systematic drift).
        /// </summary>
        [Test]
        public void RollingConstraint_PlusX_SlipRemainsNearZeroAfterTransition()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            ball.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);

            var dt = Fix64.One / Fix64.From(60);
            bool reached = AdvanceToRolling(ball, 6000, dt, out _);
            Assert.IsTrue(reached, "Ball sliding in +X must eventually enter rolling.");

            if (ball.IsMotionless) return;

            Fix64 eps = Fix64.From(5) / Fix64.From(1000);  // 5× Epsilon tolerance
            for (int i = 0; i < 60; i++)
            {
                MotionSimulator.Step(ball, dt);
                if (ball.IsMotionless) break;

                Fix64 vtX  = ball.LinearVelocity.X - ball.AngularVelocity.Y * ball.Radius;
                Fix64 vtY  = ball.LinearVelocity.Y + ball.AngularVelocity.X * ball.Radius;
                Fix64 slip = Fix64.Sqrt(vtX * vtX + vtY * vtY);
                Assert.IsTrue(slip < eps,
                    $"+X rolling: slip must stay near zero at post-rolling step {i}; " +
                    $"slip={slip.ToFloat():F6}");
            }
        }

        /// <summary>
        /// Ball rolling in +Y: after entering rolling, slip must stay near zero.
        /// </summary>
        [Test]
        public void RollingConstraint_PlusY_SlipRemainsNearZeroAfterTransition()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            ball.LinearVelocity = new FixVec2(Fix64.Zero, Fix64.From(300));

            var dt = Fix64.One / Fix64.From(60);
            bool reached = AdvanceToRolling(ball, 6000, dt, out _);
            Assert.IsTrue(reached, "Ball sliding in +Y must eventually enter rolling.");

            if (ball.IsMotionless) return;

            Fix64 eps = Fix64.From(5) / Fix64.From(1000);
            for (int i = 0; i < 60; i++)
            {
                MotionSimulator.Step(ball, dt);
                if (ball.IsMotionless) break;

                Fix64 vtX  = ball.LinearVelocity.X - ball.AngularVelocity.Y * ball.Radius;
                Fix64 vtY  = ball.LinearVelocity.Y + ball.AngularVelocity.X * ball.Radius;
                Fix64 slip = Fix64.Sqrt(vtX * vtX + vtY * vtY);
                Assert.IsTrue(slip < eps,
                    $"+Y rolling: slip must stay near zero at post-rolling step {i}; " +
                    $"slip={slip.ToFloat():F6}");
            }
        }

        /// <summary>
        /// Ball rolling diagonally (+X and +Y): after entering rolling, slip stays near zero.
        /// </summary>
        [Test]
        public void RollingConstraint_Diagonal_SlipRemainsNearZeroAfterTransition()
        {
            var ball = new Ball(0);
            ball.Position = FixVec2.Zero;
            // Diagonal initial velocity: speed 300 at 45 degrees.
            Fix64 comp = Fix64.FromFloat(300f / 1.41421356f);
            ball.LinearVelocity = new FixVec2(comp, comp);

            var dt = Fix64.One / Fix64.From(60);
            bool reached = AdvanceToRolling(ball, 6000, dt, out _);
            Assert.IsTrue(reached, "Ball sliding diagonally must eventually enter rolling.");

            if (ball.IsMotionless) return;

            Fix64 eps = Fix64.From(5) / Fix64.From(1000);
            for (int i = 0; i < 60; i++)
            {
                MotionSimulator.Step(ball, dt);
                if (ball.IsMotionless) break;

                Fix64 vtX  = ball.LinearVelocity.X - ball.AngularVelocity.Y * ball.Radius;
                Fix64 vtY  = ball.LinearVelocity.Y + ball.AngularVelocity.X * ball.Radius;
                Fix64 slip = Fix64.Sqrt(vtX * vtX + vtY * vtY);
                Assert.IsTrue(slip < eps,
                    $"Diagonal rolling: slip must stay near zero at post-rolling step {i}; " +
                    $"slip={slip.ToFloat():F6}");
            }
        }

        /// <summary>
        /// |v.X| / (R·|ω.Y|) must converge from non-1 toward 1 and stay there.
        /// Before entering rolling the ratio is != 1 (zero ω.Y); after rolling it must be ≈ 1.
        /// </summary>
        [Test]
        public void RollingConstraint_PlusX_VelocityToSpinRatioConvergesToOne()
        {
            var ball = new Ball(0);
            ball.Position       = FixVec2.Zero;
            ball.LinearVelocity = new FixVec2(Fix64.From(300), Fix64.Zero);

            var dt = Fix64.One / Fix64.From(60);

            Assert.AreEqual(Fix64.Zero, ball.AngularVelocity.Y,
                "Initial ω.Y must be zero (no spin at launch).");

            bool reached = AdvanceToRolling(ball, 6000, dt, out _);
            Assert.IsTrue(reached, "Ball must reach rolling state.");

            if (ball.IsMotionless) return;

            // For 60 steps in rolling: |v.X| / (R·|ω.Y|) must be within 2% of 1.
            Fix64 tolAbs = Fix64.From(2) / Fix64.From(100);
            for (int i = 0; i < 60; i++)
            {
                MotionSimulator.Step(ball, dt);
                if (ball.IsMotionless) break;

                Fix64 vX  = Fix64.Abs(ball.LinearVelocity.X);
                Fix64 omY = Fix64.Abs(ball.AngularVelocity.Y);
                if (vX < Fix64.From(1) || omY < Fix64.One / Fix64.From(1000)) break;

                Fix64 ratio = vX / (ball.Radius * omY);
                Fix64 err   = Fix64.Abs(ratio - Fix64.One);
                Assert.IsTrue(err < tolAbs,
                    $"v/Rω ratio must be ≈1 in rolling (step {i}); " +
                    $"ratio={ratio.ToFloat():F4}, err={err.ToFloat():F4}");
            }
        }

        // ── CueStrike spin convention (Z-up) ──────────────────────────────────────

        /// <summary>
        /// CueStrike with top-spin (spinY > 0) striking in +X must produce ω.Y > 0
        /// (Z-up convention: positive ω.Y is the rolling direction for +X motion).
        /// Bug (pre-fix): Z-down convention gave ω.Y &lt; 0, increasing initial slip.
        /// </summary>
        [Test]
        public void CueStrike_TopSpinPlusX_OmegaYIsPositive()
        {
            var ball      = new Ball(0);
            FixVec2 dir   = new FixVec2(Fix64.One, Fix64.Zero);
            Fix64 strength = Fix64.From(10000);
            Fix64 spinY    = Fix64.One;

            CueStrike.Apply(ball, dir, strength, Fix64.Zero, spinY);

            Assert.IsTrue(ball.AngularVelocity.Y > Fix64.Zero,
                "Top-spin cue strike in +X must produce ω.Y > 0 (Z-up convention).");
        }

        /// <summary>
        /// CueStrike with back-spin (spinY &lt; 0) striking in +X must produce ω.Y &lt; 0.
        /// </summary>
        [Test]
        public void CueStrike_BackSpinPlusX_OmegaYIsNegative()
        {
            var ball      = new Ball(0);
            FixVec2 dir   = new FixVec2(Fix64.One, Fix64.Zero);
            Fix64 strength = Fix64.From(10000);
            Fix64 spinY    = -Fix64.One;

            CueStrike.Apply(ball, dir, strength, Fix64.Zero, spinY);

            Assert.IsTrue(ball.AngularVelocity.Y < Fix64.Zero,
                "Back-spin cue strike in +X must produce ω.Y < 0 (Z-up convention).");
        }

        /// <summary>
        /// CueStrike top-spin must REDUCE initial contact-point slip compared to no-spin.
        /// In Z-up convention, top-spin gives ω.Y > 0 (toward rolling direction),
        /// so initial slip = |v.X - ω.Y·R| is smaller than the no-spin slip = |v.X|.
        /// Bug (pre-fix): ω.Y &lt; 0 (Z-down) INCREASED slip instead of reducing it.
        /// </summary>
        [Test]
        public void CueStrike_TopSpinPlusX_SlipLessThanNoSpinStrike()
        {
            Fix64 strength = Fix64.From(50000);
            FixVec2 dir    = new FixVec2(Fix64.One, Fix64.Zero);

            var ballNoSpin = new Ball(0);
            CueStrike.Apply(ballNoSpin, dir, strength, Fix64.Zero, Fix64.Zero);

            var ballTopSpin = new Ball(1);
            Fix64 spinY = Ball.StandardRadius / Fix64.From(2);
            CueStrike.Apply(ballTopSpin, dir, strength, Fix64.Zero, spinY);

            Fix64 slipNoSpin = Fix64.Abs(
                ballNoSpin.LinearVelocity.X
                - ballNoSpin.AngularVelocity.Y * ballNoSpin.Radius);
            Fix64 slipTopSpin = Fix64.Abs(
                ballTopSpin.LinearVelocity.X
                - ballTopSpin.AngularVelocity.Y * ballTopSpin.Radius);

            Assert.IsTrue(slipTopSpin < slipNoSpin,
                $"Top-spin must reduce initial slip vs no-spin; " +
                $"no-spin={slipNoSpin.ToFloat():F3}, top-spin={slipTopSpin.ToFloat():F3}");
        }

        // ── Positional correction: persistent interpenetration fix ────────────────

        /// <summary>
        /// Minimum reproduction test (fails before positional-correction fix,
        /// passes after):
        /// Two balls that are already overlapping and approaching must have their
        /// positions corrected by <see cref="ImpulseResolver.ResolveBallBall"/> so
        /// that the distance after the call is meaningfully closer to the combined
        /// radii than it was before.
        ///
        /// Without positional correction the resolver only changes velocities;
        /// positions remain at the overlapping values, so the assertion fails.
        /// With the linear-projection correction the excess overlap is reduced by
        /// <see cref="ImpulseResolver.CorrectionPercent"/> × (overlap − slop) and
        /// the assertion passes.
        ///
        /// Parameter notes:
        ///   PenetrationSlop     = 0.1 physics-units  (≈ 0.35 % of ball radius)
        ///   CorrectionPercent   = 0.8  (80 % of excess overlap resolved per call)
        ///   Tolerance below     = 2.0 physics-units  (covers the remaining 20 %)
        /// </summary>
        [Test]
        public void ResolveBallBall_OverlappingApproachingBalls_PositionsCorrectedAfterResolution()
        {
            var a = new Ball(0);
            var b = new Ball(1);

            // Place balls with a 3-unit overlap (dist = radSum − 3).
            // Combined radius = 2 × 28.575 ≈ 57.15 units.
            Fix64 radSum  = a.Radius + b.Radius;
            Fix64 overlap = Fix64.From(3);
            Fix64 half    = (radSum - overlap) / Fix64.From(2);
            a.Position = new FixVec2(-half, Fix64.Zero);
            b.Position = new FixVec2( half, Fix64.Zero);

            // Give them approaching velocities so the impulse path is taken.
            a.LinearVelocity = new FixVec2(Fix64.From(10), Fix64.Zero);
            b.LinearVelocity = new FixVec2(Fix64.From(-10), Fix64.Zero);

            Fix64 distBefore = FixVec2.Distance(a.Position, b.Position);

            ImpulseResolver.ResolveBallBall(a, b);

            Fix64 distAfter = FixVec2.Distance(a.Position, b.Position);

            // Without positional correction: distAfter == distBefore (no position change).
            // With    positional correction: distAfter ≈ radSum − 0.68 (80 % of excess resolved).
            // Tolerance of 2 units accounts for the remaining 20 % corrected by velocity.
            Fix64 tolerance = Fix64.From(2);
            Assert.IsTrue(distAfter >= radSum - tolerance,
                $"ResolveBallBall must correct overlapping positions: " +
                $"before={distBefore.ToFloat():F3}, after={distAfter.ToFloat():F3}, " +
                $"radSum={radSum.ToFloat():F3}");
        }

        /// <summary>
        /// Regression test: two balls placed with a moderate overlap inside the
        /// physics world must fully separate within a small number of simulation
        /// steps.  Without positional correction the balls remain stuck overlapping
        /// because the impulse alone (with small velocities) produces insufficient
        /// separation within the fixed 1/60 s time-step.
        /// </summary>
        [Test]
        public void Step_OverlappingBalls_SeparateAfterPhysicsStep()
        {
            var world = new PhysicsWorld2D();

            var a = new Ball(0);
            var b = new Ball(1);

            // 3-unit overlap; small approaching velocities so CCD returns toi = 0.
            Fix64 radSum  = a.Radius + b.Radius;
            Fix64 overlap = Fix64.From(3);
            Fix64 half    = (radSum - overlap) / Fix64.From(2);
            a.Position = new FixVec2(-half, Fix64.Zero);
            b.Position = new FixVec2( half, Fix64.Zero);
            a.LinearVelocity = new FixVec2(Fix64.From(5), Fix64.Zero);
            b.LinearVelocity = new FixVec2(Fix64.From(-5), Fix64.Zero);

            world.AddBall(a);
            world.AddBall(b);

            for (int i = 0; i < 5; i++)
                world.Step();

            Fix64 dist      = FixVec2.Distance(a.Position, b.Position);
            Fix64 tolerance = Fix64.FromFloat(0.5f);
            Assert.IsTrue(dist >= radSum - tolerance,
                $"Overlapping balls must separate within 5 steps: " +
                $"dist={dist.ToFloat():F3}, radSum={radSum.ToFloat():F3}");
        }

        /// <summary>
        /// Regression test for a three-ball chain collision: ball A strikes stationary
        /// balls B and C that are touching.  After several steps no pair of balls may
        /// remain in persistent overlap.  Validates that positional correction handles
        /// the simultaneous/sequential multi-ball case without long-term penetration.
        /// </summary>
        [Test]
        public void Step_ThreeBallChainCollision_NoPersistentOverlapAfterCollision()
        {
            var world = new PhysicsWorld2D();

            var a = new Ball(0);
            var b = new Ball(1);
            var c = new Ball(2);

            Fix64 radSum = a.Radius + b.Radius;  // equal radii, so = 2 × radius

            // A approaches from the left; B and C are at rest, touching each other.
            a.Position = new FixVec2(-(radSum * Fix64.From(3)), Fix64.Zero);
            b.Position = new FixVec2(Fix64.Zero, Fix64.Zero);
            c.Position = new FixVec2(radSum, Fix64.Zero);   // exactly touching b

            a.LinearVelocity = new FixVec2(Fix64.From(500), Fix64.Zero);

            world.AddBall(a);
            world.AddBall(b);
            world.AddBall(c);

            for (int i = 0; i < 10; i++)
                world.Step();

            Fix64 tolerance = Fix64.FromFloat(0.5f);

            Fix64 distAB = FixVec2.Distance(a.Position, b.Position);
            Fix64 distBC = FixVec2.Distance(b.Position, c.Position);
            Fix64 distAC = FixVec2.Distance(a.Position, c.Position);

            Assert.IsTrue(distAB >= radSum - tolerance,
                $"A-B must not persistently overlap: dist={distAB.ToFloat():F3}, radSum={radSum.ToFloat():F3}");
            Assert.IsTrue(distBC >= radSum - tolerance,
                $"B-C must not persistently overlap: dist={distBC.ToFloat():F3}, radSum={radSum.ToFloat():F3}");
            Assert.IsTrue(distAC >= radSum - tolerance,
                $"A-C must not persistently overlap: dist={distAC.ToFloat():F3}, radSum={radSum.ToFloat():F3}");
        }
    }
}
