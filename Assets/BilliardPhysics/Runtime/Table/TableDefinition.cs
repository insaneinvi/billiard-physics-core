using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    [CreateAssetMenu(fileName = "TableDefinition", menuName = "BilliardPhysics/TableDefinition")]
    public class TableDefinition : ScriptableObject
    {
        [System.Serializable]
        public class SegmentData
        {
            public Vector2       Start;
            public Vector2       End;
            // Optional intermediate points; the final edge is Start → CP[0] → … → End.
            public List<Vector2> ConnectionPoints = new List<Vector2>();
        }

        [SerializeField]
        private List<SegmentData> Segments = new List<SegmentData>();

        public List<Segment> BuildSegments()
        {
            var result = new List<Segment>();
            foreach (SegmentData sd in Segments)
            {
                // Expand polyline: Start → CP[0] → … → CP[n-1] → End
                var pts = new List<Vector2> { sd.Start };
                if (sd.ConnectionPoints != null) pts.AddRange(sd.ConnectionPoints);
                pts.Add(sd.End);

                for (int k = 0; k < pts.Count - 1; k++)
                {
                    FixVec2 s = new FixVec2(Fix64.FromFloat(pts[k].x),   Fix64.FromFloat(pts[k].y));
                    FixVec2 e = new FixVec2(Fix64.FromFloat(pts[k+1].x), Fix64.FromFloat(pts[k+1].y));
                    result.Add(new Segment(s, e));
                }
            }
            return result;
        }
    }
}
