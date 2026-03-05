using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.AniHelp;
using BilliardPhysics.Runtime.ViewTool;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Temporary scale-state companion for a ball undergoing the pocket-drop animation.
/// Not a persistent field on <see cref="Ball"/>; it is created when the animation
/// begins and discarded when the animation ends, keeping the physics model clean.
/// </summary>
public sealed class BallScaleState
{
    /// <summary>
    /// Current uniform scale [0..1].  Updated each frame by
    /// <see cref="BallDropController"/> and applied to the ball's
    /// <c>Transform.localScale</c>.
    /// </summary>
    public float Scale = 1f;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lightweight object pool that reuses <see cref="PocketDropAniHelper"/> instances.
/// Concurrent animations (multiple balls pocketed in the same frame) must each have
/// their own helper instance; sharing one instance would cause later calls to
/// <c>StartDrop</c> to overwrite the previous animation's state.
/// </summary>
public sealed class PocketDropAniHelperPool
{
    private readonly Stack<PocketDropAniHelper> _stack = new Stack<PocketDropAniHelper>();

    /// <summary>Returns a helper from the pool, or creates a new one if the pool is empty.</summary>
    public PocketDropAniHelper Rent()
        => _stack.Count > 0 ? _stack.Pop() : new PocketDropAniHelper();

    /// <summary>
    /// Returns a helper to the pool.  <see cref="PocketDropAniHelper.Reset"/> is called
    /// automatically before the helper is stored.
    /// </summary>
    public void Return(PocketDropAniHelper helper)
    {
        helper.Reset();
        _stack.Push(helper);
    }
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Data container for one active pocket-drop animation.</summary>
internal sealed class ActiveDrop
{
    /// <summary>Physics-layer ball (authoritative position and velocity source).</summary>
    public Ball             BallData;
    /// <summary>Render-layer transform driven by the animation.</summary>
    public Transform        BallTransform;
    /// <summary>Render-layer renderer (used to write material colour / alpha).</summary>
    public Renderer         BallRenderer;
    /// <summary>Base material colour captured at pocketing time.</summary>
    public Color            BaseColor;
    /// <summary>Temporary scale companion; discarded when animation ends.</summary>
    public BallScaleState   ScaleState;
    /// <summary>Animation driver obtained from the pool.</summary>
    public PocketDropAniHelper Helper;
    /// <summary>
    /// Current visual rotation, integrated each frame from the ball's entry
    /// <see cref="Ball.AngularVelocity"/> via
    /// <see cref="PhysicsToView.IntegrateRotation"/>.
    /// </summary>
    public Quaternion       Rotation;
    /// <summary>Post-pocket roll path (from <c>TableConfig.PostPocketRollPath</c>).</summary>
    public SegmentData      RollPath;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Data container for one active post-pocket roll animation.</summary>
internal sealed class ActiveRoll
{
    /// <summary>Physics-layer ball; radius and velocity are updated during rolling.</summary>
    public Ball       BallData;
    /// <summary>Render-layer transform driven along the path.</summary>
    public Transform  BallTransform;
    /// <summary>Path waypoints: [Start, CP0, CP1, …, End].</summary>
    public Vector3[]  Waypoints;
    /// <summary>Index of the waypoint segment the ball is currently traversing.</summary>
    public int        SegIdx;
    /// <summary>Normalised progress within the current segment, [0..1].</summary>
    public float      SegT;
    /// <summary>Rolling speed (world units / second).</summary>
    public float      Speed;
    /// <summary>Current visual rotation; updated each frame via
    /// <see cref="PhysicsToView.IntegrateRotation"/>.</summary>
    public Quaternion Rotation;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// MonoBehaviour controller that drives the complete post-pocketing animation pipeline:
///
/// <list type="number">
///   <item><b>Drop animation</b> (<see cref="PocketDropPhase"/> Attract → Sink → Vanish):
///     The ball visually rolls toward the pocket center along a Bézier trajectory blended
///     from its entry <see cref="Ball.LinearVelocity"/>, then shrinks to 0.75 of its
///     original size.  <see cref="Ball.AngularVelocity"/> drives the visual spin throughout,
///     ensuring a smooth, non-jarring handoff from physics simulation to animation.</item>
///   <item><b>Roll-path animation</b>: After the drop animation completes, the ball is
///     placed at <c>TableConfig.PostPocketRollPath.Start</c>, assigned a random initial
///     spin-direction angle so that each ball appears visually distinct, and rolls along
///     Start → ConnectionPoints → End.  <see cref="Ball.LinearVelocity"/> and
///     <see cref="Ball.AngularVelocity"/> are updated every frame to reflect the actual
///     rolling state (no-slip constraint).  If the ball contacts any previously stopped
///     ball before reaching End, it stops immediately at the contact edge.</item>
/// </list>
///
/// <para>Multiple balls can be animated concurrently; each active animation has its own
/// independent state.  Call <see cref="ClearPocketedBalls"/> at the start of a new rack
/// to clear the stopped-ball registry.</para>
/// </summary>
[AddComponentMenu("BilliardPhysics/Ball Drop Controller")]
public class BallDropController : MonoBehaviour
{
    // ── Inspector parameters ──────────────────────────────────────────────────

