using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    /// <summary>
    /// Provides runtime Game View debug visualisation for a <see cref="PhysicsWorld2D"/>.
    /// Separating the draw logic from the physics world keeps responsibilities clear and
    /// allows the visualisation to be omitted entirely in production builds.
    ///
    /// Typical usage:
    /// <code>
    ///   var debug = new PhysicsWorld2DDebug();
    ///   debug.SetTableGeometry(world.TableSegments, world.Pockets);
    ///   debug.SetBalls(world.Balls);
    ///   debug.SetDebug(true);
    ///   // …when done:
    ///   debug.SetDebug(false);
    /// </code>
    /// </summary>
    public class PhysicsWorld2DDebug : System.IDisposable
    {
        // ── Colour constants ──────────────────────────────────────────────────────
        private static readonly Color s_segmentColor = Color.green;
        private static readonly Color s_pocketColor  = Color.cyan;
        private static readonly Color s_rimColor     = Color.yellow;
        private static readonly Color s_ballColor    = Color.white;

        // ── State ─────────────────────────────────────────────────────────────────
        private bool     _enabled;
        private Material _lineMat;

        private IReadOnlyList<Segment> _tableSegments;
        private IReadOnlyList<Pocket>  _pockets;
        private IReadOnlyList<Ball>    _balls;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables runtime debug drawing in the Game view.
        /// When enabled, all geometry set via <see cref="SetTableGeometry"/> and
        /// <see cref="SetBalls"/> is visualised each frame via
        /// <see cref="Camera.onPostRender"/>.
        /// Call <c>SetDebug(false)</c> when the debug visualiser is no longer needed
        /// to unsubscribe from the render callback and release the GL material.
        /// </summary>
        public void SetDebug(bool enable)
        {
            if (enable == _enabled) return;
            _enabled = enable;
            if (enable)
            {
                Camera.onPostRender += OnDraw;
            }
            else
            {
                Camera.onPostRender -= OnDraw;
                DestroyMaterial();
            }
        }

        /// <summary>
        /// Stores references to the table geometry to draw each frame.
        /// Passing <c>null</c> or an empty list is safe and simply clears that layer.
        /// No copy is made – the caller's list is referenced directly to avoid
        /// per-frame allocations.
        /// </summary>
        public void SetTableGeometry(IReadOnlyList<Segment> tableSegments, IReadOnlyList<Pocket> pockets)
        {
            _tableSegments = tableSegments;
            _pockets       = pockets;
        }

        /// <summary>
        /// Stores a reference to the ball list to draw each frame.
        /// Each ball is drawn as a circle at <see cref="Ball.Position"/> with radius
        /// <see cref="Ball.Radius"/>.
        /// Passing <c>null</c> or an empty list is safe and simply clears that layer.
        /// No copy is made – the caller's list is referenced directly to avoid
        /// per-frame allocations.
        /// </summary>
        public void SetBalls(IReadOnlyList<Ball> balls)
        {
            _balls = balls;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Releases the GL material and unsubscribes from the render callback.
        /// Call this when the debug visualiser is being discarded.
        /// </summary>
        public void Dispose()
        {
            SetDebug(false);
        }

        // ── Internal draw callback ────────────────────────────────────────────────

        private void OnDraw(Camera cam)
        {
            Material mat = LineMaterial;
            if (mat == null) return;

            mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            // Draw table boundary segments.
            if (_tableSegments != null)
            {
                GL.Color(s_segmentColor);
                foreach (Segment seg in _tableSegments)
                    DrawSegmentGL(seg);
            }

            // Draw pocket circles and optional rim segments.
            if (_pockets != null)
            {
                foreach (Pocket pocket in _pockets)
                {
                    GL.Color(s_pocketColor);
                    DrawCircleGL(pocket.Center, pocket.Radius);

                    if (pocket.RimSegment != null)
                    {
                        GL.Color(s_rimColor);
                        DrawSegmentGL(pocket.RimSegment);
                    }
                }
            }

            // Draw balls.
            if (_balls != null)
            {
                GL.Color(s_ballColor);
                foreach (Ball ball in _balls)
                {
                    if (!ball.IsPocketed)
                        DrawCircleGL(ball.Position, ball.Radius);
                }
            }

            GL.End();
            GL.PopMatrix();
        }

        // ── GL helpers ────────────────────────────────────────────────────────────

        // Lazily-created unlit GL material used for debug lines.
        private Material LineMaterial
        {
            get
            {
                if (_lineMat == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null)
                    {
                        Debug.LogWarning("[BilliardPhysics] Debug line shader 'Hidden/Internal-Colored' not found.");
                        return null;
                    }
                    _lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _lineMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
                    _lineMat.SetInt("_ZWrite",   0);
                }
                return _lineMat;
            }
        }

        private void DestroyMaterial()
        {
            if (_lineMat != null)
            {
#if UNITY_EDITOR
                Object.DestroyImmediate(_lineMat);
#else
                Object.Destroy(_lineMat);
#endif
                _lineMat = null;
            }
        }

        // Draws the polyline Start → CP[0] → … → CP[n-1] → End using GL.LINES.
        private static void DrawSegmentGL(Segment seg)
        {
            IReadOnlyList<FixVec2> pts = seg.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                GL.Vertex3(pts[i].X.ToFloat(),     pts[i].Y.ToFloat(),     0f);
                GL.Vertex3(pts[i + 1].X.ToFloat(), pts[i + 1].Y.ToFloat(), 0f);
            }
        }

        // Approximates a circle with a line-segment polygon using GL.LINES.
        private static void DrawCircleGL(FixVec2 center, Fix64 radius, int segments = 32)
        {
            float cx   = center.X.ToFloat();
            float cy   = center.Y.ToFloat();
            float r    = radius.ToFloat();
            float step = 2f * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float a0 = step * i;
                float a1 = step * (i + 1);
                GL.Vertex3(cx + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, 0f);
                GL.Vertex3(cx + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, 0f);
            }
        }
    }
}
