using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BilliardPhysics
{
    /// <summary>
    /// Loads table and pocket data from a fixed-point binary asset produced by
    /// <c>TableAndPocketAuthoringEditor.ExportFixedBinary</c>.
    /// </summary>
    public static class TableAndPocketBinaryLoader
    {
        /// <summary>'BPHY' magic: 0x59485042 whose little-endian bytes are 0x42,0x50,0x48,0x59 = 'B','P','H','Y'.</summary>
        private const uint   k_magic      = 0x59485042u;
        private const ushort k_version3   = 3;  // current: no rimSegCount; single rim segment with CPs
        private const ushort k_version    = k_version3;

        /// <summary>
        /// Loads table segments and pockets from a binary <see cref="TextAsset"/>.
        /// </summary>
        /// <param name="asset">The .bytes asset exported from the editor.</param>
        /// <param name="tableSegments">Reconstructed table wall segments.</param>
        /// <param name="pockets">Reconstructed pocket definitions (index 0..n-1).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="asset"/> is null.</exception>
        /// <exception cref="InvalidDataException">
        ///   Thrown when the magic number or version does not match, or the data is too short.
        /// </exception>
        public static void Load(TextAsset asset,
                                out List<Segment> tableSegments,
                                out List<Pocket>  pockets)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            (tableSegments, pockets) = Load(asset.bytes);
        }

        /// <summary>
        /// Loads table segments and pockets from a binary <see cref="TextAsset"/>.
        /// </summary>
        /// <param name="asset">The .bytes asset exported from the editor.</param>
        /// <returns>A tuple of (tableSegments, pockets).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="asset"/> is null.</exception>
        /// <exception cref="InvalidDataException">
        ///   Thrown when the magic number or version does not match, or the data is too short.
        /// </exception>
        public static (List<Segment> tableSegments, List<Pocket> pockets) Load(TextAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            return Load(asset.bytes);
        }

        /// <summary>
        /// Loads table segments and pockets from a raw byte array produced by
        /// <c>TableAndPocketAuthoringEditor.ExportFixedBinary</c>.
        /// </summary>
        /// <param name="bytes">Raw bytes of the exported .bytes file.</param>
        /// <param name="tableSegments">Reconstructed table wall segments.</param>
        /// <param name="pockets">Reconstructed pocket definitions (index 0..n-1).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null.</exception>
        /// <exception cref="InvalidDataException">
        ///   Thrown when the magic number or version does not match, or the data is too short.
        /// </exception>
        public static void Load(byte[] bytes,
                                out List<Segment> tableSegments,
                                out List<Pocket>  pockets)
        {
            (tableSegments, pockets) = Load(bytes);
        }

        /// <summary>
        /// Loads table segments and pockets from a raw byte array produced by
        /// <c>TableAndPocketAuthoringEditor.ExportFixedBinary</c>.
        /// </summary>
        /// <param name="bytes">Raw bytes of the exported .bytes file.</param>
        /// <returns>A tuple of (tableSegments, pockets).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bytes"/> is null.</exception>
        /// <exception cref="InvalidDataException">
        ///   Thrown when the magic number or version does not match, or the data is too short.
        /// </exception>
        public static (List<Segment> tableSegments, List<Pocket> pockets) Load(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            using (var reader = new BinaryReader(new MemoryStream(bytes)))
            {
                return ReadBinary(reader);
            }
        }

        private static (List<Segment> tableSegments, List<Pocket> pockets) ReadBinary(BinaryReader reader)
        {
            // ── Header ────────────────────────────────────────────────
            if (reader.BaseStream.Length < 6)
                throw new InvalidDataException(
                    "Binary data is too short to contain a valid header (need at least 6 bytes).");

            uint magic = reader.ReadUInt32();
            if (magic != k_magic)
                throw new InvalidDataException(
                    $"Invalid magic number: expected 0x{k_magic:X8} ('BPHY'), got 0x{magic:X8}.");

            ushort version = reader.ReadUInt16();
            if (version != k_version3)
                throw new InvalidDataException(
                    $"Unsupported file version {version}. This loader supports version {k_version3}.");

            // ── Table segments ────────────────────────────────────────
            int segCount = ReadInt32Safe(reader, "segment count");
            if (segCount < 0)
                throw new InvalidDataException($"Segment count is negative ({segCount}).");

            var tableSegments = new List<Segment>(segCount);
            for (int i = 0; i < segCount; i++)
            {
                FixVec2 start = ReadFixVec2Safe(reader, $"table segment {i} start");
                FixVec2 end   = ReadFixVec2Safe(reader, $"table segment {i} end");
                List<FixVec2> cps = ReadConnectionPoints(reader, $"table segment {i}");
                tableSegments.Add(new Segment(start, end, cps));
            }

            // ── Pockets ───────────────────────────────────────────────
            int pocketCount = ReadInt32Safe(reader, "pocket count");
            if (pocketCount < 0)
                throw new InvalidDataException($"Pocket count is negative ({pocketCount}).");

            var pockets = new List<Pocket>(pocketCount);
            for (int i = 0; i < pocketCount; i++)
            {
                FixVec2 center           = ReadFixVec2Safe(reader, $"pocket {i} center");
                Fix64   radius           = ReadFix64Safe(reader,   $"pocket {i} radius");
                Fix64   reboundThreshold = ReadFix64Safe(reader,   $"pocket {i} reboundVelocityThreshold");

                var pocket = new Pocket(i, center, radius)
                {
                    ReboundVelocityThreshold = reboundThreshold
                };

                FixVec2       rimStart = ReadFixVec2Safe(reader, $"pocket {i} rim segment start");
                FixVec2       rimEnd   = ReadFixVec2Safe(reader, $"pocket {i} rim segment end");
                List<FixVec2> rimCps   = ReadConnectionPoints(reader, $"pocket {i} rim segment");
                pocket.RimSegment = new Segment(rimStart, rimEnd, rimCps);

                pockets.Add(pocket);
            }

            return (tableSegments, pockets);
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static List<FixVec2> ReadConnectionPoints(BinaryReader reader, string context)
        {
            int cpCount = ReadInt32Safe(reader, $"{context} ConnectionPoint count");
            if (cpCount < 0)
                throw new InvalidDataException(
                    $"ConnectionPoint count for {context} is negative ({cpCount}).");
            if (cpCount == 0)
                return null;

            var cps = new List<FixVec2>(cpCount);
            for (int k = 0; k < cpCount; k++)
                cps.Add(ReadFixVec2Safe(reader, $"{context} ConnectionPoint[{k}]"));
            return cps;
        }

        private static Fix64 ReadFix64Safe(BinaryReader reader, string fieldName)
        {
            if (reader.BaseStream.Position + 8 > reader.BaseStream.Length)
                throw new InvalidDataException(
                    $"Unexpected end of data while reading '{fieldName}' (Fix64).");
            return new Fix64 { RawValue = reader.ReadInt64() };
        }

        private static FixVec2 ReadFixVec2Safe(BinaryReader reader, string fieldName)
        {
            return new FixVec2(
                ReadFix64Safe(reader, fieldName + ".X"),
                ReadFix64Safe(reader, fieldName + ".Y"));
        }

        private static int ReadInt32Safe(BinaryReader reader, string fieldName)
        {
            if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
                throw new InvalidDataException(
                    $"Unexpected end of data while reading '{fieldName}' (Int32).");
            return reader.ReadInt32();
        }
    }
}
