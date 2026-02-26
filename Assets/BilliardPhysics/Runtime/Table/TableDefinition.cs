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
            public Vector2 Start;
            public Vector2 End;
        }

        [SerializeField]
        private List<SegmentData> Segments = new List<SegmentData>();

        public List<Segment> BuildSegments()
        {
            var result = new List<Segment>(Segments.Count);
            foreach (SegmentData sd in Segments)
            {
                FixVec2 start = new FixVec2(Fix64.FromFloat(sd.Start.x), Fix64.FromFloat(sd.Start.y));
                FixVec2 end   = new FixVec2(Fix64.FromFloat(sd.End.x),   Fix64.FromFloat(sd.End.y));
                result.Add(new Segment(start, end));
            }
            return result;
        }
    }
}
