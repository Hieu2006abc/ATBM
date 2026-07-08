using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BTL_2.Data;
using BTL_2.Models;

namespace BTL_2.Services
{
    public class SecureCVService : ISecureCVService
    {
        private readonly JobDatabaseContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly IActivityLogService _activityLogService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SecureCVService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _uploadPath;
        private const string SecureEnvelopeVersion = "cv-secure-v2";

        public SecureCVService(
            JobDatabaseContext context,
            IEncryptionService encryptionService,
            IActivityLogService activityLogService,
            IConfiguration configuration,
            ILogger<SecureCVService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _encryptionService = encryptionService;
            _activityLogService = activityLogService;
            _configuration = configuration;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;

            var configuredPath = _configuration["CVSettings:UploadPath"] ?? "wwwroot/uploads/cvs/";
            _uploadPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));

            try
            {
                if (!Directory.Exists(_uploadPath))
                {
                    Directory.CreateDirectory(_uploadPath);
                    _logger.LogInformation($"Đã tạo thư mục: {_uploadPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Không thể tạo thư mục: {_uploadPath}");
                _uploadPath = Path.GetTempPath();
            }
        }

        public async Task<CVMetadata> UploadSecureCVAsync(IFormFile file, string candidateId, int jobId)
        {
            _logger.LogInformation($"=== BẮT ĐẦU UPLOAD ===");
            _logger.LogInformation($"CandidateId: {candidateId}, JobId: {jobId}, File: {file?.FileName}");
            string? filePath = null;

            try
            {
                // 1. Validate file
                ValidateFile(file);

                // 2. Đọc file gốc
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var originalData = ms.ToArray();
                _logger.LogInformation($"Đã đọc file: {originalData.Length} bytes");

                // 3. Tính SHA256 hash TRƯỚC khi mã hóa
                var hash = _encryptionService.ComputeSHA256Hash(originalData);
                var nonce = CreateNonce();
                var uploadTime = DateTime.UtcNow;
                var expireTime = uploadTime.AddDays(_configuration.GetValue("CVSettings:CVRetentionDays", 90));
                _logger.LogInformation($"SHA256 Hash: {hash[..20]}...");

                // 4. Đóng gói metadata vào envelope rồi mã hóa toàn bộ bằng AES-256.
                var envelope = SecureCVEnvelope.Create(
                    candidateId,
                    jobId,
                    uploadTime,
                    expireTime,
                    nonce,
                    hash,
                    file.FileName,
                    Path.GetExtension(file.FileName),
                    originalData);

                var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                var (encryptedData, iv, _) = await _encryptionService.EncryptFileAsync(envelopeBytes);
                _logger.LogInformation($"Đã mã hóa file: {encryptedData.Length} bytes");

                // 5. Lưu file đã mã hóa với đuôi .enc
                var storedFileName = $"{Guid.NewGuid()}_{DateTime.UtcNow.Ticks}.enc";
                filePath = Path.Combine(_uploadPath, storedFileName);

                await File.WriteAllBytesAsync(filePath, encryptedData);
                _logger.LogInformation($"Đã lưu file mã hóa tại: {filePath}");

                // 6. Kiểm tra file đã được mã hóa chưa
                var savedData = await File.ReadAllBytesAsync(filePath);
                bool isEncrypted = savedData.Length != originalData.Length || !savedData.SequenceEqual(originalData);
                _logger.LogInformation($"File đã được mã hóa: {isEncrypted}");

                // 7. Tạo metadata và tự kiểm tra ngay file vừa mã hóa.
                var metadata = new CVMetadata
                {
                    CandidateId = candidateId,
                    JobId = jobId,
                    OriginalFileName = file.FileName,
                    StoredFileName = storedFileName,
                    FilePath = filePath,
                    FileType = Path.GetExtension(file.FileName),
                    FileSize = file.Length,
                    SHA256Hash = hash,
                    EncryptionIV = iv,
                    UploadTime = uploadTime,
                    ExpireTime = expireTime,
                    Nonce = nonce,
                    IsDeleted = false
                };

                var uploadValidation = await ReadAndValidateCVFileAsync(metadata);
                if (!uploadValidation.isValid ||
                    !string.Equals(uploadValidation.status, "Valid", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"CV vừa upload không đạt kiểm tra toàn vẹn ({uploadValidation.status}): {uploadValidation.detail}");
                }

                _logger.LogInformation("CV vừa upload đã tự kiểm tra hợp lệ: {Detail}", uploadValidation.detail);

                // 8. Lưu metadata vào database
                _context.CVMetadata.Add(metadata);
                var saveResult = await _context.SaveChangesAsync();
                _logger.LogInformation($"Đã lưu metadata vào database. Id: {metadata.Id}, Rows affected: {saveResult}");

                return metadata;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                        _logger.LogWarning($"Đã xóa file CV do lưu database thất bại: {filePath}");
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogError(deleteEx, $"Không thể xóa file CV lỗi: {filePath}");
                    }
                }

