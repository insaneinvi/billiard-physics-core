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
    /// transform and scale helpers correctly apply to <see cref="PocketConfig.PostPocketRollPath"/>.
    /// </summary>
    public class PocketConfigTransformTests
    {
        // X translation applied by the coordinate transform (must match RotateLeft90TranslateX).
        private const float k_transformOffsetX = 12.70f;

        // ── Reflection helpers ────────────────────────────────────────────

        private static readonly MethodInfo s_transformMethod =
            typeof(TableAndPocketAuthoringEditor).GetMethod(
                "TransformPocketConfig",
                BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo s_scaleMethod =
            typeof(TableAndPocketAuthoringEditor).GetMethod(
                "ScalePocketConfig",
                BindingFlags.NonPublic | BindingFlags.Static);

        private static void InvokeTransform(PocketConfig pocket)
            => s_transformMethod.Invoke(null, new object[] { pocket });

        private static void InvokeScale(PocketConfig pocket, float factor)
            => s_scaleMethod.Invoke(null, new object[] { pocket, factor });

        // ── Helpers ───────────────────────────────────────────────────────

        private static SegmentData MakePath(Vector2 start, Vector2 end, params Vector2[] cps)
            => new SegmentData
            {
                Start            = start,
                End              = end,
                ConnectionPoints = new List<Vector2>(cps),
            };

        private static PocketConfig MakePocket(Vector2 center, float radius,
                                               SegmentData rollPath = null)
            => new PocketConfig
            {
                Center              = center,
                Radius              = radius,
                RimSegments         = MakePath(Vector2.zero, Vector2.zero),
                PostPocketRollPath  = rollPath,
            };

        // ── TransformPocketConfig ─────────────────────────────────────────

        [Test]
        public void Transform_PostPocketRollPath_StartIsTransformed()
        {
            var pocket = MakePocket(Vector2.zero, 1f,
                MakePath(new Vector2(1f, 2f), new Vector2(3f, 4f)));

            InvokeTransform(pocket);

            // RotateLeft90TranslateX: newX = -oldY + 12.70, newY = oldX
            var expected = new Vector2(-2f + k_transformOffsetX, 1f);
            Assert.AreEqual(expected.x, pocket.PostPocketRollPath.Start.x, 1e-4f,
                "Start.x should be transformed.");
            Assert.AreEqual(expected.y, pocket.PostPocketRollPath.Start.y, 1e-4f,
                "Start.y should be transformed.");
        }

        [Test]
        public void Transform_PostPocketRollPath_EndIsTransformed()
        {
            var pocket = MakePocket(Vector2.zero, 1f,
                MakePath(new Vector2(1f, 2f), new Vector2(3f, 4f)));

            InvokeTransform(pocket);

            var expected = new Vector2(-4f + k_transformOffsetX, 3f);
            Assert.AreEqual(expected.x, pocket.PostPocketRollPath.End.x, 1e-4f,
                "End.x should be transformed.");
            Assert.AreEqual(expected.y, pocket.PostPocketRollPath.End.y, 1e-4f,
                "End.y should be transformed.");
        }

        [Test]
        public void Transform_PostPocketRollPath_ConnectionPointsAreTransformed()
        {
            var cp = new Vector2(5f, 6f);
            var pocket = MakePocket(Vector2.zero, 1f,
                MakePath(Vector2.zero, Vector2.zero, cp));

            InvokeTransform(pocket);

            var expected = new Vector2(-6f + k_transformOffsetX, 5f);
            Assert.AreEqual(expected.x, pocket.PostPocketRollPath.ConnectionPoints[0].x, 1e-4f,
                "CP.x should be transformed.");
            Assert.AreEqual(expected.y, pocket.PostPocketRollPath.ConnectionPoints[0].y, 1e-4f,
                "CP.y should be transformed.");
        }

        [Test]
        public void Transform_NullPostPocketRollPath_DoesNotThrow()
        {
            var pocket = MakePocket(Vector2.zero, 1f, null);

            Assert.DoesNotThrow(() => InvokeTransform(pocket),
                "Transforming a pocket with null PostPocketRollPath must not throw.");
        }

        // ── ScalePocketConfig ─────────────────────────────────────────────

        [Test]
        public void Scale_PostPocketRollPath_StartIsScaled()
        {
            var pocket = MakePocket(Vector2.zero, 1f,
                MakePath(new Vector2(2f, 3f), new Vector2(4f, 5f)));

            InvokeScale(pocket, 0.01f);

            Assert.AreEqual(0.02f, pocket.PostPocketRollPath.Start.x, 1e-5f,
                "Start.x should be scaled by factor.");
            Assert.AreEqual(0.03f, pocket.PostPocketRollPath.Start.y, 1e-5f,
                "Start.y should be scaled by factor.");
        }

        [Test]
        public void Scale_PostPocketRollPath_EndIsScaled()
        {
            var pocket = MakePocket(Vector2.zero, 1f,
                MakePath(new Vector2(2f, 3f), new Vector2(4f, 5f)));

            InvokeScale(pocket, 100f);

            Assert.AreEqual(400f, pocket.PostPocketRollPath.End.x, 1e-3f,
                "End.x should be scaled by factor.");
            Assert.AreEqual(500f, pocket.PostPocketRollPath.End.y, 1e-3f,
                "End.y should be scaled by factor.");
        }

        [Test]
        public void Scale_PostPocketRollPath_ConnectionPointsAreScaled()
        {
            var cp = new Vector2(3f, 7f);
            var pocket = MakePocket(Vector2.zero, 1f,
                MakePath(Vector2.zero, Vector2.zero, cp));

            InvokeScale(pocket, 0.01f);

            Assert.AreEqual(0.03f, pocket.PostPocketRollPath.ConnectionPoints[0].x, 1e-5f,
                "CP.x should be scaled by factor.");
            Assert.AreEqual(0.07f, pocket.PostPocketRollPath.ConnectionPoints[0].y, 1e-5f,
                "CP.y should be scaled by factor.");
        }

        [Test]
        public void Scale_NullPostPocketRollPath_DoesNotThrow()
        {
            var pocket = MakePocket(Vector2.zero, 1f, null);

            Assert.DoesNotThrow(() => InvokeScale(pocket, 100f),
                "Scaling a pocket with null PostPocketRollPath must not throw.");
        }
    }
}
#endif
