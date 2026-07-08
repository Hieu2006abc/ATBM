using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BTL_2.Services
{
    public static class TwoFactorTotpService
    {
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private const int TimeStepSeconds = 30;
        private const int CodeDigits = 6;

        public static string GenerateSecret()
        {
            return ToBase32(RandomNumberGenerator.GetBytes(20));
        }

        public static string BuildOtpAuthUri(string issuer, string accountName, string secret)
        {
            var label = $"{issuer}:{accountName}";
            return $"otpauth://totp/{Uri.EscapeDataString(label)}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits={CodeDigits}&period={TimeStepSeconds}";
        }

        public static bool VerifyCode(string secret, string code, DateTimeOffset? now = null, int allowedDriftSteps = 1)
        {
            var normalizedCode = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            if (normalizedCode.Length != CodeDigits || string.IsNullOrWhiteSpace(secret))
            {
                return false;
            }

            var currentCounter = GetCurrentCounter(now ?? DateTimeOffset.UtcNow);
            for (var offset = -allowedDriftSteps; offset <= allowedDriftSteps; offset++)
            {
                var expected = GenerateCode(secret, currentCounter + offset);
                if (FixedTimeEquals(expected, normalizedCode))
                {
                    return true;
                }
            }

            return false;
        }

        public static string FormatSecretForDisplay(string secret)
        {
            var normalized = NormalizeSecret(secret);
            return string.Join(" ", Enumerable.Range(0, (normalized.Length + 3) / 4)
                .Select(i => normalized.Substring(i * 4, Math.Min(4, normalized.Length - i * 4))));
        }

        private static string GenerateCode(string secret, long counter)
        {
            var key = FromBase32(secret);
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counterBytes);
            var offset = hash[^1] & 0x0F;
            var binary =
                ((hash[offset] & 0x7F) << 24) |
                ((hash[offset + 1] & 0xFF) << 16) |
                ((hash[offset + 2] & 0xFF) << 8) |
                (hash[offset + 3] & 0xFF);

            var otp = binary % (int)Math.Pow(10, CodeDigits);
            return otp.ToString(new string('0', CodeDigits));
        }

        private static long GetCurrentCounter(DateTimeOffset now)
        {
            return now.ToUnixTimeSeconds() / TimeStepSeconds;
        }

        private static string ToBase32(byte[] data)
        {
            var result = new StringBuilder((int)Math.Ceiling(data.Length / 5d) * 8);
            var bitBuffer = 0;
            var bitsInBuffer = 0;

            foreach (var value in data)
            {
                bitBuffer = (bitBuffer << 8) | value;
                bitsInBuffer += 8;

                while (bitsInBuffer >= 5)
                {
                    result.Append(Base32Alphabet[(bitBuffer >> (bitsInBuffer - 5)) & 0x1F]);
                    bitsInBuffer -= 5;
                    bitBuffer &= (1 << bitsInBuffer) - 1;
                }
            }

            if (bitsInBuffer > 0)
            {
                result.Append(Base32Alphabet[(bitBuffer << (5 - bitsInBuffer)) & 0x1F]);
            }

            return result.ToString();
        }

        private static byte[] FromBase32(string secret)
        {
            var normalized = NormalizeSecret(secret);
            var bytes = new byte[normalized.Length * 5 / 8];
            var bitBuffer = 0;
            var bitsInBuffer = 0;
            var byteIndex = 0;

            foreach (var character in normalized)
            {
                var value = Base32Alphabet.IndexOf(character);
                if (value < 0)
                {
                    throw new FormatException("Invalid Base32 secret.");
                }

                bitBuffer = (bitBuffer << 5) | value;
                bitsInBuffer += 5;

                if (bitsInBuffer >= 8)
                {
                    bytes[byteIndex++] = (byte)(bitBuffer >> (bitsInBuffer - 8));
                    bitsInBuffer -= 8;
                    bitBuffer &= (1 << bitsInBuffer) - 1;
                }
            }

            return bytes;
        }

        private static string NormalizeSecret(string secret)
        {
            return new string((secret ?? string.Empty)
                .Where(c => !char.IsWhiteSpace(c) && c != '=')
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.ASCII.GetBytes(left);
            var rightBytes = Encoding.ASCII.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
