using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using BTL_2.Models;
using BTL_2.Services;

namespace BTL_2.Controllers
{
    [Authorize]
    public class CVController : Controller
    {
        private readonly ISecureCVService _secureCVService;
        private readonly IActivityLogService _activityLogService;
        private readonly ILogger<CVController> _logger;

        public CVController(
            ISecureCVService secureCVService,
            IActivityLogService activityLogService,
            ILogger<CVController> logger)
        {
            _secureCVService = secureCVService;
            _activityLogService = activityLogService;
            _logger = logger;
        }

        // GET: CV/Upload
        [HttpGet]
        [Authorize(Roles = "Candidate")]
        public IActionResult Upload()
        {
            return View();
        }

        // POST: CV/Upload
        [HttpPost]
        [Authorize(Roles = "Candidate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile cvFile, int jobId)
        {
            if (cvFile == null || cvFile.Length == 0)
            {
                ModelState.AddModelError("", "Vui lòng chọn file CV");
                return View();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            try
            {
                var metadata = await _secureCVService.UploadSecureCVAsync(cvFile, userId, jobId);

                TempData["Success"] = "Upload CV thành công!";
                TempData["CVId"] = metadata.Id;

                return RedirectToAction("UploadSuccess", new { id = metadata.Id });
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError("", ex.Message);
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed for user {UserId}", userId);
                ModelState.AddModelError("", "Có lỗi xảy ra khi upload CV");
                return View();
            }
        }

        // GET: CV/UploadSuccess/{id}
        [HttpGet]
        [Authorize(Roles = "Candidate")]
        public IActionResult UploadSuccess(int id)
        {
            ViewBag.CVId = id;
            return View();
        }

        // POST: CV/GenerateToken
        [HttpPost]
        [Authorize(Roles = "Employer,Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateDownloadToken(int cvMetadataId)
        {
            var recruiterIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(recruiterIdStr))
            {
                return Unauthorized();
            }

            // Chuyển đổi string sang int
            if (!int.TryParse(recruiterIdStr, out int recruiterId))
            {
                return BadRequest("Invalid User ID");
            }

            try
            {
                var token = await _secureCVService.GenerateDownloadTokenAsync(cvMetadataId, recruiterId);

                return Json(new
                {
                    success = true,
                    token = token,
                    downloadUrl = Url.Action("Download", "CV", new { token = token, cvId = cvMetadataId }, Request.Scheme),
                    expiresIn = 5,
                    policy = "Token dùng một lần, ràng buộc với tài khoản và phiên đăng nhập hiện tại."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate token for CV {CvId}", cvMetadataId);
                return Json(new { success = false, error = "Không thể tạo token tải xuống" });
            }
        }

        // GET: CV/Download
        [HttpGet]
        [Authorize(Roles = "Employer,Admin")]
        public async Task<IActionResult> Download(string token, int cvId)
        {
            var recruiterIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(recruiterIdStr))
            {
                return Unauthorized();
            }

            // Chuyển đổi string sang int
            if (!int.TryParse(recruiterIdStr, out int recruiterId))
            {
                return BadRequest("Invalid User ID");
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = Request.Headers["User-Agent"].ToString();

            try
            {
                // Xác thực token
                var isValidToken = await _secureCVService.ValidateAndConsumeTokenAsync(token, cvId, recruiterId);

                if (!isValidToken)
                {
                    await _activityLogService.LogCVAccessAsync(recruiterIdStr, cvId, ipAddress,
                        "Failed_InvalidToken", userAgent, "Token invalid or expired");
                    return StatusCode(403, "Token không hợp lệ hoặc đã hết hạn");
                }

                // Tải CV an toàn
                var result = await _secureCVService.DownloadSecureCVAsync(
                    cvId, recruiterId, ipAddress, userAgent);

                var fileData = result.fileData;
                var metadata = result.metadata;
                var isValid = result.isValid;

                if (!isValid)
                {
                    return StatusCode(403, "File CV đã bị can thiệp. Không thể tải xuống.");
                }

                // Trả file về cho client
                var contentType = GetContentType(metadata.FileType);
                return File(fileData, contentType, metadata.OriginalFileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized download attempt by {RecruiterId}", recruiterId);
                return StatusCode(403, ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "File not found for CV {CvId}", cvId);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download failed for CV {CvId} by {RecruiterId}", cvId, recruiterId);
                return StatusCode(500, "Có lỗi xảy ra khi tải CV");
            }
        }

        private string GetContentType(string fileExtension)
        {
            return fileExtension.ToLower() switch
            {
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => "application/octet-stream"
            };
        }
    }
}