    /// <summary>Speed at which pocketed balls roll along the post-pocket path (world units / s).</summary>
    [Tooltip("Speed at which pocketed balls roll along the post-pocket path (world units per second).")]
    public float RollSpeed = 0.5f;

    // ── Internal state ────────────────────────────────────────────────────────

    // Object pool: reuse PocketDropAniHelper instances to avoid per-pocket GC.
    private readonly PocketDropAniHelperPool _pool = new PocketDropAniHelperPool();

    // Active drop animations (one entry per ball currently undergoing the drop phase).
    private readonly List<ActiveDrop> _activeDrops = new List<ActiveDrop>();

    // Active roll animations (one entry per ball rolling along the post-pocket path).
    private readonly List<ActiveRoll> _activeRolls = new List<ActiveRoll>();

    // Registry of balls that have stopped on the roll path (world position + radius).
    // Used for collision detection against newly-rolling balls.
    private readonly List<(Vector3 pos, float radius)> _stoppedBalls =
        new List<(Vector3 pos, float radius)>();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Call when the physics layer reports that a single ball has been pocketed.
    /// Reads <see cref="Ball.Position"/>, <see cref="Ball.LinearVelocity"/>, and
    /// <see cref="Ball.AngularVelocity"/> at the pocketing moment as the authoritative
    /// data for starting the animation.
    /// </summary>
    /// <param name="ball">Physics ball (position and velocity authority).</param>
    /// <param name="ballTransform">The ball's render-layer <c>Transform</c>.</param>
    /// <param name="ballRenderer">The ball's <c>Renderer</c> (for alpha writes).</param>
    /// <param name="pocketWorldPos">World-space centre of the pocket the ball entered.</param>
    /// <param name="rollPath">
    /// Post-pocket roll path from <c>TableConfig.PostPocketRollPath</c>.
    /// Pass <c>null</c> or a path where <c>Start == End</c> with no
    /// <c>ConnectionPoints</c> to skip the roll phase.
    /// </param>
    public void OnBallPocketed(
        Ball ball, Transform ballTransform, Renderer ballRenderer,
        Vector3 pocketWorldPos, SegmentData rollPath)
    {
        StartOneDrop(ball, ballTransform, ballRenderer, pocketWorldPos, rollPath);
    }

    /// <summary>
    /// Call when multiple balls are pocketed in the same frame.
    /// Each ball receives its own independent animation helper instance so concurrent
    /// animations do not interfere with each other.
    /// </summary>
    public void OnBallsPocketed(
        IReadOnlyList<(Ball ball, Transform t, Renderer r,
                       Vector3 pocketWorldPos, SegmentData rollPath)> drops)
    {
        foreach (var (ball, t, r, pocketWorldPos, rollPath) in drops)
            StartOneDrop(ball, t, r, pocketWorldPos, rollPath);
    }

    /// <summary>
    /// Clears the registry of balls that have stopped on the post-pocket roll path.
    /// Call at the start of a new rack so stopped-ball collision data from the previous
    /// rack does not affect the current one.
    /// </summary>
    public void ClearPocketedBalls() => _stoppedBalls.Clear();

