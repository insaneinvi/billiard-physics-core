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
                FixVec2 center = new FixVec2(
                    Fix64.From((long)(pd.Center.x * 100)) / Fix64.From(100),
                    Fix64.From((long)(pd.Center.y * 100)) / Fix64.From(100));
                Fix64 radius = Fix64.From((long)(pd.Radius * 100)) / Fix64.From(100);

                var pocket = new Pocket(i, center, radius);
                pocket.ReboundVelocityThreshold =
                    Fix64.From((long)(pd.ReboundVelocityThreshold * 100)) / Fix64.From(100);

                if (pd.RimSegments != null)
                {
                    foreach (SegmentData sd in pd.RimSegments)
                    {
                        FixVec2 start = new FixVec2(
                            Fix64.From((long)(sd.Start.x * 100)) / Fix64.From(100),
                            Fix64.From((long)(sd.Start.y * 100)) / Fix64.From(100));
                        FixVec2 end = new FixVec2(
                            Fix64.From((long)(sd.End.x * 100)) / Fix64.From(100),
                            Fix64.From((long)(sd.End.y * 100)) / Fix64.From(100));
                        pocket.RimSegments.Add(new Segment(start, end));
                    }
                }

                result.Add(pocket);
            }
            return result;
        }
    }
}
