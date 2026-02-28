using NUnit.Framework;
using BilliardPhysics;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for <see cref="Fix64"/> conversion methods:
    /// <see cref="Fix64.ToFloat"/>, <see cref="Fix64.ToInt"/>, and <see cref="Fix64.ToLong"/>.
    /// </summary>
    public class Fix64Tests
    {
        // ── ToFloat ───────────────────────────────────────────────────────────────

        [Test]
        public void ToFloat_Zero_ReturnsZero()
        {
            Assert.AreEqual(0f, Fix64.Zero.ToFloat());
        }

        [Test]
        public void ToFloat_One_ReturnsOne()
        {
            Assert.AreEqual(1f, Fix64.One.ToFloat(), 1e-6f);
        }

        [Test]
        public void ToFloat_NegOne_ReturnsNegOne()
        {
            Assert.AreEqual(-1f, Fix64.NegOne.ToFloat(), 1e-6f);
        }

        [Test]
        public void ToFloat_Half_ReturnsHalf()
        {
            Assert.AreEqual(0.5f, Fix64.Half.ToFloat(), 1e-6f);
        }

        [Test]
        public void ToFloat_FromFloat_RoundTrip()
        {
            float original = 3.14f;
            float result = Fix64.FromFloat(original).ToFloat();
            Assert.AreEqual(original, result, 1e-3f);
        }

        // ── ToInt ─────────────────────────────────────────────────────────────────

        [Test]
        public void ToInt_Zero_ReturnsZero()
        {
            Assert.AreEqual(0, Fix64.Zero.ToInt());
        }

        [Test]
        public void ToInt_One_ReturnsOne()
        {
            Assert.AreEqual(1, Fix64.One.ToInt());
        }

        [Test]
        public void ToInt_NegOne_ReturnsNegOne()
        {
            Assert.AreEqual(-1, Fix64.NegOne.ToInt());
        }

        [Test]
        public void ToInt_Half_ReturnsTruncatedZero()
        {
            Assert.AreEqual(0, Fix64.Half.ToInt());
        }

        [Test]
        public void ToInt_IntegerValue_ReturnsCorrectInt()
        {
            Fix64 value = Fix64.From(42);
            Assert.AreEqual(42, value.ToInt());
        }

        // ── ToLong ────────────────────────────────────────────────────────────────

        [Test]
        public void ToLong_Zero_ReturnsZero()
        {
            Assert.AreEqual(0L, Fix64.Zero.ToLong());
        }

        [Test]
        public void ToLong_One_ReturnsOne()
        {
            Assert.AreEqual(1L, Fix64.One.ToLong());
        }

        [Test]
        public void ToLong_NegOne_ReturnsNegOne()
        {
            Assert.AreEqual(-1L, Fix64.NegOne.ToLong());
        }

        [Test]
        public void ToLong_Half_ReturnsTruncatedZero()
        {
            Assert.AreEqual(0L, Fix64.Half.ToLong());
        }

        [Test]
        public void ToLong_LargeIntegerValue_ReturnsCorrectLong()
        {
            Fix64 value = Fix64.From(100000L);
            Assert.AreEqual(100000L, value.ToLong());
        }
    }
}
