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
using BilliardPhysics;

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
        /// Ignored when <see cref="rollPath"/> is non-null.
        /// </summary>
        public Vector3[] pathPoints;

        /// <summary>
        /// Roll path as a <see cref="SegmentData"/> (Start / ConnectionPoints / End)
        /// sourced directly from <c>TableConfig.PostPocketRollPath</c>.
        /// When non-null this overrides <see cref="pathPoints"/>; the 2-D waypoints
        /// are elevated to 3-D using <see cref="tableZ"/>.
        /// </summary>
        public SegmentData rollPath;

        /// <summary>
        /// World-space Z of the table surface, used to convert 2-D
        /// <see cref="rollPath"/> waypoints to 3-D world positions.
        /// Only meaningful when <see cref="rollPath"/> is non-null.
        /// </summary>
        public float tableZ;

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
        /// Overridden by <c>ball.Radius</c> when <see cref="ball"/> is non-null.
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

        /// <summary>
        /// Optional Ball instance. When non-null, <see cref="ballRadius"/> is
        /// overridden by <c>ball.Radius</c> converted to world units.
        /// </summary>
        public Ball ball;

        /// <summary>
        /// Rolling friction coefficient (≥ 0) for energy-loss simulation.
        /// At 0 (default) rolling speed is constant—identical to previous behaviour.
        /// Higher values cause the instantaneous linear and angular speed to decay
        /// exponentially toward the end of the path, simulating cloth friction.
        /// </summary>
        public float rollingFriction;
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

        /// <summary>
        /// Angular velocity (rad/s) for physically correct rolling (no-slip condition).
        /// Computed as <c>Cross(Vector3.forward, linearVelocity) / ballRadius</c>.
        /// Decays exponentially when <see cref="PocketPostRollRequest.rollingFriction"/> &gt; 0.
        /// Zero when the ball is stopped or the animation is finished.
        /// </summary>
        public Vector3 angularVelocity;

        /// <summary>Which animation phase is currently active.</summary>
        public PocketPostRollPhase phase;

        /// <summary>Overall progress through the animation, 0..1.</summary>
        public float normalizedTime;

        /// <summary>
        /// Random initial rotation angle (radians, in [0, 2π)) generated once
        /// when <see cref="PocketPostRollAniHelper.Start"/> is called.
        /// Apply this as an initial spin offset to the ball's 3-D model rotation
        /// so that each ball entering the pocket starts from a visually varied
        /// orientation.  Constant throughout the animation.
        /// </summary>
        public float initialSpinAngle;
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
        private float   _ballRadius;
        private float   _rollingFriction;
        private float   _initialSpinAngle;

        // Effective (possibly clipped) path data
        private Vector3[] _waypoints;          // full path waypoints
        private float[]   _cumulativeLengths;  // cumulative arc-length at each waypoint index
        private float     _totalLength;        // effective path length (≤ full length)
        private Vector3   _finalPosition;      // position at arc-length == _totalLength
        private float     _startZ;             // Z of the first waypoint; held constant during rolling

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
            _isCueBall       = req.isCueBall;
            _duration        = req.duration > 0f ? req.duration : DefaultDuration;
            _rollingFriction = Mathf.Max(0f, req.rollingFriction);

            // Ball radius: prefer the physics Ball's radius when provided.
            _ballRadius = (req.ball != null)
                ? req.ball.Radius.ToFloat()
                : req.ballRadius;

            // Random initial spin angle: a fresh random orientation each time
            // Start is called so each ball visually begins from a varied pose.
            _initialSpinAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            // ── Build waypoints ───────────────────────────────────────────────
            // rollPath (SegmentData) takes priority over pathPoints when provided.
            Vector3[] resolvedPoints = null;
            if (req.rollPath != null)
            {
                resolvedPoints = BuildWaypointsFromSegmentData(req.rollPath, req.tableZ);
            }
            else if (req.pathPoints != null && req.pathPoints.Length >= 2)
            {
                resolvedPoints = req.pathPoints;
            }

            _waypoints = (resolvedPoints != null && resolvedPoints.Length >= 2)
                ? resolvedPoints
                : null;

            _startZ = (_waypoints != null) ? _waypoints[0].z : 0f;

            if (_waypoints == null)
            {
                // Degenerate path – finish immediately.
                _totalLength   = 0f;
                _finalPosition = (resolvedPoints != null && resolvedPoints.Length > 0)
                    ? resolvedPoints[0]
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
                    float contactDist = _ballRadius + sb.radius;
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
            state.normalizedTime  = t;
            state.initialSpinAngle = _initialSpinAngle;

            if (t >= 1f || _totalLength <= 0f || _waypoints == null)
            {
                state.phase           = PocketPostRollPhase.Finished;
                state.position        = _finalPosition;
                state.angularVelocity = Vector3.zero;
                return state;
            }

            float arcLen = t * _totalLength;
            state.phase    = PocketPostRollPhase.Rolling;
            state.position = PositionAtArcLength(arcLen);

            // Angular velocity for physically correct rolling (no-slip condition):
            //   ω = Cross(surfaceNormal, linearVelocity) / ballRadius
            // Surface normal is Vector3.forward (Z-up, table in the XY plane).
            // Speed decays exponentially when rollingFriction > 0, simulating
            // energy loss due to cloth friction as the ball travels the path.
            Vector3 dir      = DirectionAtArcLength(arcLen);
            float   baseSpeed = _duration > 0f ? _totalLength / _duration : 0f;
            float   decay     = _rollingFriction > 0f ? Mathf.Exp(-_rollingFriction * t) : 1f;
            float   speed     = baseSpeed * decay;
            state.angularVelocity = _ballRadius > 0f
                ? Vector3.Cross(Vector3.forward, dir * speed) / _ballRadius
                : Vector3.zero;

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
            _isRunning        = false;
            _isFinished       = false;
            _elapsed          = 0f;
            _rollingFriction  = 0f;
            _initialSpinAngle = 0f;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Converts a <see cref="SegmentData"/> (2-D Start/ConnectionPoints/End)
        /// into a <c>Vector3[]</c> waypoint array for use by the animation.
        /// The Z coordinate of every waypoint is set to <paramref name="tableZ"/>.
        /// Returns <c>null</c> when the segment data does not define a valid path
        /// (fewer than two distinct points).
        /// </summary>
        private static Vector3[] BuildWaypointsFromSegmentData(SegmentData seg, float tableZ)
        {
            if (seg == null)
                return null;

            int cpCount = seg.ConnectionPoints != null ? seg.ConnectionPoints.Count : 0;
            // Total points: Start + CPs + End
            var pts = new Vector3[2 + cpCount];
            pts[0] = new Vector3(seg.Start.x, seg.Start.y, tableZ);
            for (int i = 0; i < cpCount; i++)
                pts[1 + i] = new Vector3(
                    seg.ConnectionPoints[i].x,
                    seg.ConnectionPoints[i].y,
                    tableZ);
            pts[pts.Length - 1] = new Vector3(seg.End.x, seg.End.y, tableZ);
            return pts;
        }

        /// <summary>
        /// Returns the world-space position on the polyline at
        /// <paramref name="arcLen"/> distance from the start.
        /// The Z component is always <c>_startZ</c> so the ball stays on the
        /// flat table surface regardless of any Z variation in the waypoints.
        /// </summary>
        private Vector3 PositionAtArcLength(float arcLen)
        {
            if (_waypoints == null || _waypoints.Length == 0)
                return Vector3.zero;
            if (arcLen <= 0f)
            {
                var p = _waypoints[0];
                p.z = _startZ;
                return p;
            }

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
                    var pos = Vector3.LerpUnclamped(_waypoints[i - 1], _waypoints[i], localT);
                    pos.z = _startZ;
                    return pos;
                }
            }
            var last = _waypoints[n - 1];
            last.z = _startZ;
            return last;
        }

        /// <summary>
        /// Returns the normalised forward direction of the polyline segment that
        /// contains <paramref name="arcLen"/>. Used for angular-velocity computation.
        /// </summary>
        private Vector3 DirectionAtArcLength(float arcLen)
        {
            if (_waypoints == null || _waypoints.Length < 2)
                return Vector3.zero;

            int n = _waypoints.Length;
            for (int i = 1; i < n; i++)
            {
                if (arcLen <= _cumulativeLengths[i] || i == n - 1)
                {
                    Vector3 seg    = _waypoints[i] - _waypoints[i - 1];
                    float   segLen = seg.magnitude;
                    return segLen > 0f ? seg / segLen : Vector3.zero;
                }
            }
            return Vector3.zero;
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
            state.phase            = PocketPostRollPhase.Finished;
            state.position         = _finalPosition;
            state.angularVelocity  = Vector3.zero;
            state.normalizedTime   = 1f;
            state.initialSpinAngle = _initialSpinAngle;
            return state;
        }
    }
}