    // ── MonoBehaviour ─────────────────────────────────────────────────────────

    private void Update()
    {
        TickDrops(Time.deltaTime);
        TickRolls(Time.deltaTime);
    }

    // ── Internal: start one drop animation ───────────────────────────────────

    private void StartOneDrop(
        Ball ball, Transform ballTransform, Renderer ballRenderer,
        Vector3 pocketWorldPos, SegmentData rollPath)
    {
        // Read the authoritative position from the physics model.
        // Z is taken from the render transform so the animation stays in the correct plane.
        Vector3 startPos = new Vector3(
            ball.Position.X.ToFloat(),
            ball.Position.Y.ToFloat(),
            ballTransform.position.z);

        // Convert physics LinearVelocity → Unity XY vector for Bézier trajectory blending.
        // This shapes the Attract phase so the drop animation begins in the same direction
        // the ball was rolling when it entered the pocket (no visual pop or direction jump).
        Vector2 entryLinearVelocity = new Vector2(
            ball.LinearVelocity.X.ToFloat(),
            ball.LinearVelocity.Y.ToFloat());

        // Obtain a helper from the pool and start the drop animation.
        PocketDropAniHelper helper = _pool.Rent();
        helper.StartDrop(new PocketDropRequest
        {
            startPos            = startPos,
            pocketPos           = pocketWorldPos,
            entryLinearVelocity = entryLinearVelocity,
            duration            = 0.25f,
            sinkDepth           = 0.18f,
            attractRatio        = 0.30f,
            sinkRatio           = 0.50f,
            vanishRatio         = 0.20f,
            attractStrength     = 0.25f,
            targetScale         = 0.75f,
        });

        _activeDrops.Add(new ActiveDrop
        {
            BallData      = ball,
            BallTransform = ballTransform,
            BallRenderer  = ballRenderer,
            BaseColor     = ballRenderer.material.color,
            ScaleState    = new BallScaleState { Scale = 1f },
            Helper        = helper,
            // Seed rotation from the current transform so there is no visual snap.
            Rotation      = ballTransform.rotation,
            RollPath      = rollPath,
        });
    }

    // ── Internal: drive active drop animations ────────────────────────────────

    private void TickDrops(float deltaTime)
    {
        for (int i = _activeDrops.Count - 1; i >= 0; i--)
        {
            ActiveDrop      drop  = _activeDrops[i];
            PocketDropState state = drop.Helper.Update(deltaTime);

            // Apply position and scale to the render transform.
            drop.BallTransform.position   = state.position;
            drop.ScaleState.Scale         = state.scale;
            drop.BallTransform.localScale = Vector3.one * state.scale;

            // Apply colour alpha (kept at 1 for this animation; field is reserved
            // for callers that want to add a fade effect).
            Color c = drop.BaseColor;
            drop.BallRenderer.material.color = new Color(c.r, c.g, c.b, state.alpha);

            // Integrate visual rotation from the ball's entry angular velocity.
            // PhysicsToView.IntegrateRotation applies the correct axial-vector
            // coordinate transform (physics Z-up → view −Z).
            Vector3 physOmega = new Vector3(
                drop.BallData.AngularVelocity.X.ToFloat(),
                drop.BallData.AngularVelocity.Y.ToFloat(),
                drop.BallData.AngularVelocity.Z.ToFloat());
            drop.Rotation               = PhysicsToView.IntegrateRotation(drop.Rotation, physOmega, deltaTime);
            drop.BallTransform.rotation = drop.Rotation;

            if (state.phase == PocketDropPhase.Finished)
            {
                // Return the helper to the pool; ScaleState has no further use.
                _pool.Return(drop.Helper);

                // Check whether a valid roll path is configured.
                SegmentData path    = drop.RollPath;
                bool        hasPath = path != null &&
                                      (path.Start != path.End ||
                                       (path.ConnectionPoints != null &&
                                        path.ConnectionPoints.Count > 0));
                if (hasPath)
                {
                    // Restore original scale and hand off to the roll phase.
                    drop.BallTransform.localScale = Vector3.one;
                    StartRoll(drop.BallData, drop.BallTransform, path);
                }
                else
                {
                    // No roll path configured: hide the ball.
                    drop.BallTransform.gameObject.SetActive(false);
                }

                _activeDrops.RemoveAt(i);
            }
        }
    }

