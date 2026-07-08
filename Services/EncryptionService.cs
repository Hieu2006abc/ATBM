using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BTL_2.Services
{
    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly ILogger<EncryptionService> _logger;

        public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
        {
            _logger = logger;
            var keyString = configuration["EncryptionSettings:AESKey"] ?? configuration["Encryption:Key"];

            if (string.IsNullOrEmpty(keyString))
            {
                throw new InvalidOperationException("AES key không được cấu hình");
            }

            // Đảm bảo key đủ 32 byte
            if (keyString.Length < 32)
                _key = Encoding.UTF8.GetBytes(keyString.PadRight(32, '0'));
            else if (keyString.Length > 32)
                _key = Encoding.UTF8.GetBytes(keyString[..32]);
            else
                _key = Encoding.UTF8.GetBytes(keyString);
        }

        public string ComputeSHA256Hash(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(data);
            return Convert.ToBase64String(hashBytes);
        }

        public string ComputeSHA256HashFromFile(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToBase64String(hashBytes);
        }

        public async Task<(byte[] encryptedData, string iv, string hash)> EncryptFileAsync(byte[] fileData)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _key;
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();

            // Ghi IV vào đầu stream
            await ms.WriteAsync(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                await cs.WriteAsync(fileData, 0, fileData.Length);
                await cs.FlushFinalBlockAsync();
            }

            var encryptedData = ms.ToArray();
            var iv = Convert.ToBase64String(aes.IV);
            var hash = ComputeSHA256Hash(fileData);

            return (encryptedData, iv, hash);
        }

        public async Task<(byte[] decryptedData, bool integrityValid)> DecryptAndVerifyAsync(
            byte[] encryptedData, string iv, string expectedHash)
        {
            var ivBytes = Convert.FromBase64String(iv);

            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = ivBytes;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var offset = encryptedData.Length >= ivBytes.Length && encryptedData.Take(ivBytes.Length).SequenceEqual(ivBytes)
                ? ivBytes.Length
                : 0;

            using var ms = new MemoryStream(encryptedData, offset, encryptedData.Length - offset);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var resultMs = new MemoryStream();

            await cs.CopyToAsync(resultMs);
            var decryptedData = resultMs.ToArray();

            var computedHash = ComputeSHA256Hash(decryptedData);
            var integrityValid = computedHash == expectedHash || EnvelopeFileHashMatches(decryptedData, expectedHash);

            return (decryptedData, integrityValid);
        }

        private static bool EnvelopeFileHashMatches(byte[] decryptedData, string expectedHash)
        {
            if (string.IsNullOrWhiteSpace(expectedHash) || decryptedData.Length == 0)
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(decryptedData);
                var root = document.RootElement;

                if (!root.TryGetProperty("file_sha256", out var hashProperty) ||
                    !root.TryGetProperty("content_base64", out var contentProperty))
                {
                    return false;
                }

                var embeddedHash = hashProperty.GetString();
                var contentBase64 = contentProperty.GetString();
                if (string.IsNullOrWhiteSpace(embeddedHash) || string.IsNullOrWhiteSpace(contentBase64))
                {
                    return false;
                }

                var fileBytes = Convert.FromBase64String(contentBase64);
                using var sha256 = SHA256.Create();
                var actualHash = Convert.ToBase64String(sha256.ComputeHash(fileBytes));

                return FixedTimeEquals(actualHash, embeddedHash) && FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
