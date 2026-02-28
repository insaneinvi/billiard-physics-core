using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="TableAndPocketBinaryLoader"/> (version 3 format).
    /// Covers:
    ///   - Roundtrip: pocket rim with ConnectionPoints loads into <see cref="Pocket.RimSegment"/>.
    ///   - Loader throws <see cref="InvalidDataException"/> on truncated pocket rim data.
    ///   - Loader throws <see cref="InvalidDataException"/> on truncated pocket rim CP data.
    ///   - Loader rejects unsupported (old) version numbers.
    /// </summary>
    public class TableAndPocketBinaryLoaderTests
    {
        // ── Binary layout constants (must stay in sync with exporter) ────────────

        private const uint   k_magic   = 0x59485042u;
        private const ushort k_version = 3;

        // Fix64 scale factor: 1 << 32
        private static long ToRaw(float value) => Fix64.FromFloat(value).RawValue;

        // ── Helper: build a minimal v3 binary blob ────────────────────────────────

        /// <summary>
        /// Writes a complete v3 binary blob with zero table segments and the
        /// supplied pocket list.
        /// </summary>
        private static byte[] BuildBlob(Action<BinaryWriter> writePockets)
        {
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                // Header
                w.Write(k_magic);
                w.Write(k_version);

                // Table segments: none
                w.Write(0);

                // Pockets
                writePockets(w);

                return ms.ToArray();
            }
        }

        private static void WritePocket(
            BinaryWriter w,
            float cx, float cy, float radius, float rebound,
            float rimSx, float rimSy, float rimEx, float rimEy,
            IList<(float x, float y)> cps = null)
        {
            w.Write(ToRaw(cx));
            w.Write(ToRaw(cy));
            w.Write(ToRaw(radius));
            w.Write(ToRaw(rebound));
            w.Write(ToRaw(rimSx));
            w.Write(ToRaw(rimSy));
            w.Write(ToRaw(rimEx));
            w.Write(ToRaw(rimEy));
            int cpCount = cps?.Count ?? 0;
            w.Write(cpCount);
            if (cps != null)
                foreach (var (x, y) in cps)
                {
                    w.Write(ToRaw(x));
                    w.Write(ToRaw(y));
                }
        }

        // ── Roundtrip: single pocket, no CPs ─────────────────────────────────────

        [Test]
        public void Load_SinglePocket_NoCPs_ReturnsCorrectRimSegment()
        {
            byte[] blob = BuildBlob(w =>
            {
                w.Write(1); // pocketCount
                WritePocket(w, cx: 1f, cy: 2f, radius: 0.1f, rebound: 1f,
                               rimSx: 3f, rimSy: 4f, rimEx: 5f, rimEy: 6f);
            });

            var (segs, pockets) = TableAndPocketBinaryLoader.Load(blob);

            Assert.AreEqual(0, segs.Count);
            Assert.AreEqual(1, pockets.Count);

            Pocket p = pockets[0];
            Assert.IsNotNull(p.RimSegment, "RimSegment must not be null.");
            Assert.AreEqual(3f, p.RimSegment.Start.X.ToFloat(), 1e-4f);
            Assert.AreEqual(4f, p.RimSegment.Start.Y.ToFloat(), 1e-4f);
            Assert.AreEqual(5f, p.RimSegment.End.X.ToFloat(), 1e-4f);
            Assert.AreEqual(6f, p.RimSegment.End.Y.ToFloat(), 1e-4f);
            Assert.AreEqual(0, p.RimSegment.ConnectionPoints.Count, "No CPs expected.");
        }

        // ── Roundtrip: single pocket with ConnectionPoints ────────────────────────

        [Test]
        public void Load_SinglePocket_WithCPs_RimSegmentHasCPs()
        {
            var cps = new List<(float x, float y)> { (10f, 20f), (30f, 40f) };

            byte[] blob = BuildBlob(w =>
            {
                w.Write(1);
                WritePocket(w, cx: 0f, cy: 0f, radius: 0.2f, rebound: 1f,
                               rimSx: 1f, rimSy: 0f, rimEx: -1f, rimEy: 0f,
                               cps: cps);
            });

            var (_, pockets) = TableAndPocketBinaryLoader.Load(blob);

            Pocket p = pockets[0];
            Assert.IsNotNull(p.RimSegment);
            Assert.AreEqual(2, p.RimSegment.ConnectionPoints.Count);
            Assert.AreEqual(10f, p.RimSegment.ConnectionPoints[0].X.ToFloat(), 1e-4f);
            Assert.AreEqual(20f, p.RimSegment.ConnectionPoints[0].Y.ToFloat(), 1e-4f);
            Assert.AreEqual(30f, p.RimSegment.ConnectionPoints[1].X.ToFloat(), 1e-4f);
            Assert.AreEqual(40f, p.RimSegment.ConnectionPoints[1].Y.ToFloat(), 1e-4f);
        }

        // ── Import roundtrip: Load then BuildPocketConfigs ────────────────────────

        [Test]
        public void ImportRoundtrip_PocketRimWithCPs_MapsToSegmentData()
        {
            var cps = new List<(float x, float y)> { (5f, 6f) };

            byte[] blob = BuildBlob(w =>
            {
                w.Write(1);
                WritePocket(w, cx: 1f, cy: 2f, radius: 0.15f, rebound: 0.5f,
                               rimSx: 0f, rimSy: 1f, rimEx: 0f, rimEy: -1f,
                               cps: cps);
            });

            var (_, pockets) = TableAndPocketBinaryLoader.Load(blob);
            var configs = BilliardPhysics.Editor.ImportFixedBinaryHelper.BuildPocketConfigs(pockets);

            Assert.AreEqual(1, configs.Count);
            var cfg = configs[0];
            Assert.AreEqual(0f,  cfg.RimSegments.Start.x, 1e-4f);
            Assert.AreEqual(1f,  cfg.RimSegments.Start.y, 1e-4f);
            Assert.AreEqual(0f,  cfg.RimSegments.End.x, 1e-4f);
            Assert.AreEqual(-1f, cfg.RimSegments.End.y, 1e-4f);
            Assert.AreEqual(1, cfg.RimSegments.ConnectionPoints.Count);
            Assert.AreEqual(5f, cfg.RimSegments.ConnectionPoints[0].x, 1e-4f);
            Assert.AreEqual(6f, cfg.RimSegments.ConnectionPoints[0].y, 1e-4f);
        }

        // ── Error: unsupported version ────────────────────────────────────────────

        [Test]
        public void Load_UnsupportedVersion_ThrowsInvalidDataException()
        {
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(k_magic);
                w.Write((ushort)2); // old version 2
                w.Write(0);        // segCount
                w.Write(0);        // pocketCount
                byte[] blob = ms.ToArray();

                Assert.Throws<InvalidDataException>(() => TableAndPocketBinaryLoader.Load(blob));
            }
        }

        // ── Error: truncated pocket rim start ────────────────────────────────────

        [Test]
        public void Load_TruncatedAfterRimStart_ThrowsInvalidDataException()
        {
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(k_magic);
                w.Write(k_version);
                w.Write(0);       // segCount
                w.Write(1);       // pocketCount
                w.Write(ToRaw(0f)); w.Write(ToRaw(0f));  // center
                w.Write(ToRaw(0.1f));                    // radius
                w.Write(ToRaw(1f));                      // rebound
                // Rim start X only (truncated mid-way)
                w.Write(ToRaw(1f));
                byte[] blob = ms.ToArray();

                Assert.Throws<InvalidDataException>(() => TableAndPocketBinaryLoader.Load(blob));
            }
        }

        // ── Error: truncated pocket rim CP data ───────────────────────────────────

        [Test]
        public void Load_TruncatedAfterRimCPCount_ThrowsInvalidDataException()
        {
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(k_magic);
                w.Write(k_version);
                w.Write(0);          // segCount
                w.Write(1);          // pocketCount
                w.Write(ToRaw(0f)); w.Write(ToRaw(0f)); // center
                w.Write(ToRaw(0.1f));                   // radius
                w.Write(ToRaw(1f));                     // rebound
                w.Write(ToRaw(1f)); w.Write(ToRaw(0f)); // rim start
                w.Write(ToRaw(-1f)); w.Write(ToRaw(0f)); // rim end
                w.Write(2);          // cpCount = 2
                // Only one CP written (second is missing)
                w.Write(ToRaw(5f)); w.Write(ToRaw(6f));
                byte[] blob = ms.ToArray();

                Assert.Throws<InvalidDataException>(() => TableAndPocketBinaryLoader.Load(blob));
            }
        }
    }
}
