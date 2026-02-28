using System.Collections.Generic;

namespace BilliardPhysics
{
    public class Pocket
    {
        public int             Id;
        public FixVec2         Center;
        public Fix64           Radius;
        public Segment         RimSegments;
        /// <summary>Speed below which a ball entering the pocket mouth is captured.</summary>
        public Fix64           ReboundVelocityThreshold;

        public Pocket(int id, FixVec2 center, Fix64 radius)
        {
            Id                       = id;
            Center                   = center;
            Radius                   = radius;
            ReboundVelocityThreshold = Fix64.One;
        }
    }
}
