using NUnit.Framework;
using BilliardPhysics.Editor;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="ExportFixedBinaryHelper"/>.
    /// Covers <see cref="ExportFixedBinaryHelper.NormalizeExtension"/> and
    /// <see cref="ExportFixedBinaryHelper.ValidateFileName"/>.
    /// </summary>
    public class ExportFixedBinaryHelperTests
    {
        // ── NormalizeExtension ────────────────────────────────────────────

        [Test]
        public void NormalizeExtension_NoExtension_AppendsDotBytes()
        {
            Assert.AreEqual("mytable.bytes",
                ExportFixedBinaryHelper.NormalizeExtension("mytable"));
        }

        [Test]
        public void NormalizeExtension_AlreadyDotBytes_Unchanged()
        {
            Assert.AreEqual("mytable.bytes",
                ExportFixedBinaryHelper.NormalizeExtension("mytable.bytes"));
        }

        [Test]
        public void NormalizeExtension_OtherExtension_ReplacedWithDotBytes()
        {
            Assert.AreEqual("mytable.bytes",
                ExportFixedBinaryHelper.NormalizeExtension("mytable.json"));
        }

        [Test]
        public void NormalizeExtension_BinExtension_ReplacedWithDotBytes()
        {
            Assert.AreEqual("mytable.bytes",
                ExportFixedBinaryHelper.NormalizeExtension("mytable.bin"));
        }

        [Test]
        public void NormalizeExtension_DotBytesCaseInsensitive_Unchanged()
        {
            // ".BYTES" should be treated as already correct.
            string result = ExportFixedBinaryHelper.NormalizeExtension("mytable.BYTES");
            Assert.AreEqual("mytable.BYTES", result,
                "A case-insensitive match for .bytes should be left unchanged.");
        }

        [Test]
        public void NormalizeExtension_NullInput_ReturnsNull()
        {
            Assert.IsNull(ExportFixedBinaryHelper.NormalizeExtension(null));
        }

        [Test]
        public void NormalizeExtension_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty,
                ExportFixedBinaryHelper.NormalizeExtension(string.Empty));
        }

        // ── ValidateFileName ──────────────────────────────────────────────

        [Test]
        public void ValidateFileName_EmptyString_ReturnsFalse()
        {
            bool ok = ExportFixedBinaryHelper.ValidateFileName("", out string error);
            Assert.IsFalse(ok, "Empty string should be invalid.");
            Assert.IsFalse(string.IsNullOrEmpty(error),
                "An error message should be provided.");
        }

        [Test]
        public void ValidateFileName_WhitespaceOnly_ReturnsFalse()
        {
            bool ok = ExportFixedBinaryHelper.ValidateFileName("   ", out string error);
            Assert.IsFalse(ok, "Whitespace-only name should be invalid.");
            Assert.IsFalse(string.IsNullOrEmpty(error),
                "An error message should be provided.");
        }

        [Test]
        public void ValidateFileName_Null_ReturnsFalse()
        {
            bool ok = ExportFixedBinaryHelper.ValidateFileName(null, out string error);
            Assert.IsFalse(ok, "Null name should be invalid.");
            Assert.IsFalse(string.IsNullOrEmpty(error),
                "An error message should be provided.");
        }

        [TestCase("/")]
        [TestCase("\\")]
        [TestCase(":")]
        [TestCase("*")]
        [TestCase("?")]
        [TestCase("\"")]
        [TestCase("<")]
        [TestCase(">")]
        [TestCase("|")]
        public void ValidateFileName_SingleIllegalChar_ReturnsFalse(string illegalChar)
        {
            string name = "table" + illegalChar + ".bytes";
            bool ok = ExportFixedBinaryHelper.ValidateFileName(name, out string error);
            Assert.IsFalse(ok, $"Name containing '{illegalChar}' should be invalid.");
            Assert.IsFalse(string.IsNullOrEmpty(error),
                "An error message should be provided.");
        }

        [Test]
        public void ValidateFileName_ValidName_ReturnsTrue()
        {
            bool ok = ExportFixedBinaryHelper.ValidateFileName("mytable.bytes", out string error);
            Assert.IsTrue(ok, "A simple valid name should pass validation.");
            Assert.IsNull(error, "Error message should be null when valid.");
        }

        [Test]
        public void ValidateFileName_ValidNameWithHyphensAndUnderscores_ReturnsTrue()
        {
            bool ok = ExportFixedBinaryHelper.ValidateFileName("my-table_v2.bytes", out _);
            Assert.IsTrue(ok, "Name with hyphens and underscores should be valid.");
        }

        [Test]
        public void ValidateFileName_ErrorMessageMentionsIllegalChars()
        {
            ExportFixedBinaryHelper.ValidateFileName("bad:name.bytes", out string error);
            StringAssert.Contains("illegal", error.ToLowerInvariant(),
                "Error message should mention illegal characters.");
        }

        [Test]
        public void ValidateFileName_EmptyErrorMessageMentionsEmpty()
        {
            ExportFixedBinaryHelper.ValidateFileName("", out string error);
            StringAssert.Contains("empty", error.ToLowerInvariant(),
                "Error message for empty name should mention 'empty'.");
        }
    }
}
