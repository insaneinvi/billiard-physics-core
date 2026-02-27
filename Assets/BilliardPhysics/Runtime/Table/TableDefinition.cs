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
                FixVec2 s = new FixVec2(Fix64.FromFloat(sd.Start.x), Fix64.FromFloat(sd.Start.y));
                FixVec2 e = new FixVec2(Fix64.FromFloat(sd.End.x),   Fix64.FromFloat(sd.End.y));

                List<FixVec2> cps = null;
                if (sd.ConnectionPoints != null && sd.ConnectionPoints.Count > 0)
                {
                    cps = new List<FixVec2>(sd.ConnectionPoints.Count);
                    foreach (Vector2 cp in sd.ConnectionPoints)
                        cps.Add(new FixVec2(Fix64.FromFloat(cp.x), Fix64.FromFloat(cp.y)));
                }

                result.Add(new Segment(s, e, cps));
            }
            return result;
        }
    }
}