    // ── Internal: build roll path and enqueue the rolling animation ───────────

    private void StartRoll(Ball ball, Transform ballTransform, SegmentData path)
    {
        int cpCount = path.ConnectionPoints?.Count ?? 0;

        // Build the flat waypoint array: [Start, CP0, CP1, …, End].
        // All Z values are kept equal (per requirement: all balls share the same Z).
        float     z         = ballTransform.position.z;
        Vector3[] waypoints = new Vector3[cpCount + 2];
        waypoints[0] = new Vector3(path.Start.x, path.Start.y, z);
        for (int k = 0; k < cpCount; k++)
            waypoints[k + 1] = new Vector3(
                path.ConnectionPoints[k].x, path.ConnectionPoints[k].y, z);
        waypoints[cpCount + 1] = new Vector3(path.End.x, path.End.y, z);

        // Place the ball at the path start point.
        ballTransform.position = waypoints[0];

        // Assign a random roll-direction angle at Start so each ball begins the path
        // with a unique visual orientation.  The angle is a random rotation around the
        // Z-axis (table normal), making every ball look like it arrived from a different
        // spin orientation while still rolling along the prescribed path direction.
        float      randomYaw    = Random.Range(0f, 360f);
        Quaternion startRotation = Quaternion.Euler(0f, 0f, randomYaw);
        ballTransform.rotation  = startRotation;

        // Initialise the ball's physics velocity to match the rolling state at the first
        // path segment.  This keeps Ball.LinearVelocity and Ball.AngularVelocity in sync
        // with the visual motion from the very first frame.
        SetBallRollingVelocity(ball, waypoints, 0, RollSpeed);

        _activeRolls.Add(new ActiveRoll
        {
            BallData      = ball,
            BallTransform = ballTransform,
            Waypoints     = waypoints,
            SegIdx        = 0,
            SegT          = 0f,
            Speed         = RollSpeed,
            Rotation      = startRotation,
        });
    }

    // ── Internal: drive active roll animations ────────────────────────────────

    private void TickRolls(float deltaTime)
    {
        for (int i = _activeRolls.Count - 1; i >= 0; i--)
        {
            ActiveRoll roll    = _activeRolls[i];
            bool       stopped = AdvanceRoll(roll, deltaTime);

            if (!stopped)
            {
                // Integrate visual rotation from the ball's current angular velocity.
                // The angular velocity was just updated inside AdvanceRoll to match the
                // rolling state, so this produces correct spin for the path direction.
                Vector3 physOmega = new Vector3(
                    roll.BallData.AngularVelocity.X.ToFloat(),
                    roll.BallData.AngularVelocity.Y.ToFloat(),
                    roll.BallData.AngularVelocity.Z.ToFloat());
                roll.Rotation               = PhysicsToView.IntegrateRotation(roll.Rotation, physOmega, deltaTime);
                roll.BallTransform.rotation = roll.Rotation;
            }

            if (stopped)
            {
                // Record the stopping position for future ball collision checks.
                _stoppedBalls.Add((
                    roll.BallTransform.position,
                    roll.BallData.Radius.ToFloat()));

                // Zero out the ball's physics velocity now that it is stationary.
                roll.BallData.LinearVelocity  = FixVec2.Zero;
                roll.BallData.AngularVelocity = FixVec3.Zero;

                // Hide the ball (hand back to a ball pool here if available).
                roll.BallTransform.gameObject.SetActive(false);
                _activeRolls.RemoveAt(i);
            }
        }
    }

    // ── Internal: advance one roll by deltaTime; returns true when ball stops ──

