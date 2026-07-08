using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTL_2.Data;
using BTL_2.Models;
using BTL_2.Models.ViewModels;
using BTL_2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTL_2.Controllers
{
    public class TestSecurityController : Controller
    {
        private readonly JobDatabaseContext _context;
        private readonly ISecureCVService _secureCVService;

        public TestSecurityController(
            JobDatabaseContext context,
            ISecureCVService secureCVService)
        {
            _context = context;
            _secureCVService = secureCVService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (!IsEmployerOrAdmin())
            {
                TempData["ErrorMessage"] = "Bạn không có quyền truy cập trang kiểm thử bảo mật.";
                return RedirectToAction("Login", "Account");
            }

            ViewBag.HasMetadata = GetScopedMetadataBaseQuery().Any();
            ViewBag.HasLogs = GetScopedActivityLogQuery().Any();
            ViewBag.AccessibleCvCount = GetRealMetadataQuery().Count();
            return View();
        }

        [HttpGet]
        public Task<IActionResult> RunRequiredTestsGet()
        {
            return RunRequiredTestsCore();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> RunRequiredTests()
        {
            return RunRequiredTestsCore();
        }

        private async Task<IActionResult> RunRequiredTestsCore()
        {
            if (!IsEmployerOrAdmin())
            {
                TempData["ErrorMessage"] = "Bạn không có quyền chạy kiểm thử bảo mật.";
                return RedirectToAction("Login", "Account");
            }

            var results = new List<TestResultViewModel>
            {
                await RunSafely("Quét toàn vẹn CV thật", TestRealCvIntegrityScan)
            };

            return View("RequiredTests", results);
        }

        private static async Task<TestResultViewModel> RunSafely(string testName, Func<Task<TestResultViewModel>> test)
        {
            try
            {
                return await test();
            }
            catch (Exception ex)
            {
                return new TestResultViewModel
                {
                    TestName = testName,
                    ExpectedResult = "Test phải chạy xong và trả về PASS/FAIL rõ ràng.",
                    ActualResult = $"FAIL: Test bị lỗi runtime: {ex.Message}",
                    Passed = false
                };
            }
        }

        private async Task<TestResultViewModel> TestValidCandidateUpload()
        {
            var hasValidCv = await GetScopedMetadataBaseQuery().AnyAsync(m =>
                !m.IsDeleted &&
                !string.IsNullOrWhiteSpace(m.CandidateId) &&
                m.JobId > 0 &&
                m.UploadTime != default &&
                m.ExpireTime != default &&
                !string.IsNullOrWhiteSpace(m.Nonce));

            return new TestResultViewModel
            {
                TestName = "Ứng viên hợp lệ gửi CV",
                ExpectedResult = "Có CV được lưu với metadata đầy đủ candidate_id, job_id, upload_time, expire_time, nonce.",
                ActualResult = hasValidCv
                    ? "PASS: Có ít nhất 1 CV hợp lệ với metadata đầy đủ."
                    : "FAIL: Chưa tìm thấy CV hợp lệ có metadata đầy đủ.",
                Passed = hasValidCv
            };
        }

        private async Task<TestResultViewModel> TestInvalidTokenAccess()
        {
            var recruiterId = GetCurrentRecruiterId();
            var anyCvId = await GetScopedMetadataBaseQuery()
                .Select(m => m.Id)
                .FirstOrDefaultAsync();

            if (recruiterId <= 0 || anyCvId <= 0)
            {
                return new TestResultViewModel
                {
                    TestName = "Truy cập bằng token không hợp lệ",
                    ExpectedResult = "Hệ thống từ chối token giả hoặc token không hợp lệ.",
                    ActualResult = "FAIL: Thiếu dữ liệu để kiểm thử, cần đăng nhập recruiter/admin và có CV metadata.",
                    Passed = false
                };
            }

            var valid = await _secureCVService.ValidateAndConsumeTokenAsync("invalid_token.invalid_code", anyCvId, recruiterId);
            return new TestResultViewModel
            {
                TestName = "Truy cập bằng token không hợp lệ",
                ExpectedResult = "Token sai phải bị từ chối.",
                ActualResult = !valid
                    ? "PASS: Token không hợp lệ đã bị từ chối."
                    : "FAIL: Token giả lại được chấp nhận.",
                Passed = !valid
            };
        }

        private async Task<TestResultViewModel> TestRealCvIntegrityScan()
        {
            var scanItems = new List<CVIntegrityScanItemViewModel>();
            var metadataList = await GetRealMetadataQuery().ToListAsync();

            if (!metadataList.Any())
            {
                ViewBag.IntegrityScan = scanItems;
                return new TestResultViewModel
                {
                    TestName = "Quét toàn vẹn CV thật",
                    ExpectedResult = "Có CV thật trong phạm vi quyền truy cập để giải mã và đối chiếu SHA-256.",
                    ActualResult = "FAIL: Không có CV bảo mật thật để quét. Hãy cho ứng viên ứng tuyển bằng CV mới trước.",
                    Passed = false
                };
            }

            var candidateIds = metadataList
                .Select(m => int.TryParse(m.CandidateId, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var candidates = await _context.Users
                .Where(u => candidateIds.Contains(u.UserId))
                .Select(u => new { u.UserId, u.FullName, u.Email })
                .ToDictionaryAsync(u => u.UserId);

            var jobIds = metadataList.Select(m => m.JobId).Distinct().ToList();
            var jobs = await _context.Jobs
                .Where(j => jobIds.Contains(j.JobId))
                .Select(j => new { j.JobId, j.Title })
                .ToDictionaryAsync(j => j.JobId);

            foreach (var metadata in metadataList)
            {
                var candidateId = int.TryParse(metadata.CandidateId, out var parsedCandidateId)
                    ? parsedCandidateId
                    : 0;
                candidates.TryGetValue(candidateId, out var candidate);
                jobs.TryGetValue(metadata.JobId, out var job);

                var item = new CVIntegrityScanItemViewModel
                {
                    CVMetadataId = metadata.Id,
                    CandidateName = candidate?.FullName ?? $"Candidate #{metadata.CandidateId}",
                    CandidateEmail = candidate?.Email ?? string.Empty,
                    JobTitle = job?.Title ?? $"Job #{metadata.JobId}",
                    OriginalFileName = metadata.OriginalFileName,
                    StoredFileName = metadata.StoredFileName,
                    FilePath = metadata.FilePath,
                    UploadTime = metadata.UploadTime,
                    ExpireTime = metadata.ExpireTime
                };

                try
                {
                    if (!string.IsNullOrWhiteSpace(metadata.FilePath) && System.IO.File.Exists(metadata.FilePath))
                    {
                        var fileInfo = new FileInfo(metadata.FilePath);
                        item.ServerFileSize = fileInfo.Length;
                        item.ServerLastWriteTime = fileInfo.LastWriteTime;
                    }

                    var integrity = await _secureCVService.CheckCVIntegrityAsync(metadata.Id);
                    item.Passed = integrity.isValid;
                    item.Status = integrity.status;
                    item.Detail = integrity.detail;
                }
                catch (Exception ex)
                {
                    item.Passed = false;
                    item.Status = "DecryptFailed";
                    item.Detail = $"Không giải mã hoặc kiểm tra được file: {ex.Message}";
                }

                scanItems.Add(item);
            }

            ViewBag.IntegrityScan = scanItems;
            var failedItems = scanItems.Where(i => !i.Passed).ToList();

            return new TestResultViewModel
            {
                TestName = "Quét toàn vẹn CV thật",
                ExpectedResult = "Tất cả CV thật trong phạm vi quyền truy cập phải giải mã được và SHA-256 khớp metadata.",
                ActualResult = failedItems.Any()
                    ? $"FAIL: {failedItems.Count}/{scanItems.Count} CV có vấn đề toàn vẹn hoặc thiếu file."
                    : $"PASS: {scanItems.Count} CV thật đều hợp lệ, chưa phát hiện chỉnh sửa.",
                Passed = !failedItems.Any()
            };
        }

        private async Task<TestResultViewModel> TestRealCvTamperDetection()
        {
            var metadataList = await GetRealMetadataQuery().ToListAsync();

            if (!metadataList.Any())
            {
                return new TestResultViewModel
                {
                    TestName = "Kiểm tra CV thật bị sửa",
                    ExpectedResult = "Quét trực tiếp các file CV thật đang lưu trên server, không tạo file hoặc metadata kiểm thử.",
                    ActualResult = "FAIL: Không có CV bảo mật thật để kiểm tra.",
                    Passed = false
                };
            }

            var problemItems = new List<string>();
            foreach (var metadata in metadataList)
            {
                var integrity = await _secureCVService.CheckCVIntegrityAsync(metadata.Id);
                if (!integrity.isValid)
                {
                    problemItems.Add($"#{metadata.Id} {metadata.OriginalFileName}: {integrity.status}");
                }
            }

            return new TestResultViewModel
            {
                TestName = "Kiểm tra CV thật bị sửa",
                ExpectedResult = "CV thật bị sửa, hỏng hoặc thiếu file phải bị báo lỗi khi quét.",
                ActualResult = problemItems.Any()
                    ? $"FAIL: Phát hiện {problemItems.Count}/{metadataList.Count} CV thật có vấn đề: {string.Join("; ", problemItems.Take(3))}"
                    : $"PASS: Đã quét {metadataList.Count} CV thật, chưa phát hiện chỉnh sửa.",
                Passed = !problemItems.Any()
            };
        }

        private async Task<TestResultViewModel> TestExpiredCvDownload()
        {
            var recruiterId = GetCurrentRecruiterId();
            var baseMetadata = await GetUsableMetadataQuery().FirstOrDefaultAsync();

            if (recruiterId <= 0 || baseMetadata == null)
            {
                return new TestResultViewModel
                {
                    TestName = "Tải CV sau thời gian hết hạn",
                    ExpectedResult = "CV hết hạn phải bị từ chối tải.",
                    ActualResult = "FAIL: Thiếu dữ liệu kiểm thử.",
                    Passed = false
                };
            }

            var temp = new CVMetadata
            {
                CandidateId = baseMetadata.CandidateId,
                JobId = baseMetadata.JobId,
                OriginalFileName = baseMetadata.OriginalFileName,
                StoredFileName = baseMetadata.StoredFileName,
                FilePath = baseMetadata.FilePath,
                FileType = baseMetadata.FileType,
                FileSize = baseMetadata.FileSize,
                SHA256Hash = baseMetadata.SHA256Hash,
                EncryptionIV = baseMetadata.EncryptionIV,
                Nonce = baseMetadata.Nonce,
                UploadTime = DateTime.UtcNow.AddDays(-2),
                ExpireTime = DateTime.UtcNow.AddMinutes(-1),
                IsDeleted = false
            };

            _context.CVMetadata.Add(temp);
            await _context.SaveChangesAsync();

            try
            {
                await _secureCVService.DownloadSecureCVAsync(temp.Id, recruiterId, "127.0.0.1", "Security-Test");
                return new TestResultViewModel
                {
                    TestName = "Tải CV sau thời gian hết hạn",
                    ExpectedResult = "CV hết hạn phải bị từ chối.",
                    ActualResult = "FAIL: CV hết hạn nhưng vẫn tải được.",
                    Passed = false
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new TestResultViewModel
                {
                    TestName = "Tải CV sau thời gian hết hạn",
                    ExpectedResult = "CV hết hạn phải bị từ chối.",
                    ActualResult = "PASS: CV hết hạn đã bị từ chối tải.",
                    Passed = true
                };
            }
            finally
            {
                _context.CVMetadata.Remove(temp);
                await _context.SaveChangesAsync();
            }
        }

        private async Task<TestResultViewModel> TestUnauthorizedRecruiterDownload()
        {
            var cv = await GetUsableMetadataQuery().FirstOrDefaultAsync();
            if (cv == null)
            {
                return new TestResultViewModel
                {
                    TestName = "Nhà tuyển dụng không có quyền tải CV",
                    ExpectedResult = "Nhà tuyển dụng không thuộc công ty của job phải bị từ chối.",
                    ActualResult = "FAIL: Không có dữ liệu CV để kiểm thử.",
                    Passed = false
                };
            }

            var companyOwnerId = await _context.Jobs
                .Where(j => j.JobId == cv.JobId && j.CompanyId.HasValue)
                .Join(_context.Companies, j => j.CompanyId, c => (int?)c.CompanyId, (j, c) => c.EmployerId)
                .FirstOrDefaultAsync();

            var unauthorizedRecruiterId = await _context.Users
                .Where(u => u.Role == "Employer" && u.UserId != (companyOwnerId ?? -1))
                .Select(u => u.UserId)
                .FirstOrDefaultAsync();

            if (unauthorizedRecruiterId <= 0)
            {
                return new TestResultViewModel
                {
                    TestName = "Nhà tuyển dụng không có quyền tải CV",
                    ExpectedResult = "Nhà tuyển dụng không thuộc công ty của job phải bị từ chối.",
                    ActualResult = "FAIL: Không đủ dữ liệu employer khác công ty để kiểm thử.",
                    Passed = false
                };
            }

            try
            {
                await _secureCVService.DownloadSecureCVAsync(cv.Id, unauthorizedRecruiterId, "127.0.0.1", "Security-Test");
                return new TestResultViewModel
                {
                    TestName = "Nhà tuyển dụng không có quyền tải CV",
                    ExpectedResult = "Phải bị từ chối tải.",
                    ActualResult = "FAIL: Employer không có quyền nhưng vẫn tải được.",
                    Passed = false
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new TestResultViewModel
                {
                    TestName = "Nhà tuyển dụng không có quyền tải CV",
                    ExpectedResult = "Phải bị từ chối tải.",
                    ActualResult = "PASS: Employer không có quyền đã bị từ chối.",
                    Passed = true
                };
            }
        }

        private IQueryable<CVMetadata> GetUsableMetadataQuery()
        {
            return GetScopedMetadataBaseQuery()
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
                });
        }

        private IQueryable<CVMetadata> GetRealMetadataQuery()
        {
            return GetScopedMetadataBaseQuery()
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
                .OrderByDescending(m => m.UploadTime)
                .ThenByDescending(m => m.Id);
        }

        private IQueryable<CVMetadata> GetScopedMetadataBaseQuery()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role == "Admin")
            {
                return _context.CVMetadata.Where(m => !m.IsDeleted);
            }

            var recruiterId = GetCurrentRecruiterId();
            if (role != "Employer" || recruiterId <= 0)
            {
                return _context.CVMetadata.Where(m => false);
            }

            return from metadata in _context.CVMetadata
                   join job in _context.Jobs on metadata.JobId equals job.JobId
                   join company in _context.Companies on job.CompanyId equals (int?)company.CompanyId
                   where !metadata.IsDeleted && company.EmployerId == recruiterId
                   select metadata;
        }

        private IQueryable<CVActivityLog> GetScopedActivityLogQuery()
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role == "Admin")
            {
                return _context.CVActivityLogs;
            }

            var recruiterId = GetCurrentRecruiterId();
            if (role != "Employer" || recruiterId <= 0)
            {
                return _context.CVActivityLogs.Where(l => false);
            }

            return from log in _context.CVActivityLogs
                   join metadata in _context.CVMetadata on log.CVMetadataId equals metadata.Id
                   join job in _context.Jobs on metadata.JobId equals job.JobId
                   join company in _context.Companies on job.CompanyId equals (int?)company.CompanyId
                   where !metadata.IsDeleted && company.EmployerId == recruiterId
                   select log;
        }

        private bool IsEmployerOrAdmin()
        {
            var role = HttpContext.Session.GetString("UserRole");
            return role == "Employer" || role == "Admin";
        }

        private int GetCurrentRecruiterId()
        {
            var userId = HttpContext.Session.GetString("UserId");
            return int.TryParse(userId, out var id) ? id : 0;
        }
    }
}
