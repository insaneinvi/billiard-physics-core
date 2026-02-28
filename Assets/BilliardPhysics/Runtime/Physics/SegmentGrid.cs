using System.Collections.Generic;

namespace BilliardPhysics
{
    /// <summary>
    /// Uniform grid for static segment broadphase.
    /// Segments are pre-inserted at construction time; the grid is immutable afterwards.
    /// Each segment's AABB is computed and mapped to grid cells so that per-step
    /// ball–segment broadphase can query only the cells a ball's swept AABB touches,
    /// skipping distant segments entirely.
    /// </summary>
    public class SegmentGrid
    {
        private readonly Fix64 _minX, _minY;
        private readonly Fix64 _cellW, _cellH;
        private readonly int   _cols, _rows;

        // Each cell stores the indices of segments whose AABB overlaps it.
        private readonly List<int>[] _cells;

        private readonly Segment[] _allSegments;

        // Per-segment stamp for allocation-free de-duplication inside Query.
        private readonly int[] _stamp;
        private int _currentStamp;

        /// <param name="segments">All static table segments.</param>
        /// <param name="cols">Grid columns (default 8).</param>
        /// <param name="rows">Grid rows (default 4).</param>
        public SegmentGrid(IReadOnlyList<Segment> segments, int cols = 8, int rows = 4)
        {
            _cols = cols < 1 ? 1 : cols;
            _rows = rows < 1 ? 1 : rows;

            int count    = segments.Count;
            _allSegments = new Segment[count];
            _stamp       = new int[count];
            for (int i = 0; i < count; i++)
                _allSegments[i] = segments[i];

            if (count == 0)
            {
                // No segments: use a trivial 1×1 grid with unit cells so that
                // Query calls on an empty world always return an empty result list
                // without any division-by-zero or invalid-cell arithmetic.
                _minX  = Fix64.Zero;
                _minY  = Fix64.Zero;
                _cellW = Fix64.One;
                _cellH = Fix64.One;
            }
            else
            {
                // Compute world AABB from all segment points.
                Fix64 minX = Fix64.MaxValue, minY = Fix64.MaxValue;
                Fix64 maxX = Fix64.MinValue, maxY = Fix64.MinValue;
                for (int s = 0; s < count; s++)
                {
                    IReadOnlyList<FixVec2> pts = _allSegments[s].Points;
                    for (int p = 0; p < pts.Count; p++)
                    {
                        FixVec2 pt = pts[p];
                        if (pt.X < minX) minX = pt.X;
                        if (pt.Y < minY) minY = pt.Y;
                        if (pt.X > maxX) maxX = pt.X;
                        if (pt.Y > maxY) maxY = pt.Y;
                    }
                }

                // Add 1-unit margin on each side so boundary segments fall inside a cell.
                Fix64 margin = Fix64.From(1);
                _minX = minX - margin;
                _minY = minY - margin;
                Fix64 totalW = (maxX + margin) - _minX;
                Fix64 totalH = (maxY + margin) - _minY;
                _cellW = totalW / Fix64.From(_cols);
                _cellH = totalH / Fix64.From(_rows);
            }

            _cells = new List<int>[_cols * _rows];
            for (int i = 0; i < _cells.Length; i++)
                _cells[i] = new List<int>();

            for (int s = 0; s < count; s++)
                InsertSegment(s);
        }

        private void InsertSegment(int segIdx)
        {
            IReadOnlyList<FixVec2> pts = _allSegments[segIdx].Points;
            Fix64 minX = Fix64.MaxValue, minY = Fix64.MaxValue;
            Fix64 maxX = Fix64.MinValue, maxY = Fix64.MinValue;
            for (int p = 0; p < pts.Count; p++)
            {
                FixVec2 pt = pts[p];
                if (pt.X < minX) minX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y > maxY) maxY = pt.Y;
            }

            int c0 = WorldToCol(minX), c1 = WorldToCol(maxX);
            int r0 = WorldToRow(minY), r1 = WorldToRow(maxY);
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
                _cells[r * _cols + c].Add(segIdx);
        }

        private int WorldToCol(Fix64 x)
        {
            if (_cellW == Fix64.Zero) return 0;
            long c = ((x - _minX) / _cellW).ToLong();
            return c < 0 ? 0 : c >= _cols ? _cols - 1 : (int)c;
        }

        private int WorldToRow(Fix64 y)
        {
            if (_cellH == Fix64.Zero) return 0;
            long r = ((y - _minY) / _cellH).ToLong();
            return r < 0 ? 0 : r >= _rows ? _rows - 1 : (int)r;
        }

        /// <summary>
        /// Returns unique candidate segments whose grid cells overlap the query AABB.
        /// Candidates are appended to <paramref name="results"/> (caller must clear it first).
        /// Uses a stamp-based de-duplication that requires no per-query allocation.
        /// </summary>
        public void Query(Fix64 minX, Fix64 minY, Fix64 maxX, Fix64 maxY, List<Segment> results)
        {
            _currentStamp++;
            int c0 = WorldToCol(minX), c1 = WorldToCol(maxX);
            int r0 = WorldToRow(minY), r1 = WorldToRow(maxY);
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                List<int> cell = _cells[r * _cols + c];
                for (int i = 0; i < cell.Count; i++)
                {
                    int idx = cell[i];
                    if (_stamp[idx] != _currentStamp)
                    {
                        _stamp[idx] = _currentStamp;
                        results.Add(_allSegments[idx]);
                    }
                }
            }
        }
    }
}
