using UnityEngine;

namespace BilliardPhysics.AniHelp
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Types
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sequential phases of the ball-pocket-drop animation.
    /// </summary>
    public enum PocketDropPhase
    {
        /// <summary>
        /// Ball slides from its entry position toward the pocket center along a
        /// quadratic Bézier curve shaped by the ball's entry linear velocity.
        /// Easing: EaseOut (fast start, gentle landing).
        /// </summary>
        Attract,

        /// <summary>
        /// Ball dips downward along −Z toward the pocket depth.
        /// Easing: EaseIn (gentle start, fast landing).
        /// </summary>
        Sink,

        /// <summary>
        /// Ball shrinks from its original scale toward <see cref="PocketDropRequest.targetScale"/>
        /// (default 0.75).  Ball remains at the pocket XY position.
        /// Easing: EaseIn.
        /// </summary>
        Vanish,

        /// <summary>Animation has played to completion.</summary>
        Finished,
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-frame animation snapshot returned by <see cref="PocketDropAniHelper.Update"/>.
    /// Returned as a value type — no heap allocation.
    /// </summary>
    public struct PocketDropState
    {
        /// <summary>Suggested world-space position for the ball's transform this frame.</summary>
        public Vector3        position;

        /// <summary>
        /// Uniform scale for the ball this frame.
        /// Interpolates from 1.0 toward <see cref="PocketDropRequest.targetScale"/> over the
        /// full animation; the scale only changes visibly during the Vanish phase.
        /// </summary>
        public float          scale;

        /// <summary>
        /// Material opacity this frame.  Always 1.0 in the current implementation
        /// (the drop animation does not fade the ball out; it only shrinks it).
        /// Reserved for caller use if a fade effect is needed.
        /// </summary>
        public float          alpha;

        /// <summary>Current animation phase.</summary>
        public PocketDropPhase phase;

        /// <summary>Normalized time over the full animation, [0..1].</summary>
        public float          normalizedTime;
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parameters for <see cref="PocketDropAniHelper.StartDrop"/>.
    /// </summary>
    public struct PocketDropRequest
    {
        /// <summary>
        /// Ball world position at the pocketing moment.
        /// Should be derived from <c>Ball.Position</c> (the single authoritative source).
        /// </summary>
        public Vector3 startPos;

        /// <summary>Pocket-center world position.</summary>
        public Vector3 pocketPos;

        /// <summary>
        /// Ball XY linear velocity (world units/s) at the pocketing moment, taken from
        /// <c>Ball.LinearVelocity</c>.  The Attract phase blends this direction into the
        /// trajectory so the animation begins in the same direction the ball was traveling,
        /// ensuring a natural, non-jarring transition.
        /// </summary>
        public Vector2 entryLinearVelocity;

        /// <summary>Total animation duration in seconds.  Values ≤ 0 use the default (0.25 s).</summary>
        public float duration;

        /// <summary>
        /// Distance (world units) the ball moves along −Z during the Sink phase.
        /// Values &lt; 0 use the default (0.18).
        /// </summary>
        public float sinkDepth;

        /// <summary>
        /// Fraction of the total duration devoted to the Attract phase, [0..1].
        /// Values outside the valid range use the default (0.30).
        /// The three ratios are automatically re-normalised so they sum to 1.
        /// </summary>
        public float attractRatio;

        /// <summary>
        /// Fraction of the total duration devoted to the Sink phase, [0..1].
        /// Values outside the valid range use the default (0.50).
        /// </summary>
        public float sinkRatio;

        /// <summary>
        /// Fraction of the total duration devoted to the Vanish phase, [0..1].
        /// Values outside the valid range use the default (0.20).
        /// </summary>
        public float vanishRatio;

        /// <summary>
        /// How far the Attract trajectory is offset along the entry-velocity direction,
        /// expressed as a fraction of the startPos→pocketPos distance.
        /// Values ≤ 0 use the default (0.25).
        /// Typical range 0..1; values above 1 overshoot the pocket before correcting back.
        /// </summary>
        public float attractStrength;

        /// <summary>
        /// Target uniform scale at the end of the Vanish phase.
        /// The ball smoothly shrinks from 1.0 to this value during the animation.
        /// Values ≤ 0 use the default (0.75).
        /// </summary>
        public float targetScale;

        /// <summary>
        /// Explicit world-space end position of the Attract phase (where the ball arrives
        /// before transitioning to the Sink phase).  Only the XY components are used;
        /// the Z component is always inherited from <see cref="startPos"/>.
        ///
        /// <para>When this is the zero vector, the Attract phase ends at the pocket XY
        /// (<see cref="pocketPos"/>) — the original behaviour.</para>
        ///
        /// <para>Compute using <see cref="PocketDropAniHelper.CalcDropTarget"/>:
        /// <code>
        /// dropTarget = PocketDropAniHelper.CalcDropTarget(dropStartPos, pocketWorldPos, ballDiameter);
        /// </code>
        /// </para>
        /// </summary>
        public Vector3 dropTarget;
    }

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure-logic, GC-free animation helper that drives the three-phase ball-pocket-drop
    /// visual effect.
    ///
    /// <para><b>Phase sequence:</b>
    /// <list type="number">
    ///   <item><see cref="PocketDropPhase.Attract"/> — Ball slides from its entry position
    ///     to the pocket XY, following a quadratic Bézier curve whose control point is
    ///     offset along the ball's entry <see cref="PocketDropRequest.entryLinearVelocity"/>
    ///     direction.  This ensures the trajectory visually picks up from the ball's last
    ///     rolling direction (EaseOut).</item>
    ///   <item><see cref="PocketDropPhase.Sink"/> — Ball sinks downward (−Z) toward the
    ///     pocket depth while staying at the pocket XY (EaseIn).</item>
    ///   <item><see cref="PocketDropPhase.Vanish"/> — Ball shrinks from 1.0 to
    ///     <see cref="PocketDropRequest.targetScale"/> (default 0.75) (EaseIn).</item>
    /// </list></para>
    ///
    /// <para><b>Pool-friendly:</b> call <see cref="Reset"/> before returning to an object
    /// pool; call <see cref="StartDrop"/> to reuse.  No <c>MonoBehaviour</c>,
    /// <c>Transform</c>, or <c>Renderer</c> references are held.</para>
    /// </summary>
    public sealed class PocketDropAniHelper
    {
        // ── Default parameter values ───────────────────────────────────────────────
        private const float DefaultDuration        = 0.25f;
        private const float DefaultSinkDepth       = 0.18f;
        private const float DefaultAttractRatio    = 0.30f;
        private const float DefaultSinkRatio       = 0.50f;
        private const float DefaultVanishRatio     = 0.20f;
        private const float DefaultAttractStrength = 0.25f;
        private const float DefaultTargetScale     = 0.75f;

        // Shared threshold for treating a vector's squared magnitude as zero.
        // Used both in StartDrop (dropTarget sentinel check) and CalcDropTarget.
        private const float ZeroVectorThreshold = 1e-6f;

        // ── Baked animation parameters (set once on StartDrop) ────────────────────
        private Vector3 _startPos;
        private Vector3 _pocketPos;
        private float   _duration;

        // Phase end-times (absolute seconds from t=0)
        private float _attractEnd;  // end of Attract phase
        private float _sinkEnd;     // end of Sink phase
        // (_duration is the end of Vanish phase)

        // Quadratic-Bézier control point for the Attract trajectory
        private Vector3 _attractCtrl;

        // World-space position at the start of the Sink phase
        // (pocket XY, startPos Z) → pocket XY + sinkDepth in −Z
        private Vector3 _sinkStart;

        private float _targetScale;

        // ── Playback state ────────────────────────────────────────────────────────
        private float _elapsed;
        private bool  _running;

        // ── Public properties ─────────────────────────────────────────────────────

        /// <summary>Returns <c>true</c> while the animation is playing.</summary>
        public bool IsRunning  => _running;

        /// <summary>Returns <c>true</c> after the animation has reached the end.</summary>
        public bool IsFinished => !_running && _elapsed > 0f;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Starts (or restarts after <see cref="Reset"/>) the drop animation.
        /// All positions must be in the same coordinate space (typically Unity world space).
        /// </summary>
        /// <param name="req">Animation parameters.  See <see cref="PocketDropRequest"/>.</param>
        public void StartDrop(in PocketDropRequest req)
        {
            // ── Validate / fill defaults ──
            float duration        = req.duration        > 0f    ? req.duration        : DefaultDuration;
            float sinkDepth       = req.sinkDepth       >= 0f   ? req.sinkDepth       : DefaultSinkDepth;
            float attractStrength = req.attractStrength > 0f    ? req.attractStrength : DefaultAttractStrength;
            float targetScale     = req.targetScale     > 0f    ? req.targetScale     : DefaultTargetScale;

            // Phase ratios: must be positive; re-normalise so they sum to 1.
            float attractRatio = (req.attractRatio > 0f && req.attractRatio < 1f)
                ? req.attractRatio : DefaultAttractRatio;
            float sinkRatio = (req.sinkRatio > 0f && req.sinkRatio < 1f)
                ? req.sinkRatio : DefaultSinkRatio;
            float vanishRatio = (req.vanishRatio > 0f && req.vanishRatio < 1f)
                ? req.vanishRatio : DefaultVanishRatio;

            float sum = attractRatio + sinkRatio + vanishRatio;
            if (sum < 1e-4f)
            {
                attractRatio = DefaultAttractRatio;
                sinkRatio    = DefaultSinkRatio;
                vanishRatio  = DefaultVanishRatio;
                sum          = 1f;
            }
            attractRatio /= sum;
            sinkRatio    /= sum;
            // vanishRatio  = remainder (not used beyond phase boundaries)

            // ── Cache baked values ──
            _startPos    = req.startPos;
            _pocketPos   = req.pocketPos;
            _duration    = duration;
            _targetScale = targetScale;
            _attractEnd  = duration * attractRatio;
            _sinkEnd     = _attractEnd + duration * sinkRatio;

            // ── Build Bézier control point for the Attract trajectory ──
            // The control point is offset from startPos along the entry-velocity direction
            // by (attractStrength * distance-to-pocket).  This biases the curve so that the
            // ball initially travels in the same direction it was rolling when pocketed,
            // creating a smooth, non-jarring handoff from physics simulation to animation.
            float   dist  = Vector3.Distance(req.startPos, req.pocketPos);
            Vector3 velDir;
            if (req.entryLinearVelocity.sqrMagnitude > 1e-6f)
            {
                velDir = new Vector3(req.entryLinearVelocity.x,
                                     req.entryLinearVelocity.y, 0f).normalized;
            }
            else
            {
                // No meaningful entry velocity: fall back to the direct pocket direction.
                Vector3 d = req.pocketPos - req.startPos;
                velDir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;
            }
            _attractCtrl = req.startPos + velDir * (dist * attractStrength);

            // Determine the Attract-phase endpoint (XY plane, same Z as startPos).
            // If an explicit dropTarget is provided, the ball stops there at the end of
            // the Attract phase before beginning the Sink descent.  This lets callers
            // limit the lateral travel to one ball-diameter toward the pocket center
            // (see CalcDropTarget).  When dropTarget is the zero vector, the original
            // behaviour is preserved: the ball travels all the way to the pocket XY.
            Vector3 attractEndXY = req.dropTarget.sqrMagnitude > ZeroVectorThreshold
                ? req.dropTarget
                : req.pocketPos;

            // Sink starts at the Attract endpoint (at table-surface Z) and descends
            // to pocketPos (full pocket depth).
            _sinkStart = new Vector3(attractEndXY.x, attractEndXY.y, req.startPos.z);

            // ── Reset playback ──
            _elapsed = 0f;
            _running = true;
        }

        /// <summary>
        /// Advances the animation by <paramref name="deltaTime"/> seconds and returns the
        /// current animation state snapshot.  No heap allocations.
        /// </summary>
        /// <param name="deltaTime">Frame delta time in seconds.</param>
        /// <returns>
        /// A <see cref="PocketDropState"/> describing the ball's suggested position, scale,
        /// and current phase.  When <see cref="PocketDropPhase.Finished"/> is returned the
        /// animation has completed and this helper may be returned to its object pool.
        /// </returns>
        public PocketDropState Update(float deltaTime)
        {
            if (!_running)
                return EvaluateAt(_duration);

            _elapsed += deltaTime;
            if (_elapsed >= _duration)
            {
                _elapsed = _duration;
                _running = false;
            }
            return EvaluateAt(_elapsed);
        }

        /// <summary>
        /// Samples the animation at an arbitrary normalised time [0..1] without modifying
        /// internal playback state.  Useful for scrubbing or previewing.
        /// </summary>
        public PocketDropState Evaluate(float normalizedTime)
            => EvaluateAt(Mathf.Clamp01(normalizedTime) * _duration);

        /// <summary>
        /// Immediately stops the animation without marking it as finished.
        /// <see cref="IsFinished"/> will remain <c>false</c>; <see cref="IsRunning"/>
        /// will become <c>false</c>.
        /// </summary>
        public void Stop() => _running = false;

        /// <summary>
        /// Resets to idle state.  Call before returning this helper to an object pool.
        /// After <see cref="Reset"/>, both <see cref="IsRunning"/> and
        /// <see cref="IsFinished"/> are <c>false</c>.
        /// </summary>
        public void Reset()
        {
            _elapsed = 0f;
            _running = false;
        }

        // ── Static utility: drop-target and move-time helpers ─────────────────

        /// <summary>
        /// Computes the Attract-phase end position for the drop animation.
        ///
        /// <para>The ball moves from <paramref name="dropStartPos"/> one
        /// <paramref name="ballDiameter"/> toward <paramref name="pocketWorldPos"/>, so it
        /// travels just far enough to visually enter the pocket opening without overshooting
        /// all the way to the pocket center in a single frame-locked step:</para>
        /// <code>
        /// result = dropStartPos + (pocketWorldPos − dropStartPos).normalized × ballDiameter
        /// </code>
        /// <para>Pass the result as <see cref="PocketDropRequest.dropTarget"/> before
        /// calling <see cref="StartDrop"/>.</para>
        /// </summary>
        /// <param name="dropStartPos">Ball world position at the pocketing moment.</param>
        /// <param name="pocketWorldPos">World-space centre of the pocket.</param>
        /// <param name="ballDiameter">Ball diameter in the same world units (e.g. 0.5715).</param>
        /// <returns>The target position one <paramref name="ballDiameter"/> toward the pocket.</returns>
        public static Vector3 CalcDropTarget(
            Vector3 dropStartPos, Vector3 pocketWorldPos, float ballDiameter)
        {
            Vector3 dir = pocketWorldPos - dropStartPos;
            // Guard against degenerate case where start == pocket.
            if (dir.sqrMagnitude < ZeroVectorThreshold)
                return dropStartPos;
            return dropStartPos + dir.normalized * ballDiameter;
        }

        /// <summary>
        /// Computes the total drop-animation duration from the ball's linear speed at the
        /// moment of pocketing:
        /// <code>
        /// moveTime = ballDiameter / ballLinearSpeed
        /// </code>
        /// <para>This ensures faster-moving balls complete the drop animation quickly while
        /// slower balls take proportionally longer, preserving the visual sense of speed.</para>
        /// <para>The result is clamped to [<paramref name="minDuration"/>,
        /// <paramref name="maxDuration"/>] to avoid excessively long or imperceptibly short
        /// animations when the ball speed is near zero or extremely high.</para>
        /// </summary>
        /// <param name="ballDiameter">Ball diameter in world units.</param>
        /// <param name="ballLinearSpeed">Ball linear speed (world units/s) at pocketing.</param>
        /// <param name="minDuration">Minimum allowed duration (default 0.05 s).</param>
        /// <param name="maxDuration">Maximum allowed duration (default 1.0 s).</param>
        /// <returns>Clamped animation duration in seconds.</returns>
        public static float CalcDropMoveTime(
            float ballDiameter, float ballLinearSpeed,
            float minDuration = 0.05f, float maxDuration = 1.0f)
        {
            // Guard against near-zero speed to prevent division by zero or huge durations.
            if (ballLinearSpeed < 1e-4f)
                return minDuration;
            return Mathf.Clamp(ballDiameter / ballLinearSpeed, minDuration, maxDuration);
        }

        // ── Private: evaluate state at an absolute time ───────────────────────────

        private PocketDropState EvaluateAt(float time)
        {
            float normalizedTime = _duration > 0f ? Mathf.Clamp01(time / _duration) : 1f;

            // Animation is complete
            if (time >= _duration)
            {
                return new PocketDropState
                {
                    position      = _pocketPos,
                    scale         = _targetScale,
                    alpha         = 1f,
                    phase         = PocketDropPhase.Finished,
                    normalizedTime = 1f,
                };
            }

            Vector3        pos;
            float          scale;
            PocketDropPhase phase;

            if (time < _attractEnd)
            {
                // ── Attract phase ───────────────────────────────────────────────
                // Quadratic Bézier from startPos → _attractCtrl → _sinkStart (EaseOut).
                // _sinkStart is the Attract endpoint (pocket XY or explicit dropTarget XY,
                // at the table-surface Z).  Using _sinkStart here keeps the Attract
                // endpoint consistent with the start of the following Sink phase.
                float phaseT = _attractEnd > 0f ? time / _attractEnd : 1f;
                float easeT  = 1f - (1f - phaseT) * (1f - phaseT);  // EaseOut quadratic

                float   u = 1f - easeT;
                pos  = u * u * _startPos
                     + 2f * u * easeT * _attractCtrl
                     + easeT * easeT * _sinkStart;

                scale = 1f;   // scale unchanged during Attract
                phase = PocketDropPhase.Attract;
            }
            else if (time < _sinkEnd)
            {
                // ── Sink phase ──────────────────────────────────────────────────
                // Linear Z descent from _sinkStart (pocket XY, table Z) → _pocketPos (EaseIn)
                float phaseT = (_sinkEnd > _attractEnd)
                    ? (time - _attractEnd) / (_sinkEnd - _attractEnd)
                    : 1f;
                float easeT = phaseT * phaseT;  // EaseIn quadratic

                pos   = Vector3.Lerp(_sinkStart, _pocketPos, easeT);
                scale = 1f;   // scale unchanged during Sink
                phase = PocketDropPhase.Sink;
            }
            else
            {
                // ── Vanish phase ────────────────────────────────────────────────
                // Ball is at pocketPos; smoothly shrinks from 1.0 → targetScale (EaseIn)
                float vanishDur = _duration - _sinkEnd;
                float phaseT    = vanishDur > 0f ? (time - _sinkEnd) / vanishDur : 1f;
                float easeT     = phaseT * phaseT;  // EaseIn quadratic

                pos   = _pocketPos;
                scale = Mathf.Lerp(1f, _targetScale, easeT);
                phase = PocketDropPhase.Vanish;
            }

            return new PocketDropState
            {
                position      = pos,
                scale         = scale,
                alpha         = 1f,
                phase         = phase,
                normalizedTime = normalizedTime,
            };
        }
    }
}
