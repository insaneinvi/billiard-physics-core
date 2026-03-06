// AimAssistRenderer.cs
// Unity MonoBehaviour that draws billiard aim-assist lines using LineRenderer.
//
// ── Quick-start (in a MonoBehaviour.Update or input handler) ─────────────────
//
//   // --- Setup (once, e.g. in Start) ---
//   var aimAssist = gameObject.AddComponent<AimAssistRenderer>();
//   aimAssist.SetPhysicsWorld(world);   // world = your PhysicsWorld2D instance
//
//   // --- Draw each frame the player is aiming ---
//   Vector3 cueBallPos = new Vector3(cueBall.Position.X.ToFloat(), cueBall.Position.Y.ToFloat(), 0f);
//   Vector3 cueDir     = new Vector3(directionX, directionY, 0f);
//   aimAssist.DrawAimAssist(cueBallPos, cueDir);
//
//   // --- Hide when not aiming ---
//   aimAssist.Clear();
//
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace BilliardPhysics.AimAssist
{
    /// <summary>
    /// Renders billiard aim-assist lines for a given cue-ball position and direction.
    ///
    /// <para>
    /// Call <see cref="DrawAimAssist"/> each frame while the player is aiming.
    /// Call <see cref="Clear"/> (or <see cref="Hide"/>) when aim-assist should disappear.
    /// </para>
    ///
    /// <para>
    /// Set <see cref="PhysicsWorld"/> (or call <see cref="SetPhysicsWorld"/>) before drawing.
    /// The component reads <c>Balls</c>, <c>TableSegments</c>, and <c>Pockets</c> from the world
    /// to detect the first obstacle in the shot direction.
    /// </para>
    ///
    /// <para>
    /// All geometry is computed in 2D (XY plane).  The Z coordinate of
    /// <c>cueBallPosition</c> is preserved for 3D scene placement.
    /// </para>
    /// </summary>
    [AddComponentMenu("BilliardPhysics/Aim Assist Renderer")]
    public class AimAssistRenderer : MonoBehaviour
    {
        // ── Internal constants ────────────────────────────────────────────────────

        // Squared distance threshold: a ball whose centre is closer than
        // (BallRadius * CueBallSkipFactor)² is assumed to be the cue ball itself
        // and is excluded from ball-ball collision tests.
        private const float CueBallSkipFactor = 0.5f;

        // Minimum squared magnitude of the cue direction to be considered non-zero.
        private const float MinDirSqrMagnitude = 1e-10f;

        // Minimum value used for V0 to prevent zero-length post-collision lines.
        private const float MinSpeed = 1e-6f;

        // Minimum segment length (in world units) below which a sub-segment is skipped.
        private const float MinSegmentLength = 1e-6f;

        // ── Configurable parameters ───────────────────────────────────────────────

        [Header("Physics")]
        [Tooltip("Cue-ball radius in physics units (default = standard billiard ball radius).")]
        public float BallRadius  = 0.28575f;

        [Tooltip("Maximum cast distance.  The path line extends at most this far.")]
        public float MaxDistance = 10f;

        [Header("Rendering")]
        [Tooltip("Number of line segments used to approximate the ghost circle.")]
        public int CircleSegments = 32;

        [Tooltip("Width (in world units) of all rendered lines.")]
        public float LineWidth = 0.02f;

        [Tooltip("Color of the path line from cue ball to first-contact position.")]
        public Color PathColor = Color.white;

        [Tooltip("Color of the hollow ghost-ball circle drawn at the contact position.")]
        public Color GhostCircleColor = Color.white;

        [Tooltip("Color of the post-collision direction line for the cue ball (ball-ball only).")]
        public Color CueBallPostColor = Color.white;

        [Tooltip("Color of the post-collision direction line for the target ball (ball-ball only).")]
        public Color TargetBallPostColor = Color.yellow;

        [Tooltip("Optional material applied to all LineRenderers.  Leave null to use the default.")]
        public Material LineMaterial;

        [Header("Outline")]
        [Tooltip("When true, each line/circle is drawn with a thicker outline beneath it.")]
        public bool EnableOutline = true;

        [Tooltip("Color of the outline drawn behind every line and circle.")]
        public Color OutlineColor = Color.black;

        [Tooltip("Extra width added to LineWidth to produce the outline width.  Values below 0 are clamped to 0.")]
        public float OutlineExtraWidth = 0.015f;

        [Header("Post-Collision")]
        [Tooltip("Reference speed used only for post-collision line lengths (must be > 0).")]
        public float V0 = 1f;

        [Tooltip("Multiplier: post-collision line length = speed_component * ScaleFactor.")]
        public float ScaleFactor = 1f;

        // ── Physics world reference ───────────────────────────────────────────────

        /// <summary>
        /// The physics world whose balls, segments, and pockets are tested for collisions.
        /// Assign via Inspector reference (if you expose it as a serialized field in a
        /// wrapper MonoBehaviour) or call <see cref="SetPhysicsWorld"/> at runtime.
        /// </summary>
        public PhysicsWorld2D PhysicsWorld { get; private set; }

        /// <summary>Sets the physics world used for collision queries.</summary>
        public void SetPhysicsWorld(PhysicsWorld2D world)
        {
            PhysicsWorld = world;
        }

        // ── LineRenderers (created once in Awake) ─────────────────────────────────

        // Main renderers (rendered on top, original color & width).
        private LineRenderer _pathLine;
        private LineRenderer _ghostCircle;
        private LineRenderer _cueBallPostLine;
        private LineRenderer _targetBallPostLine;

        // Outline renderers (rendered beneath, thicker, outline color).
        private LineRenderer _pathLineOutline;
        private LineRenderer _ghostCircleOutline;
        private LineRenderer _cueBallPostLineOutline;
        private LineRenderer _targetBallPostLineOutline;

        // Runtime capsule-line material (created when LineMaterial is null; destroyed in OnDestroy).
        private Material _capsuleLineMat;

        // Runtime ghost-circle material (created when LineMaterial is null; destroyed in OnDestroy).
        // Ghost circle does not use the capsule shader (UV layout is incompatible), so it gets its
        // own material that supports vertex color and alpha blending.
        private Material _ghostCircleMat;

        // Reusable property block for setting per-renderer shader properties.
        private MaterialPropertyBlock _propBlock;

        // ── MonoBehaviour lifecycle ───────────────────────────────────────────────

        private void Awake()
        {
            // Create a runtime capsule-line material when the user has not supplied one.
            _propBlock = new MaterialPropertyBlock();
            if (LineMaterial == null)
            {
                var shader = Shader.Find("BilliardPhysics/AimAssist/CapsuleLine");
                if (shader != null)
                    _capsuleLineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                else
                    Debug.LogWarning("[AimAssistRenderer] Shader 'BilliardPhysics/AimAssist/CapsuleLine' not found. " +
                                     "Line renderers will use the default Unity material.", this);

                // Ghost circle does not use the capsule shader; give it a built-in shader that
                // supports vertex color and alpha blending so it renders correctly by default.
                var circleShader = Shader.Find("Sprites/Default");
                if (circleShader != null)
                    _ghostCircleMat = new Material(circleShader) { hideFlags = HideFlags.HideAndDontSave };
                else
                    Debug.LogWarning("[AimAssistRenderer] Shader 'Sprites/Default' not found. " +
                                     "Ghost circle will use the default Unity material.", this);
            }

            // Outline renderers are created first (sortingOrder 0) so they render behind.
            _pathLineOutline           = CreateLineRenderer("AimAssist_Path_Outline",           sortingOrder: 0);
            _ghostCircleOutline        = CreateLineRenderer("AimAssist_GhostCircle_Outline",    sortingOrder: 0);
            _cueBallPostLineOutline    = CreateLineRenderer("AimAssist_CueBallPost_Outline",    sortingOrder: 0);
            _targetBallPostLineOutline = CreateLineRenderer("AimAssist_TargetBallPost_Outline", sortingOrder: 0);

            // Main renderers are created after (sortingOrder 1) so they render on top.
            _pathLine           = CreateLineRenderer("AimAssist_Path",           sortingOrder: 1);
            _ghostCircle        = CreateLineRenderer("AimAssist_GhostCircle",    sortingOrder: 1);
            _cueBallPostLine    = CreateLineRenderer("AimAssist_CueBallPost",    sortingOrder: 1);
            _targetBallPostLine = CreateLineRenderer("AimAssist_TargetBallPost", sortingOrder: 1);

            // Apply capsule setup to line renderers only; ghost circle keeps original settings.
            ApplyCapsuleSetup(_pathLine);
            ApplyCapsuleSetup(_pathLineOutline);
            ApplyCapsuleSetup(_cueBallPostLine);
            ApplyCapsuleSetup(_cueBallPostLineOutline);
            ApplyCapsuleSetup(_targetBallPostLine);
            ApplyCapsuleSetup(_targetBallPostLineOutline);

            // Apply ghost circle default material when LineMaterial was not provided.
            if (_ghostCircleMat != null)
            {
                _ghostCircle.material        = _ghostCircleMat;
                _ghostCircleOutline.material = _ghostCircleMat;
            }

            Clear();
        }

        private void OnDestroy()
        {
            DestroyChildGO(_pathLine);
            DestroyChildGO(_ghostCircle);
            DestroyChildGO(_cueBallPostLine);
            DestroyChildGO(_targetBallPostLine);
            DestroyChildGO(_pathLineOutline);
            DestroyChildGO(_ghostCircleOutline);
            DestroyChildGO(_cueBallPostLineOutline);
            DestroyChildGO(_targetBallPostLineOutline);

            if (_capsuleLineMat != null)
            {
                Destroy(_capsuleLineMat);
                _capsuleLineMat = null;
            }

            if (_ghostCircleMat != null)
            {
                Destroy(_ghostCircleMat);
                _ghostCircleMat = null;
            }
        }

        private void SimpleSimulatorAimDirection(Vector2 dir2D,HitResult hit, out Vector2 v1After, out Vector2 v2After)
        {
            Vector2 n = (hit.PHit - hit.TargetBallCenter).normalized;

            float   speed  = Mathf.Max(V0, MinSpeed);
            Vector2 v1     = dir2D * speed;
            float   vn     = Vector2.Dot(v1, n);
            v1After = v1 - vn * n;
            v2After = vn * n;
        }

        private void PhysicsSimulatorAimDirection(Vector2 dir2D, HitResult hit, out Vector2 v1after, out Vector2 v2after)
        {
            var ball1 = new Ball(1)
            {
                Position = new FixVec2(Fix64.FromFloat(hit.PHit.x), Fix64.FromFloat(hit.PHit.y)),
                Radius = BilliardsPhysicsDefaults.Ball_Radius,
                Mass = BilliardsPhysicsDefaults.Ball_Mass,
                LinearVelocity = new FixVec2(Fix64.FromFloat(dir2D.x*V0), Fix64.FromFloat(dir2D.y*V0))
            };

            var ball2 = new Ball(2)
            {
                Position = new FixVec2(Fix64.FromFloat(hit.TargetBallCenter.x), Fix64.FromFloat(hit.TargetBallCenter.y)),
                Radius = BilliardsPhysicsDefaults.Ball_Radius,
                Mass = BilliardsPhysicsDefaults.Ball_Mass,
                LinearVelocity = FixVec2.Zero
            };
            ImpulseResolver.ResolveBallBall(ball1,ball2);
            v1after = new Vector2(ball1.LinearVelocity.X.ToFloat(), ball1.LinearVelocity.Y.ToFloat());
            v2after = new Vector2(ball2.LinearVelocity.X.ToFloat(), ball2.LinearVelocity.Y.ToFloat());
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Calculates the first collision along the shot and draws all aim-assist visuals:
        /// <list type="number">
        ///   <item>Path line: from <paramref name="cueBallPosition"/> to the contact position P_hit.</item>
        ///   <item>Ghost circle: hollow ring at P_hit with radius equal to <see cref="BallRadius"/>.</item>
        ///   <item>
        ///     Post-collision lines (ball-ball collisions only):
        ///     cue-ball and target-ball directions after an elastic equal-mass collision.
        ///   </item>
        /// </list>
        /// Call this every frame while the player is aiming.
        /// </summary>
        /// <param name="cueBallPosition">
        /// World-space position of the cue-ball centre.
        /// Only the XY components are used for collision detection;
        /// the Z value is preserved when positioning rendered geometry.
        /// </param>
        /// <param name="cueDirection">
        /// Shot direction in world space.  Does not need to be normalised.
        /// Only the XY components are used.
        /// </param>
        public void DrawAimAssist(Vector3 cueBallPosition, Vector3 cueDirection)
        {
            Clear();

            if (PhysicsWorld == null) return;

            Vector2 origin2D = new Vector2(cueBallPosition.x, cueBallPosition.y);
            Vector2 dir2D    = new Vector2(cueDirection.x,    cueDirection.y);

            if (dir2D.sqrMagnitude < MinDirSqrMagnitude) return;
            dir2D.Normalize();

            float z = cueBallPosition.z;

            // ── Find first collision ──────────────────────────────────────────────
            HitResult hit = FindFirstCollision(origin2D, dir2D);

            Vector2 endPos2D = hit.HasHit
                ? hit.PHit
                : origin2D + dir2D * MaxDistance;

            // ── 1. Path line: cueBallPosition → endPos2D ─────────────────────────
            DrawLineWithOutline(_pathLine, _pathLineOutline,
                                ToV3(origin2D, z), ToV3(endPos2D, z), PathColor);

            if (!hit.HasHit) return;

            // ── 2. Ghost circle at P_hit ──────────────────────────────────────────
            DrawCircleWithOutline(_ghostCircle, _ghostCircleOutline,
                                  ToV3(hit.PHit, z), BallRadius,
                                  CircleSegments, GhostCircleColor);

            // ── 3. Post-collision lines (ball-ball only) ──────────────────────────
            if (!hit.IsBallBall) return;
            Vector2 v1After, v2After;
            
            PhysicsSimulatorAimDirection(dir2D, hit, out v1After, out v2After);
            
            // Draw cue-ball post-collision line from P_hit.
            Vector2 cueBallEnd = hit.PHit + v1After * ScaleFactor;
            DrawLineWithOutline(_cueBallPostLine, _cueBallPostLineOutline,
                                ToV3(hit.PHit, z),
                                ToV3(cueBallEnd, z),
                                CueBallPostColor);

            // Draw target-ball post-collision line from target-ball centre.
            Vector2 targetBallEnd = hit.TargetBallCenter + v2After * ScaleFactor;
            DrawLineWithOutline(_targetBallPostLine, _targetBallPostLineOutline,
                                ToV3(hit.TargetBallCenter, z),
                                ToV3(targetBallEnd, z),
                                TargetBallPostColor);
        }

        /// <summary>Disables all aim-assist LineRenderer GameObjects without clearing their data.</summary>
        public void Hide()
        {
            SetActive(_pathLine,                   false);
            SetActive(_ghostCircle,                false);
            SetActive(_cueBallPostLine,            false);
            SetActive(_targetBallPostLine,         false);
            SetActive(_pathLineOutline,            false);
            SetActive(_ghostCircleOutline,         false);
            SetActive(_cueBallPostLineOutline,     false);
            SetActive(_targetBallPostLineOutline,  false);
        }

        /// <summary>Clears all LineRenderer positions and hides them.</summary>
        public void Clear()
        {
            ClearLine(_pathLine);
            ClearLine(_ghostCircle);
            ClearLine(_cueBallPostLine);
            ClearLine(_targetBallPostLine);
            ClearLine(_pathLineOutline);
            ClearLine(_ghostCircleOutline);
            ClearLine(_cueBallPostLineOutline);
            ClearLine(_targetBallPostLineOutline);
        }

        // ── Collision-detection result ────────────────────────────────────────────

        private struct HitResult
        {
            public bool    HasHit;
            public bool    IsBallBall;
            public Vector2 PHit;
            public Vector2 TargetBallCenter;   // valid only when IsBallBall == true
            public float   TargetBallRadius;   // valid only when IsBallBall == true
        }

        // ── Collision detection ───────────────────────────────────────────────────

        /// <summary>
        /// Finds the closest obstacle hit by the cue-ball sweeping from
        /// <paramref name="origin"/> in direction <paramref name="dir"/> (normalised).
        /// Tests all non-pocketed balls, every sub-segment of every table cushion,
        /// and every pocket area in <see cref="PhysicsWorld"/>.
        /// </summary>
        private HitResult FindFirstCollision(Vector2 origin, Vector2 dir)
        {
            HitResult best = new HitResult { HasHit = false };
            float     bestDist = MaxDistance;

            // ── Test vs other balls ───────────────────────────────────────────────
            foreach (Ball ball in PhysicsWorld.Balls)
            {
                if (ball.IsPocketed) continue;

                Vector2 ballPos = new Vector2(ball.Position.X.ToFloat(),
                                              ball.Position.Y.ToFloat());

                float targetRadius = ball.Radius.ToFloat();

                // Skip the ball whose centre is (approximately) the cue-ball position.
                if ((ballPos - origin).sqrMagnitude < BallRadius * BallRadius * CueBallSkipFactor * CueBallSkipFactor)
                    continue;

                float contactDist = BallRadius + targetRadius;
                float d;
                if (SweptPointVsCircle(origin, dir, ballPos, contactDist, out d)
                    && d >= 0f && d < bestDist)
                {
                    bestDist = d;
                    best = new HitResult
                    {
                        HasHit           = true,
                        IsBallBall       = true,
                        PHit             = origin + dir * d,
                        TargetBallCenter = ballPos,
                        TargetBallRadius = targetRadius,
                    };
                }
            }

            // ── Test vs table segments ────────────────────────────────────────────
            foreach (Segment seg in PhysicsWorld.TableSegments)
            {
                IReadOnlyList<FixVec2> pts = seg.Points;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector2 sA = new Vector2(pts[i].X.ToFloat(),     pts[i].Y.ToFloat());
                    Vector2 sB = new Vector2(pts[i + 1].X.ToFloat(), pts[i + 1].Y.ToFloat());

                    float d;
                    if (SweptCircleVsSegment(origin, dir, sA, sB, BallRadius, out d)
                        && d >= 0f && d < bestDist)
                    {
                        bestDist = d;
                        best = new HitResult
                        {
                            HasHit     = true,
                            IsBallBall = false,
                            PHit       = origin + dir * d,
                        };
                    }
                }
            }

            // ── Test vs pockets ───────────────────────────────────────────────────
            // A pocket is "hit" when the cue-ball centre first enters the pocket
            // capture area, i.e. |ballCentre - pocketCentre| = pocketRadius.
            foreach (Pocket pocket in PhysicsWorld.Pockets)
            {
                Vector2 pc = new Vector2(pocket.Center.X.ToFloat(),
                                         pocket.Center.Y.ToFloat());
                float pr = pocket.Radius.ToFloat();

                float d;
                if (SweptPointVsCircle(origin, dir, pc, pr, out d)
                    && d >= 0f && d < bestDist)
                {
                    bestDist = d;
                    best = new HitResult
                    {
                        HasHit     = true,
                        IsBallBall = false,
                        PHit       = origin + dir * d,
                    };
                }
            }

            return best;
        }

        // ── Geometry helpers ──────────────────────────────────────────────────────

        /// <summary>
        /// Finds the distance <c>d</c> at which a ray (<c>origin + d·dir</c>) is exactly
        /// <paramref name="contactDist"/> away from <paramref name="target"/>.
        /// Solves the quadratic that arises from |dp + d·dir|² = contactDist².
        /// Returns <c>true</c> and sets <paramref name="distance"/> to the smallest
        /// non-negative root; returns <c>false</c> if no such root exists.
        /// </summary>
        internal static bool SweptPointVsCircle(Vector2 origin, Vector2 dir,
                                                Vector2 target, float contactDist,
                                                out float distance)
        {
            Vector2 dp = origin - target;
            // a = dot(dir, dir) = 1  (dir is normalised)
            float b    = 2f * Vector2.Dot(dp, dir);
            float c    = Vector2.Dot(dp, dp) - contactDist * contactDist;

            // Already inside: resolve immediately if moving inward.
            if (c <= 0f)
            {
                distance = 0f;
                return b < 0f;   // approaching → hit at t=0; separating → no hit
            }

            float disc = b * b - 4f * c;   // a == 1, so 4*a*c = 4*c
            if (disc < 0f) { distance = 0f; return false; }

            float sqrtDisc = Mathf.Sqrt(disc);
            float d1 = (-b - sqrtDisc) * 0.5f;
            float d2 = (-b + sqrtDisc) * 0.5f;

            if (d1 >= 0f) { distance = d1; return true; }
            if (d2 >= 0f) { distance = d2; return true; }

            distance = 0f;
            return false;
        }

        /// <summary>
        /// Finds the distance <c>d</c> at which a swept circle (centre <c>origin + d·dir</c>,
        /// radius <paramref name="radius"/>) first touches the line segment
        /// (<paramref name="segA"/>–<paramref name="segB"/>).
        ///
        /// Tests both face normals so the ball can approach from either side, and also
        /// tests the two endpoint circles to handle corner contacts.
        /// </summary>
        internal static bool SweptCircleVsSegment(Vector2 origin, Vector2 dir,
                                                  Vector2 segA,   Vector2 segB,
                                                  float   radius, out float distance)
        {
            float best  = float.MaxValue;
            bool  found = false;

            // ── Face test (forward normal) ────────────────────────────────────────
            float d;
            if (FaceHit(origin, dir, segA, segB, radius, out d) && d >= 0f && d < best)
            {
                best  = d;
                found = true;
            }

            // ── Face test (reverse normal — ball approaching from the other side) ──
            if (FaceHit(origin, dir, segB, segA, radius, out d) && d >= 0f && d < best)
            {
                best  = d;
                found = true;
            }

            // ── Endpoint circles ──────────────────────────────────────────────────
            if (SweptPointVsCircle(origin, dir, segA, radius, out d) && d >= 0f && d < best)
            {
                best  = d;
                found = true;
            }
            if (SweptPointVsCircle(origin, dir, segB, radius, out d) && d >= 0f && d < best)
            {
                best  = d;
                found = true;
            }

            distance = found ? best : 0f;
            return found;
        }

        /// <summary>
        /// Tests one face of a line segment (the left-perpendicular normal of segStart→segEnd).
        /// Returns <c>true</c> and the intersection distance <paramref name="d"/> when:
        /// <list type="bullet">
        ///   <item>the ball is on the positive-normal side of the segment,</item>
        ///   <item>the ball is moving toward the segment,</item>
        ///   <item>the contact point projects within the segment extents.</item>
        /// </list>
        /// </summary>
        private static bool FaceHit(Vector2 origin, Vector2 dir,
                                    Vector2 segStart, Vector2 segEnd,
                                    float radius, out float d)
        {
            d = 0f;
            Vector2 segVec = segEnd - segStart;
            float   segLen = segVec.magnitude;
            if (segLen < MinSegmentLength) return false;

            Vector2 segDir = segVec / segLen;
            // Left-perpendicular (matches Segment.Normal convention).
            Vector2 normal = new Vector2(-segDir.y, segDir.x);

            float vn = Vector2.Dot(dir, normal);
            if (vn >= 0f) return false;   // moving away from or parallel to this face

            float dist = Vector2.Dot(origin - segStart, normal);
            if (dist < 0f) return false;  // ball on the wrong side of this face

            d = (dist - radius) / (-vn);
            if (d < 0f) return false;

            // Confirm the contact point falls within the segment extents.
            Vector2 hitPos = origin + dir * d;
            float   proj   = Vector2.Dot(hitPos - segStart, segDir);
            return proj >= 0f && proj <= segLen;
        }

        // ── LineRenderer management ───────────────────────────────────────────────

        private LineRenderer CreateLineRenderer(string childName, int sortingOrder = 0)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);

            var lr               = go.AddComponent<LineRenderer>();
            lr.useWorldSpace     = true;
            lr.startWidth        = LineWidth;
            lr.endWidth          = LineWidth;
            lr.positionCount     = 0;
            lr.loop              = false;
            lr.sortingOrder      = sortingOrder;
            if (LineMaterial != null) lr.material = LineMaterial;
            return lr;
        }

        /// <summary>
        /// Configures <paramref name="lr"/> for use with the capsule-line shader:
        /// sets <c>textureMode = Stretch</c>, disables built-in cap vertices (the shader
        /// handles the rounded ends), and assigns the runtime capsule material when
        /// no user material was provided.
        /// </summary>
        private void ApplyCapsuleSetup(LineRenderer lr)
        {
            if (lr == null) return;
            lr.textureMode    = LineTextureMode.Stretch;
            lr.numCapVertices = 0;
            if (_capsuleLineMat != null)
                lr.material = _capsuleLineMat;
        }

        /// <summary>Draws a two-point line with the given <paramref name="width"/>.</summary>
        private void DrawLine(LineRenderer lr, Vector3 start, Vector3 end, Color color, float width)
        {
            if (lr == null) return;
            lr.gameObject.SetActive(true);
            lr.startColor    = color;
            lr.endColor      = color;
            lr.startWidth    = width;
            lr.endWidth      = width;
            lr.loop          = false;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            // Inform the capsule shader of the line's aspect ratio (length / width) so
            // the SDF end-caps remain perfectly circular regardless of line length.
            if (_propBlock != null)
            {
                float lineLength = Vector3.Distance(start, end);
                float aspect     = width > 0f ? lineLength / width : 1f;
                _propBlock.SetFloat("_Aspect", aspect);
                lr.SetPropertyBlock(_propBlock);
            }
        }

        /// <summary>Draws a circle approximation with the given <paramref name="width"/>.</summary>
        private void DrawCircle(LineRenderer lr, Vector3 centre, float radius,
                                int segments, Color color, float width)
        {
            if (lr == null) return;
            lr.gameObject.SetActive(true);
            lr.startColor    = color;
            lr.endColor      = color;
            lr.startWidth    = width;
            lr.endWidth      = width;
            lr.loop          = true;
            lr.positionCount = segments;

            float step = 2f * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = step * i;
                lr.SetPosition(i, new Vector3(
                    centre.x + Mathf.Cos(angle) * radius,
                    centre.y + Mathf.Sin(angle) * radius,
                    centre.z));
            }
        }

        /// <summary>
        /// Draws a line on <paramref name="main"/> (at <see cref="LineWidth"/>) and,
        /// when <see cref="EnableOutline"/> is true, also on <paramref name="outline"/>
        /// at the outline width.
        /// </summary>
        private void DrawLineWithOutline(LineRenderer main, LineRenderer outline,
                                         Vector3 start, Vector3 end, Color color)
        {
            DrawLine(main, start, end, color, LineWidth);
            if (EnableOutline)
                DrawLine(outline, start, end, OutlineColor, OutlineWidth);
            else
                ClearLine(outline);
        }

        /// <summary>
        /// Draws a circle on <paramref name="main"/> (at <see cref="LineWidth"/>) and,
        /// when <see cref="EnableOutline"/> is true, also on <paramref name="outline"/>
        /// at the outline width.
        /// </summary>
        private void DrawCircleWithOutline(LineRenderer main, LineRenderer outline,
                                            Vector3 centre, float radius,
                                            int segments, Color color)
        {
            DrawCircle(main, centre, radius, segments, color, LineWidth);
            if (EnableOutline)
                DrawCircle(outline, centre, radius, segments, OutlineColor, OutlineWidth);
            else
                ClearLine(outline);
        }

        private static void ClearLine(LineRenderer lr)
        {
            if (lr == null) return;
            lr.positionCount = 0;
            lr.gameObject.SetActive(false);
        }

        private static void SetActive(LineRenderer lr, bool active)
        {
            if (lr != null) lr.gameObject.SetActive(active);
        }

        private static void DestroyChildGO(LineRenderer lr)
        {
            if (lr != null) Destroy(lr.gameObject);
        }

        // ── Coordinate conversion ─────────────────────────────────────────────────

        private static Vector3 ToV3(Vector2 v, float z) => new Vector3(v.x, v.y, z);

        // Outline width = LineWidth + clamped extra; always >= LineWidth.
        private float OutlineWidth => LineWidth + Mathf.Max(0f, OutlineExtraWidth);
    }
}
