using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    [CreateAssetMenu(fileName = "PocketDefinition", menuName = "BilliardPhysics/PocketDefinition")]
    public class PocketDefinition : ScriptableObject
    {
        [System.Serializable]
        public class SegmentData
        {
            public Vector2 Start;
            public Vector2 End;
        }

        [System.Serializable]
        public class PocketData
        {
            public Vector2           Center;
            public float             Radius;
            public float             ReboundVelocityThreshold = 1f;
            public List<SegmentData> RimSegments = new List<SegmentData>();
        }

        [SerializeField]
        private List<PocketData> Pockets = new List<PocketData>();

        public List<Pocket> BuildPockets()
        {
            var result = new List<Pocket>(Pockets.Count);
            for (int i = 0; i < Pockets.Count; i++)
            {
                PocketData pd = Pockets[i];
                FixVec2 center = new FixVec2(Fix64.FromFloat(pd.Center.x), Fix64.FromFloat(pd.Center.y));
                Fix64   radius = Fix64.FromFloat(pd.Radius);

                var pocket = new Pocket(i, center, radius);
                pocket.ReboundVelocityThreshold = Fix64.FromFloat(pd.ReboundVelocityThreshold);

                if (pd.RimSegments != null)
                {
                    foreach (SegmentData sd in pd.RimSegments)
                    {
                        FixVec2 start = new FixVec2(Fix64.FromFloat(sd.Start.x), Fix64.FromFloat(sd.Start.y));
                        FixVec2 end   = new FixVec2(Fix64.FromFloat(sd.End.x),   Fix64.FromFloat(sd.End.y));
                        pocket.RimSegments.Add(new Segment(start, end));
                    }
                }

                result.Add(pocket);
            }
            return result;
        }
    }
}
