// PocketPostRollAniHelper.cs
// Pure-logic animation helper that rolls a pocketed ball along the table's
// PostPocketRollPath after the drop animation finishes.
// No MonoBehaviour / Transform / Renderer / Material references – fully decoupled
// from the presentation layer.
//
// ── Minimal usage example (comments only) ──────────────────────────────────
//
//   // --- Convert SegmentData path to 3-D waypoints (caller's responsibility) ---
//   var pathPoints = new Vector3[]
//   {
//       new Vector3(rollPath.Start.x,               rollPath.Start.y,               tableZ),
//       new Vector3(rollPath.ConnectionPoints[0].x,  rollPath.ConnectionPoints[0].y,  tableZ),
//       new Vector3(rollPath.End.x,                 rollPath.End.y,                 tableZ),
//   };
//
//   // --- Setup (once, pool-friendly) ---
//   var helper = new PocketPostRollAniHelper();
//   helper.OnStop            = (pos)    => Debug.Log($"Stopped at {pos}");
//   helper.OnCueBallRetrieved = ()      => RespotCueBall();
//
//   // --- Start (called after PocketDropAniHelper finishes) ---
//   var req = new PocketPostRollRequest
//   {
//       pathPoints   = pathPoints,
//       duration     = 1.0f,
//       ballRadius   = 0.286f,
//       isCueBall    = false,
//       stoppedBalls = alreadyStoppedBalls,
//   };
//   helper.Start(in req);
//
//   // --- Per-frame ---
//   if (helper.IsRunning)
//   {
//       PocketPostRollState state = helper.Update(Time.deltaTime);
//       ball.transform.position = state.position;
//
//       if (state.phase == PocketPostRollPhase.Finished)
//           // record this ball as stopped for future post-roll helpers
//           stoppedBalls.Add(new StoppedBallInfo { ... });
//   }
//
// ──────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace BilliardPhysics.AniHelp
{
    // ─────────────────────────────────────────────────────────────────────────
    // Supporting types
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes a ball that has already completed its post-pocket roll and
    /// stopped somewhere on the roll path. Used to detect early-stop collisions
    /// for subsequent balls.
    /// </summary>
    public struct StoppedBallInfo
    {
        /// <summary>Identifier of the stopped ball.</summary>
        public int ballId;

        /// <summary>World-space position of the stopped ball's centre.</summary>
        public Vector3 position;

        /// <summary>Radius of the stopped ball in world units.</summary>
        public float radius;
    }

    /// <summary>Current phase of the post-pocket roll animation.</summary>
    public enum PocketPostRollPhase
    {
        None,
        Rolling,
        Finished,
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Request struct
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Immutable configuration for a single post-pocket roll animation.
    /// Pass with <c>in</c> to avoid copies on the call-site.
    /// </summary>
    public struct PocketPostRollRequest
    {
        /// <summary>
        /// World-space waypoints of the roll path in order:
        /// [start, CP0, CP1, …, end].  The first element should match the
        /// drop animation's final position (<see cref="PocketDropState.position"/>)
        /// so that state is continuous between the two animations.
        /// Must contain at least two points; passing null or fewer than two
        /// points results in an immediately-finished animation.
        /// </summary>
        public Vector3[] pathPoints;

        /// <summary>
        /// Total animation duration in seconds for the full path length.
        /// When the ball stops early (due to a stopped-ball collision) the
        /// actual duration is scaled proportionally to maintain constant speed.
        /// Values &lt;= 0 fall back to the default.
        /// </summary>
        public float duration;

        /// <summary>
        /// Radius of the rolling ball in world units.
        /// Used together with each <see cref="StoppedBallInfo.radius"/> to
        /// determine contact distance (r_rolling + r_stopped).
        /// </summary>
        public float ballRadius;

        /// <summary>
        /// When <c>true</c> the ball is the cue ball.  After it stops,
        /// <see cref="PocketPostRollAniHelper.OnCueBallRetrieved"/> is raised
        /// so the caller can remove it from the path and re-spot it on the table.
        /// </summary>
        public bool isCueBall;

        /// <summary>
        /// Balls that have already stopped on the path and may block this ball.
        /// May be <c>null</c> or empty.
        /// </summary>
        public StoppedBallInfo[] stoppedBalls;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // State struct  (GC-free return value)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot of the post-pocket roll animation state at a given point in time.
    /// Apply to Transform from outside; the helper never touches scene objects.
    /// </summary>
    public struct PocketPostRollState
    {
        /// <summary>Recommended world position for the ball.</summary>
        public Vector3 position;

        /// <summary>Which animation phase is currently active.</summary>
        public PocketPostRollPhase phase;

        /// <summary>Overall progress through the animation, 0..1.</summary>
        public float normalizedTime;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper class
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pure-logic helper that drives the "ball rolls along the post-pocket path"
    /// animation following a <see cref="PocketDropAniHelper"/> drop sequence.
    /// <para>
    /// The ball travels from <c>pathPoints[0]</c> toward <c>pathPoints[last]</c>.
    /// If a previously stopped ball lies within contact range on the path the
    /// rolling ball stops at the contact point instead of reaching the end.
    /// </para>
    /// <para>
    /// When <see cref="PocketPostRollRequest.isCueBall"/> is <c>true</c>, the
    /// <see cref="OnCueBallRetrieved"/> callback fires once the ball stops so
    /// the caller can remove it from the path and re-spot it on the table.
    /// </para>
    /// One instance is reusable: call <see cref="Start"/> multiple times.
    /// Does <b>not</b> reference <c>MonoBehaviour</c>, <c>Transform</c>,
    /// <c>Renderer</c>, or <c>Material</c>.
    /// </summary>
    public sealed class PocketPostRollAniHelper
    {
        // ── Default parameters ────────────────────────────────────────────────
        private const float DefaultDuration = 1.0f;

        // ── Runtime state ─────────────────────────────────────────────────────
        private bool    _isRunning;
        private bool    _isFinished;
        private float   _elapsed;
        private float   _duration;
        private bool    _isCueBall;

        // Effective (possibly clipped) path data
        private Vector3[] _waypoints;          // full path waypoints
        private float[]   _cumulativeLengths;  // cumulative arc-length at each waypoint index
        private float     _totalLength;        // effective path length (≤ full length)
        private Vector3   _finalPosition;      // position at arc-length == _totalLength

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Whether the animation is currently playing.</summary>
        public bool IsRunning  => _isRunning;

        /// <summary>Whether the animation has completed (Finished state).</summary>
        public bool IsFinished => _isFinished;

        /// <summary>
        /// Raised once when the ball reaches its final stop position (either the
        /// path end or an early contact point). Argument: final world-space position.
        /// </summary>
        public System.Action<Vector3> OnStop;

        /// <summary>
        /// Raised once after <see cref="OnStop"/> when the ball is the cue ball
        /// (<see cref="PocketPostRollRequest.isCueBall"/> == <c>true</c>).
        /// The caller should remove the cue ball from the path track and re-spot
        /// it on the table. After this event the cue ball must no longer appear in
        /// <see cref="PocketPostRollRequest.stoppedBalls"/> for subsequent rolls.
        /// </summary>
        public System.Action OnCueBallRetrieved;

        /// <summary>
        /// Initialise (or re-initialise) the helper with the supplied request.
        /// Safe to call on a pooled instance that was previously used.
        /// </summary>
        public void Start(in PocketPostRollRequest req)
        {
            _isCueBall = req.isCueBall;
            _duration  = req.duration > 0f ? req.duration : DefaultDuration;

            // ── Build waypoints ───────────────────────────────────────────────
            _waypoints = (req.pathPoints != null && req.pathPoints.Length >= 2)
                ? req.pathPoints
                : null;

            if (_waypoints == null)
            {
                // Degenerate path – finish immediately.
                _totalLength   = 0f;
                _finalPosition = (req.pathPoints != null && req.pathPoints.Length > 0)
                    ? req.pathPoints[0]
                    : Vector3.zero;
                _isRunning  = false;
                _isFinished = true;
                return;
            }

            // ── Compute cumulative arc-lengths ────────────────────────────────
            int n = _waypoints.Length;
            _cumulativeLengths    = new float[n];
            _cumulativeLengths[0] = 0f;
            float fullLength = 0f;
            for (int i = 1; i < n; i++)
            {
                fullLength           += (_waypoints[i] - _waypoints[i - 1]).magnitude;
                _cumulativeLengths[i] = fullLength;
            }

            // ── Find earliest blocking contact point ──────────────────────────
            float blockArcLen = fullLength;
            if (req.stoppedBalls != null)
            {
                foreach (var sb in req.stoppedBalls)
                {
                    float contactDist = req.ballRadius + sb.radius;
                    float arcLen      = FindContactArcLength(_waypoints, contactDist, sb.position);
                    if (arcLen < blockArcLen)
                        blockArcLen = arcLen;
                }
            }

            _totalLength   = Mathf.Max(0f, blockArcLen);
            _finalPosition = PositionAtArcLength(_totalLength);

            // Scale duration proportionally so rolling speed stays constant.
            if (fullLength > 0f)
                _duration *= _totalLength / fullLength;

            _elapsed    = 0f;
            _isRunning  = _totalLength > 0f;
            _isFinished = false;

            // Zero-length effective path: finish immediately without events.
            if (_totalLength <= 0f)
            {
                _isRunning  = false;
                _isFinished = true;
            }
        }

        /// <summary>
        /// Advance the animation by <paramref name="deltaTime"/> seconds and
        /// return the current state.  No heap allocations.
        /// </summary>
        public PocketPostRollState Update(float deltaTime)
        {
            if (!_isRunning)
                return BuildFinishedState();

            _elapsed += deltaTime;
            float t = _duration > 0f ? _elapsed / _duration : 1f;

            if (t >= 1f)
            {
                t          = 1f;
                _isRunning  = false;
                _isFinished = true;

                var finishedState = Evaluate(t);
                OnStop?.Invoke(finishedState.position);
                if (_isCueBall)
                    OnCueBallRetrieved?.Invoke();
                return finishedState;
            }

            return Evaluate(t);
        }

        /// <summary>
        /// Sample the animation at an arbitrary normalised time (0..1) without
        /// modifying internal state.  Input is clamped to [0, 1].
        /// No heap allocations.
        /// </summary>
        public PocketPostRollState Evaluate(float normalizedTime)
        {
            float t = Mathf.Clamp01(normalizedTime);

            PocketPostRollState state;
            state.normalizedTime = t;

            if (t >= 1f || _totalLength <= 0f || _waypoints == null)
            {
                state.phase    = PocketPostRollPhase.Finished;
                state.position = _finalPosition;
                return state;
            }

            state.phase    = PocketPostRollPhase.Rolling;
            state.position = PositionAtArcLength(t * _totalLength);
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

        /// <summary>
        /// Returns the world-space position on the polyline at
        /// <paramref name="arcLen"/> distance from the start.
        /// </summary>
        private Vector3 PositionAtArcLength(float arcLen)
        {
            if (_waypoints == null || _waypoints.Length == 0)
                return Vector3.zero;
            if (arcLen <= 0f)
                return _waypoints[0];

            int n = _waypoints.Length;
            for (int i = 1; i < n; i++)
            {
                float segEnd = _cumulativeLengths[i];
                if (arcLen <= segEnd || i == n - 1)
                {
                    float segStart = _cumulativeLengths[i - 1];
                    float segLen   = segEnd - segStart;
                    float localT   = (segLen > 0f)
                        ? Mathf.Clamp01((arcLen - segStart) / segLen)
                        : 1f;
                    return Vector3.LerpUnclamped(_waypoints[i - 1], _waypoints[i], localT);
                }
            }
            return _waypoints[n - 1];
        }

        /// <summary>
        /// Returns the arc-length along the polyline at which the rolling ball
        /// (combined contact distance <paramref name="contactDist"/> = r_rolling + r_stopped)
        /// first touches the stopped ball at <paramref name="stoppedCenter"/>.
        /// <para>
        /// Uses a quadratic ray-sphere intersection on each polyline segment.
        /// Only the first contact point encountered along the forward direction
        /// is returned.  Returns <see cref="float.MaxValue"/> when no contact
        /// occurs along the path.
        /// </para>
        /// </summary>
        private float FindContactArcLength(Vector3[] pts, float contactDist, Vector3 stoppedCenter)
        {
            float distSq = contactDist * contactDist;

            for (int i = 1; i < pts.Length; i++)
            {
                Vector3 A    = pts[i - 1];
                Vector3 B    = pts[i];
                Vector3 segV = B - A;
                Vector3 u    = A - stoppedCenter;

                float a    = Vector3.Dot(segV, segV);
                float b    = 2f * Vector3.Dot(u, segV);
                float c    = Vector3.Dot(u, u) - distSq;
                float disc = b * b - 4f * a * c;

                if (disc < 0f) continue; // no intersection with this segment

                float sqrtDisc = Mathf.Sqrt(disc);

                // Take the smallest non-negative root (first entering contact).
                float t1 = (-b - sqrtDisc) / (2f * a);
                float t2 = (-b + sqrtDisc) / (2f * a);

                float tHit = -1f;
                if      (t1 >= 0f && t1 <= 1f) tHit = t1;
                else if (t2 >= 0f && t2 <= 1f) tHit = t2;

                if (tHit < 0f) continue;

                // Arc-length from path start to the contact point.
                float segArcLen = _cumulativeLengths[i - 1] + tHit * segV.magnitude;
                return segArcLen;
            }

            return float.MaxValue; // no contact along path
        }

        private PocketPostRollState BuildFinishedState()
        {
            PocketPostRollState state;
            state.phase          = PocketPostRollPhase.Finished;
            state.position       = _finalPosition;
            state.normalizedTime = 1f;
            return state;
        }
    }
}
