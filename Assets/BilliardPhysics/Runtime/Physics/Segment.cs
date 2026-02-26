namespace BilliardPhysics
{
    public struct Segment
    {
        public FixVec2 Start;
        public FixVec2 End;
        /// <summary>Pre-computed outward normal (left-hand perpendicular of the direction vector).</summary>
        public FixVec2 Normal;

        public Segment(FixVec2 start, FixVec2 end)
        {
            Start  = start;
            End    = end;
            // Left-hand perpendicular of (End - Start), normalized.
            Normal = (end - start).Perp().Normalized;
        }

        /// <summary>Length of the segment.</summary>
        public Fix64 Length => (End - Start).Magnitude;

        /// <summary>Normalized direction from Start to End.</summary>
        public FixVec2 Direction => (End - Start).Normalized;
    }
}
