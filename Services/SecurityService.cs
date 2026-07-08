using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BTL_2.Services
{
    public interface ISecurityService
    {
        string GenerateAccessToken();
        string GenerateNonce();
        string ComputeFileChecksum(byte[] fileBytes);
        bool VerifyFileIntegrity(byte[] fileBytes, string expectedChecksum);
        Task<string> EncryptWithMetadataAsync(byte[] fileBytes, int candidateId, int jobId, DateTime expireTime, string nonce);
        Task<byte[]> DecryptAndVerifyAsync(byte[] encryptedBytes, string expectedNonce, string iv, string expectedHash);
        bool IsTokenValid(Guid token, DateTime expireTime);
    }

    public class SecurityService : ISecurityService
    {
        private readonly IEncryptionService _encryptionService;

        public SecurityService(IEncryptionService encryptionService)
        {
            _encryptionService = encryptionService;
        }

        public string GenerateAccessToken()
        {
            return Guid.NewGuid().ToString();
        }

        public string GenerateNonce()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        public string ComputeFileChecksum(byte[] fileBytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(fileBytes);
                return Convert.ToBase64String(hash);
            }
        }

        public bool VerifyFileIntegrity(byte[] fileBytes, string expectedChecksum)
        {
            var currentChecksum = ComputeFileChecksum(fileBytes);
            return currentChecksum == expectedChecksum;
        }

        public async Task<string> EncryptWithMetadataAsync(byte[] fileBytes, int candidateId, int jobId, DateTime expireTime, string nonce)
        {
            // Tạo metadata
            var metadata = $"{candidateId}|{jobId}|{expireTime:yyyyMMddHHmmss}|{nonce}";
            var metadataBytes = Encoding.UTF8.GetBytes(metadata);

            // Kết hợp metadata với file
            var combined = new byte[metadataBytes.Length + fileBytes.Length];
            Buffer.BlockCopy(metadataBytes, 0, combined, 0, metadataBytes.Length);
            Buffer.BlockCopy(fileBytes, 0, combined, metadataBytes.Length, fileBytes.Length);

            // Mã hóa toàn bộ
            var (encryptedData, iv, hash) = await _encryptionService.EncryptFileAsync(combined);

            // Kết hợp IV + encrypted data để trả về
            var result = new byte[iv.Length + encryptedData.Length];
            var ivBytes = Encoding.UTF8.GetBytes(iv);
            Buffer.BlockCopy(ivBytes, 0, result, 0, ivBytes.Length);
            Buffer.BlockCopy(encryptedData, 0, result, ivBytes.Length, encryptedData.Length);

            return Convert.ToBase64String(result);
        }

        public async Task<byte[]> DecryptAndVerifyAsync(byte[] encryptedBytes, string expectedNonce, string iv, string expectedHash)
        {
            // Giải mã
            var (decryptedData, integrityValid) = await _encryptionService.DecryptAndVerifyAsync(encryptedBytes, iv, expectedHash);

            if (!integrityValid)
            {
                throw new UnauthorizedAccessException("Tính toàn vẹn của file không được đảm bảo!");
            }

            // Chuyển đổi dữ liệu giải mã thành string để kiểm tra metadata
            var decryptedString = Encoding.UTF8.GetString(decryptedData);

            // Tách metadata (giả sử metadata cách file bởi dấu |)
            var parts = decryptedString.Split('|');
            if (parts.Length >= 4)
            {
                var nonce = parts[3];
                if (nonce != expectedNonce)
                {
                    throw new UnauthorizedAccessException("Nonce không hợp lệ!");
                }
            }

            // Trả về phần dữ liệu file (bỏ metadata)
            // Tìm vị trí kết thúc của metadata
            var metadataEndIndex = decryptedString.IndexOf('|', decryptedString.IndexOf('|', decryptedString.IndexOf('|') + 1) + 1);
            var metadataLength = Encoding.UTF8.GetByteCount(decryptedString.Substring(0, metadataEndIndex + 1));

            var fileData = new byte[decryptedData.Length - metadataLength];
            Buffer.BlockCopy(decryptedData, metadataLength, fileData, 0, fileData.Length);

            return fileData;
        }

        public bool IsTokenValid(Guid token, DateTime expireTime)
        {
            return token != Guid.Empty && DateTime.UtcNow < expireTime;
        }
    }
}