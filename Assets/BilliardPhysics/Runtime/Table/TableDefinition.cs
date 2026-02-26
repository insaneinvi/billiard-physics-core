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
                FixVec2 start = new FixVec2(Fix64.From((long)(sd.Start.x * 100)) / Fix64.From(100),
                                            Fix64.From((long)(sd.Start.y * 100)) / Fix64.From(100));
                FixVec2 end   = new FixVec2(Fix64.From((long)(sd.End.x   * 100)) / Fix64.From(100),
                                            Fix64.From((long)(sd.End.y   * 100)) / Fix64.From(100));
                result.Add(new Segment(start, end));
            }
            return result;
        }
    }
}
