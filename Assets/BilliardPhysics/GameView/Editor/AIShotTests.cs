using System.Collections.Generic;
using NUnit.Framework;
using BilliardPhysics;
using BilliardPhysics.AI;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode tests for the billiards AI system:
    /// BallState, TableState, Shot, ShotResult, ShotSimulator,
    /// ShotGenerator, ShotEvaluator, and ShotSearch.
    /// </summary>
    public class AIShotTests
    {
        // ── Shared rule implementation ─────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IBilliardsRule"/> that treats every non-cue, non-pocketed
        /// ball as a legal target and considers only a scratch as a foul.
        /// </summary>
        private sealed class AllLegalRule : IBilliardsRule
        {
            public IReadOnlyList<int> GetLegalTargetBalls(TableState state, int player)
            {
                var ids = new List<int>();
                foreach (BallState b in state.Balls)
                    if (b.Id != state.CueBallId && !b.IsPocketed)
                        ids.Add(b.Id);
                return ids;
            }

            public bool IsFoul(Shot shot, ShotResult result, TableState before)
                => result.CueBallPocketed;

            public int GetScore(Shot shot, ShotResult result, TableState before)
            {
                int s = 0;
                foreach (int id in result.PocketedBallIds)
                    if (id != before.CueBallId) s++;
                return s;
            }

            public bool IsGameOver(TableState state)
            {
                foreach (BallState b in state.Balls)
                    if (b.Id != state.CueBallId && !b.IsPocketed)
                        return false;
                return true;
            }
        }

        // ── World-builder helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Creates a two-ball physics world (cueBall id=0, targetBall id=1) with a
        /// single pocket.  No table segments — balls roll freely.
        /// Both balls start at rest.
        /// </summary>
        private static (PhysicsWorld2D world, Ball cueBall, Ball targetBall) MakeWorldAtRest(
            float cueBallX, float targetBallX,
            float pocketX,  float pocketRadius = 1.0f)
        {
            // Use the IDs that AddBall will assign (0 for first ball, 1 for second).
            var cueBall    = new Ball(0);
            var targetBall = new Ball(1);

            cueBall.Position    = new FixVec2(Fix64.FromFloat(cueBallX),    Fix64.Zero);
            targetBall.Position = new FixVec2(Fix64.FromFloat(targetBallX), Fix64.Zero);
            cueBall.Reset();
            targetBall.Reset();

            var world = new PhysicsWorld2D();
            world.AddBall(cueBall);     // cueBall.Id  = 0
            world.AddBall(targetBall);  // targetBall.Id = 1

            world.AddPocket(new Pocket(0,
                new FixVec2(Fix64.FromFloat(pocketX), Fix64.Zero),
                Fix64.FromFloat(pocketRadius)));

            return (world, cueBall, targetBall);
        }

        /// <summary>
        /// Creates a two-ball world where the target ball is pre-set to a slow velocity
        /// (v = 1, well below the capture threshold of 5) and is already within the
        /// pocket's capture radius.  Simulating any shot will cause the target to be
        /// captured in the very first physics step, making this a deterministic
        /// pocketing scenario independent of ball-ball collision dynamics.
        /// </summary>
        private static (PhysicsWorld2D world, Ball cueBall, Ball targetBall) MakeWorldWithSlowTargetInPocket(
            float cueBallX = -5f)
        {
            // Use the IDs that AddBall will assign (0 for first ball, 1 for second).
            var cueBall    = new Ball(0);
            var targetBall = new Ball(1);

            // Cue ball is far away and at rest.
            cueBall.Position = new FixVec2(Fix64.FromFloat(cueBallX), Fix64.Zero);
            cueBall.Reset();

            // Target ball moves slowly in +X at speed 1, well below threshold (5).
            targetBall.Position       = new FixVec2(Fix64.Zero, Fix64.Zero);
            targetBall.LinearVelocity = new FixVec2(Fix64.One, Fix64.Zero);  // speed = 1 < threshold
            targetBall.AngularVelocity = FixVec3.Zero;
            targetBall.IsMotionless   = false;
            targetBall.IsPocketed     = false;

            var world = new PhysicsWorld2D();
            world.AddBall(cueBall);
            world.AddBall(targetBall);

            // Pocket centre at (0.3, 0), radius 0.5 — target at (0, 0) is 0.3 units from
            // centre, which is less than the 0.5 radius, so capture is checked from the
            // very first CheckPocketCaptures call (after advancing one dt).
            world.AddPocket(new Pocket(0,
                new FixVec2(Fix64.FromFloat(0.3f), Fix64.Zero),
                Fix64.FromFloat(0.5f)));

            return (world, cueBall, targetBall);
        }

        // ── BallState ─────────────────────────────────────────────────────────────

        [Test]
        public void BallState_FromBall_CapturesAllFields()
        {
            var ball = new Ball(7);
            ball.Position        = new FixVec2(Fix64.From(3), Fix64.From(4));
            ball.LinearVelocity  = new FixVec2(Fix64.From(1), Fix64.From(2));
            ball.AngularVelocity = new FixVec3(Fix64.From(5), Fix64.From(6), Fix64.From(7));
            ball.IsPocketed      = true;
            ball.IsMotionless    = false;

            BallState state = BallState.FromBall(ball);

            Assert.AreEqual(7,                    state.Id);
            Assert.AreEqual(ball.Position,        state.Position);
            Assert.AreEqual(ball.LinearVelocity,  state.Velocity);
            Assert.AreEqual(ball.AngularVelocity, state.Angular);
            Assert.IsTrue(state.IsPocketed);
            Assert.IsFalse(state.IsMotionless);
        }

        // ── TableState ────────────────────────────────────────────────────────────

        [Test]
        public void TableState_GetBall_ReturnsBallById()
        {
            var bs0 = new BallState(0, FixVec2.Zero, FixVec2.Zero, FixVec3.Zero, false, true);
            var bs1 = new BallState(1, FixVec2.One,  FixVec2.Zero, FixVec3.Zero, false, true);
            var ts  = new TableState(new[] { bs0, bs1 }, cueBallId: 0);

            Assert.AreSame(bs1, ts.GetBall(1));
            Assert.IsNull(ts.GetBall(99));
        }

        [Test]
        public void TableState_CueBall_ReturnsCorrectBall()
        {
            var bs0 = new BallState(0, FixVec2.Zero, FixVec2.Zero, FixVec3.Zero, false, true);
            var bs1 = new BallState(1, FixVec2.One,  FixVec2.Zero, FixVec3.Zero, false, true);
            var ts  = new TableState(new[] { bs0, bs1 }, cueBallId: 0);

            Assert.AreSame(bs0, ts.CueBall);
        }

        [Test]
        public void TableState_FromWorld_CapturesAllBalls()
        {
            var (world, cueBall, targetBall) = MakeWorldAtRest(-1f, 1f, 5f);
            TableState ts = TableState.FromWorld(world.Balls, cueBall.Id);

            Assert.AreEqual(2, ts.Balls.Count);
            Assert.IsNotNull(ts.GetBall(cueBall.Id));
            Assert.IsNotNull(ts.GetBall(targetBall.Id));
        }

        // ── ShotSimulator: state restoration ─────────────────────────────────────

        [Test]
        public void ShotSimulator_RestoresBallPositions_AfterSimulation()
        {
            var (world, cueBall, targetBall) = MakeWorldAtRest(-1f, 1f, 5f);

            FixVec2 cueBallPosBefore    = cueBall.Position;
            FixVec2 targetBallPosBefore = targetBall.Position;

            TableState state = TableState.FromWorld(world.Balls, cueBall.Id);
            var shot = new Shot(new FixVec2(Fix64.One, Fix64.Zero), Fix64.From(15),
                                Fix64.Zero, Fix64.Zero);
            new ShotSimulator().Simulate(world, state, shot, cueBall.Id);

            Assert.AreEqual(cueBallPosBefore,    cueBall.Position,
                "Cue ball position must be restored after simulation.");
            Assert.AreEqual(targetBallPosBefore, targetBall.Position,
                "Target ball position must be restored after simulation.");
        }

        [Test]
        public void ShotSimulator_RestoresBallVelocities_AfterSimulation()
        {
            var (world, cueBall, targetBall) = MakeWorldAtRest(-1f, 1f, 5f);

            FixVec2 cueBallVelBefore    = cueBall.LinearVelocity;   // (0,0)
            FixVec2 targetBallVelBefore = targetBall.LinearVelocity; // (0,0)

            TableState state = TableState.FromWorld(world.Balls, cueBall.Id);
            var shot = new Shot(new FixVec2(Fix64.One, Fix64.Zero), Fix64.From(15),
                                Fix64.Zero, Fix64.Zero);
            new ShotSimulator().Simulate(world, state, shot, cueBall.Id);

            Assert.AreEqual(cueBallVelBefore,    cueBall.LinearVelocity,
                "Cue ball velocity must be restored after simulation.");
            Assert.AreEqual(targetBallVelBefore, targetBall.LinearVelocity,
                "Target ball velocity must be restored after simulation.");
        }

        [Test]
        public void ShotSimulator_RestoresIsPockedFlag_WhenBallWasPocketedDuringSimulation()
        {
            // Target ball is already slowly moving inside the pocket capture zone.
            // It will be pocketed (IsPocketed→true) during simulation.
            // Afterwards the flag must be restored to false.
            var (world, cueBall, targetBall) = MakeWorldWithSlowTargetInPocket();

            TableState state = TableState.FromWorld(world.Balls, cueBall.Id);
            var shot = new Shot(new FixVec2(Fix64.One, Fix64.Zero),
                                BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMin,
                                Fix64.Zero, Fix64.Zero);

            ShotResult result = new ShotSimulator().Simulate(world, state, shot, cueBall.Id);

            // Simulation should have pocketed the target …
            Assert.IsTrue(result.PocketedBallIds.Contains(targetBall.Id),
                "Target ball should have been pocketed during simulation.");

            // … but the live world must be restored.
            Assert.IsFalse(targetBall.IsPocketed,
                "IsPocketed must be restored to false after simulation.");
        }

        // ── ShotSimulator: simulation results ────────────────────────────────────

        [Test]
        public void ShotSimulator_SlowBallAlreadyInPocketZone_GetsPocketed()
        {
            // This test verifies the full pocketing pipeline without depending on the
            // exact outcome of a ball-ball collision.  The target ball is already
            // inside the pocket capture zone and moving slowly (speed = 1 < threshold 5),
            // so it is captured in the very first physics step.
            var (world, cueBall, targetBall) = MakeWorldWithSlowTargetInPocket();

            TableState state = TableState.FromWorld(world.Balls, cueBall.Id);
            var shot = new Shot(new FixVec2(Fix64.One, Fix64.Zero),
                                BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMin,
                                Fix64.Zero, Fix64.Zero);

            ShotResult result = new ShotSimulator().Simulate(world, state, shot, cueBall.Id);

            Assert.IsTrue(result.PocketedBallIds.Contains(targetBall.Id),
                "Target ball should be pocketed when it is slow and inside the pocket zone.");
            Assert.IsFalse(result.CueBallPocketed,
                "Cue ball should not be pocketed.");
            Assert.IsTrue(result.StateAfter.GetBall(targetBall.Id).IsPocketed,
                "StateAfter must record the target as pocketed.");
        }

        [Test]
        public void ShotSimulator_SimulationCompletesWithinMaxSteps()
        {
            var (world, cueBall, _) = MakeWorldAtRest(-1f, 1f, 5f);
            TableState state = TableState.FromWorld(world.Balls, cueBall.Id);
            var shot = new Shot(new FixVec2(Fix64.One, Fix64.Zero), Fix64.From(15),
                                Fix64.Zero, Fix64.Zero);

            ShotResult result = new ShotSimulator().Simulate(world, state, shot, cueBall.Id);

            Assert.Less(result.StepsSimulated, ShotSimulator.MaxSimSteps,
                "Simulation must stop (all balls settle) before the hard step limit.");
        }

        // ── ShotGenerator ─────────────────────────────────────────────────────────

        [Test]
        public void ShotGenerator_GeneratesDirectPotShots_ForEachPocket()
        {
            // cueBall at (-2,0), target at (0,0), one pocket.
            var (world, cueBall, _) = MakeWorldAtRest(-2f, 0f, 5f);
            TableState state   = TableState.FromWorld(world.Balls, cueBall.Id);
            var        adapter = new RuleAdapter(new AllLegalRule());

            IReadOnlyList<Shot> shots =
                new ShotGenerator().GenerateShots(state, world.Pockets, world.TableSegments, adapter, 0);

            bool hasDirectPot = false;
            foreach (Shot s in shots)
                if (s.Kind == ShotKind.DirectPot) hasDirectPot = true;

            Assert.IsTrue(hasDirectPot,
                "ShotGenerator must produce at least one DirectPot shot when a pocket exists.");
        }

        [Test]
        public void ShotGenerator_GeneratesSafetyShots_ForEachLegalTarget()
        {
            var (world, cueBall, _) = MakeWorldAtRest(-2f, 0f, 5f);
            TableState state   = TableState.FromWorld(world.Balls, cueBall.Id);
            var        adapter = new RuleAdapter(new AllLegalRule());

            IReadOnlyList<Shot> shots =
                new ShotGenerator().GenerateShots(state, world.Pockets, world.TableSegments, adapter, 0);

            bool hasSafety = false;
            foreach (Shot s in shots)
                if (s.Kind == ShotKind.Safety) hasSafety = true;

            Assert.IsTrue(hasSafety,
                "ShotGenerator must produce at least one Safety shot for each legal target.");
        }

        [Test]
        public void ShotGenerator_DirectPotDirection_PointsTowardGhostBall()
        {
            // cueBall at (-2,0), target at (0,0), pocket at (3,0).
            // Ghost ball = (0,0) + (-3,0).Normalized * twoRadii = (-0.5715, 0).
            // Aim direction from (-2,0) to (-0.5715,0) ≈ (+1, 0).
            var (world, cueBall, _) = MakeWorldAtRest(-2f, 0f, 3f);
            TableState state   = TableState.FromWorld(world.Balls, cueBall.Id);
            var        adapter = new RuleAdapter(new AllLegalRule());

            IReadOnlyList<Shot> shots =
                new ShotGenerator().GenerateShots(state, world.Pockets, world.TableSegments, adapter, 0);

            Shot directPot = null;
            foreach (Shot s in shots)
                if (s.Kind == ShotKind.DirectPot) { directPot = s; break; }

            Assert.IsNotNull(directPot, "A DirectPot shot should be generated for this inline scenario.");
            Assert.Greater(directPot.Direction.X.ToFloat(), 0.9f,
                "DirectPot direction X should be close to +1 for an inline cue–target–pocket setup.");
            Assert.Less(System.Math.Abs(directPot.Direction.Y.ToFloat()), 0.1f,
                "DirectPot direction Y should be near 0 for an inline setup.");
        }

        [Test]
        public void ShotGenerator_WithNoPocketsAndNoSegments_OnlyGeneratesSafetyShots()
        {
            // Fresh world with no pockets and no rail segments.
            // Use the IDs that AddBall will assign (0 for first ball, 1 for second).
            var cueBall = new Ball(0);
            var target  = new Ball(1);
            cueBall.Position = new FixVec2(Fix64.From(-2), Fix64.Zero); cueBall.Reset();
            target.Position  = new FixVec2(Fix64.Zero, Fix64.Zero);     target.Reset();

            var world = new PhysicsWorld2D();
            world.AddBall(cueBall);
            world.AddBall(target);

            TableState state   = TableState.FromWorld(world.Balls, cueBall.Id);
            var        adapter = new RuleAdapter(new AllLegalRule());

            IReadOnlyList<Shot> shots =
                new ShotGenerator().GenerateShots(state, world.Pockets, world.TableSegments, adapter, 0);

            Assert.Greater(shots.Count, 0,
                "Should generate at least one safety shot even without pockets/segments.");
            foreach (Shot s in shots)
                Assert.AreEqual(ShotKind.Safety, s.Kind,
                    "Without pockets or segments only safety shots should be generated.");
        }

        // ── ShotEvaluator ─────────────────────────────────────────────────────────

        [Test]
        public void ShotEvaluator_PottingLegalBall_ScoresPositive()
        {
            var adapter   = new RuleAdapter(new AllLegalRule());
            var evaluator = new ShotEvaluator();

            var stateBefore = MakeTableStateTwoBalls(
                cueBallPos: FixVec2.Zero,
                targetPos:  new FixVec2(Fix64.From(3), Fix64.Zero));

            // Target (id=1) was pocketed; cue ball (id=0) stays on the table.
            var stateAfter = new TableState(new[]
            {
                new BallState(0, new FixVec2(Fix64.One, Fix64.Zero), FixVec2.Zero, FixVec3.Zero, false, true),
                new BallState(1, FixVec2.Zero,                       FixVec2.Zero, FixVec3.Zero, true,  true),
            }, cueBallId: 0);
            var result = new ShotResult(stateAfter, new[] { 1 }, false, 100);

            Fix64 score = evaluator.Evaluate(MakeShot(), result, stateBefore, adapter, 0);

            Assert.Greater(score.ToFloat(), 0f,
                "Legally potting a ball must yield a positive score.");
        }

        [Test]
        public void ShotEvaluator_Scratch_ScoresNegative()
        {
            var adapter   = new RuleAdapter(new AllLegalRule());
            var evaluator = new ShotEvaluator();

            var stateBefore = MakeTableStateTwoBalls(FixVec2.Zero, new FixVec2(Fix64.From(3), Fix64.Zero));

            // Cue ball (id=0) was pocketed (scratch).
            var stateAfter = new TableState(new[]
            {
                new BallState(0, FixVec2.Zero,                       FixVec2.Zero, FixVec3.Zero, true,  true),
                new BallState(1, new FixVec2(Fix64.From(3), Fix64.Zero), FixVec2.Zero, FixVec3.Zero, false, true),
            }, cueBallId: 0);
            var result = new ShotResult(stateAfter, new[] { 0 }, true, 50);

            Fix64 score = evaluator.Evaluate(MakeShot(), result, stateBefore, adapter, 0);

            Assert.Less(score.ToFloat(), 0f,
                "A scratch (cue ball pocketed) must yield a negative score.");
        }

        [Test]
        public void ShotEvaluator_PottedBallScoresHigherThan_MissedShot()
        {
            var adapter   = new RuleAdapter(new AllLegalRule());
            var evaluator = new ShotEvaluator();
            var stateBefore = MakeTableStateTwoBalls(FixVec2.Zero, new FixVec2(Fix64.From(3), Fix64.Zero));
            var shot = MakeShot();

            // Miss: target stays on table.
            var missState = new TableState(new[]
            {
                new BallState(0, new FixVec2(Fix64.One,  Fix64.Zero), FixVec2.Zero, FixVec3.Zero, false, true),
                new BallState(1, new FixVec2(Fix64.From(4), Fix64.Zero), FixVec2.Zero, FixVec3.Zero, false, true),
            }, cueBallId: 0);
            Fix64 missScore = evaluator.Evaluate(shot,
                new ShotResult(missState, new int[0], false, 100), stateBefore, adapter, 0);

            // Pot: target is pocketed.
            var potState = new TableState(new[]
            {
                new BallState(0, new FixVec2(Fix64.One, Fix64.Zero), FixVec2.Zero, FixVec3.Zero, false, true),
                new BallState(1, FixVec2.Zero,                       FixVec2.Zero, FixVec3.Zero, true,  true),
            }, cueBallId: 0);
            Fix64 potScore = evaluator.Evaluate(shot,
                new ShotResult(potState, new[] { 1 }, false, 100), stateBefore, adapter, 0);

            Assert.Greater(potScore.ToFloat(), missScore.ToFloat(),
                "Potting a legal ball must score higher than missing.");
        }

        // ── ShotSearch ────────────────────────────────────────────────────────────

        [Test]
        public void ShotSearch_FindBest_ReturnsNonNull_WhenLegalTargetsExist()
        {
            var (world, cueBall, _) = MakeWorldAtRest(-2f, 0f, 3f);
            TableState state  = TableState.FromWorld(world.Balls, cueBall.Id);
            var rules  = new RuleAdapter(new AllLegalRule());
            var search = new ShotSearch(new ShotGenerator(), new ShotSimulator(), new ShotEvaluator());

            ScoredShot best = search.FindBest(state, world, cueBall.Id, rules, 0);

            Assert.IsNotNull(best,  "ShotSearch must return a candidate when legal targets exist.");
            Assert.IsNotNull(best.Shot,   "The returned ScoredShot must contain a Shot.");
            Assert.IsNotNull(best.Result, "The returned ScoredShot must contain a ShotResult.");
        }

        [Test]
        public void ShotSearch_FindBest_WorldStateIsRestoredAfterAllCandidatesAreEvaluated()
        {
            var (world, cueBall, targetBall) = MakeWorldAtRest(-2f, 0f, 3f);

            FixVec2 cueBallPosBefore    = cueBall.Position;
            FixVec2 targetBallPosBefore = targetBall.Position;

            TableState state  = TableState.FromWorld(world.Balls, cueBall.Id);
            var rules  = new RuleAdapter(new AllLegalRule());
            new ShotSearch(new ShotGenerator(), new ShotSimulator(), new ShotEvaluator())
                .FindBest(state, world, cueBall.Id, rules, 0);

            Assert.AreEqual(cueBallPosBefore,    cueBall.Position,
                "Cue ball position must be restored after ShotSearch completes.");
            Assert.AreEqual(targetBallPosBefore, targetBall.Position,
                "Target ball position must be restored after ShotSearch completes.");
        }

        [Test]
        public void ShotSearch_FindBest_ScoreIsMaximumAmongAllCandidates()
        {
            // Independently re-evaluate every candidate and confirm the returned
            // ScoredShot has the highest (or equal-highest) score.
            var (world, cueBall, _) = MakeWorldAtRest(-2f, 0f, 3f);
            TableState state = TableState.FromWorld(world.Balls, cueBall.Id);

            var rules     = new RuleAdapter(new AllLegalRule());
            var generator = new ShotGenerator();
            var simulator = new ShotSimulator();
            var evaluator = new ShotEvaluator();
            var search    = new ShotSearch(generator, simulator, evaluator);

            ScoredShot best = search.FindBest(state, world, cueBall.Id, rules, 0);
            Assert.IsNotNull(best);

            IReadOnlyList<Shot> candidates =
                generator.GenerateShots(state, world.Pockets, world.TableSegments, rules, 0);

            // Allow a tiny tolerance (1e-4) for any fixed-point rounding in repeated evaluations.
            const float tolerance = 1e-4f;
            foreach (Shot shot in candidates)
            {
                ShotResult r     = simulator.Simulate(world, state, shot, cueBall.Id);
                Fix64      score = evaluator.Evaluate(shot, r, state, rules, 0);
                Assert.LessOrEqual(score.ToFloat(), best.Score.ToFloat() + tolerance,
                    "No individual candidate should score higher than the best found by ShotSearch.");
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static TableState MakeTableStateTwoBalls(FixVec2 cueBallPos, FixVec2 targetPos)
        {
            return new TableState(new[]
            {
                new BallState(0, cueBallPos, FixVec2.Zero, FixVec3.Zero, false, true),
                new BallState(1, targetPos,  FixVec2.Zero, FixVec3.Zero, false, true),
            }, cueBallId: 0);
        }

        private static Shot MakeShot()
            => new Shot(new FixVec2(Fix64.One, Fix64.Zero), Fix64.From(15),
                        Fix64.Zero, Fix64.Zero);
    }
}
