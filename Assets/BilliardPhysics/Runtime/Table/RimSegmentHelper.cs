using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    /// <summary>
    /// Pure-data helpers for maintaining Start / ConnectionPoints / End
    /// ordering on a <see cref="PocketConfig.SegmentData"/>.
    ///
    /// Invariant: the ordered polyline is  Start → CP[0] → CP[1] → … → CP[n-1] → End.
    ///
    /// Fallback when ConnectionPoints is empty:
    ///   Both methods return <c>false</c> and leave the segment unmodified.
    ///   Callers should surface a log warning or editor HelpBox so the designer
    ///   knows the operation was blocked (the segment would otherwise degenerate
    ///   to a single point).
    /// </summary>
    public static class RimSegmentHelper
    {
        /// <summary>
        /// Removes the current <c>End</c> by promoting the last ConnectionPoint
        /// to become the new <c>End</c> (the promoted point is removed from
        /// ConnectionPoints).
        /// </summary>
        /// <returns>
        /// <c>true</c> on success; <c>false</c> if ConnectionPoints is empty
        /// (operation is blocked to prevent segment degeneration).
        /// </returns>
        public static bool TryPromoteLastCPToEnd(PocketConfig.SegmentData seg)
        {
            if (seg == null) return false;
            if (seg.ConnectionPoints == null || seg.ConnectionPoints.Count == 0)
                return false;

            int last = seg.ConnectionPoints.Count - 1;
            seg.End = seg.ConnectionPoints[last];
            seg.ConnectionPoints.RemoveAt(last);
            return true;
        }

        /// <summary>
        /// Removes the current <c>Start</c> by promoting the first ConnectionPoint
        /// to become the new <c>Start</c> (the promoted point is removed from
        /// ConnectionPoints).
        /// </summary>
        /// <returns>
        /// <c>true</c> on success; <c>false</c> if ConnectionPoints is empty
        /// (operation is blocked to prevent segment degeneration).
        /// </returns>
        public static bool TryPromoteFirstCPToStart(PocketConfig.SegmentData seg)
        {
            if (seg == null) return false;
            if (seg.ConnectionPoints == null || seg.ConnectionPoints.Count == 0)
                return false;

            seg.Start = seg.ConnectionPoints[0];
            seg.ConnectionPoints.RemoveAt(0);
            return true;
        }
    }
}
