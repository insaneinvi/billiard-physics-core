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
            public Vector2       Start;
            public Vector2       End;
            // Optional intermediate points; the final edge is Start → CP[0] → … → End.
            public List<Vector2> ConnectionPoints = new List<Vector2>();
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
                        // Expand polyline: Start → CP[0] → … → CP[n-1] → End
                        var pts = new List<Vector2> { sd.Start };
                        if (sd.ConnectionPoints != null) pts.AddRange(sd.ConnectionPoints);
                        pts.Add(sd.End);

                        for (int k = 0; k < pts.Count - 1; k++)
                        {
                            FixVec2 s = new FixVec2(Fix64.FromFloat(pts[k].x),   Fix64.FromFloat(pts[k].y));
                            FixVec2 e = new FixVec2(Fix64.FromFloat(pts[k+1].x), Fix64.FromFloat(pts[k+1].y));
                            pocket.RimSegments.Add(new Segment(s, e));
                        }
                    }
                }

                result.Add(pocket);
            }
            return result;
        }
    }
}
