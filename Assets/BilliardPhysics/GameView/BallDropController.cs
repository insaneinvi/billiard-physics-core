using System;
using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.AniHelp;
using BilliardPhysics.Runtime.BallInfo;
using BilliardPhysics.Runtime.ViewTool;
using UnityEngine;
using Random = UnityEngine.Random;

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types
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
    /// <summary>Animation driver obtained from the pool.</summary>
    public PocketDropAniHelper Helper;
    /// <summary>
    /// Current visual rotation, integrated each frame from the ball's entry
    /// <see cref="Ball.LastAngularVelocity"/> via
    /// <see cref="PhysicsToView.IntegrateRotation"/>.
    /// </summary>
    public Quaternion       Rotation;
    /// <summary>
    /// Physics-space angular velocity (rad/s) at the pocketing moment, taken from
    /// <see cref="Ball.LastAngularVelocity"/>.  Gradually decays during the drop
    /// animation to simulate the ball slowing to a stop.
    /// </summary>
    public Vector3          EntryAngularVelocity;
    /// <summary>Post-pocket roll path (from <c>TableConfig.PostPocketRollPath</c>).</summary>
    public SegmentData      RollPath;
    /// <summary>
    /// World-space centre of the pocket the ball entered.  Stored so the Z coordinate
    /// (table depth) can be reused when building the post-pocket roll-path waypoints.
    /// </summary>
    public Vector3          PocketWorldPos;
}

// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Data container for one active post-pocket roll animation.</summary>
internal sealed class ActiveRoll
{
    /// <summary>Physics-layer ball; radius and velocity are updated during rolling.</summary>
    public Ball       BallData;
    /// <summary>Current world-space position of the rolling ball.</summary>
    public Vector3    CurrentPosition;
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
    // ── Callbacks ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called every frame for each ball that is actively animating (drop or roll phase).
    /// Parameters: ball ID, world-space position, uniform scale, world-space rotation.
    ///
    /// <para>Register a handler here to apply visual updates (position, scale, rotation)
    /// to the ball's render-layer <c>Transform</c>.  Example registration from
    /// <c>BilliardWorld</c>:
    /// <code>
    /// ballDropController.OnBallAnimationUpdate += (id, pos, scale, rot) =>
    /// {
    ///     ballDict[id].transform.SetPositionAndRotation(pos, rot);
    ///     ballDict[id].transform.localScale = Vector3.one * scale;
    /// };
    /// </code>
    /// </para>
    /// </summary>
    public Action<int, Vector3, float, Quaternion, bool> OnBallAnimationUpdate;

    /// <summary>
    /// Called once when a ball's animation is fully complete and the ball should
    /// be hidden or returned to a pool.  Parameter is the ball's <see cref="Ball.Id"/>.
    ///
    /// <para>Register a handler here to deactivate the ball's GameObject, e.g.:
    /// <code>
    /// ballDropController.OnBallHide += id => ballDict[id].SetActive(false);
    /// </code>
    /// </para>
    /// </summary>
    public Action<int> OnBallHide;

    // ── Inspector parameters ──────────────────────────────────────────────────

    /// <summary>Speed at which pocketed balls roll along the post-pocket path (world units / s).</summary>
    [Tooltip("Speed at which pocketed balls roll along the post-pocket path (world units per second).")]
    public float RollSpeed = 0.5f;

    public float originScale = 0.5715f;
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
    /// <see cref="Ball.LastAngularVelocity"/> at the pocketing moment as the authoritative
    /// data for starting the animation.
    ///
    /// <para>Each frame the animation runs, <see cref="OnBallAnimationUpdate"/> is invoked
    /// with the ball's <see cref="Ball.Id"/>, animated world-space position, uniform scale,
    /// and rotation.  Register a handler on that field to apply visual updates to the
    /// ball's render-layer <c>Transform</c>.</para>
    /// </summary>
    /// <param name="ball">Physics ball (position and velocity authority).</param>
    /// <param name="pocketWorldPos">World-space centre of the pocket the ball entered.</param>
    /// <param name="rollPath">
    /// Post-pocket roll path from <c>TableConfig.PostPocketRollPath</c>.
    /// Pass <c>null</c> or a path where <c>Start == End</c> with no
    /// <c>ConnectionPoints</c> to skip the roll phase.
    /// </param>
    /// <param name="entryRotation">
    /// Visual rotation of the ball's GameObject at the pocketing moment.
    /// Pass the value stored in the view-layer rotation dictionary so the drop
    /// animation continues smoothly from the ball's last rendered orientation.
    /// Pass <c>null</c> (default) to start from <see cref="Quaternion.identity"/>.
    /// </param>
    public void OnBallPocketed(Ball ball, Vector3 pocketWorldPos, SegmentData rollPath,
                               Quaternion? entryRotation = null)
    {
        StartOneDrop(ball, pocketWorldPos, rollPath, entryRotation);
    }