                _logger.LogError(ex, $"LỖI UPLOAD: {ex.Message}");
                throw;
            }
        }

        public async Task<(byte[] fileData, CVMetadata metadata, bool isValid)> DownloadSecureCVAsync(
            int cvMetadataId, int recruiterId, string ipAddress, string userAgent)
        {
            _logger.LogInformation($"=== BẮT ĐẦU DOWNLOAD ===");
            _logger.LogInformation($"CVId: {cvMetadataId}, RecruiterId: {recruiterId}");

            try
            {
                var metadata = await GetActiveCVMetadataAsync(cvMetadataId);

                if (metadata == null)
                {
                    await _activityLogService.LogCVAccessAsync(recruiterId.ToString(), cvMetadataId, ipAddress,
                        "Failed_NotFound", userAgent, "CV metadata not found");
                    throw new FileNotFoundException("CV không tồn tại");
                }

                if (metadata.ExpireTime <= DateTime.UtcNow)
                {
                    await _activityLogService.LogCVAccessAsync(recruiterId.ToString(), cvMetadataId, ipAddress,
                        "Failed_Expired", userAgent, "CV download link has expired");
                    throw new UnauthorizedAccessException("CV đã hết hạn tải xuống");
                }

                // Kiểm tra quyền
                if (!await HasRecruiterPermissionAsync(recruiterId, metadata.JobId))
                {
                    await _activityLogService.LogCVAccessAsync(recruiterId.ToString(), cvMetadataId, ipAddress,
                        "Failed_Permission", userAgent, "Recruiter does not have permission");
                    throw new UnauthorizedAccessException("Bạn không có quyền tải CV này");
                }

                var integrityResult = await ReadAndValidateCVFileAsync(metadata);
                var status = integrityResult.isValid
                    ? "Success"
                    : integrityResult.status == "MissingFile" ? "Failed_FileMissing" : "Failed_Integrity";

                await _activityLogService.LogCVAccessAsync(recruiterId.ToString(), cvMetadataId, ipAddress,
                    status, userAgent, integrityResult.isValid ? null : integrityResult.detail);

                if (!integrityResult.isValid)
                {
                    _logger.LogWarning($"INTEGRITY CHECK FAILED! CV có thể đã bị sửa đổi.");
                    if (integrityResult.status == "MissingFile")
                    {
                        throw new FileNotFoundException(integrityResult.detail);
                    }

                    throw new InvalidDataException(integrityResult.detail);
                }

                _logger.LogInformation($"Download thành công! File gốc: {metadata.OriginalFileName}");

                return (integrityResult.fileData, metadata, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"LỖI DOWNLOAD: {ex.Message}");
                throw;
            }
        }

        public async Task<(bool isValid, string status, string detail)> CheckCVIntegrityAsync(int cvMetadataId)
        {
            var metadata = await GetActiveCVMetadataAsync(cvMetadataId);
            if (metadata == null)
            {
                return (false, "MissingMetadata", "Không tìm thấy metadata CV trong database.");
            }

            var result = await ReadAndValidateCVFileAsync(metadata);
            return (result.isValid, result.status, result.detail);
        }

        public async Task<string> GenerateDownloadTokenAsync(int cvMetadataId, int recruiterId)
        {
            var metadata = await _context.CVMetadata
                .Where(m => m.Id == cvMetadataId && !m.IsDeleted)
                .Select(m => new
                {
                    m.Id,
                    m.JobId,
                    m.ExpireTime
                })
                .FirstOrDefaultAsync();

            if (metadata == null)
                throw new FileNotFoundException("CV không tồn tại");

            if (metadata.ExpireTime <= DateTime.UtcNow)
                throw new UnauthorizedAccessException("CV đã hết hạn tải xuống");

            if (!await HasRecruiterPermissionAsync(recruiterId, metadata.JobId))
                throw new UnauthorizedAccessException("Bạn không có quyền tải CV này");

            var rawToken = CreateUrlSafeToken();
            var verificationCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var sessionId = _httpContextAccessor.HttpContext?.Session.Id;

            var token = new DownloadToken
            {
                Token = rawToken,
                CVMetadataId = cvMetadataId,
                RecruiterId = recruiterId.ToString(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_configuration.GetValue("CVSettings:TokenExpiryMinutes", 5)),
                IsUsed = false,
                IsRevoked = false,
                SessionId = sessionId,
                VerificationCodeHash = HashVerificationCode(verificationCode)
            };

            _context.DownloadTokens.Add(token);
            await _context.SaveChangesAsync();
            await _activityLogService.LogCVAccessAsync(
                recruiterId.ToString(),
                cvMetadataId,
                _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                "Token_Created",
                _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"],
                $"Token một lần hết hạn lúc {token.ExpiresAt:O}; ràng buộc SessionId={sessionId}");

            _logger.LogInformation($"Token created for CV {cvMetadataId}, recruiter {recruiterId}, session {sessionId}");

            return $"{rawToken}.{verificationCode}";
        }

        public async Task<bool> ValidateAndConsumeTokenAsync(string token, int cvMetadataId, int recruiterId)
        {
            var (rawToken, verificationCode) = SplitTokenAndVerificationCode(token);
            if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(verificationCode)) return false;

            var downloadToken = await _context.DownloadTokens
                .FirstOrDefaultAsync(t => t.Token == rawToken && t.CVMetadataId == cvMetadataId);

            if (downloadToken == null) return false;
            if (downloadToken.IsUsed || downloadToken.IsRevoked) return false;
            if (downloadToken.ExpiresAt < DateTime.UtcNow) return false;
            if (downloadToken.RecruiterId != recruiterId.ToString()) return false;

            var currentSessionId = _httpContextAccessor.HttpContext?.Session.Id;
            if (!string.IsNullOrWhiteSpace(downloadToken.SessionId) && downloadToken.SessionId != currentSessionId) return false;
            if (!string.IsNullOrWhiteSpace(downloadToken.VerificationCodeHash) &&
                downloadToken.VerificationCodeHash != HashVerificationCode(verificationCode)) return false;

            downloadToken.IsUsed = true;
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> HasRecruiterPermissionAsync(int recruiterId, int jobId)
        {
            var companyId = await _context.Jobs
                .Where(j => j.JobId == jobId)
                .Select(j => j.CompanyId)
                .FirstOrDefaultAsync();

            if (!companyId.HasValue) return false;

            var recruiterRole = await _context.Users
                .Where(u => u.UserId == recruiterId)
                .Select(u => u.Role)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(recruiterRole)) return false;

            // Admin có toàn quyền
            if (recruiterRole == "Admin") return true;

            return await _context.Companies.AnyAsync(c =>
                c.EmployerId == recruiterId && c.CompanyId == companyId.Value);
        }

        private Task<CVMetadata?> GetActiveCVMetadataAsync(int cvMetadataId)
        {
            return _context.CVMetadata
                .Where(m => m.Id == cvMetadataId && !m.IsDeleted)
                .Select(m => new CVMetadata
                {
                    Id = m.Id,
                    CandidateId = m.CandidateId ?? string.Empty,
                    JobId = m.JobId,
                    OriginalFileName = m.OriginalFileName ?? "cv.pdf",
                    StoredFileName = m.StoredFileName ?? string.Empty,
                    FilePath = m.FilePath ?? string.Empty,
                    FileType = m.FileType ?? "application/pdf",
                    FileSize = m.FileSize,
                    SHA256Hash = m.SHA256Hash ?? string.Empty,
                    EncryptionIV = m.EncryptionIV ?? string.Empty,
                    Nonce = m.Nonce ?? string.Empty,
                    UploadTime = m.UploadTime,
                    ExpireTime = m.ExpireTime,
                    IsDeleted = m.IsDeleted
                })
                .FirstOrDefaultAsync();
        }

        private async Task<(byte[] fileData, bool isValid, string status, string detail)> ReadAndValidateCVFileAsync(CVMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.FilePath) || !File.Exists(metadata.FilePath))
            {
                return (Array.Empty<byte>(), false, "MissingFile", "Không tìm thấy file mã hóa trên server.");
            }

            if (string.IsNullOrWhiteSpace(metadata.EncryptionIV) || string.IsNullOrWhiteSpace(metadata.SHA256Hash))
            {
                return (Array.Empty<byte>(), false, "InvalidMetadata", "Thiếu IV hoặc SHA-256 hash trong metadata.");
            }

            try
            {
                var encryptedData = await File.ReadAllBytesAsync(metadata.FilePath);
                _logger.LogInformation($"Đã đọc file mã hóa: {encryptedData.Length} bytes");

                if (encryptedData.Length < 32 || encryptedData.Length % 16 != 0)
                {
                    return (Array.Empty<byte>(), false, "DecryptFailed",
                        "File mã hóa không đúng định dạng AES block; file có thể là dữ liệu cũ, bị ghi sai, bị cắt byte hoặc đã bị chỉnh sửa.");
                }

                var (decryptedData, integrityValid) = await _encryptionService.DecryptAndVerifyAsync(
                    encryptedData, metadata.EncryptionIV, metadata.SHA256Hash);

                if (!integrityValid)
                {
                    return (Array.Empty<byte>(), false, "Tampered", "SHA-256 không khớp metadata, CV có thể đã bị chỉnh sửa.");
                }

                var (fileData, envelopeValid) = ValidateEnvelopeOrLegacyPayload(decryptedData, metadata);
                if (!envelopeValid)
                {
                    return (Array.Empty<byte>(), false, "Tampered", "Metadata trong file mã hóa không khớp database hoặc nội dung CV đã bị sửa.");
                }

                return (fileData, true, "Valid", "File giải mã được, SHA-256 và metadata envelope khớp.");
            }
            catch (Exception ex)
            {
                return (Array.Empty<byte>(), false, "DecryptFailed", $"Không giải mã hoặc kiểm tra được file: {ex.Message}");
            }
        }

        private static string CreateUrlSafeToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string CreateNonce()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        }

        private static (string rawToken, string verificationCode) SplitTokenAndVerificationCode(string compositeToken)
        {
            if (string.IsNullOrWhiteSpace(compositeToken)) return (string.Empty, string.Empty);
            var parts = compositeToken.Split('.', 2);
            return parts.Length == 2 ? (parts[0], parts[1]) : (compositeToken, string.Empty);
        }

        private static string HashVerificationCode(string verificationCode)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(verificationCode));
            return Convert.ToBase64String(bytes);
        }

        private void ValidateFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File không được để trống");

            var maxSize = 5 * 1024 * 1024;
            if (file.Length > maxSize)
                throw new ArgumentException($"File vượt quá kích thước cho phép (5MB)");

            var extension = Path.GetExtension(file.FileName).ToLower();
            var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException($"Định dạng file không được hỗ trợ. Chỉ chấp nhận: PDF, DOC, DOCX");
        }

        private (byte[] fileData, bool isValid) ValidateEnvelopeOrLegacyPayload(byte[] decryptedData, CVMetadata metadata)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<SecureCVEnvelope>(decryptedData);
                if (envelope?.Version != SecureEnvelopeVersion)
                {
                    return (decryptedData, true);
                }

                var metadataMatches =
                    envelope.CandidateId == metadata.CandidateId &&
                    envelope.JobId == metadata.JobId &&
                    envelope.Nonce == metadata.Nonce &&
                    SameStoredTime(envelope.UploadTime, metadata.UploadTime) &&
                    SameStoredTime(envelope.ExpireTime, metadata.ExpireTime);

                if (!metadataMatches || string.IsNullOrWhiteSpace(envelope.ContentBase64))
                {
                    return (Array.Empty<byte>(), false);
                }

                var fileData = Convert.FromBase64String(envelope.ContentBase64);
                var fileHash = _encryptionService.ComputeSHA256Hash(fileData);
                var hashMatches =
                    FixedTimeEquals(fileHash, metadata.SHA256Hash) &&
                    FixedTimeEquals(fileHash, envelope.FileSha256);

                return (fileData, hashMatches);
            }
            catch (JsonException)
            {
                return (decryptedData, true);
            }
            catch (FormatException)
            {
                return (Array.Empty<byte>(), false);
            }
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null) return false;
            var leftBytes = Encoding.UTF8.GetBytes(left);
            var rightBytes = Encoding.UTF8.GetBytes(right);
            return leftBytes.Length == rightBytes.Length &&
                CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private static bool SameStoredTime(DateTime left, DateTime right)
        {
            return Math.Abs((left - right).TotalSeconds) < 5;
        }

        private sealed class SecureCVEnvelope
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = SecureEnvelopeVersion;

            [JsonPropertyName("candidate_id")]
            public string CandidateId { get; set; } = string.Empty;

            [JsonPropertyName("job_id")]
            public int JobId { get; set; }

            [JsonPropertyName("upload_time")]
            public DateTime UploadTime { get; set; }

            [JsonPropertyName("expire_time")]
            public DateTime ExpireTime { get; set; }

            [JsonPropertyName("nonce")]
            public string Nonce { get; set; } = string.Empty;

            [JsonPropertyName("file_sha256")]
            public string FileSha256 { get; set; } = string.Empty;

            [JsonPropertyName("original_file_name")]
            public string OriginalFileName { get; set; } = string.Empty;

            [JsonPropertyName("file_type")]
            public string FileType { get; set; } = string.Empty;

            [JsonPropertyName("content_base64")]
            public string ContentBase64 { get; set; } = string.Empty;

            public static SecureCVEnvelope Create(
                string candidateId,
                int jobId,
                DateTime uploadTime,
                DateTime expireTime,
                string nonce,
                string fileSha256,
                string originalFileName,
                string fileType,
                byte[] fileData)
            {
                return new SecureCVEnvelope
                {
                    CandidateId = candidateId,
                    JobId = jobId,
                    UploadTime = uploadTime,
                    ExpireTime = expireTime,
                    Nonce = nonce,
                    FileSha256 = fileSha256,
                    OriginalFileName = originalFileName,
                    FileType = fileType,
                    ContentBase64 = Convert.ToBase64String(fileData)
                };
            }
        }
    }
}
