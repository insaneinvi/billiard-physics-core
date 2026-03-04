#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using BilliardPhysics;
using BilliardPhysics.Editor;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests verifying that <see cref="TableAndPocketAuthoringEditor"/>
    /// transform and scale helpers correctly apply to <see cref="TableConfig.PostPocketRollPath"/>.
    /// </summary>
    public class TableConfigTransformTests
    {
        // X translation applied by the coordinate transform (must match RotateLeft90TranslateX).
        private const float k_transformOffsetX = 12.70f;

        // ── Reflection helpers ────────────────────────────────────────────

        private static readonly MethodInfo s_transformMethod =
            typeof(TableAndPocketAuthoringEditor).GetMethod(
                "TransformTableConfig",
                BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo s_scaleMethod =
            typeof(TableAndPocketAuthoringEditor).GetMethod(
                "ScaleTableConfig",
                BindingFlags.NonPublic | BindingFlags.Static);

        private static void InvokeTransform(TableConfig table)
            => s_transformMethod.Invoke(null, new object[] { table });

        private static void InvokeScale(TableConfig table, float factor)
            => s_scaleMethod.Invoke(null, new object[] { table, factor });

        // ── Helpers ───────────────────────────────────────────────────────

        private static SegmentData MakePath(Vector2 start, Vector2 end, params Vector2[] cps)
            => new SegmentData
            {
                Start            = start,
                End              = end,
                ConnectionPoints = new List<Vector2>(cps),
            };

        private static TableConfig MakeTable(SegmentData rollPath)
            => new TableConfig
            {
                Segments          = new List<SegmentData>(),
                PostPocketRollPath = rollPath,
            };

        // ── TransformTableConfig ──────────────────────────────────────────

        [Test]
        public void Transform_PostPocketRollPath_StartIsTransformed()
        {
            var table = MakeTable(MakePath(new Vector2(1f, 2f), new Vector2(3f, 4f)));

            InvokeTransform(table);

            // RotateLeft90TranslateX: newX = -oldY + 12.70, newY = oldX
            var expected = new Vector2(-2f + k_transformOffsetX, 1f);
            Assert.AreEqual(expected.x, table.PostPocketRollPath.Start.x, 1e-4f,
                "Start.x should be transformed.");
            Assert.AreEqual(expected.y, table.PostPocketRollPath.Start.y, 1e-4f,
                "Start.y should be transformed.");
        }

        [Test]
        public void Transform_PostPocketRollPath_EndIsTransformed()
        {
            var table = MakeTable(MakePath(new Vector2(1f, 2f), new Vector2(3f, 4f)));

            InvokeTransform(table);

            var expected = new Vector2(-4f + k_transformOffsetX, 3f);
            Assert.AreEqual(expected.x, table.PostPocketRollPath.End.x, 1e-4f,
                "End.x should be transformed.");
            Assert.AreEqual(expected.y, table.PostPocketRollPath.End.y, 1e-4f,
                "End.y should be transformed.");
        }

        [Test]
        public void Transform_PostPocketRollPath_ConnectionPointsAreTransformed()
        {
            var cp = new Vector2(5f, 6f);
            var table = MakeTable(MakePath(Vector2.zero, Vector2.zero, cp));

            InvokeTransform(table);

            var expected = new Vector2(-6f + k_transformOffsetX, 5f);
            Assert.AreEqual(expected.x, table.PostPocketRollPath.ConnectionPoints[0].x, 1e-4f,
                "CP.x should be transformed.");
            Assert.AreEqual(expected.y, table.PostPocketRollPath.ConnectionPoints[0].y, 1e-4f,
                "CP.y should be transformed.");
        }

        // ── ScaleTableConfig ──────────────────────────────────────────────

        [Test]
        public void Scale_PostPocketRollPath_StartIsScaled()
        {
            var table = MakeTable(MakePath(new Vector2(2f, 3f), new Vector2(4f, 5f)));

            InvokeScale(table, 0.01f);

            Assert.AreEqual(0.02f, table.PostPocketRollPath.Start.x, 1e-5f,
                "Start.x should be scaled by factor.");
            Assert.AreEqual(0.03f, table.PostPocketRollPath.Start.y, 1e-5f,
                "Start.y should be scaled by factor.");
        }

        [Test]
        public void Scale_PostPocketRollPath_EndIsScaled()
        {
            var table = MakeTable(MakePath(new Vector2(2f, 3f), new Vector2(4f, 5f)));

            InvokeScale(table, 100f);

            Assert.AreEqual(400f, table.PostPocketRollPath.End.x, 1e-3f,
                "End.x should be scaled by factor.");
            Assert.AreEqual(500f, table.PostPocketRollPath.End.y, 1e-3f,
                "End.y should be scaled by factor.");
        }

        [Test]
        public void Scale_PostPocketRollPath_ConnectionPointsAreScaled()
        {
            var cp = new Vector2(3f, 7f);
            var table = MakeTable(MakePath(Vector2.zero, Vector2.zero, cp));

            InvokeScale(table, 0.01f);

            Assert.AreEqual(0.03f, table.PostPocketRollPath.ConnectionPoints[0].x, 1e-5f,
                "CP.x should be scaled by factor.");
            Assert.AreEqual(0.07f, table.PostPocketRollPath.ConnectionPoints[0].y, 1e-5f,
                "CP.y should be scaled by factor.");
        }
    }
}
#endif