    /// <summary>
    /// Call when multiple balls are pocketed in the same frame.
    /// Each ball receives its own independent animation helper instance so concurrent
    /// animations do not interfere with each other.
    /// </summary>
    public void OnBallsPocketed(
        IReadOnlyList<(Ball ball, Vector3 pocketWorldPos, SegmentData rollPath, Quaternion? entryRotation)> drops)
    {
        foreach (var (ball, pocketWorldPos, rollPath, entryRotation) in drops)
            StartOneDrop(ball, pocketWorldPos, rollPath, entryRotation);
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

    private void StartOneDrop(Ball ball, Vector3 pocketWorldPos, SegmentData rollPath,
                              Quaternion? entryRotation = null)
    {
        // Read the authoritative position from the physics model.
        // Z is taken from pocketWorldPos so the animation stays in the correct plane.
        Vector3 startPos = new Vector3(
            ball.Position.X.ToFloat(),
            ball.Position.Y.ToFloat(),
            pocketWorldPos.z);

        // Convert physics LinearVelocity → Unity XY vector for Bézier trajectory blending.
        // This shapes the Attract phase so the drop animation begins in the same direction
        // the ball was rolling when it entered the pocket (no visual pop or direction jump).
        Vector2 entryLinearVelocity = new Vector2(
            ball.LinearVelocity.X.ToFloat(),
            ball.LinearVelocity.Y.ToFloat());

        // ── Compute new-style drop-target and move-time ──────────────────────
        // Ball diameter in Unity world units (same coordinate system as pocketWorldPos).
        float ballDiameter = BallRackHelper.HalfBallDiameter * 2f;

        // The Attract-phase endpoint: one ball-diameter from startPos toward the pocket,
        // or pocketWorldPos itself when the pocket is closer than one ball-diameter.
        // This prevents the visual target from ever overshooting the pocket center.
        Vector3 dropTarget = PocketDropAniHelper.CalcDropTarget(startPos, pocketWorldPos, ballDiameter);

        // Duration scales with the ball's actual travel distance (start → dropTarget) and
        // speed, so the visual pace matches the ball's physical motion.
        // When the pocket is very close, dropTarget == pocketWorldPos and the distance is
        // shorter than a full ball-diameter; the duration shrinks accordingly.
        float ballLinearSpeed    = entryLinearVelocity.magnitude;
        float actualDropDistance = Vector3.Distance(dropTarget, startPos);
        float moveTime           = PocketDropAniHelper.CalcDropMoveTime(actualDropDistance, ballLinearSpeed);

        // Obtain a helper from the pool and start the drop animation.
        PocketDropAniHelper helper = _pool.Rent();
        helper.StartDrop(new PocketDropRequest
        {
            startPos            = startPos,
            pocketPos           = pocketWorldPos,
            dropTarget          = dropTarget,      // explicit Attract-phase endpoint
            entryLinearVelocity = entryLinearVelocity,
            duration            = moveTime,        // dynamically computed from ball speed
            sinkDepth           = 0.18f,
            attractRatio        = 0.30f,
            sinkRatio           = 0.50f,
            vanishRatio         = 0.20f,
            attractStrength     = 0.25f,
            targetScale         = 0.75f,
        });

        // Capture the angular velocity recorded just before pocketing so the drop
        // animation can spin the ball naturally, then decay it toward zero.
        Vector3 entryOmega = new Vector3(
            ball.LastAngularVelocity.X.ToFloat(),
            ball.LastAngularVelocity.Y.ToFloat(),
            ball.LastAngularVelocity.Z.ToFloat());

        _activeDrops.Add(new ActiveDrop
        {
            BallData             = ball,
            Helper               = helper,
            // Preserve the ball's last rendered orientation so the drop animation
            // continues smoothly from where the physics simulation left off.
            Rotation             = entryRotation ?? Quaternion.identity,
            EntryAngularVelocity = entryOmega,
            RollPath             = rollPath,
            PocketWorldPos       = pocketWorldPos,
        });
    }

    // Exponential decay rate for drop-phase spin (s⁻¹).
    // At this rate spin halves approximately every 0.17 s, fading naturally over the
    // full 0.25 s drop animation so the ball visibly slows as it enters the pocket.
    private const float DropSpinDecay = 4f;

    // ── Internal: drive active drop animations ────────────────────────────────

    private void TickDrops(float deltaTime)
    {
        for (int i = _activeDrops.Count - 1; i >= 0; i--)
        {
            ActiveDrop      drop  = _activeDrops[i];
            PocketDropState state = drop.Helper.Update(deltaTime);

            // Decay the entry angular velocity toward zero to simulate the ball
            // gradually slowing as it drops into the pocket.
            drop.EntryAngularVelocity *= Mathf.Exp(-DropSpinDecay * deltaTime);

            // Integrate visual rotation from the decaying entry angular velocity.
            // PhysicsToView.IntegrateRotation applies the correct axial-vector
            // coordinate transform (physics Z-up → view −Z).
            drop.Rotation = PhysicsToView.IntegrateRotation(
                drop.Rotation, drop.EntryAngularVelocity, deltaTime);

            // Notify the presentation layer: position, scale, and rotation for this frame.
            // The caller (e.g. BilliardWorld) uses ball.Id to look up the view GameObject
            // and applies these values to its Transform.
            OnBallAnimationUpdate?.Invoke(drop.BallData.Id, state.position, state.scale, drop.Rotation, false);

            if (state.phase == PocketDropPhase.Finished)
            {
                // Return the helper to the pool.
                _pool.Return(drop.Helper);

                // Check whether a valid roll path is configured.
                SegmentData path    = drop.RollPath;
                bool        hasPath = path != null &&
                                      (path.Start != path.End ||
                                       (path.ConnectionPoints != null &&
                                        path.ConnectionPoints.Count > 0));
                if (hasPath)
                {
                    // Hand off to the roll phase.  Scale is restored to 1 via the first
                    // OnBallAnimationUpdate callback fired inside StartRoll.
                    StartRoll(drop.BallData, path, drop.PocketWorldPos.z);
                }
                else
                {
                    // No roll path configured: notify the caller to hide the ball.
                    OnBallHide?.Invoke(drop.BallData.Id);
                }

                _activeDrops.RemoveAt(i);
            }
        }
    }

    // ── Internal: build roll path and enqueue the rolling animation ───────────

    private void StartRoll(Ball ball, SegmentData path, float z)
    {
        int cpCount = path.ConnectionPoints?.Count ?? 0;

        // Build the flat waypoint array: [Start, CP0, CP1, …, End].
        // All Z values are kept equal (per requirement: all balls share the same Z).
        Vector3[] waypoints = new Vector3[cpCount + 2];
        waypoints[0] = new Vector3(path.Start.x, path.Start.y, z);
        for (int k = 0; k < cpCount; k++)
            waypoints[k + 1] = new Vector3(
                path.ConnectionPoints[k].x, path.ConnectionPoints[k].y, z);
        waypoints[cpCount + 1] = new Vector3(path.End.x, path.End.y, z);

        // Assign a random roll-direction angle at Start so each ball begins the path
        // with a unique visual orientation.  The angle is a random rotation around the
        // Z-axis (table normal), making every ball look like it arrived from a different
        // spin orientation while still rolling along the prescribed path direction.
        float      randomYaw     = Random.Range(0f, 360f);
        Quaternion startRotation = Quaternion.Euler(0f, 0f, randomYaw);

        // Initialise the ball's physics velocity to match the rolling state at the first
        // path segment.  This keeps Ball.LinearVelocity and Ball.AngularVelocity in sync
        // with the visual motion from the very first frame.
        SetBallRollingVelocity(ball, waypoints, 0, RollSpeed);

        // Notify the presentation layer of the initial roll position (scale=1, full size).
        OnBallAnimationUpdate?.Invoke(ball.Id, waypoints[0], 1f, startRotation, true);

        _activeRolls.Add(new ActiveRoll
        {
            BallData        = ball,
            CurrentPosition = waypoints[0],
            Waypoints       = waypoints,
            SegIdx          = 0,
            SegT            = 0f,
            Speed           = RollSpeed,
            Rotation        = startRotation,
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
                roll.Rotation = PhysicsToView.IntegrateRotation(roll.Rotation, physOmega, deltaTime);

                // Notify the presentation layer with the updated position and rotation.
                OnBallAnimationUpdate?.Invoke(roll.BallData.Id, roll.CurrentPosition, 1f, roll.Rotation, true);
            }

            if (stopped)
            {
                // Record the stopping position for future ball collision checks.
                _stoppedBalls.Add((
                    roll.CurrentPosition,
                    roll.BallData.Radius.ToFloat()));

                // Zero out the ball's physics velocity now that it is stationary.
                roll.BallData.LinearVelocity  = FixVec2.Zero;
                roll.BallData.AngularVelocity = FixVec3.Zero;

                // Notify the presentation layer to hide the ball (or return it to a pool).
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
    /// Updates <see cref="ActiveRoll.CurrentPosition"/> with the new world-space position.
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
                    roll.CurrentPosition = stoppedPos + dir.normalized * contactDist;
                    return true;
                }
            }

            roll.CurrentPosition = newPos;

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
