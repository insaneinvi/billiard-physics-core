// PocketDropAniHelper.cs
// Pure-logic animation helper for the "ball pocketed" visual effect.
// No MonoBehaviour / Transform / Renderer / Material references – fully decoupled from the presentation layer.
//
// ── Minimal usage example (comments only) ──────────────────────────────────────
//
//   // --- Setup (once, pool-friendly) ---
//   var helper = new PocketDropAniHelper();
//
//   // --- Start drop (option A: supply a Ball to auto-fill position & initial spin) ---
//   var req = new PocketDropRequest
//   {
//       ball           = physicsBall,               // Ball.Position → startPos XY; Ball.AngularVelocity → initial spin
//       startPos       = new Vector3(0, 0, tableZ), // Z is still taken from startPos when ball is provided
//       pocketPos      = pocket.transform.position,
//       duration       = 0.25f,
//       sinkDepth      = 0.18f,
//       attractRatio   = 0.30f,
//       sinkRatio      = 0.50f,
//       vanishRatio    = 0.20f,
//       attractStrength = 0.25f,
//   };
//
//   // --- Start drop (option B: manual position, no Ball) ---
//   var req = new PocketDropRequest
//   {
//       startPos       = ball.transform.position,
//       pocketPos      = pocket.transform.position,
//       duration       = 0.25f,
//       sinkDepth      = 0.18f,
//   };
//   helper.StartDrop(in req);
//
//   // --- Per-frame (e.g. inside a MonoBehaviour.Update) ---
//   if (helper.IsRunning)
//   {
//       PocketDropState state = helper.Update(Time.deltaTime);
//       ball.transform.position   = state.position;
//       ball.transform.localScale = Vector3.one * state.scale;
//       renderer.material.color   = new Color(r, g, b, state.alpha);
//       // state.angularVelocity is in physics coords – feed to PhysicsToView.IntegrateRotation
//
//       if (state.phase == PocketDropPhase.Finished)
//           objectPool.Return(ball);   // pool return is the caller's responsibility
//   }
//
// ──────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace BilliardPhysics.AniHelp
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase enum
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Current phase of the pocket-drop animation.</summary>
    public enum PocketDropPhase
    {
        None,
        Attract,
        Sink,
        Vanish,
        Finished,
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Request struct
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Immutable configuration for a single pocket-drop animation.
    /// Pass with <c>in</c> to avoid copies on the call-site.
    /// </summary>
    public struct PocketDropRequest
    {
        /// <summary>
        /// Optional physics ball.  When non-null:
        /// <list type="bullet">
        ///   <item><see cref="Ball.Position"/> overrides the XY components of <see cref="startPos"/>.</item>
        ///   <item><see cref="Ball.AngularVelocity"/> seeds the initial angular velocity that is
        ///     output via <see cref="PocketDropState.angularVelocity"/> and decays to zero during
        ///     the Attract phase so the visual spin matches the physics state at pocketing time.</item>
        /// </list>
        /// When <c>null</c> (default) <see cref="startPos"/> and zero initial spin are used.
        /// </summary>
        public Ball ball;

        /// <summary>
        /// World position of the ball at the moment it is pocketed.
        /// When <see cref="ball"/> is non-null the X and Y components are overridden by
        /// <see cref="Ball.Position"/>; the Z component is always taken from this field
        /// (use it to specify the table-surface height in world space).
        /// </summary>
        public Vector3 startPos;

        /// <summary>World position of the pocket centre.</summary>
        public Vector3 pocketPos;

        /// <summary>Total animation duration in seconds. Values &lt;= 0 fall back to the default.</summary>
        public float duration;

        /// <summary>How far the ball sinks along <c>-Z</c> (world) during the Sink phase. Clamped &gt;= 0.</summary>
        public float sinkDepth;

        /// <summary>Fraction of <see cref="duration"/> spent in the Attract phase. Normalised if ratios are invalid.</summary>
        public float attractRatio;

        /// <summary>Fraction of <see cref="duration"/> spent in the Sink phase. Normalised if ratios are invalid.</summary>
        public float sinkRatio;

        /// <summary>Fraction of <see cref="duration"/> spent in the Vanish phase. Normalised if ratios are invalid.</summary>
        public float vanishRatio;

        /// <summary>
        /// Maximum lateral movement during the Attract phase expressed as a fraction of the
        /// <see cref="startPos"/>–<see cref="pocketPos"/> displacement. 0.25 = 25 % of the way.
        /// </summary>
        public float attractStrength;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // State struct  (GC-free return value)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of the animation state at a given point in time.
    /// Apply to Transform/material from outside; the helper never touches scene objects.
    /// </summary>
    public struct PocketDropState
    {
        /// <summary>Recommended world position for the ball.</summary>
        public Vector3 position;

        /// <summary>Uniform scale (1 → 0 during Vanish).</summary>
        public float scale;

        /// <summary>Alpha / opacity (slight decay during Vanish).</summary>
        public float alpha;

        /// <summary>
        /// Angular velocity (rad/s) in the physics coordinate system (Z-up).
        /// Seeded from <see cref="Ball.AngularVelocity"/> when a <see cref="Ball"/> is
        /// supplied via <see cref="PocketDropRequest.ball"/>; decays to zero over the
        /// Attract phase so the visual spin smoothly stops as the ball enters the pocket.
        /// Zero during Sink, Vanish, and Finished phases.
        /// Feed this value to <see cref="BilliardPhysics.Runtime.ViewTool.PhysicsToView.IntegrateRotation"/>
        /// to drive the ball's visual rotation.
        /// </summary>
        public Vector3 angularVelocity;

        /// <summary>Which animation phase is currently active.</summary>
        public PocketDropPhase phase;

        /// <summary>Overall progress through the animation, 0..1.</summary>
        public float normalizedTime;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper class
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure-logic, GC-free helper that drives the three-phase "ball pocketed" animation:
    /// <list type="number">
    ///   <item><b>Attract</b> – ball slides slightly toward the pocket (EaseOut).</item>
    ///   <item><b>Sink</b>    – ball drops along world <c>-Z</c> (EaseIn).</item>
    ///   <item><b>Vanish</b>  – ball shrinks to zero scale, alpha fades slightly (EaseIn).</item>
    /// </list>
    /// One instance is reusable: call <see cref="StartDrop"/> multiple times.
    /// Does <b>not</b> reference <c>MonoBehaviour</c>, <c>Transform</c>, <c>Renderer</c>, or <c>Material</c>.
    /// </summary>
    public sealed class PocketDropAniHelper
    {
        // ── Default parameters ────────────────────────────────────────────────
        private const float DefaultDuration        = 0.25f;
        private const float DefaultAttractRatio    = 0.30f;
        private const float DefaultSinkRatio       = 0.50f;
        private const float DefaultAttractStrength = 0.25f;
        private const float DefaultSinkDepth       = 0.18f;

        // ── Runtime state ─────────────────────────────────────────────────────
        private bool    _isRunning;
        private bool    _isFinished;
        private float   _elapsed;

        // Resolved parameters (stored to avoid re-computation each frame)
        private Vector3 _startPos;
        private Vector3 _attractTarget;   // startPos + attractStrength * (pocketPos - startPos)
        private Vector3 _sinkStartPos;    // == _attractTarget (begin of sink phase)
        private Vector3 _sinkEndPos;      // sinkStartPos + Vector3.back * sinkDepth
        private float   _duration;
        private float   _attractEnd;      // normalised time boundary: end of Attract
        private float   _sinkEnd;         // normalised time boundary: end of Sink
        private Vector3 _startAngularVelocity; // initial spin seeded from Ball.AngularVelocity

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Whether the animation is currently playing.</summary>
        public bool IsRunning  => _isRunning;

        /// <summary>Whether the animation has completed (Finished state).</summary>
        public bool IsFinished => _isFinished;

        /// <summary>
        /// Initialise (or re-initialise) the helper with the supplied request.
        /// Safe to call on a pooled instance that was previously used.
        /// </summary>
        public void StartDrop(in PocketDropRequest req)
        {
            // ── Resolve duration ──────────────────────────────────────────────
            _duration = req.duration > 0f ? req.duration : DefaultDuration;

            // ── Resolve ratios ────────────────────────────────────────────────
            float ar = req.attractRatio;
            float sr = req.sinkRatio;
            float vr = req.vanishRatio;

            bool ratiosInvalid = ar <= 0f || sr <= 0f || vr <= 0f;
            if (!ratiosInvalid)
            {
                float sum = ar + sr + vr;
                if (sum <= 0f)
                {
                    ratiosInvalid = true;
                }
                else
                {
                    // Normalise so they sum to exactly 1
                    ar /= sum;
                    sr /= sum;
                    // vr is implied: 1 - ar - sr (avoids floating-point drift)
                }
            }

            if (ratiosInvalid)
            {
                ar = DefaultAttractRatio;
                sr = DefaultSinkRatio;
            }

            _attractEnd = ar;
            _sinkEnd    = ar + sr;

            // ── Resolve positions ─────────────────────────────────────────────
            float strength = req.attractStrength > 0f ? req.attractStrength : DefaultAttractStrength;
            float depth    = req.sinkDepth >= 0f ? req.sinkDepth : DefaultSinkDepth;

            // When a Ball is supplied, override startPos XY from physics coordinates
            // and seed the initial angular velocity from the ball's current spin.
            if (req.ball != null)
            {
                var bp = req.ball.Position;
                _startPos = new Vector3(bp.X.ToFloat(), bp.Y.ToFloat(), req.startPos.z);
                var av = req.ball.AngularVelocity;
                _startAngularVelocity = new Vector3(av.X.ToFloat(), av.Y.ToFloat(), av.Z.ToFloat());
            }
            else
            {
                _startPos             = req.startPos;
                _startAngularVelocity = Vector3.zero;
            }

            _attractTarget = _startPos + (req.pocketPos - _startPos) * strength;
            _sinkStartPos  = _attractTarget;
            _sinkEndPos    = _sinkStartPos + Vector3.back * depth;

            // ── Reset playback ────────────────────────────────────────────────
            _elapsed    = 0f;
            _isRunning  = true;
            _isFinished = false;
        }

        /// <summary>
        /// Advance the animation by <paramref name="deltaTime"/> seconds and return the current state.
        /// No heap allocations.
        /// </summary>
        public PocketDropState Update(float deltaTime)
        {
            if (!_isRunning)
                return BuildFinishedState();

            _elapsed += deltaTime;
            float t = _duration > 0f ? _elapsed / _duration : 1f;

            if (t >= 1f)
            {
                t          = 1f;
                _isRunning = false;
                _isFinished = true;
            }

            return Evaluate(t);
        }

        /// <summary>
        /// Sample the animation at an arbitrary normalised time (0..1) without modifying internal state.
        /// Input is clamped to [0, 1].  No heap allocations.
        /// </summary>
        public PocketDropState Evaluate(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);

            PocketDropState state;
            state.normalizedTime = t;

            if (t >= 1f)
            {
                state.phase           = PocketDropPhase.Finished;
                state.position        = _sinkEndPos;
                state.scale           = 0f;
                state.alpha           = 0f;
                state.angularVelocity = Vector3.zero;
                return state;
            }

            if (t < _attractEnd)
            {
                // ── Attract phase ─────────────────────────────────────────────
                float phaseT  = _attractEnd > 0f ? t / _attractEnd : 1f;
                float eased   = EaseOut(phaseT);

                state.phase           = PocketDropPhase.Attract;
                state.position        = Vector3.LerpUnclamped(_startPos, _attractTarget, eased);
                state.scale           = 1f;
                state.alpha           = 1f;
                // Angular velocity decays from the ball's initial spin to zero as
                // the ball glides toward the pocket (spin dissipates during attraction).
                state.angularVelocity = Vector3.Lerp(_startAngularVelocity, Vector3.zero, eased);
            }
            else if (t < _sinkEnd)
            {
                // ── Sink phase ────────────────────────────────────────────────
                float span   = _sinkEnd - _attractEnd;
                float phaseT = span > 0f ? (t - _attractEnd) / span : 1f;
                float eased  = EaseIn(phaseT);

                state.phase           = PocketDropPhase.Sink;
                state.position        = Vector3.LerpUnclamped(_sinkStartPos, _sinkEndPos, eased);
                state.scale           = 1f;
                state.alpha           = 1f;
                state.angularVelocity = Vector3.zero;
            }
            else
            {
                // ── Vanish phase ──────────────────────────────────────────────
                float span   = 1f - _sinkEnd;
                float phaseT = span > 0f ? (t - _sinkEnd) / span : 1f;
                float eased  = EaseIn(phaseT);

                state.phase           = PocketDropPhase.Vanish;
                state.position        = _sinkEndPos;
                state.scale           = 1f - eased;                  // 1 → 0  (primary: scale drives invisibility)
                state.alpha           = 1f - eased;                  // 1 → 0  (secondary alpha fade mirrors scale)
                state.angularVelocity = Vector3.zero;
            }

            return state;
        }

        /// <summary>Stop the animation immediately without completing it.</summary>
        public void Stop()
        {
            _isRunning  = false;
            _isFinished = false;
        }

        /// <summary>
        /// Reset the helper to its initial (idle) state.
        /// Call before returning the instance to a pool.
        /// </summary>
        public void Reset()
        {
            _isRunning  = false;
            _isFinished = false;
            _elapsed    = 0f;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>Quadratic EaseOut: fast start, slow end.</summary>
        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        /// <summary>Quadratic EaseIn: slow start, fast end.</summary>
        private static float EaseIn(float t) => t * t;

        private PocketDropState BuildFinishedState()
        {
            PocketDropState state;
            state.phase          = PocketDropPhase.Finished;
            state.position       = _sinkEndPos;
            state.scale          = 0f;
            state.alpha          = 0f;
            state.angularVelocity = Vector3.zero;
            state.normalizedTime = 1f;
            return state;
        }
    }
}
