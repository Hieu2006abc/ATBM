using BTL_2.Models;
using BTL_2.Models.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using BTL_2.Services;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BTL_2.Controllers
{
    public class AdminController : Controller
    {
        private readonly string _connectionString;
        private readonly IWebHostEnvironment _environment;
        private readonly ISecureCVService _secureCVService;
        private readonly EmailService _emailService;

        public AdminController(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ISecureCVService secureCVService,
            EmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _environment = environment;
            _secureCVService = secureCVService;
            _emailService = emailService;
        }

        public IActionResult Index()
        {
            return RedirectToAction(nameof(Dashboard));
        }

        public IActionResult Dashboard()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            LoadDashboardStats();
            return View();
        }

        public IActionResult ManageUsers(int page = 1)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            const int pageSize = 50;
            var users = new List<User>();

            using (var conn = new SqlConnection(_connectionString))
            {
                var countCmd = new SqlCommand("SELECT COUNT(*) FROM Users", conn);
                conn.Open();
                var total = Convert.ToInt32(countCmd.ExecuteScalar());

                var cmd = new SqlCommand(@"
                    SELECT UserId, FullName, Email, Phone, Address, Role, CreatedDate, IsActive
                    FROM Users
                    ORDER BY UserId DESC
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);
                cmd.Parameters.AddWithValue("@Offset", Math.Max(0, page - 1) * pageSize);
                cmd.Parameters.AddWithValue("@PageSize", pageSize);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    users.Add(MapUser(reader));
                }

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            }

            return View(users);
        }

        public IActionResult CreateUser()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            LoadCompanyAssignmentData();
            return View(new User { IsActive = true, Role = "Candidate" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(User model, int[] selectedCompanyIds)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            selectedCompanyIds ??= Array.Empty<int>();
            model.FullName = model.FullName?.Trim();
            model.Email = model.Email?.Trim();

            ModelState.Remove("Password");
            ModelState.Remove("Applications");
            ModelState.Remove("Companies");

            if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError("", "Vui lòng nhập đầy đủ họ tên và email.");
            }

            if (!ModelState.IsValid)
            {
                LoadCompanyAssignmentData(selectedCompanyIds);
                return View(model);
            }

            var role = string.IsNullOrWhiteSpace(model.Role) ? "Candidate" : model.Role.Trim();
            model.Role = role;
            var temporaryPassword = GenerateTemporaryPassword();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Users (FullName, Email, Password, Phone, Address, Role, CreatedDate, IsActive, MustChangePassword)
                    OUTPUT INSERTED.UserId
                    VALUES (@FullName, @Email, @Password, @Phone, @Address, @Role, GETDATE(), @IsActive, 1)", conn, transaction);
                cmd.Parameters.AddWithValue("@FullName", model.FullName);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                cmd.Parameters.AddWithValue("@Password", HashPassword(temporaryPassword));
                cmd.Parameters.AddWithValue("@Phone", (object?)model.Phone ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Address", (object?)model.Address ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Role", role);
                cmd.Parameters.AddWithValue("@IsActive", model.IsActive == true);

                var userId = Convert.ToInt32(cmd.ExecuteScalar());
                AssignCompanies(conn, transaction, userId, role, selectedCompanyIds);
                LogActivity(conn, transaction, "Tạo user", $"Tạo tài khoản {model.FullName} ({model.Role})", model.FullName, model.Email, model.Role, userId, "Success");
                transaction.Commit();

                var emailSent = await SendTemporaryPasswordEmailAsync(model.Email, model.FullName, temporaryPassword);
                TempData["SuccessMessage"] = emailSent
                    ? $"Tạo tài khoản thành công. Mật khẩu tạm thời đã được gửi về email. Mật khẩu tạm thời: {temporaryPassword}"
                    : $"Tạo tài khoản thành công. Chưa gửi được email, mật khẩu tạm thời là: {temporaryPassword}";
                return RedirectToAction(nameof(ManageUsers));
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                ModelState.AddModelError("", "Không thể tạo tài khoản: " + ex.Message);
                LoadCompanyAssignmentData(selectedCompanyIds);
                return View(model);
            }
        }

        public IActionResult CreateEmployer()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            LoadCompanyAssignmentData();
            return View(new EmployerRegisterViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEmployer(EmployerRegisterViewModel model, int[] selectedCompanyIds)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            selectedCompanyIds ??= Array.Empty<int>();
            model.FullName = model.FullName?.Trim();
            model.Email = model.Email?.Trim();

            ModelState.Remove("Password");
            ModelState.Remove("ConfirmPassword");

            if (!ModelState.IsValid)
            {
                LoadCompanyAssignmentData(selectedCompanyIds);
                return View(model);
            }

            var temporaryPassword = GenerateTemporaryPassword();

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                var cmd = new SqlCommand(@"
                    INSERT INTO Users (FullName, Email, Password, Phone, Address, Role, CreatedDate, IsActive, MustChangePassword)
                    OUTPUT INSERTED.UserId
                    VALUES (@FullName, @Email, @Password, @Phone, @Address, 'Employer', GETDATE(), 1, 1)", conn, transaction);
                cmd.Parameters.AddWithValue("@FullName", model.FullName);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                cmd.Parameters.AddWithValue("@Password", HashPassword(temporaryPassword));
                cmd.Parameters.AddWithValue("@Phone", (object?)model.Phone ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Address", (object?)model.Address ?? DBNull.Value);

                var userId = Convert.ToInt32(cmd.ExecuteScalar());
                AssignCompanies(conn, transaction, userId, "Employer", selectedCompanyIds);
                LogActivity(conn, transaction, "Tạo nhà tuyển dụng", $"Tạo tài khoản nhà tuyển dụng {model.FullName}", model.FullName, model.Email, "Employer", userId, "Success");
                transaction.Commit();

                var emailSent = await SendTemporaryPasswordEmailAsync(model.Email, model.FullName, temporaryPassword);
                TempData["SuccessMessage"] = emailSent
                    ? $"Tạo tài khoản nhà tuyển dụng thành công. Mật khẩu tạm thời đã được gửi về email. Mật khẩu tạm thời: {temporaryPassword}"
                    : $"Tạo tài khoản nhà tuyển dụng thành công. Chưa gửi được email, mật khẩu tạm thời là: {temporaryPassword}";
                return RedirectToAction(nameof(ManageUsers));
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                ModelState.AddModelError("", "Không thể tạo nhà tuyển dụng: " + ex.Message);
                LoadCompanyAssignmentData(selectedCompanyIds);
                return View(model);
            }
        }

        public IActionResult EditUser(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT UserId, FullName, Email, Phone, Address, Role, CreatedDate, IsActive
                FROM Users
                WHERE UserId = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            conn.Open();

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return NotFound();

            var user = MapUser(reader);
            reader.Close();
            LoadCompanyAssignmentData(GetAssignedCompanyIds(conn, id));
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditUser(User model, int[] selectedCompanyIds)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            model.Role = string.IsNullOrWhiteSpace(model.Role) ? "Candidate" : model.Role.Trim();
            selectedCompanyIds ??= Array.Empty<int>();

            ModelState.Remove("Password");
            ModelState.Remove("Applications");
            ModelState.Remove("Companies");

            if (string.IsNullOrWhiteSpace(model.FullName))
            {
                ModelState.AddModelError("FullName", "Vui lòng nhập họ và tên.");
            }

            if (string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError("Email", "Vui lòng nhập email.");
            }

            var validRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Admin", "Employer", "Candidate" };
            if (!validRoles.Contains(model.Role))
            {
                ModelState.AddModelError("Role", "Vai trò không hợp lệ.");
            }

            if (string.Equals(model.Role, "Employer", StringComparison.OrdinalIgnoreCase) && selectedCompanyIds.Length == 0)
            {
                ModelState.AddModelError("", "Vui lòng chọn ít nhất một công ty để cấp quyền cho nhà tuyển dụng.");
            }

            if (!ModelState.IsValid)
            {
                LoadCompanyAssignmentData(selectedCompanyIds);
                TempData["ErrorMessage"] = "Không thể lưu thay đổi. Vui lòng kiểm tra lại thông tin và quyền công ty.";
                return View(model);
            }

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                var cmd = new SqlCommand(@"
                    UPDATE Users
                    SET FullName = @FullName,
                        Email = @Email,
                        Phone = @Phone,
                        Address = @Address,
                        Role = @Role,
                        IsActive = @IsActive
                    WHERE UserId = @UserId", conn, transaction);
                cmd.Parameters.AddWithValue("@UserId", model.UserId);
                cmd.Parameters.AddWithValue("@FullName", model.FullName);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                cmd.Parameters.AddWithValue("@Phone", (object?)model.Phone ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Address", (object?)model.Address ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Role", model.Role);
                cmd.Parameters.AddWithValue("@IsActive", model.IsActive == true);
                cmd.ExecuteNonQuery();

                AssignCompanies(conn, transaction, model.UserId, model.Role, selectedCompanyIds);
                LogActivity(conn, transaction, "Cập nhật user", $"Cập nhật tài khoản {model.FullName} và quyền công ty", model.FullName, model.Email, model.Role, model.UserId, "Success");
                transaction.Commit();

                var assignedText = string.Equals(model.Role, "Employer", StringComparison.OrdinalIgnoreCase)
                    ? $" Đã cấp quyền quản lý {selectedCompanyIds.Length} công ty."
                    : "";
                TempData["SuccessMessage"] = $"Lưu thành công tài khoản {model.FullName} với vai trò {GetRoleDisplayName(model.Role)}.{assignedText}";
                return RedirectToAction(nameof(ManageUsers));
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["ErrorMessage"] = "Không thể cập nhật người dùng: " + ex.Message;
                ModelState.AddModelError("", "Không thể cập nhật người dùng: " + ex.Message);
                LoadCompanyAssignmentData(selectedCompanyIds);
                return View(model);
            }
        }

        public IActionResult ManageJobs()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            var jobs = new List<Job>();
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT j.JobId, j.Title, j.CompanyId, j.SalaryMin, j.SalaryMax, j.Location, j.JobType,
                       j.CreatedDate, j.IsActive, j.Views,
                       c.CompanyName, c.Logo,
                       COUNT(a.ApplicationId) AS ApplicationCount
                FROM Jobs j
                LEFT JOIN Companies c ON j.CompanyId = c.CompanyId
                LEFT JOIN Applications a ON j.JobId = a.JobId
                GROUP BY j.JobId, j.Title, j.CompanyId, j.SalaryMin, j.SalaryMax, j.Location, j.JobType,
                         j.CreatedDate, j.IsActive, j.Views, c.CompanyName, c.Logo
                ORDER BY j.CreatedDate DESC, j.JobId DESC", conn);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var count = reader["ApplicationCount"] != DBNull.Value ? Convert.ToInt32(reader["ApplicationCount"]) : 0;
                jobs.Add(new Job
                {
                    JobId = Convert.ToInt32(reader["JobId"]),
                    Title = reader["Title"]?.ToString(),
                    CompanyId = reader["CompanyId"] != DBNull.Value ? Convert.ToInt32(reader["CompanyId"]) : (int?)null,
                    SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                    SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                    Location = reader["Location"]?.ToString(),
                    JobType = reader["JobType"]?.ToString(),
                    CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : (DateTime?)null,
                    IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"]),
                    Views = reader["Views"] != DBNull.Value ? Convert.ToInt32(reader["Views"]) : 0,
                    Company = new Company
                    {
                        CompanyName = reader["CompanyName"]?.ToString(),
                        Logo = reader["Logo"]?.ToString()
                    },
                    Applications = BuildApplicationPlaceholders(count)
                });
            }

            return View(jobs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleStatus(int userId)
        {
            if (!IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền thực hiện thao tác này." });
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var transaction = conn.BeginTransaction();

                var cmd = new SqlCommand(@"
                    UPDATE Users
                    SET IsActive = CASE WHEN ISNULL(IsActive, 0) = 1 THEN 0 ELSE 1 END
                    OUTPUT inserted.IsActive
                    WHERE UserId = @UserId", conn, transaction);
                cmd.Parameters.AddWithValue("@UserId", userId);
                var result = cmd.ExecuteScalar();

                if (result == null || result == DBNull.Value)
                {
                    transaction.Rollback();
                    return Json(new { success = false, message = "Không tìm thấy tài khoản." });
                }

                var isActive = Convert.ToBoolean(result);

                if (isActive)
                {
                    var resetCmd = new SqlCommand("DELETE FROM PasswordResets WHERE Email = (SELECT Email FROM Users WHERE UserId = @UserId)", conn, transaction);
                    resetCmd.Parameters.AddWithValue("@UserId", userId);
                    resetCmd.ExecuteNonQuery();
                }

                LogActivity(conn, transaction, isActive ? "Mở khóa user" : "Khóa user", $"Thay đổi trạng thái tài khoản #{userId}", null, null, null, userId, "Success");
                transaction.Commit();

                return Json(new { success = true, isActive, message = isActive ? "Đã mở khóa tài khoản." : "Đã khóa tài khoản." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Không thể cập nhật trạng thái: " + ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            var currentUserId = HttpContext.Session.GetString("UserId");
            if (int.TryParse(currentUserId, out var adminId) && adminId == id)
            {
                TempData["ErrorMessage"] = "Bạn không thể xóa chính tài khoản đang đăng nhập.";
                return RedirectToAction(nameof(ManageUsers));
            }

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                var user = GetUserForDelete(conn, transaction, id);
                if (user == null)
                {
                    transaction.Rollback();
                    TempData["ErrorMessage"] = "Không tìm thấy tài khoản cần xóa.";
                    return RedirectToAction(nameof(ManageUsers));
                }

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('PasswordResets', 'U') IS NOT NULL
                        DELETE FROM PasswordResets WHERE Email = @Email", cmd =>
                {
                    cmd.Parameters.AddWithValue("@Email", user.Email ?? string.Empty);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('UserNotifications', 'U') IS NOT NULL
                        DELETE FROM UserNotifications WHERE UserId = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('SavedJobs', 'U') IS NOT NULL
                        DELETE FROM SavedJobs WHERE UserId = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('Follows', 'U') IS NOT NULL
                        DELETE FROM Follows WHERE UserId = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('Messages', 'U') IS NOT NULL
                    BEGIN
                        IF OBJECT_ID('Conversations', 'U') IS NOT NULL
                        BEGIN
                            DELETE m
                            FROM Messages m
                            INNER JOIN Conversations c ON c.ConversationId = m.ConversationId
                            WHERE c.User1Id = @UserId OR c.User2Id = @UserId;
                        END

                        IF COL_LENGTH('Messages', 'SenderId') IS NOT NULL
                            DELETE FROM Messages WHERE SenderId = @UserId;

                        IF COL_LENGTH('Messages', 'ReceiverId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM Messages WHERE ReceiverId = @DynamicUserId',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;
                    END", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('Conversations', 'U') IS NOT NULL
                        DELETE FROM Conversations WHERE User1Id = @UserId OR User2Id = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('Companies', 'U') IS NOT NULL
                        UPDATE Companies SET EmployerId = NULL WHERE EmployerId = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('CVDownloadLogs', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('CVDownloadLogs', 'RecruiterId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVDownloadLogs WHERE CONVERT(NVARCHAR(450), RecruiterId) = CONVERT(NVARCHAR(450), @DynamicUserId)',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('CVDownloadLogs', 'ApplicationId') IS NOT NULL
                           AND OBJECT_ID('Applications', 'U') IS NOT NULL
                           AND COL_LENGTH('Applications', 'ApplicationId') IS NOT NULL
                           AND COL_LENGTH('Applications', 'UserId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVDownloadLogs
                                  WHERE ApplicationId IN (
                                      SELECT ApplicationId
                                      FROM Applications
                                      WHERE UserId = @DynamicUserId
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('CVDownloadLogs', 'CVMetadataId') IS NOT NULL
                           AND OBJECT_ID('Applications', 'U') IS NOT NULL
                           AND COL_LENGTH('Applications', 'CVMetadataId') IS NOT NULL
                           AND COL_LENGTH('Applications', 'UserId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVDownloadLogs
                                  WHERE CVMetadataId IN (
                                      SELECT CVMetadataId
                                      FROM Applications
                                      WHERE UserId = @DynamicUserId AND CVMetadataId IS NOT NULL
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('CVDownloadLogs', 'CVMetadataId') IS NOT NULL
                           AND OBJECT_ID('CVMetadata', 'U') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'Id') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'CandidateId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVDownloadLogs
                                  WHERE CVMetadataId IN (
                                      SELECT Id
                                      FROM CVMetadata
                                      WHERE CandidateId = CONVERT(NVARCHAR(450), @DynamicUserId)
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;
                    END", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('CVActivityLogs', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('CVActivityLogs', 'RecruiterId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVActivityLogs WHERE CONVERT(NVARCHAR(450), RecruiterId) = CONVERT(NVARCHAR(450), @DynamicUserId)',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('CVActivityLogs', 'CVMetadataId') IS NOT NULL
                           AND OBJECT_ID('Applications', 'U') IS NOT NULL
                           AND COL_LENGTH('Applications', 'CVMetadataId') IS NOT NULL
                           AND COL_LENGTH('Applications', 'UserId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVActivityLogs
                                  WHERE CVMetadataId IN (
                                      SELECT CVMetadataId
                                      FROM Applications
                                      WHERE UserId = @DynamicUserId AND CVMetadataId IS NOT NULL
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('CVActivityLogs', 'CVMetadataId') IS NOT NULL
                           AND OBJECT_ID('CVMetadata', 'U') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'Id') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'CandidateId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM CVActivityLogs
                                  WHERE CVMetadataId IN (
                                      SELECT Id
                                      FROM CVMetadata
                                      WHERE CandidateId = CONVERT(NVARCHAR(450), @DynamicUserId)
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;
                    END", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('DownloadTokens', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('DownloadTokens', 'RecruiterId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM DownloadTokens WHERE CONVERT(NVARCHAR(450), RecruiterId) = CONVERT(NVARCHAR(450), @DynamicUserId)',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('DownloadTokens', 'CVMetadataId') IS NOT NULL
                           AND OBJECT_ID('Applications', 'U') IS NOT NULL
                           AND COL_LENGTH('Applications', 'CVMetadataId') IS NOT NULL
                           AND COL_LENGTH('Applications', 'UserId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM DownloadTokens
                                  WHERE CVMetadataId IN (
                                      SELECT CVMetadataId
                                      FROM Applications
                                      WHERE UserId = @DynamicUserId AND CVMetadataId IS NOT NULL
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('DownloadTokens', 'CVMetadataId') IS NOT NULL
                           AND OBJECT_ID('CVMetadata', 'U') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'Id') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'CandidateId') IS NOT NULL
                            EXEC sp_executesql
                                N'DELETE FROM DownloadTokens
                                  WHERE CVMetadataId IN (
                                      SELECT Id
                                      FROM CVMetadata
                                      WHERE CandidateId = CONVERT(NVARCHAR(450), @DynamicUserId)
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;
                    END", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('CVMetadata', 'U') IS NOT NULL
                    BEGIN
                        IF COL_LENGTH('CVMetadata', 'IsDeleted') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'CandidateId') IS NOT NULL
                            EXEC sp_executesql
                                N'UPDATE CVMetadata
                                  SET IsDeleted = 1
                                  WHERE CandidateId = CONVERT(NVARCHAR(450), @DynamicUserId)',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;

                        IF COL_LENGTH('CVMetadata', 'IsDeleted') IS NOT NULL
                           AND COL_LENGTH('CVMetadata', 'Id') IS NOT NULL
                           AND OBJECT_ID('Applications', 'U') IS NOT NULL
                           AND COL_LENGTH('Applications', 'CVMetadataId') IS NOT NULL
                           AND COL_LENGTH('Applications', 'UserId') IS NOT NULL
                            EXEC sp_executesql
                                N'UPDATE CVMetadata
                                  SET IsDeleted = 1
                                  WHERE Id IN (
                                      SELECT CVMetadataId
                                      FROM Applications
                                      WHERE UserId = @DynamicUserId AND CVMetadataId IS NOT NULL
                                  )',
                                N'@DynamicUserId INT',
                                @DynamicUserId = @UserId;
                    END", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, @"
                    IF OBJECT_ID('Applications', 'U') IS NOT NULL
                        DELETE FROM Applications WHERE UserId = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                ExecuteNonQuery(conn, transaction, "DELETE FROM Users WHERE UserId = @UserId", cmd =>
                {
                    cmd.Parameters.AddWithValue("@UserId", id);
                });

                LogActivity(conn, transaction, "Xóa user", $"Xóa tài khoản {user.FullName} ({user.Role})", user.FullName, user.Email, user.Role, id, "Success");
                transaction.Commit();

                TempData["SuccessMessage"] = $"Đã xóa tài khoản {user.FullName}.";
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                TempData["ErrorMessage"] = "Không thể xóa tài khoản: " + ex.Message;
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        public IActionResult CreateJob()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            return View(new CreateJobViewModel { Companies = GetCompanies(), Deadline = DateTime.Today.AddDays(30), IsFeatured = false });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateJob(CreateJobViewModel model)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            model.Companies = GetCompanies();
            RemoveOptionalJobModelState();
            if (!ModelState.IsValid) return View(model);

            model.NumberOfRecruits ??= 1;
            model.Deadline ??= DateTime.Today.AddDays(30);

            var imagePath = await SaveUploadAsync(model.JobImageFile, "jobs");

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                INSERT INTO Jobs (Title, CompanyId, SalaryMin, SalaryMax, Location, JobType, Description, Requirement, Benefit, Deadline, CreatedDate, IsActive, Views)
                OUTPUT INSERTED.JobId
                VALUES (@Title, @CompanyId, @SalaryMin, @SalaryMax, @Location, @JobType, @Description, @Requirement, @Benefit, @Deadline, GETDATE(), 1, 0)", conn);
            AddJobParameters(cmd, model, imagePath);
            conn.Open();
            var jobId = Convert.ToInt32(cmd.ExecuteScalar());
            LogActivity(conn, null, "Đăng tin", $"Đăng tin tuyển dụng: {model.Title}", model.Title, null, "Job", jobId, "Success");

            TempData["SuccessMessage"] = "Tạo tin tuyển dụng thành công.";
            return RedirectToAction(nameof(ManageJobs));
        }

        public IActionResult EditJob(int id)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT JobId, Title, CompanyId, SalaryMin, SalaryMax, Location, JobType, Description, Requirement, Benefit, Deadline
                FROM Jobs WHERE JobId = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return NotFound();

            var model = new EditJobViewModel
            {
                JobId = Convert.ToInt32(reader["JobId"]),
                Title = reader["Title"]?.ToString(),
                CompanyId = reader["CompanyId"] != DBNull.Value ? Convert.ToInt32(reader["CompanyId"]) : (int?)null,
                SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                Location = reader["Location"]?.ToString(),
                JobType = reader["JobType"]?.ToString(),
                Description = reader["Description"]?.ToString(),
                Requirement = reader["Requirement"]?.ToString(),
                Benefit = reader["Benefit"]?.ToString(),
                Deadline = reader["Deadline"] != DBNull.Value ? Convert.ToDateTime(reader["Deadline"]) : (DateTime?)null,
                Companies = GetCompanies()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditJob(EditJobViewModel model)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            model.Companies = GetCompanies();
            RemoveOptionalJobModelState();
            if (!ModelState.IsValid) return View(model);

            await SaveUploadAsync(model.JobImageFile, "jobs");

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                UPDATE Jobs
                SET Title = @Title,
                    CompanyId = @CompanyId,
                    SalaryMin = @SalaryMin,
                    SalaryMax = @SalaryMax,
                    Location = @Location,
                    JobType = @JobType,
                    Description = @Description,
                    Requirement = @Requirement,
                    Benefit = @Benefit,
                    Deadline = @Deadline
                WHERE JobId = @JobId", conn);
            cmd.Parameters.AddWithValue("@JobId", model.JobId);
            AddJobParameters(cmd, model, null);
            conn.Open();
            cmd.ExecuteNonQuery();
            LogActivity(conn, null, "Cập nhật tin", $"Cập nhật tin tuyển dụng: {model.Title}", model.Title, null, "Job", model.JobId, "Success");

            TempData["SuccessMessage"] = "Cập nhật tin tuyển dụng thành công.";
            return RedirectToAction(nameof(ManageJobs));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleJobStatus(int jobId)
        {
            if (!IsAdmin()) return Json(new { success = false, message = "Không có quyền thực hiện" });

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("UPDATE Jobs SET IsActive = CASE WHEN ISNULL(IsActive, 0) = 1 THEN 0 ELSE 1 END WHERE JobId = @JobId", conn);
            cmd.Parameters.AddWithValue("@JobId", jobId);
            conn.Open();
            var success = cmd.ExecuteNonQuery() > 0;
            if (success) LogActivity(conn, null, "Đổi trạng thái tin", $"Bật/tắt trạng thái tin #{jobId}", null, null, "Job", jobId, "Success");
            return Json(new { success });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteJob(int jobId)
        {
            if (!IsAdmin()) return Json(new { success = false, message = "Không có quyền thực hiện" });

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("UPDATE Jobs SET IsActive = 0 WHERE JobId = @JobId", conn);
            cmd.Parameters.AddWithValue("@JobId", jobId);
            conn.Open();
            var success = cmd.ExecuteNonQuery() > 0;
            if (success) LogActivity(conn, null, "Ẩn tin", $"Ẩn tin tuyển dụng #{jobId}", null, null, "Job", jobId, "Success");
            return Json(new { success });
        }

        public IActionResult ManageAnnouncements()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            var announcements = new List<Announcement>();
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT Id, Title, Content, Type, TargetRole, CreatedAt, ExpiryDate, IsActive, CreatedBy, ImageUrl, LinkUrl
                FROM Announcements
                ORDER BY CreatedAt DESC, Id DESC", conn);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                announcements.Add(MapAnnouncement(reader));
            }

            return View(announcements);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddAnnouncement(AnnouncementViewModel model)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");
            if (string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.Content))
            {
                TempData["ErrorMessage"] = "Vui lòng nhập tiêu đề và nội dung thông báo.";
                return RedirectToAction(nameof(ManageAnnouncements));
            }

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                INSERT INTO Announcements (Title, Content, Type, TargetRole, CreatedAt, ExpiryDate, IsActive, CreatedBy, LinkUrl)
                VALUES (@Title, @Content, @Type, @TargetRole, GETDATE(), @ExpiryDate, 1, @CreatedBy, @LinkUrl)", conn);
            AddAnnouncementParameters(cmd, model, includeCreatedBy: true);
            conn.Open();
            cmd.ExecuteNonQuery();
            LogActivity(conn, null, "Thêm thông báo", $"Thêm thông báo: {model.Title}", model.Title, null, "Announcement", null, "Success");

            TempData["SuccessMessage"] = "Thêm thông báo thành công.";
            return RedirectToAction(nameof(ManageAnnouncements));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateAnnouncement(AnnouncementViewModel model)
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                UPDATE Announcements
                SET Title = @Title,
                    Content = @Content,
                    Type = @Type,
                    TargetRole = @TargetRole,
                    ExpiryDate = @ExpiryDate,
                    LinkUrl = @LinkUrl
                WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", model.Id);
            AddAnnouncementParameters(cmd, model, includeCreatedBy: false);
            conn.Open();
            cmd.ExecuteNonQuery();
            LogActivity(conn, null, "Cập nhật thông báo", $"Cập nhật thông báo: {model.Title}", model.Title, null, "Announcement", model.Id, "Success");

            TempData["SuccessMessage"] = "Cập nhật thông báo thành công.";
            return RedirectToAction(nameof(ManageAnnouncements));
        }

        public IActionResult GetAnnouncement(int id)
        {
            if (!IsAdmin()) return Unauthorized();

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT Id, Title, Content, Type, TargetRole, CreatedAt, ExpiryDate, IsActive, CreatedBy, ImageUrl, LinkUrl
                FROM Announcements
                WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return NotFound();

            var ann = MapAnnouncement(reader);
            return Json(new
            {
                id = ann.Id,
                title = ann.Title,
                content = ann.Content,
                type = ann.Type,
                targetRole = ann.TargetRole,
                linkUrl = ann.LinkUrl,
                expiryDate = ann.ExpiryDate?.ToString("yyyy-MM-dd")
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleAnnouncement(int id)
        {
            if (!IsAdmin()) return Json(new { success = false, message = "Không có quyền thực hiện" });

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("UPDATE Announcements SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            conn.Open();
            var success = cmd.ExecuteNonQuery() > 0;
            if (success) LogActivity(conn, null, "Đổi trạng thái thông báo", $"Bật/tắt thông báo #{id}", null, null, "Announcement", id, "Success");
            return Json(new { success });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteAnnouncement(int id)
        {
            if (!IsAdmin()) return Json(new { success = false, message = "Không có quyền thực hiện" });

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("DELETE FROM Announcements WHERE Id = @Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            conn.Open();
            var success = cmd.ExecuteNonQuery() > 0;
            if (success) LogActivity(conn, null, "Xóa thông báo", $"Xóa thông báo #{id}", null, null, "Announcement", id, "Success");
            return Json(new { success });
        }

        public async Task<IActionResult> ManageApplications(string status = "All")
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            var applications = new List<Application>();

            using (var conn = new SqlConnection(_connectionString))
            {
                var query = @"
                    SELECT a.ApplicationId, a.JobId, a.UserId, a.CVFile, a.OriginalFileName, a.CoverLetter,
                           a.Status, a.ApplyDate, a.CVMetadataId,
                           u.FullName, u.Email,
                           j.Title, c.CompanyName, c.Logo
                    FROM Applications a
                    LEFT JOIN Users u ON a.UserId = u.UserId
                    LEFT JOIN Jobs j ON a.JobId = j.JobId
                    LEFT JOIN Companies c ON j.CompanyId = c.CompanyId";

                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    query += " WHERE a.Status = @Status";
                }

                query += " ORDER BY a.ApplyDate DESC";

                var cmd = new SqlCommand(query, conn);
                if (!string.IsNullOrWhiteSpace(status) && status != "All")
                {
                    cmd.Parameters.AddWithValue("@Status", status);
                }

                conn.Open();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    applications.Add(new Application
                    {
                        ApplicationId = Convert.ToInt32(reader["ApplicationId"]),
                        JobId = reader["JobId"] != DBNull.Value ? Convert.ToInt32(reader["JobId"]) : (int?)null,
                        UserId = reader["UserId"] != DBNull.Value ? Convert.ToInt32(reader["UserId"]) : (int?)null,
                        CVFile = reader["CVFile"]?.ToString(),
                        OriginalFileName = reader["OriginalFileName"]?.ToString(),
                        CoverLetter = reader["CoverLetter"]?.ToString(),
                        Status = reader["Status"]?.ToString(),
                        ApplyDate = reader["ApplyDate"] != DBNull.Value ? Convert.ToDateTime(reader["ApplyDate"]) : (DateTime?)null,
                        CVMetadataId = reader["CVMetadataId"] != DBNull.Value ? Convert.ToInt32(reader["CVMetadataId"]) : (int?)null,
                        User = new User
                        {
                            FullName = reader["FullName"]?.ToString(),
                            Email = reader["Email"]?.ToString()
                        },
                        Job = new Job
                        {
                            Title = reader["Title"]?.ToString(),
                            Company = new Company
                            {
                                CompanyName = reader["CompanyName"]?.ToString(),
                                Logo = reader["Logo"]?.ToString()
                            }
                        }
                    });
                }
            }

            await AttachCVIntegrityStatusAsync(applications);

            ViewBag.CurrentStatus = string.IsNullOrWhiteSpace(status) ? "All" : status;
            return View(applications);
        }

        public IActionResult CVAccessLogs()
        {
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            var logs = new List<CVAccessLogViewModel>();
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT TOP 200 l.Id, l.RecruiterId, l.CVMetadataId, l.AccessTime, l.IPAddress, l.Status, l.ErrorMessage, l.UserAgent,
                       u.FullName AS RecruiterName, u.Email AS RecruiterEmail,
                       m.OriginalFileName, m.CandidateId, m.JobId,
                       j.Title AS JobTitle
                FROM CVActivityLogs l
                LEFT JOIN Users u ON TRY_CONVERT(INT, l.RecruiterId) = u.UserId
                LEFT JOIN CVMetadata m ON l.CVMetadataId = m.Id
                LEFT JOIN Jobs j ON m.JobId = j.JobId
                ORDER BY l.AccessTime DESC, l.Id DESC", conn);

            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(new CVAccessLogViewModel
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    RecruiterName = reader["RecruiterName"]?.ToString() ?? reader["RecruiterId"]?.ToString() ?? "",
                    RecruiterEmail = reader["RecruiterEmail"]?.ToString() ?? "",
                    CVMetadataId = reader["CVMetadataId"] != DBNull.Value ? Convert.ToInt32(reader["CVMetadataId"]) : 0,
                    OriginalFileName = reader["OriginalFileName"]?.ToString() ?? "",
                    CandidateId = reader["CandidateId"]?.ToString() ?? "",
                    JobId = reader["JobId"] != DBNull.Value ? Convert.ToInt32(reader["JobId"]) : 0,
                    JobTitle = reader["JobTitle"]?.ToString() ?? "",
                    AccessTime = reader["AccessTime"] != DBNull.Value ? Convert.ToDateTime(reader["AccessTime"]) : DateTime.Now,
                    IPAddress = reader["IPAddress"]?.ToString() ?? "",
                    Status = reader["Status"]?.ToString() ?? "",
                    ErrorMessage = reader["ErrorMessage"]?.ToString(),
                    UserAgent = reader["UserAgent"]?.ToString()
                });
            }

            return View(logs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateApplicationStatus(int id, string status)
        {
            if (!IsAdmin()) return Json(new { success = false, message = "Không có quyền thực hiện" });
            if (status != "Pending" && status != "Approved" && status != "Rejected" && status != "Withdrawn")
            {
                return Json(new { success = false, message = "Trạng thái không hợp lệ" });
            }

            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand("UPDATE Applications SET Status = @Status WHERE ApplicationId = @Id", conn);
                cmd.Parameters.AddWithValue("@Status", status);
                cmd.Parameters.AddWithValue("@Id", id);
                conn.Open();
                var success = cmd.ExecuteNonQuery() > 0;
                if (success) LogActivity(conn, null, "Duyệt CV", $"Cập nhật hồ sơ ứng tuyển #{id} sang {status}", null, null, "Application", id, "Success");
                return Json(new { success });
            }
        }

        private bool IsAdmin()
        {
            return HttpContext.Session.GetString("UserRole") == "Admin";
        }

        private async Task AttachCVIntegrityStatusAsync(IEnumerable<Application> applications)
        {
            var integrityByCvId = new Dictionary<int, (bool isValid, string status, string detail)>();

            foreach (var app in applications.Where(a => a.CVMetadataId.HasValue))
            {
                var cvId = app.CVMetadataId!.Value;
                if (!integrityByCvId.TryGetValue(cvId, out var integrity))
                {
                    try
                    {
                        integrity = await _secureCVService.CheckCVIntegrityAsync(cvId);
                    }
                    catch (Exception ex)
                    {
                        integrity = (false, "DecryptFailed", $"Không giải mã hoặc kiểm tra được file: {ex.Message}");
                    }

                    integrityByCvId[cvId] = integrity;
                }

                app.CVIntegrityStatus = integrity.status;
                app.CVIntegrityDetail = integrity.detail;
                app.CanDownloadSecureCV = integrity.isValid &&
                    string.Equals(integrity.status, "Valid", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string GetRoleDisplayName(string role)
        {
            return role switch
            {
                "Admin" => "Quản trị viên",
                "Employer" => "Nhà tuyển dụng",
                "Candidate" => "Ứng viên",
                _ => role
            };
        }

        private void LoadDashboardStats()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            ViewBag.TotalUsers = Scalar(conn, "SELECT COUNT(*) FROM Users");
            ViewBag.TotalJobs = Scalar(conn, "SELECT COUNT(*) FROM Jobs");
            ViewBag.TotalApplications = Scalar(conn, "SELECT COUNT(*) FROM Applications");
            ViewBag.TotalCompanies = Scalar(conn, "SELECT COUNT(*) FROM Companies");
            ViewBag.PendingApplications = Scalar(conn, "SELECT COUNT(*) FROM Applications WHERE Status = 'Pending'");
            ViewBag.TotalEmployers = Scalar(conn, "SELECT COUNT(*) FROM Users WHERE Role = 'Employer'");
            ViewBag.TotalCandidates = Scalar(conn, "SELECT COUNT(*) FROM Users WHERE Role = 'Candidate'");
            ViewBag.ActiveJobs = Scalar(conn, "SELECT COUNT(*) FROM Jobs WHERE ISNULL(IsActive, 0) = 1");
            ViewBag.RecentActivities = LoadRecentActivities(conn);
        }

        private void LoadCompanyAssignmentData(IEnumerable<int> selectedCompanyIds = null)
        {
            ViewBag.Companies = GetCompanies();
            ViewBag.SelectedCompanyIds = selectedCompanyIds != null ? new HashSet<int>(selectedCompanyIds) : new HashSet<int>();
            ViewBag.Roles = new List<string> { "Candidate", "Employer", "Admin" };
        }

        private void RemoveOptionalJobModelState()
        {
            ModelState.Remove("Skills");
            ModelState.Remove("Experience");
            ModelState.Remove("Education");
            ModelState.Remove("NumberOfRecruits");
            ModelState.Remove("Deadline");
            ModelState.Remove("JobImageFile");
            ModelState.Remove("JobImage");
            ModelState.Remove("VideoUrl");
            ModelState.Remove("Tags");
            ModelState.Remove("Companies");
        }

        private List<Company> GetCompanies()
        {
            var companies = new List<Company>();
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("SELECT CompanyId, CompanyName, EmployerId FROM Companies ORDER BY CompanyName", conn);
            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                companies.Add(new Company
                {
                    CompanyId = Convert.ToInt32(reader["CompanyId"]),
                    CompanyName = reader["CompanyName"]?.ToString(),
                    EmployerId = reader["EmployerId"] != DBNull.Value ? Convert.ToInt32(reader["EmployerId"]) : (int?)null
                });
            }
            return companies;
        }

        private List<int> GetAssignedCompanyIds(SqlConnection conn, int userId)
        {
            var ids = new List<int>();
            var cmd = new SqlCommand("SELECT CompanyId FROM Companies WHERE EmployerId = @EmployerId", conn);
            cmd.Parameters.AddWithValue("@EmployerId", userId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(Convert.ToInt32(reader["CompanyId"]));
            }
            return ids;
        }

        private void AssignCompanies(SqlConnection conn, SqlTransaction transaction, int userId, string role, int[] selectedCompanyIds)
        {
            var clearCmd = new SqlCommand("UPDATE Companies SET EmployerId = NULL WHERE EmployerId = @EmployerId", conn, transaction);
            clearCmd.Parameters.AddWithValue("@EmployerId", userId);
            clearCmd.ExecuteNonQuery();

            if (!string.Equals(role, "Employer", StringComparison.OrdinalIgnoreCase) || selectedCompanyIds == null)
            {
                return;
            }

            foreach (var companyId in selectedCompanyIds)
            {
                var assignCmd = new SqlCommand("UPDATE Companies SET EmployerId = @EmployerId WHERE CompanyId = @CompanyId", conn, transaction);
                assignCmd.Parameters.AddWithValue("@EmployerId", userId);
                assignCmd.Parameters.AddWithValue("@CompanyId", companyId);
                assignCmd.ExecuteNonQuery();
            }
        }

        private static User MapUser(SqlDataReader reader)
        {
            return new User
            {
                UserId = Convert.ToInt32(reader["UserId"]),
                FullName = reader["FullName"]?.ToString(),
                Email = reader["Email"]?.ToString(),
                Phone = reader["Phone"]?.ToString(),
                Address = reader["Address"]?.ToString(),
                Role = reader["Role"]?.ToString(),
                CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : (DateTime?)null,
                IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"])
            };
        }

        private static User? GetUserForDelete(SqlConnection conn, SqlTransaction transaction, int userId)
        {
            using var cmd = new SqlCommand(@"
                SELECT UserId, FullName, Email, Phone, Address, Role, CreatedDate, IsActive
                FROM Users
                WHERE UserId = @UserId", conn, transaction);
            cmd.Parameters.AddWithValue("@UserId", userId);

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? MapUser(reader) : null;
        }

        private static void ExecuteNonQuery(SqlConnection conn, SqlTransaction transaction, string sql, Action<SqlCommand>? configure)
        {
            using var cmd = new SqlCommand(sql, conn, transaction);
            configure?.Invoke(cmd);
            cmd.ExecuteNonQuery();
        }

        private static Announcement MapAnnouncement(SqlDataReader reader)
        {
            return new Announcement
            {
                Id = Convert.ToInt32(reader["Id"]),
                Title = reader["Title"]?.ToString(),
                Content = reader["Content"]?.ToString(),
                Type = reader["Type"]?.ToString(),
                TargetRole = reader["TargetRole"]?.ToString(),
                CreatedAt = reader["CreatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedAt"]) : DateTime.Now,
                ExpiryDate = reader["ExpiryDate"] != DBNull.Value ? Convert.ToDateTime(reader["ExpiryDate"]) : (DateTime?)null,
                IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"]),
                CreatedBy = reader["CreatedBy"] != DBNull.Value ? Convert.ToInt32(reader["CreatedBy"]) : (int?)null,
                ImageUrl = reader["ImageUrl"]?.ToString(),
                LinkUrl = reader["LinkUrl"]?.ToString()
            };
        }

        private static ICollection<Application> BuildApplicationPlaceholders(int count)
        {
            var applications = new List<Application>();
            for (var i = 0; i < count; i++)
            {
                applications.Add(new Application());
            }
            return applications;
        }

        private static void AddJobParameters(SqlCommand cmd, CreateJobViewModel model, string imagePath)
        {
            cmd.Parameters.AddWithValue("@Title", model.Title);
            cmd.Parameters.AddWithValue("@CompanyId", (object?)model.CompanyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SalaryMin", (object?)model.SalaryMin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SalaryMax", (object?)model.SalaryMax ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Location", (object?)model.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@JobType", (object?)model.JobType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)model.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Requirement", (object?)model.Requirement ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Benefit", (object?)model.Benefit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Deadline", (object?)model.Deadline ?? DBNull.Value);
        }

        private static void AddJobParameters(SqlCommand cmd, EditJobViewModel model, string imagePath)
        {
            cmd.Parameters.AddWithValue("@Title", (object?)model.Title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CompanyId", (object?)model.CompanyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SalaryMin", (object?)model.SalaryMin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SalaryMax", (object?)model.SalaryMax ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Location", (object?)model.Location ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@JobType", (object?)model.JobType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)model.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Requirement", (object?)model.Requirement ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Benefit", (object?)model.Benefit ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Deadline", (object?)model.Deadline ?? DBNull.Value);
        }

        private void AddAnnouncementParameters(SqlCommand cmd, AnnouncementViewModel model, bool includeCreatedBy)
        {
            cmd.Parameters.AddWithValue("@Title", model.Title);
            cmd.Parameters.AddWithValue("@Content", (object?)model.Content ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Type", string.IsNullOrWhiteSpace(model.Type) ? "info" : model.Type);
            cmd.Parameters.AddWithValue("@TargetRole", string.IsNullOrWhiteSpace(model.TargetRole) ? "All" : model.TargetRole);
            cmd.Parameters.AddWithValue("@ExpiryDate", (object?)model.ExpiryDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LinkUrl", (object?)model.LinkUrl ?? DBNull.Value);

            if (!includeCreatedBy) return;

            var currentUser = HttpContext.Session.GetString("UserId");
            cmd.Parameters.AddWithValue("@CreatedBy", int.TryParse(currentUser, out var userId) ? (object)userId : DBNull.Value);
        }

        private async Task<string> SaveUploadAsync(IFormFile file, string folder)
        {
            if (file == null || file.Length == 0) return null;

            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", folder);
            Directory.CreateDirectory(uploadRoot);
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}";
            var fullPath = Path.Combine(uploadRoot, fileName);
            await using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);

            return $"/uploads/{folder}/{fileName}";
        }

        private static string GenerateTemporaryPassword()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%";
            var bytes = RandomNumberGenerator.GetBytes(14);
            var password = new char[14];

            for (var i = 0; i < password.Length; i++)
            {
                password[i] = chars[bytes[i] % chars.Length];
            }

            return new string(password);
        }

        private async Task<bool> SendTemporaryPasswordEmailAsync(string email, string fullName, string temporaryPassword)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var subject = "JobPortal - Mat khau tam thoi";
            var body = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <title>Mat khau tam thoi</title>
                </head>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 10px;'>
                        <h2 style='color: #2563eb; text-align: center;'>JobPortal - Tai khoan moi</h2>
                        <p>Xin chao {System.Net.WebUtility.HtmlEncode(fullName ?? email)},</p>
                        <p>Tai khoan JobPortal cua ban da duoc tao boi quan tri vien.</p>
                        <div style='background: #eff6ff; border: 1px solid #bfdbfe; padding: 16px; border-radius: 8px; margin: 18px 0;'>
                            <p style='margin: 0 0 8px 0;'><strong>Email dang nhap:</strong> {System.Net.WebUtility.HtmlEncode(email)}</p>
                            <p style='margin: 0;'><strong>Mat khau tam thoi:</strong> {System.Net.WebUtility.HtmlEncode(temporaryPassword)}</p>
                        </div>
                        <p>Vi ly do bao mat, he thong se yeu cau ban doi mat khau ngay sau lan dang nhap dau tien.</p>
                        <p>Neu ban khong yeu cau tai khoan nay, vui long lien he quan tri vien.</p>
                    </div>
                </body>
                </html>";

            return await _emailService.SendEmailAsync(email, subject, body);
        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var builder = new StringBuilder();

            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        private static int Scalar(SqlConnection conn, string sql)
        {
            using var cmd = new SqlCommand(sql, conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private List<ActivityLog> LoadRecentActivities(SqlConnection conn)
        {
            var activities = new List<ActivityLog>();
            using var cmd = new SqlCommand(@"
                SELECT TOP 12 LogId, Username, UserRole, Action, Details, TargetUser, TargetEmail, TargetRole, TargetId, IpAddress, CreatedAt, Status
                FROM ActivityLogs
                WHERE UserRole = 'Admin'
                ORDER BY CreatedAt DESC, LogId DESC", conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                activities.Add(new ActivityLog
                {
                    LogId = Convert.ToInt32(reader["LogId"]),
                    Username = reader["Username"]?.ToString(),
                    UserRole = reader["UserRole"]?.ToString(),
                    Action = reader["Action"]?.ToString(),
                    Details = reader["Details"]?.ToString(),
                    TargetUser = reader["TargetUser"]?.ToString(),
                    TargetEmail = reader["TargetEmail"]?.ToString(),
                    TargetRole = reader["TargetRole"]?.ToString(),
                    TargetId = reader["TargetId"] != DBNull.Value ? Convert.ToInt32(reader["TargetId"]) : (int?)null,
                    IpAddress = reader["IpAddress"]?.ToString(),
                    CreatedAt = reader["CreatedAt"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedAt"]) : DateTime.Now,
                    Status = reader["Status"]?.ToString()
                });
            }

            return activities;
        }

        private void LogActivity(SqlConnection conn, SqlTransaction transaction, string action, string details, string targetUser, string targetEmail, string targetRole, int? targetId, string status)
        {
            using var cmd = new SqlCommand(@"
                INSERT INTO ActivityLogs (Username, UserRole, Action, Details, TargetUser, TargetEmail, TargetRole, TargetId, IpAddress, CreatedAt, Status)
                VALUES (@Username, @UserRole, @Action, @Details, @TargetUser, @TargetEmail, @TargetRole, @TargetId, @IpAddress, GETDATE(), @Status)", conn, transaction);
            cmd.Parameters.AddWithValue("@Username", HttpContext.Session.GetString("UserName") ?? "Admin");
            cmd.Parameters.AddWithValue("@UserRole", HttpContext.Session.GetString("UserRole") ?? "Admin");
            cmd.Parameters.AddWithValue("@Action", action);
            cmd.Parameters.AddWithValue("@Details", details);
            cmd.Parameters.AddWithValue("@TargetUser", (object?)targetUser ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetEmail", (object?)targetEmail ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetRole", (object?)targetRole ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetId", (object?)targetId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IpAddress", (object?)HttpContext.Connection.RemoteIpAddress?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }
}
