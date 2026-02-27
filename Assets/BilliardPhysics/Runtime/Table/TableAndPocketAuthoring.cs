using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    [System.Serializable]
    public class SegmentData
    {
        public Vector2       Start;
        public Vector2       End;
        // Optional intermediate points; the final edge is Start → CP[0] → CP[1] → … → End.
        public List<Vector2> ConnectionPoints = new List<Vector2>();
    }

    [System.Serializable]
    public class TableConfig
    {
        // A billiard table has exactly four border sides.
        public List<SegmentData> Segments = new List<SegmentData>
        {
            new SegmentData(),
            new SegmentData(),
            new SegmentData(),
            new SegmentData(),
        };
    }

    [System.Serializable]
    public class PocketConfig
    {
        public Vector2           Center;
        public float             Radius                   = 0.1f;
        public float             ReboundVelocityThreshold = 1f;
        public List<SegmentData> RimSegments              = new List<SegmentData>();
    }

    [AddComponentMenu("BilliardPhysics/Table And Pocket Authoring")]
    public class TableAndPocketAuthoring : MonoBehaviour
    {
        public TableConfig        Table   = new TableConfig();
        public List<PocketConfig> Pockets = new List<PocketConfig>();
    }
}
