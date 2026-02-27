using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="TableAndPocketBinaryLoader.Load(byte[])"/>.
    /// Covers success, wrong magic, wrong version, and truncated/corrupt data.
    /// </summary>
    public class TableAndPocketBinaryLoaderTests
    {
        // Magic 'BPHY' = 0x59485042 (little-endian bytes: B, P, H, Y)
        private const uint   k_magic   = 0x59485042u;
        private const ushort k_version = 1;

        // ── Binary builder helpers ─────────────────────────────────────────

        private static byte[] BuildValidBinary(
            List<(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by)> segs,
            List<(Fix64 cx, Fix64 cy, Fix64 r, Fix64 rvt,
                  List<(Fix64 ax, Fix64 ay, Fix64 bx, Fix64 by)> rims)> pockets)
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);

            w.Write(k_magic);
            w.Write(k_version);

            w.Write(segs.Count);
            foreach (var s in segs)
            {
                w.Write(s.ax.RawValue); w.Write(s.ay.RawValue);
                w.Write(s.bx.RawValue); w.Write(s.by.RawValue);
            }

            w.Write(pockets.Count);
            foreach (var p in pockets)
            {
                w.Write(p.cx.RawValue); w.Write(p.cy.RawValue);
                w.Write(p.r.RawValue);
                w.Write(p.rvt.RawValue);
                w.Write(p.rims.Count);
                foreach (var rim in p.rims)
                {
                    w.Write(rim.ax.RawValue); w.Write(rim.ay.RawValue);
                    w.Write(rim.bx.RawValue); w.Write(rim.by.RawValue);
                }
            }

            return ms.ToArray();
        }

        // ── Null guard ────────────────────────────────────────────────────

        [Test]
        public void Load_NullBytes_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => TableAndPocketBinaryLoader.Load((byte[])null, out _, out _));
        }

        // ── Success cases ─────────────────────────────────────────────────

        [Test]
        public void Load_EmptyTableAndPockets_Succeeds()
        {
            byte[] bytes = BuildValidBinary(
                new List<(Fix64, Fix64, Fix64, Fix64)>(),
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>());

            TableAndPocketBinaryLoader.Load(bytes, out var segs, out var pockets);

            Assert.AreEqual(0, segs.Count,    "No segments expected.");
            Assert.AreEqual(0, pockets.Count, "No pockets expected.");
        }

        [Test]
        public void Load_OneSegment_CorrectValues()
        {
            Fix64 ax = Fix64.FromFloat(1f);
            Fix64 ay = Fix64.FromFloat(2f);
            Fix64 bx = Fix64.FromFloat(3f);
            Fix64 by = Fix64.FromFloat(4f);

            var segList = new List<(Fix64, Fix64, Fix64, Fix64)> { (ax, ay, bx, by) };
            byte[] bytes = BuildValidBinary(
                segList,
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>());

            TableAndPocketBinaryLoader.Load(bytes, out var segs, out _);

            Assert.AreEqual(1, segs.Count);
            Assert.AreEqual(ax, segs[0].Start.X, "Segment Start.X should match.");
            Assert.AreEqual(ay, segs[0].Start.Y, "Segment Start.Y should match.");
            Assert.AreEqual(bx, segs[0].End.X,   "Segment End.X should match.");
            Assert.AreEqual(by, segs[0].End.Y,   "Segment End.Y should match.");
        }

        [Test]
        public void Load_OnePocketWithRimSegments_CorrectValues()
        {
            Fix64 cx  = Fix64.FromFloat(0.5f);
            Fix64 cy  = Fix64.FromFloat(-0.5f);
            Fix64 r   = Fix64.FromFloat(0.1f);
            Fix64 rvt = Fix64.FromFloat(2f);

            Fix64 rax = Fix64.FromFloat(0.6f);
            Fix64 ray = Fix64.FromFloat(-0.4f);
            Fix64 rbx = Fix64.FromFloat(0.4f);
            Fix64 rby = Fix64.FromFloat(-0.6f);

            var rims = new List<(Fix64, Fix64, Fix64, Fix64)> { (rax, ray, rbx, rby) };
            var pocketList = new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>
            {
                (cx, cy, r, rvt, rims)
            };
            byte[] bytes = BuildValidBinary(
                new List<(Fix64, Fix64, Fix64, Fix64)>(),
                pocketList);

            TableAndPocketBinaryLoader.Load(bytes, out _, out var pockets);

            Assert.AreEqual(1, pockets.Count);
            Assert.AreEqual(cx,  pockets[0].Center.X,                "Pocket Center.X should match.");
            Assert.AreEqual(cy,  pockets[0].Center.Y,                "Pocket Center.Y should match.");
            Assert.AreEqual(r,   pockets[0].Radius,                  "Pocket Radius should match.");
            Assert.AreEqual(rvt, pockets[0].ReboundVelocityThreshold,"ReboundVelocityThreshold should match.");
            Assert.AreEqual(1,   pockets[0].RimSegments.Count,       "One rim segment expected.");
            Assert.AreEqual(rax, pockets[0].RimSegments[0].Start.X,  "Rim Start.X should match.");
            Assert.AreEqual(ray, pockets[0].RimSegments[0].Start.Y,  "Rim Start.Y should match.");
            Assert.AreEqual(rbx, pockets[0].RimSegments[0].End.X,    "Rim End.X should match.");
            Assert.AreEqual(rby, pockets[0].RimSegments[0].End.Y,    "Rim End.Y should match.");
        }

        [Test]
        public void Load_MultiplePocketsWithId_PocketIdsAreSequential()
        {
            var pocketList = new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>
            {
                (Fix64.Zero, Fix64.Zero, Fix64.FromFloat(0.1f), Fix64.One, new List<(Fix64, Fix64, Fix64, Fix64)>()),
                (Fix64.One,  Fix64.Zero, Fix64.FromFloat(0.1f), Fix64.One, new List<(Fix64, Fix64, Fix64, Fix64)>()),
                (Fix64.Zero, Fix64.One,  Fix64.FromFloat(0.1f), Fix64.One, new List<(Fix64, Fix64, Fix64, Fix64)>()),
            };
            byte[] bytes = BuildValidBinary(
                new List<(Fix64, Fix64, Fix64, Fix64)>(),
                pocketList);

            TableAndPocketBinaryLoader.Load(bytes, out _, out var pockets);

            Assert.AreEqual(3, pockets.Count);
            Assert.AreEqual(0, pockets[0].Id, "Pocket[0].Id should be 0.");
            Assert.AreEqual(1, pockets[1].Id, "Pocket[1].Id should be 1.");
            Assert.AreEqual(2, pockets[2].Id, "Pocket[2].Id should be 2.");
        }

        // ── Error cases ───────────────────────────────────────────────────

        [Test]
        public void Load_TooShortForHeader_ThrowsInvalidDataException()
        {
            var tooShort = new byte[] { 0x42, 0x50, 0x48 }; // only 3 bytes
            Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(tooShort, out _, out _),
                "Should throw InvalidDataException when data is shorter than 6 bytes.");
        }

        [Test]
        public void Load_WrongMagic_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(0xDEADBEEFu); // wrong magic
            w.Write(k_version);
            w.Write(0); // segment count
            w.Write(0); // pocket count
            byte[] bytes = ms.ToArray();

            var ex = Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _));
            StringAssert.Contains("magic", ex.Message,
                "Error message should mention magic number.");
        }

        [Test]
        public void Load_WrongVersion_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(k_magic);
            w.Write((ushort)99); // unsupported version
            w.Write(0); // segment count
            w.Write(0); // pocket count
            byte[] bytes = ms.ToArray();

            var ex = Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _));
            StringAssert.Contains("version", ex.Message,
                "Error message should mention version.");
            StringAssert.Contains("99", ex.Message,
                "Error message should include the bad version number.");
        }

        [Test]
        public void Load_TruncatedAfterSegmentCount_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(k_magic);
            w.Write(k_version);
            w.Write(5);   // claims 5 segments but writes none
            byte[] bytes = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _),
                "Should throw when segment data is missing.");
        }

        [Test]
        public void Load_TruncatedMidSegment_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(k_magic);
            w.Write(k_version);
            w.Write(1);             // 1 segment
            w.Write(1L);            // only partial data (1 long instead of 4)
            byte[] bytes = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _),
                "Should throw when segment is truncated mid-read.");
        }

        [Test]
        public void Load_TruncatedMidPocket_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(k_magic);
            w.Write(k_version);
            w.Write(0);             // 0 segments
            w.Write(2);             // claims 2 pockets
            // Write only partial pocket data (center.X only)
            w.Write(Fix64.FromFloat(0.5f).RawValue);
            byte[] bytes = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _),
                "Should throw when pocket data is truncated.");
        }

        [Test]
        public void Load_NegativeSegmentCount_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(k_magic);
            w.Write(k_version);
            w.Write(-1);  // negative segment count
            byte[] bytes = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _),
                "Should throw when segment count is negative.");
        }

        [Test]
        public void Load_NegativePocketCount_ThrowsInvalidDataException()
        {
            using var ms = new MemoryStream();
            using var w  = new BinaryWriter(ms);
            w.Write(k_magic);
            w.Write(k_version);
            w.Write(0);   // 0 segments
            w.Write(-3);  // negative pocket count
            byte[] bytes = ms.ToArray();

            Assert.Throws<InvalidDataException>(
                () => TableAndPocketBinaryLoader.Load(bytes, out _, out _),
                "Should throw when pocket count is negative.");
        }

        // ── TextAsset overload still works (smoke test) ───────────────────

        [Test]
        public void Load_BytesAndOutOverload_ReturnSameResults()
        {
            byte[] bytes = BuildValidBinary(
                new List<(Fix64, Fix64, Fix64, Fix64)> { (Fix64.One, Fix64.Zero, Fix64.Zero, Fix64.One) },
                new List<(Fix64, Fix64, Fix64, Fix64, List<(Fix64, Fix64, Fix64, Fix64)>)>());

            var (segs1, pockets1) = TableAndPocketBinaryLoader.Load(bytes);
            TableAndPocketBinaryLoader.Load(bytes, out var segs2, out var pockets2);

            Assert.AreEqual(segs1.Count,    segs2.Count,    "Segment counts should match between overloads.");
            Assert.AreEqual(pockets1.Count, pockets2.Count, "Pocket counts should match between overloads.");
        }
    }
}