    /// <summary>
    /// Moves <paramref name="roll"/> along its waypoint path.
    /// Returns <c>true</c> when the ball reaches the End waypoint or is stopped by a
    /// previously-stopped ball.  Updates <see cref="Ball.LinearVelocity"/> and
    /// <see cref="Ball.AngularVelocity"/> to match the rolling state each frame.
    /// </summary>
    private bool AdvanceRoll(ActiveRoll roll, float deltaTime)
    {
        float selfRadius = roll.BallData.Radius.ToFloat();

        while (deltaTime > 0f && roll.SegIdx < roll.Waypoints.Length - 1)
        {
            Vector3 from   = roll.Waypoints[roll.SegIdx];
            Vector3 to     = roll.Waypoints[roll.SegIdx + 1];
            float   segLen = Vector3.Distance(from, to);

            if (segLen < 0.0001f)
            {
                // Degenerate segment (zero length): skip without consuming time.
                roll.SegIdx++;
                continue;
            }

            float   remaining = (1f - roll.SegT) * segLen;
            float   step      = roll.Speed * deltaTime;
            Vector3 newPos;

            if (step >= remaining)
            {
                // Ball reaches the next waypoint within this time step.
                newPos      = to;
                roll.SegT   = 0f;
                roll.SegIdx++;
                deltaTime  -= remaining / roll.Speed;
            }
            else
            {
                // Ball stays within the current segment.
                roll.SegT += step / segLen;
                deltaTime  = 0f;
                newPos     = Vector3.Lerp(from, to, roll.SegT);
            }

            // ── Collision check against previously stopped balls ──────────────
            foreach (var (stoppedPos, stoppedRadius) in _stoppedBalls)
            {
                float contactDist = selfRadius + stoppedRadius;
                if (Vector3.Distance(newPos, stoppedPos) <= contactDist)
                {
                    // Stop at the contact edge rather than overlapping.
                    Vector3 dir = newPos - stoppedPos;
                    if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
                    roll.BallTransform.position = stoppedPos + dir.normalized * contactDist;
                    return true;
                }
            }

            roll.BallTransform.position = newPos;

            // ── Update physics velocity to match rolling state ────────────────
            // Keep LinearVelocity and AngularVelocity in sync with the path direction.
            // Only update while still within the path (not after reaching End).
            if (roll.SegIdx < roll.Waypoints.Length - 1)
                SetBallRollingVelocity(roll.BallData, roll.Waypoints, roll.SegIdx, roll.Speed);
        }

        // Returns true if the ball has reached (or overshot) the End waypoint.
        return roll.SegIdx >= roll.Waypoints.Length - 1;
    }

    // ── Helper: set Ball.LinearVelocity + AngularVelocity for rolling along path ──

    /// <summary>
    /// Sets <see cref="Ball.LinearVelocity"/> and <see cref="Ball.AngularVelocity"/> to
    /// match the no-slip rolling state for the specified path segment and speed.
    ///
    /// <para>Rolling-without-slip constraint (physics Z-up frame, r = ball radius):
    /// <c>ω.X = −Lv.Y / r</c>, <c>ω.Y = +Lv.X / r</c>, <c>ω.Z = 0</c>.</para>
    /// </summary>
    /// <param name="ball">Ball whose velocity fields are updated.</param>
    /// <param name="waypoints">Path waypoint array.</param>
    /// <param name="segIdx">Index of the current segment (start waypoint index).</param>
    /// <param name="speed">Rolling speed (world units / second).</param>
    private static void SetBallRollingVelocity(
        Ball ball, Vector3[] waypoints, int segIdx, float speed)
    {
        if (segIdx >= waypoints.Length - 1) return;

        Vector3 dir = waypoints[segIdx + 1] - waypoints[segIdx];
        if (dir.sqrMagnitude < 1e-8f) return;
        dir.Normalize();

        float vx = dir.x * speed;
        float vy = dir.y * speed;
        float r  = ball.Radius.ToFloat();

        ball.LinearVelocity = new FixVec2(
            Fix64.FromFloat(vx),
            Fix64.FromFloat(vy));

        // No-slip rolling constraint: ω.X = −Lv.Y / r,  ω.Y = +Lv.X / r,  ω.Z = 0
        ball.AngularVelocity.X = Fix64.FromFloat(-vy / r);
        ball.AngularVelocity.Y = Fix64.FromFloat( vx / r);
        ball.AngularVelocity.Z = Fix64.Zero;
    }
}
