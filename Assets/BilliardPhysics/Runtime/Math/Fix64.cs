using System;

namespace BilliardPhysics
{
    /// <summary>
    /// 64-bit signed fixed-point number in 32.32 format.
    /// Upper 32 bits = integer part, lower 32 bits = fractional part.
    /// </summary>
    public struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
    {
        public long RawValue;

        // ── Constants ────────────────────────────────────────────────────────────
        private const long ONE = 1L << 32;

        public static readonly Fix64 Zero    = new Fix64 { RawValue = 0 };
        public static readonly Fix64 One     = new Fix64 { RawValue = ONE };
        public static readonly Fix64 Two     = new Fix64 { RawValue = 2L * ONE };
        public static readonly Fix64 Half    = new Fix64 { RawValue = ONE >> 1 };
        public static readonly Fix64 NegOne  = new Fix64 { RawValue = -ONE };
        public static readonly Fix64 MaxValue = new Fix64 { RawValue = long.MaxValue };
        public static readonly Fix64 MinValue = new Fix64 { RawValue = long.MinValue };

        // Pi ≈ 3.14159265358979  →  raw = round(Pi * 2^32) = 13493037704
        public static readonly Fix64 Pi = new Fix64 { RawValue = 13493037704L };

        // ── Factory helpers ───────────────────────────────────────────────────────
        public static Fix64 From(int value)  => new Fix64 { RawValue = (long)value * ONE };
        public static Fix64 From(long value) => new Fix64 { RawValue = value * ONE };

        // ── Implicit conversions ──────────────────────────────────────────────────
        public static implicit operator Fix64(int value)  => From(value);
        public static implicit operator Fix64(long value) => From(value);

        // ── Arithmetic operators ──────────────────────────────────────────────────
        public static Fix64 operator +(Fix64 a, Fix64 b) => new Fix64 { RawValue = a.RawValue + b.RawValue };
        public static Fix64 operator -(Fix64 a, Fix64 b) => new Fix64 { RawValue = a.RawValue - b.RawValue };
        public static Fix64 operator -(Fix64 a)          => new Fix64 { RawValue = -a.RawValue };

        public static Fix64 operator *(Fix64 a, Fix64 b)
        {
            // Perform sign-magnitude unsigned 128-bit multiply, then shift right 32.
            bool negative = (a.RawValue < 0) != (b.RawValue < 0);
            ulong ua = (ulong)(a.RawValue < 0 ? -a.RawValue : a.RawValue);
            ulong ub = (ulong)(b.RawValue < 0 ? -b.RawValue : b.RawValue);

            ulong aLo = ua & 0xFFFFFFFFUL;
            ulong aHi = ua >> 32;
            ulong bLo = ub & 0xFFFFFFFFUL;
            ulong bHi = ub >> 32;

            ulong lo   = aLo * bLo;
            ulong m1   = aHi * bLo;
            ulong m2   = aLo * bHi;
            ulong hi   = aHi * bHi;

            // Sum the cross-product contributions at the 32-bit boundary.
            ulong loPart = (lo >> 32) + (m1 & 0xFFFFFFFFUL) + (m2 & 0xFFFFFFFFUL);
            ulong hiPart = hi + (m1 >> 32) + (m2 >> 32) + (loPart >> 32);
            ulong r      = (hiPart << 32) | (loPart & 0xFFFFFFFFUL);

            long result = negative ? -(long)r : (long)r;
            return new Fix64 { RawValue = result };
        }

        public static Fix64 operator /(Fix64 a, Fix64 b)
        {
            if (b.RawValue == 0) throw new DivideByZeroException("Fix64 division by zero.");
            // (a.RawValue << 32) / b.RawValue, handling sign properly.
            bool negative = (a.RawValue < 0) != (b.RawValue < 0);
            ulong ua = (ulong)(a.RawValue < 0 ? -a.RawValue : a.RawValue);
            ulong ub = (ulong)(b.RawValue < 0 ? -b.RawValue : b.RawValue);

            // Compute (ua << 32) / ub via 128-bit long division.
            ulong num128Hi = ua >> 32;
            ulong num128Lo = ua << 32;
            ulong quotient = UDiv128By64(num128Hi, num128Lo, ub);

            long res = negative ? -(long)quotient : (long)quotient;
            return new Fix64 { RawValue = res };
        }

        // 128-bit unsigned division: (hi << 64 | lo) / d, returns lower 64 bits of quotient.
        private static ulong UDiv128By64(ulong hi, ulong lo, ulong d)
        {
            if (hi == 0) return lo / d;

            // Use bit-by-bit long division (slow but correct and deterministic).
            ulong qHi = 0, qLo = 0;
            ulong rHi = 0, rLo = 0;

            for (int i = 127; i >= 0; i--)
            {
                // Shift remainder left by 1.
                rHi = (rHi << 1) | (rLo >> 63);
                rLo = rLo << 1;

                // Bring in next bit of numerator.
                if (i >= 64)
                {
                    if (((hi >> (i - 64)) & 1) != 0)
                        rLo |= 1;
                }
                else
                {
                    if (((lo >> i) & 1) != 0)
                        rLo |= 1;
                }

                // Remainder >= d?
                if (rHi > 0 || rLo >= d)
                {
                    // Subtract d from remainder.
                    if (rLo >= d) { rLo -= d; }
                    else          { rHi--; rLo = rLo - d; } // rHi was >0
                    // Set quotient bit.
                    if (i >= 64) qHi |= 1UL << (i - 64);
                    else         qLo |= 1UL << i;
                }
            }
            return qLo;
        }

        // ── Comparison operators ──────────────────────────────────────────────────
        public static bool operator ==(Fix64 a, Fix64 b) => a.RawValue == b.RawValue;
        public static bool operator !=(Fix64 a, Fix64 b) => a.RawValue != b.RawValue;
        public static bool operator  <(Fix64 a, Fix64 b) => a.RawValue  < b.RawValue;
        public static bool operator  >(Fix64 a, Fix64 b) => a.RawValue  > b.RawValue;
        public static bool operator <=(Fix64 a, Fix64 b) => a.RawValue <= b.RawValue;
        public static bool operator >=(Fix64 a, Fix64 b) => a.RawValue >= b.RawValue;

        // ── Math functions ────────────────────────────────────────────────────────
        public static Fix64 Abs(Fix64 x)  => x.RawValue < 0 ? new Fix64 { RawValue = -x.RawValue } : x;
        public static Fix64 Min(Fix64 a, Fix64 b) => a.RawValue <= b.RawValue ? a : b;
        public static Fix64 Max(Fix64 a, Fix64 b) => a.RawValue >= b.RawValue ? a : b;

        public static Fix64 Sign(Fix64 x)
        {
            if (x.RawValue > 0) return One;
            if (x.RawValue < 0) return NegOne;
            return Zero;
        }

        /// <summary>
        /// Integer square root: floor(sqrt(n)).
        /// </summary>
        private static ulong IntSqrt(ulong n)
        {
            if (n == 0) return 0;
            ulong x = n;
            ulong y = (x + 1) >> 1;
            while (y < x)
            {
                x = y;
                y = (x + n / x) >> 1;
            }
            return x;
        }

        /// <summary>
        /// Fixed-point square root.
        /// result.RawValue = sqrt(x.RawValue) * 2^16
        /// because sqrt(x_real) = sqrt(x.RawValue / 2^32), so
        /// result.RawValue = sqrt(x_real) * 2^32 = sqrt(x.RawValue) * 2^16.
        /// </summary>
        public static Fix64 Sqrt(Fix64 x)
        {
            if (x.RawValue < 0)  throw new ArgumentException("Cannot take sqrt of negative Fix64.");
            if (x.RawValue == 0) return Zero;

            ulong rawVal  = (ulong)x.RawValue;
            ulong sqrtRaw = IntSqrt(rawVal);
            return new Fix64 { RawValue = (long)(sqrtRaw << 16) };
        }

        // ── IEquatable / IComparable ──────────────────────────────────────────────
        public bool Equals(Fix64 other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is Fix64 f && Equals(f);
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(Fix64 other) => RawValue.CompareTo(other.RawValue);

        // ── ToString ──────────────────────────────────────────────────────────────
        public override string ToString()
        {
            long intPart  = RawValue >> 32;
            ulong fracRaw = (ulong)(RawValue & 0xFFFFFFFFL);
            // Convert fractional part to decimal by multiplying by 10^9.
            ulong fracDec = (fracRaw * 1000000000UL) >> 32;
            string sign   = (RawValue < 0 && intPart == 0) ? "-" : "";
            return $"{sign}{intPart}.{fracDec:D9}";
        }
    }
}
