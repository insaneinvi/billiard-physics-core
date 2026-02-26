using System.Collections.Generic;
using UnityEngine;

namespace BilliardPhysics
{
    [System.Serializable]
    public class TableConfig
    {
        [System.Serializable]
        public class SegmentData
        {
            public Vector2 Start;
            public Vector2 End;
        }

        public List<SegmentData> Segments = new List<SegmentData>();
    }

    [System.Serializable]
    public class PocketConfig
    {
        [System.Serializable]
        public class SegmentData
        {
            public Vector2 Start;
            public Vector2 End;
        }

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
