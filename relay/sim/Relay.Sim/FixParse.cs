using System;

namespace Relay.Sim
{
    /// <summary>
    /// Machine-file numbers into fixed-point, with integer maths only.
    /// This is the one place where text becomes sim state, so it is the one
    /// place a platform disagreement would be invisible until a phone
    /// produced a different hash. Nothing here goes through a floating-point
    /// parse, not even briefly: digits are accumulated into a long by hand
    /// and handed to Fix.Ratio.
    ///
    /// .NET's double parsing is in fact well-defined - the point is the habit,
    /// and that this code can be read and verified in one sitting.
    /// </summary>
    public static class FixParse
    {
        /// <summary>
        /// Most decimal places a machine file may use. Q31.32's resolution
        /// is about 2.3e-10, so six is far inside what Fix can hold; the limit
        /// exists to keep authored numbers legible and to keep the mantissa
        /// well clear of overflow.
        /// </summary>
        public const int MaxDecimals = 6;

        /// <summary>
        /// Largest magnitude accepted. Fix reaches 2.1e9, but a machine number
        /// that big is a typo - the playfield is 9 by 16 units - and rejecting
        /// early keeps the shift in Fix.Ratio comfortably inside a long.
        /// </summary>
        public const int MaxMagnitude = 1000000;

        /// <summary>
        /// Parses a plain decimal: optional '-', digits, optionally '.'
        /// then up to MaxDecimals digits. No '+', no exponent, no whitespace,
        /// nothing else. <paramref name="where"/> names the field, so an error
        /// says which one.
        /// </summary>
        public static Fix Decimal(string raw, string where)
        {
            if (string.IsNullOrEmpty(raw))
                throw new MachineFormatException($"{where}: empty number");

            int i = 0;
            bool negative = raw[0] == '-';

            if (negative)
                i++;

            if (i >= raw.Length)
            {
                throw new MachineFormatException(
                    $"{where}: \"{raw}\" is just a sign");
            }

            // Integer part.
            long whole = 0;
            int intDigits = 0;

            while (i < raw.Length && IsDigit(raw[i]))
            {
                whole = whole * 10 + (raw[i] - '0');

                if (whole > MaxMagnitude)
                {
                    throw new MachineFormatException(
                        $"{where}: \"{raw}\" is larger than {MaxMagnitude}");
                }

                i++;
                intDigits++;
            }

            if (intDigits == 0)
            {
                throw new MachineFormatException(
                    $"{where}: \"{raw}\" needs a digit before the decimal point");
            }

            // Fractional part, accumulated as mantissa over scale:
            // "0.185" -> 185/1000.
            long mantissa = 0;
            long scale = 1;

            if (i < raw.Length && raw[i] == '.')
            {
                i++;

                int fracDigits = 0;

                while (i < raw.Length && IsDigit(raw[i]))
                {
                    fracDigits++;

                    if (fracDigits > MaxDecimals)
                    {
                        throw new MachineFormatException(
                            $"{where}: \"{raw}\" has more than {MaxDecimals} decimal places");
                    }

                    mantissa = mantissa * 10 + (raw[i] - '0');
                    scale *= 10;

                    i++;
                }

                if (fracDigits == 0)
                {
                    throw new MachineFormatException(
                        $"{where}: \"{raw}\" has a decimal point with no digits after it");
                }
            }

            if (i != raw.Length)
            {
                throw new MachineFormatException(
                    $"{where}: \"{raw}\" has trailing characters " +
                    $"(starting at \"{raw.Substring(i)}\")");
            }

            // Built in two pieces on purpose. Fix.Ratio shifts its numerator
            // left by 32, so handing it 6 decimal places of a large number
            // would overflow a long. The fraction alone never exceeds 1e6,
            // which is nowhere near it.
            Fix value = Fix.FromInt((int)whole);

            if (mantissa != 0)
                value += Fix.Ratio((int)mantissa, (int)scale);

            return negative ? -value : value;
        }

        /// <summary>
        /// A whole number, for version and tick counts. No decimal point allowed.
        /// </summary>
        public static int Integer(string raw, string where)
        {
            if (string.IsNullOrEmpty(raw))
                throw new MachineFormatException($"{where}: empty number");

            int i = 0;
            bool negative = raw[0] == '-';

            if (negative)
                i++;

            long value = 0;
            int digits = 0;

            while (i < raw.Length && IsDigit(raw[i]))
            {
                value = value * 10 + (raw[i] - '0');

                if (value > int.MaxValue)
                {
                    throw new MachineFormatException(
                        $"{where}: \"{raw}\" does not fit in an int");
                }

                i++;
                digits++;
            }

            if (digits == 0)
            {
                throw new MachineFormatException(
                    $"{where}: \"{raw}\" is not a whole number");
            }

            if (i != raw.Length)
            {
                throw new MachineFormatException(
                    $"{where}: \"{raw}\" must be a whole number, with no decimal point");
            }

            return (int)(negative ? -value : value);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}