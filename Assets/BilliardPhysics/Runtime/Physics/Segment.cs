using System.Collections.Generic;

namespace BilliardPhysics
{
    public class Segment
    {
        public FixVec2       Start;
        public FixVec2       End;

        /// <summary>
        /// Optional intermediate vertices ordered Start → CP[0] → … → CP[n-1] → End.
        /// Empty list means a single straight sub-segment (degenerate polyline).
        /// </summary>
        public List<FixVec2> ConnectionPoints { get; }

        /// <summary>
        /// Pre-computed outward normals, one per sub-segment (left-hand perpendicular).
        /// Normal[i] = Direction[i].Perp().
        /// Length == ConnectionPoints.Count + 1.
        /// </summary>
        public FixVec2[] Normal { get; }

        /// <summary>
        /// Pre-computed normalized direction vectors, one per sub-segment.
        /// Direction[i] = (P[i+1] - P[i]).Normalized.
        /// Length == ConnectionPoints.Count + 1.
        /// </summary>
        public FixVec2[] Direction { get; }

        /// <summary>Total polyline length: sum of all sub-segment lengths.</summary>
        public Fix64 Length { get; }

        /// <summary>
        /// Coefficient of restitution for this cushion surface (0–1).
        /// Controls how much of the ball's normal velocity is preserved on bounce:
        /// <c>1.0</c> = perfectly elastic (no energy loss, default);
        /// <c>0.0</c> = perfectly inelastic (normal velocity fully absorbed).
        /// Set per-segment to simulate different cushion materials or worn rails.
        /// Combined with <see cref="Ball.Restitution"/> via the minimum of both values
        /// inside <see cref="ImpulseResolver.ResolveBallCushion"/>.
        /// </summary>
        public Fix64 Restitution = Fix64.One;

        // ── Constructors ──────────────────────────────────────────────────────────

        /// <summary>Creates a single-sub-segment (degenerate polyline) from Start to End.</summary>
        public Segment(FixVec2 start, FixVec2 end)
            : this(start, end, null) { }

        /// <summary>
        /// Creates a polyline segment.
        /// <paramref name="connectionPoints"/> may be null or empty for a single sub-segment.
        /// The list is stored as-is (not copied); callers must not mutate it after construction.
        /// </summary>
        public Segment(FixVec2 start, FixVec2 end, List<FixVec2> connectionPoints)
        {
            Start            = start;
            End              = end;
            ConnectionPoints = connectionPoints ?? new List<FixVec2>();

            int subSegCount = ConnectionPoints.Count + 1;
            Direction       = new FixVec2[subSegCount];
            Normal          = new FixVec2[subSegCount];

            // Pre-build the cached points array.
            _points = new FixVec2[subSegCount + 1];
            _points[0] = Start;
            for (int i = 0; i < ConnectionPoints.Count; i++)
                _points[i + 1] = ConnectionPoints[i];
            _points[subSegCount] = End;

            Fix64 totalLen = Fix64.Zero;
            for (int i = 0; i < subSegCount; i++)
            {
                FixVec2 delta = _points[i + 1] - _points[i];
                Direction[i]  = delta.Normalized;
                Normal[i]     = Direction[i].Perp();
                totalLen     += delta.Magnitude;
            }
            Length = totalLen;
        }

        // ── Points accessor ───────────────────────────────────────────────────────

        private readonly FixVec2[] _points;

        /// <summary>
        /// All polyline vertices in order: [Start, CP[0], …, CP[n-1], End].
        /// The returned list is the cached internal array; do not mutate it.
        /// </summary>
        public IReadOnlyList<FixVec2> Points => _points;
    }
}
