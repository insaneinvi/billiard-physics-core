using System.Collections.Generic;

namespace BilliardPhysics.AI
{
    /// <summary>
    /// A shot candidate together with its simulated outcome and evaluation score.
    /// </summary>
    public sealed class ScoredShot
    {
        public Shot       Shot   { get; }
        public ShotResult Result { get; }
        public Fix64      Score  { get; }

        public ScoredShot(Shot shot, ShotResult result, Fix64 score)
        {
            Shot   = shot;
            Result = result;
            Score  = score;
        }
    }

    /// <summary>
    /// Searches for the best shot among all candidates by generating them with
    /// <see cref="ShotGenerator"/>, simulating each one with <see cref="ShotSimulator"/>,
    /// and scoring the result with <see cref="ShotEvaluator"/>.
    /// </summary>
    public sealed class ShotSearch
    {
        private readonly ShotGenerator _generator;
        private readonly ShotSimulator _simulator;
        private readonly ShotEvaluator _evaluator;

        public ShotSearch(ShotGenerator generator, ShotSimulator simulator, ShotEvaluator evaluator)
        {
            _generator = generator;
            _simulator = simulator;
            _evaluator = evaluator;
        }

        /// <summary>
        /// Evaluates all candidate shots and returns the highest-scoring one,
        /// or <c>null</c> if no candidates could be generated.
        /// </summary>
        /// <param name="state">Current table state.</param>
        /// <param name="world">
        /// Live physics world used for simulation.
        /// The world's ball state is restored after each candidate is evaluated.
        /// </param>
        /// <param name="cueBallId">ID of the cue ball in <paramref name="world"/>.</param>
        /// <param name="rules">Rule adapter for legality and scoring queries.</param>
        /// <param name="currentPlayer">Current player index.</param>
        public ScoredShot FindBest(
            TableState     state,
            PhysicsWorld2D world,
            int            cueBallId,
            RuleAdapter    rules,
            int            currentPlayer)
        {
            IReadOnlyList<Shot> candidates = _generator.GenerateShots(
                state, world.Pockets, world.TableSegments, rules, currentPlayer);

            ScoredShot best = null;

            foreach (Shot shot in candidates)
            {
                ShotResult result = _simulator.Simulate(world, state, shot, cueBallId);
                Fix64      score  = _evaluator.Evaluate(shot, result, state, rules, currentPlayer);

                if (best == null || score > best.Score)
                    best = new ScoredShot(shot, result, score);
            }

            return best;
        }
    }
}
