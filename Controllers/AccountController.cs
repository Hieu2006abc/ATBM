using BTL_2.Models;
using BTL_2.Models.ViewModels;
using BTL_2.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace JobPortal.Controllers
{
    public class AccountController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly EmailService _emailService;
        private readonly IDataProtector _twoFactorProtector;

        public AccountController(
            IConfiguration configuration,
            EmailService emailService,
            IDataProtectionProvider dataProtectionProvider)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _emailService = emailService;
            _twoFactorProtector = dataProtectionProvider.CreateProtector("BTL_2.Account.TwoFactorSecret.v1");
        }

        // GET: Account/Login
        [HttpGet]
        public IActionResult Login(string returnUrl = null, string role = null)
        {
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.RoleHint = role;
            return View();
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var user = AuthenticateUser(model.Email, model.Password);

                    if (user != null)
                    {
                        if (user.TwoFactorEnabled)
                        {
                            HttpContext.Session.SetString("PendingTwoFactorUserId", user.UserId.ToString());
                            HttpContext.Session.SetString("PendingTwoFactorRememberMe", model.RememberMe ? "true" : "false");
                            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                            {
                                HttpContext.Session.SetString("PendingTwoFactorReturnUrl", returnUrl);
                            }

                            return RedirectToAction("VerifyTwoFactor", new { returnUrl });
                        }

                        return await CompleteSignInAsync(user, returnUrl, model.RememberMe);
                    }

                    if (IsKnownInactiveAccount(model.Email))
                    {
                        ModelState.AddModelError("", "Tài khoản đã bị khóa. Vui lòng liên hệ Admin để mở khóa.");
                        return View(model);
                    }

                    ModelState.AddModelError("", "Email hoặc mật khẩu không đúng");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Login error: {ex.Message}");
                    ModelState.AddModelError("", "Không thể đăng nhập lúc này. Vui lòng thử lại sau.");
                }
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult VerifyTwoFactor(string returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("PendingTwoFactorUserId")))
            {
                return RedirectToAction("Login");
            }

            return View(new VerifyTwoFactorViewModel
            {
                ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl)
                    ? returnUrl
                    : HttpContext.Session.GetString("PendingTwoFactorReturnUrl")
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyTwoFactor(VerifyTwoFactorViewModel model)
        {
            var pendingUserId = HttpContext.Session.GetString("PendingTwoFactorUserId");
            if (string.IsNullOrWhiteSpace(pendingUserId) || !int.TryParse(pendingUserId, out var userId))
            {
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = GetUserById(userId);
            var secret = UnprotectTwoFactorSecret(user?.TwoFactorSecret);
            if (user == null || !user.TwoFactorEnabled || !TwoFactorTotpService.VerifyCode(secret, model.Code))
            {
                ModelState.AddModelError("Code", "Mã xác thực không đúng hoặc đã hết hạn");
                return View(model);
            }

            HttpContext.Session.Remove("PendingTwoFactorUserId");
            HttpContext.Session.Remove("PendingTwoFactorReturnUrl");
            var rememberMe = HttpContext.Session.GetString("PendingTwoFactorRememberMe") == "true";
            HttpContext.Session.Remove("PendingTwoFactorRememberMe");
            UpdateTwoFactorLastVerified(user.UserId);

            return await CompleteSignInAsync(user, model.ReturnUrl, rememberMe);
        }

        // GET: Account/Register
        [HttpGet]
        public IActionResult Register()
        {
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: Account/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (CheckEmailExists(model.Email))
                {
                    ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống");
                    return View(model);
                }

                if (!string.IsNullOrEmpty(model.Phone) && CheckPhoneExists(model.Phone))
                {
                    ModelState.AddModelError("Phone", "Số điện thoại đã tồn tại trong hệ thống");
                    return View(model);
                }

                if (CreateUser(model))
                {
                    TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
                    return RedirectToAction("Login");
                }

                ModelState.AddModelError("", "Có lỗi xảy ra khi đăng ký");
                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Register: {ex.Message}");
                ModelState.AddModelError("", "Có lỗi xảy ra: " + ex.Message);
                return View(model);
            }
        }

        // GET: Account/CreateEmployer
        [HttpGet]
        public IActionResult CreateEmployer()
        {
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: Account/CreateEmployer
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateEmployer(EmployerRegisterViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (CheckEmailExists(model.Email))
                {
                    ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống");
                    return View(model);
                }

                if (!string.IsNullOrEmpty(model.Phone) && CheckPhoneExists(model.Phone))
                {
                    ModelState.AddModelError("Phone", "Số điện thoại đã tồn tại trong hệ thống");
                    return View(model);
                }

                if (CreateEmployerUser(model))
                {
                    TempData["SuccessMessage"] = "Tạo tài khoản nhà tuyển dụng thành công! Vui lòng đăng nhập.";
                    return RedirectToAction("Login");
                }

                ModelState.AddModelError("", "Có lỗi xảy ra khi tạo tài khoản");
                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CreateEmployer: {ex.Message}");
                ModelState.AddModelError("", "Có lỗi xảy ra: " + ex.Message);
                return View(model);
            }
        }

        // POST: Account/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync("Cookie");
            TempData["SuccessMessage"] = "Đăng xuất thành công!";
            return RedirectToAction("Index", "Home");
        }

        private async Task<IActionResult> CompleteSignInAsync(User user, string returnUrl, bool rememberMe)
        {
            HttpContext.Session.SetString("UserId", user.UserId.ToString());
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetString("UserRole", user.Role);
            HttpContext.Session.SetString("UserName", user.FullName);
            if (user.MustChangePassword)
            {
                HttpContext.Session.SetString("MustChangePassword", "true");
            }
            else
            {
                HttpContext.Session.Remove("MustChangePassword");
            }

            await SignInUserAsync(user, rememberMe);

            TempData["SuccessMessage"] = "Đăng nhập thành công!";

            if (user.MustChangePassword)
            {
                TempData["WarningMessage"] = "Bạn đang dùng mật khẩu tạm thời. Vui lòng đổi mật khẩu trước khi tiếp tục.";
                return RedirectToAction("ChangePassword", "Account");
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            if (user.Role == "Admin")
            {
                return RedirectToAction("ManageUsers", "Admin");
            }

            if (user.Role == "Employer")
            {
                return RedirectToAction("ManageApplications", "Jobs");
            }

            return RedirectToAction("Index", "Home");
        }

        private async Task SignInUserAsync(User user, bool rememberMe)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? user.Email ?? user.UserId.ToString()),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Role, user.Role ?? string.Empty)
            };

            var identity = new ClaimsIdentity(claims, "Cookie");
            var principal = new ClaimsPrincipal(identity);
            var properties = new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe
                    ? DateTimeOffset.UtcNow.AddDays(14)
                    : DateTimeOffset.UtcNow.AddMinutes(30)
            };

            await HttpContext.SignInAsync("Cookie", principal, properties);
        }

        // GET: Account/Profile
        [HttpGet]
        public IActionResult Profile(string tab = "info")
        {
            if (HttpContext.Session.GetString("UserId") == null)
            {
                return RedirectToAction("Login");
            }

            var userId = int.Parse(HttpContext.Session.GetString("UserId"));
            var user = GetUserById(userId);

            var viewModel = new ProfileViewModel
            {
                User = user,
                Applications = GetUserApplications(userId) ?? new List<Application>(),
                AllJobs = GetAllJobs() ?? new List<Job>(),
                FollowedCompanies = GetFollowedCompanies(userId) ?? new List<Company>(),
                ActiveTab = tab
            };

            if (tab == "security")
            {
                PrepareTwoFactorSetup(user);
            }

            return View(viewModel);
        }

        // POST: Account/UpdateProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateProfile(User model)
        {
            if (ModelState.IsValid)
            {
                var currentUserId = int.Parse(HttpContext.Session.GetString("UserId"));

                if (!string.IsNullOrEmpty(model.Phone) && CheckPhoneExists(model.Phone, currentUserId))
                {
                    ModelState.AddModelError("Phone", "Số điện thoại đã tồn tại trong hệ thống");

                    var user = GetUserById(currentUserId);
                    var viewModel = new ProfileViewModel
                    {
                        User = user,
                        Applications = GetUserApplications(currentUserId) ?? new List<Application>(),
                        AllJobs = GetAllJobs() ?? new List<Job>(),
                        FollowedCompanies = GetFollowedCompanies(currentUserId) ?? new List<Company>(),
                        ActiveTab = "info"
                    };
                    return View("Profile", viewModel);
                }

                model.UserId = currentUserId;
                UpdateUser(model);
                HttpContext.Session.SetString("UserName", model.FullName);
                TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
                return RedirectToAction("Profile");
            }

            var currentUserId2 = int.Parse(HttpContext.Session.GetString("UserId"));
            var currentUser = GetUserById(currentUserId2);
            var currentViewModel = new ProfileViewModel
            {
                User = currentUser,
                Applications = GetUserApplications(currentUserId2) ?? new List<Application>(),
                AllJobs = GetAllJobs() ?? new List<Job>(),
                FollowedCompanies = GetFollowedCompanies(currentUserId2) ?? new List<Company>(),
                ActiveTab = "info"
            };
            return View("Profile", currentViewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EnableTwoFactor(string code)
        {
            var userIdValue = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrWhiteSpace(userIdValue) || !int.TryParse(userIdValue, out var userId))
            {
                return RedirectToAction("Login");
            }

            var protectedPendingSecret = HttpContext.Session.GetString("PendingTwoFactorSetupSecret");
            var secret = UnprotectTwoFactorSecret(protectedPendingSecret);
            if (string.IsNullOrWhiteSpace(secret))
            {
                TempData["ErrorMessage"] = "Phiên thiết lập 2FA đã hết hạn. Vui lòng tạo lại mã.";
                return RedirectToAction("Profile", new { tab = "security" });
            }

            if (!TwoFactorTotpService.VerifyCode(secret, code))
            {
                TempData["ErrorMessage"] = "Mã 2FA không đúng hoặc đã hết hạn.";
                return RedirectToAction("Profile", new { tab = "security" });
            }

            SaveTwoFactorSecret(userId, ProtectTwoFactorSecret(secret));
            HttpContext.Session.Remove("PendingTwoFactorSetupSecret");
            TempData["SuccessMessage"] = "Đã bật xác thực hai lớp cho tài khoản.";
            return RedirectToAction("Profile", new { tab = "security" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DisableTwoFactor(string code)
        {
            var userIdValue = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrWhiteSpace(userIdValue) || !int.TryParse(userIdValue, out var userId))
            {
                return RedirectToAction("Login");
            }

            var user = GetUserById(userId);
            var secret = UnprotectTwoFactorSecret(user?.TwoFactorSecret);
            if (user == null || !user.TwoFactorEnabled || !TwoFactorTotpService.VerifyCode(secret, code))
            {
                TempData["ErrorMessage"] = "Mã 2FA không đúng. Không thể tắt bảo vệ tài khoản.";
                return RedirectToAction("Profile", new { tab = "security" });
            }

            DisableTwoFactorForUser(userId);
            HttpContext.Session.Remove("PendingTwoFactorSetupSecret");
            TempData["SuccessMessage"] = "Đã tắt xác thực hai lớp.";
            return RedirectToAction("Profile", new { tab = "security" });
        }

        // POST: Account/CheckPhone
        [HttpPost]
        public JsonResult CheckPhone(string phone)
        {
            var userId = HttpContext.Session.GetString("UserId");
            int? currentUserId = userId != null ? int.Parse(userId) : (int?)null;
            bool exists = CheckPhoneExists(phone, currentUserId);
            return Json(new { exists = exists });
        }

        // GET: Account/ChangePassword
        [HttpGet]
        public IActionResult ChangePassword()
        {
            if (HttpContext.Session.GetString("UserId") == null)
            {
                return RedirectToAction("Login");
            }
            return View();
        }

        // POST: Account/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    var userId = HttpContext.Session.GetString("UserId");
                    if (string.IsNullOrEmpty(userId))
                    {
                        return RedirectToAction("Login");
                    }

                    int id = int.Parse(userId);

                    if (!CheckCurrentPassword(id, model.CurrentPassword))
                    {
                        ModelState.AddModelError("CurrentPassword", "Mật khẩu hiện tại không đúng");
                        return View(model);
                    }

                    if (UpdatePassword(id, model.NewPassword))
                    {
                        TempData["SuccessMessage"] = "Đổi mật khẩu thành công! Vui lòng đăng nhập lại.";
                        HttpContext.Session.Clear();
                        await HttpContext.SignOutAsync("Cookie");
                        return RedirectToAction("Login");
                    }
                    else
                    {
                        ModelState.AddModelError("", "Có lỗi xảy ra khi đổi mật khẩu");
                    }
                }
                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ChangePassword: {ex.Message}");
                ModelState.AddModelError("", "Có lỗi xảy ra: " + ex.Message);
                return View(model);
            }
        }

        // GET: Account/MyApplications
        [HttpGet]
        public IActionResult MyApplications()
        {
            if (HttpContext.Session.GetString("UserId") == null)
            {
                return RedirectToAction("Login");
            }

            if (HttpContext.Session.GetString("UserRole") != "Candidate")
            {
                TempData["ErrorMessage"] = "Chức năng chỉ dành cho ứng viên";
                return RedirectToAction("Index", "Home");
            }

            var userId = int.Parse(HttpContext.Session.GetString("UserId"));
            var applications = GetUserApplications(userId);
            return View(applications);
        }

        // POST: Account/DeleteApplication
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteApplication(int id)
        {
            try
            {
                if (HttpContext.Session.GetString("UserId") == null)
                {
                    return RedirectToAction("Login");
                }

                var userId = int.Parse(HttpContext.Session.GetString("UserId"));
                var userRole = HttpContext.Session.GetString("UserRole");

                if (userRole != "Candidate")
                {
                    TempData["ErrorMessage"] = "Bạn không có quyền thực hiện thao tác này";
                    return RedirectToAction("MyApplications");
                }

                if (!CheckApplicationOwnership(id, userId))
                {
                    TempData["ErrorMessage"] = "Không tìm thấy hồ sơ hoặc bạn không có quyền xóa";
                    return RedirectToAction("MyApplications");
                }

                if (WithdrawApplicationFromDb(id))
                {
                    TempData["SuccessMessage"] = "Đã rút đơn ứng tuyển thành công. Bạn có thể nộp lại hồ sơ cho công việc này.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Có lỗi xảy ra khi rút hồ sơ";
                }

                return RedirectToAction("MyApplications");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteApplication: {ex.Message}");
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                return RedirectToAction("MyApplications");
            }
        }

        // POST: Account/FollowCompany
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult FollowCompany(int companyId)
        {
            if (HttpContext.Session.GetString("UserId") == null)
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập" });
            }

            var userId = int.Parse(HttpContext.Session.GetString("UserId"));

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string checkQuery = "SELECT COUNT(*) FROM Follows WHERE UserId = @UserId AND CompanyId = @CompanyId";
                    SqlCommand checkCmd = new SqlCommand(checkQuery, conn);
                    checkCmd.Parameters.AddWithValue("@UserId", userId);
                    checkCmd.Parameters.AddWithValue("@CompanyId", companyId);

                    conn.Open();
                    int count = (int)checkCmd.ExecuteScalar();

                    if (count > 0)
                    {
                        string deleteQuery = "DELETE FROM Follows WHERE UserId = @UserId AND CompanyId = @CompanyId";
                        SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn);
                        deleteCmd.Parameters.AddWithValue("@UserId", userId);
                        deleteCmd.Parameters.AddWithValue("@CompanyId", companyId);
                        deleteCmd.ExecuteNonQuery();
                        return Json(new { success = true, action = "unfollowed", message = "Đã hủy theo dõi công ty" });
                    }
                    else
                    {
                        string insertQuery = "INSERT INTO Follows (UserId, CompanyId, FollowedDate) VALUES (@UserId, @CompanyId, GETDATE())";
                        SqlCommand insertCmd = new SqlCommand(insertQuery, conn);
                        insertCmd.Parameters.AddWithValue("@UserId", userId);
                        insertCmd.Parameters.AddWithValue("@CompanyId", companyId);
                        insertCmd.ExecuteNonQuery();
                        return Json(new { success = true, action = "followed", message = "Đã theo dõi công ty" });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Account/ForgotPassword
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // POST: Account/ForgotPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> ForgotPassword(string email)
        {
            try
            {
                Console.WriteLine($"=== FORGOT PASSWORD ===");
                Console.WriteLine($"Email: {email}");

                if (string.IsNullOrEmpty(_connectionString))
                {
                    return Json(new { success = false, message = "Lỗi cấu hình database" });
                }

                email = email?.Trim();

                if (!CheckEmailExists(email))
                {
                    return Json(new { success = false, message = "Email không tồn tại trong hệ thống" });
                }

                if (IsKnownInactiveAccount(email))
                {
                    return Json(new { success = false, message = "Tài khoản này đang bị khóa. Vui lòng liên hệ Admin để mở khóa." });
                }

                // Xóa OTP cũ
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    EnsurePasswordResetSchema(conn);
                    string deleteQuery = "DELETE FROM PasswordResets WHERE Email = @Email";
                    SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn);
                    deleteCmd.Parameters.AddWithValue("@Email", email);
                    deleteCmd.ExecuteNonQuery();
                    Console.WriteLine("✅ Old OTP deleted");
                }

                string otpCode = _emailService.GenerateOtpCode();
                string token = Guid.NewGuid().ToString("N");

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    string insertQuery = @"
                        INSERT INTO PasswordResets (Email, Token, OtpCode, ExpiryTime, IsUsed, FailedAttempts, CreatedAt) 
                        VALUES (@Email, @Token, @OtpCode, DATEADD(minute, 15, GETDATE()), 0, 0, GETDATE())";

                    SqlCommand insertCmd = new SqlCommand(insertQuery, conn);
                    insertCmd.Parameters.AddWithValue("@Email", email);
                    insertCmd.Parameters.AddWithValue("@Token", token);
                    insertCmd.Parameters.AddWithValue("@OtpCode", otpCode);
                    insertCmd.ExecuteNonQuery();

                    Console.WriteLine($"✅ OTP saved: {otpCode}");
                }

                // Gửi email
                string subject = "JobPortal - Mã xác thực đặt lại mật khẩu";
                string body = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                        <h2 style='color: #667eea;'>Xác thực OTP</h2>
                        <p>Xin chào,</p>
                        <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>
                        <p>Mã xác thực OTP của bạn là:</p>
                        <div style='font-size: 36px; font-weight: bold; color: #764ba2; padding: 20px; background: #f5f5f5; text-align: center; border-radius: 8px; letter-spacing: 5px;'>
                            {otpCode}
                        </div>
                        <p>Mã này có hiệu lực trong <strong>15 phút</strong>.</p>
                        <p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này.</p>
                        <hr style='margin: 20px 0;'>
                        <p style='color: #666; font-size: 12px;'>Đây là email tự động, vui lòng không trả lời.</p>
                    </div>";

                bool emailSent = await _emailService.SendEmailAsync(email, subject, body);

                if (emailSent)
                {
                    Console.WriteLine($"✅ Email sent to {email}");
                    return Json(new { success = true, message = "Mã xác thực đã được gửi đến email của bạn" });
                }
                else
                {
                    return Json(new { success = false, message = "Có lỗi khi gửi email. Vui lòng thử lại sau" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Account/VerifyOtp - Hiển thị form nhập OTP
        [HttpGet]
        public IActionResult VerifyOtp(string email)
        {
            Console.WriteLine($"=== GET VERIFY OTP ===");
            Console.WriteLine($"Email: {email}");

            if (string.IsNullOrEmpty(email))
            {
                TempData["ErrorMessage"] = "Thông tin không hợp lệ";
                return RedirectToAction("ForgotPassword");
            }

            // Kiểm tra xem có OTP hợp lệ không
            bool hasValidOtp = false;
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"
                        SELECT COUNT(*) FROM PasswordResets 
                        WHERE Email = @Email AND IsUsed = 0 AND ExpiryTime > GETDATE()";

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Email", email);

                    conn.Open();
                    EnsurePasswordResetSchema(conn);
                    int count = (int)cmd.ExecuteScalar();
                    hasValidOtp = count > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking OTP: {ex.Message}");
            }

            if (!hasValidOtp)
            {
                TempData["ErrorMessage"] = "Không tìm thấy mã OTP hợp lệ. Vui lòng gửi lại yêu cầu.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.Email = email;
            return View();
        }

        // POST: Account/VerifyOtp - Xác thực OTP
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> VerifyOtp(string email, string otpCode)
        {
            Console.WriteLine($"=== VERIFY OTP CALLED ===");
            Console.WriteLine($"Email: {email}");
            Console.WriteLine($"OTP Code: {otpCode}");

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(otpCode))
            {
                return Json(new { success = false, isValid = false, message = "Thông tin không hợp lệ!" });
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    EnsurePasswordResetSchema(conn);
                    Console.WriteLine("✅ Database connected");

                    string query = @"
                        SELECT TOP 1 Id, OtpCode, ExpiryTime, IsUsed, FailedAttempts 
                        FROM PasswordResets 
                        WHERE Email = @Email AND IsUsed = 0 
                        ORDER BY CreatedAt DESC";

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Email", email);

                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows)
                        {
                            Console.WriteLine("❌ No OTP record found");
                            return Json(new { success = false, isValid = false, message = "Không tìm thấy mã OTP! Vui lòng gửi lại." });
                        }

                        await reader.ReadAsync();

                        int id = Convert.ToInt32(reader["Id"]);
                        string storedOtp = reader["OtpCode"].ToString();
                        DateTime expiryTime = Convert.ToDateTime(reader["ExpiryTime"]);
                        int failedAttempts = reader["FailedAttempts"] == DBNull.Value ? 0 : Convert.ToInt32(reader["FailedAttempts"]);

                        reader.Close();

                        Console.WriteLine($"Found OTP: {storedOtp}, Expires: {expiryTime}");

                        if (failedAttempts >= 3)
                        {
                            LockAccountByEmail(conn, email);
                            return Json(new { success = false, isValid = false, isLocked = true, attemptsLeft = 0, message = "Tài khoản đã bị khóa do nhập sai OTP quá 3 lần. Vui lòng liên hệ Admin để mở khóa." });
                        }

                        if (storedOtp != otpCode)
                        {
                            Console.WriteLine($"❌ OTP mismatch");
                            failedAttempts++;
                            string failQuery = "UPDATE PasswordResets SET FailedAttempts = @FailedAttempts WHERE Id = @Id";
                            SqlCommand failCmd = new SqlCommand(failQuery, conn);
                            failCmd.Parameters.AddWithValue("@FailedAttempts", failedAttempts);
                            failCmd.Parameters.AddWithValue("@Id", id);
                            await failCmd.ExecuteNonQueryAsync();

                            int attemptsLeft = Math.Max(0, 3 - failedAttempts);
                            if (failedAttempts >= 3)
                            {
                                LockAccountByEmail(conn, email);
                                string usedQuery = "UPDATE PasswordResets SET IsUsed = 1 WHERE Id = @Id";
                                SqlCommand usedCmd = new SqlCommand(usedQuery, conn);
                                usedCmd.Parameters.AddWithValue("@Id", id);
                                await usedCmd.ExecuteNonQueryAsync();
                                return Json(new { success = false, isValid = false, isLocked = true, attemptsLeft = 0, message = "Bạn đã nhập sai OTP 3 lần. Tài khoản đã bị khóa, vui lòng liên hệ Admin để mở khóa." });
                            }

                            return Json(new { success = false, isValid = false, attemptsLeft, message = $"Mã OTP không đúng. Bạn còn {attemptsLeft} lần thử." });
                        }

                        DateTime currentTime = DateTime.Now;
                        Console.WriteLine($"Current Time: {currentTime}");
                        Console.WriteLine($"Expires Time: {expiryTime}");

                        if (currentTime > expiryTime)
                        {
                            Console.WriteLine("❌ OTP EXPIRED");
                            return Json(new { success = false, isValid = false, message = "Mã OTP đã hết hạn! Vui lòng gửi lại." });
                        }

                        // Đánh dấu đã sử dụng
                        string markUsedQuery = "UPDATE PasswordResets SET IsUsed = 1 WHERE Id = @Id";
                        SqlCommand markUsedCmd = new SqlCommand(markUsedQuery, conn);
                        markUsedCmd.Parameters.AddWithValue("@Id", id);
                        await markUsedCmd.ExecuteNonQueryAsync();

                        Console.WriteLine($"✅ OTP is VALID! ID: {id}");

                        // ✅ CHUYỂN HƯỚNG ĐẾN TRANG ĐẶT LẠI MẬT KHẨU
                        return Json(new
                        {
                            success = true,
                            isValid = true,
                            message = "Xác thực thành công!",
                            redirectUrl = $"/Account/ResetPassword?email={Uri.EscapeDataString(email)}&otpCode={otpCode}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in VerifyOtp: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra, vui lòng thử lại!" });
            }
        }

        // GET: Account/ResetPassword
        [HttpGet]
        public IActionResult ResetPassword(string email, string otpCode)
        {
            try
            {
                if (!string.IsNullOrEmpty(email) && email.Contains("%40"))
                {
                    email = System.Web.HttpUtility.UrlDecode(email);
                }

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(otpCode))
                {
                    TempData["ErrorMessage"] = "Thông tin không hợp lệ";
                    return RedirectToAction("ForgotPassword");
                }

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"
                        SELECT COUNT(*) FROM PasswordResets 
                        WHERE Email = @Email 
                        AND OtpCode = @OtpCode 
                        AND IsUsed = 1 
                        AND ExpiryTime > GETDATE()";

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Email", email);
                    cmd.Parameters.AddWithValue("@OtpCode", otpCode);

                    conn.Open();
                    EnsurePasswordResetSchema(conn);
                    int count = (int)cmd.ExecuteScalar();

                    if (count == 0)
                    {
                        TempData["ErrorMessage"] = "Mã OTP không hợp lệ hoặc đã hết hạn. Vui lòng thử lại.";
                        return RedirectToAction("ForgotPassword");
                    }
                }

                ViewBag.Email = email;
                ViewBag.OtpCode = otpCode;
                return View();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                return RedirectToAction("ForgotPassword");
            }
        }

        // POST: Account/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public JsonResult ResetPassword(string email, string otpCode, string newPassword)
        {
            try
            {
                Console.WriteLine($"=== RESET PASSWORD CALLED ===");
                Console.WriteLine($"Email: {email}");
                Console.WriteLine($"OTP Code: {otpCode}");

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(otpCode) || string.IsNullOrEmpty(newPassword))
                {
                    return Json(new { success = false, message = "Thông tin không đầy đủ" });
                }

                if (newPassword.Length < 6)
                {
                    return Json(new { success = false, message = "Mật khẩu phải có ít nhất 6 ký tự" });
                }

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    EnsurePasswordResetSchema(conn);

                    if (IsKnownInactiveAccount(email, conn))
                    {
                        return Json(new { success = false, message = "Tài khoản đã bị khóa. Vui lòng liên hệ Admin để mở khóa." });
                    }

                    string checkQuery = @"
                        SELECT Id FROM PasswordResets 
                        WHERE Email = @Email 
                        AND OtpCode = @OtpCode 
                        AND IsUsed = 1";

                    SqlCommand checkCmd = new SqlCommand(checkQuery, conn);
                    checkCmd.Parameters.AddWithValue("@Email", email);
                    checkCmd.Parameters.AddWithValue("@OtpCode", otpCode);

                    var resetId = checkCmd.ExecuteScalar();

                    if (resetId == null)
                    {
                        return Json(new { success = false, message = "Mã OTP không hợp lệ" });
                    }

                    string hashedPassword = HashPassword(newPassword);
                    string updateQuery = "UPDATE Users SET Password = @Password WHERE Email = @Email";
                    SqlCommand updateCmd = new SqlCommand(updateQuery, conn);
                    updateCmd.Parameters.AddWithValue("@Password", hashedPassword);
                    updateCmd.Parameters.AddWithValue("@Email", email);

                    int rowsAffected = updateCmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        // Xóa OTP đã dùng
                        string deleteQuery = "DELETE FROM PasswordResets WHERE Id = @Id";
                        SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn);
                        deleteCmd.Parameters.AddWithValue("@Id", resetId);
                        deleteCmd.ExecuteNonQuery();

                        return Json(new { success = true, message = "Đặt lại mật khẩu thành công!" });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Không thể cập nhật mật khẩu" });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // ==================== PRIVATE METHODS ====================

        private User AuthenticateUser(string email, string password)
        {
            string hashedPassword = HashPassword(password);
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT * FROM Users WHERE Email = @Email AND Password = @Password AND IsActive = 1";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Email", email);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new User
                    {
                        UserId = Convert.ToInt32(reader["UserId"]),
                        FullName = reader["FullName"].ToString(),
                        Email = reader["Email"].ToString(),
                        Role = reader["Role"].ToString(),
                        TwoFactorEnabled = HasColumn(reader, "TwoFactorEnabled") &&
                            reader["TwoFactorEnabled"] != DBNull.Value &&
                            Convert.ToBoolean(reader["TwoFactorEnabled"]),
                        MustChangePassword = HasColumn(reader, "MustChangePassword") &&
                            reader["MustChangePassword"] != DBNull.Value &&
                            Convert.ToBoolean(reader["MustChangePassword"]),
                        TwoFactorSecret = HasColumn(reader, "TwoFactorSecret")
                            ? reader["TwoFactorSecret"]?.ToString()
                            : null
                    };
                }
            }
            return null;
        }

        private bool CheckEmailExists(string email)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM Users WHERE Email = @Email";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Email", email);
                conn.Open();
                int count = (int)cmd.ExecuteScalar();
                return count > 0;
            }
        }

        private bool CheckPhoneExists(string phone, int? excludeUserId = null)
        {
            if (string.IsNullOrEmpty(phone)) return false;
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM Users WHERE Phone = @Phone";
                if (excludeUserId.HasValue) query += " AND UserId != @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Phone", phone);
                if (excludeUserId.HasValue) cmd.Parameters.AddWithValue("@UserId", excludeUserId.Value);
                conn.Open();
                int count = (int)cmd.ExecuteScalar();
                return count > 0;
            }
        }

        private bool CreateUser(RegisterViewModel model)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"INSERT INTO Users (FullName, Email, Password, Phone, Address, Role, CreatedDate, IsActive) 
                                 VALUES (@FullName, @Email, @Password, @Phone, @Address, 'Candidate', GETDATE(), 1)";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@FullName", model.FullName);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                cmd.Parameters.AddWithValue("@Password", HashPassword(model.Password));
                cmd.Parameters.AddWithValue("@Phone", string.IsNullOrEmpty(model.Phone) ? DBNull.Value : (object)model.Phone);
                cmd.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(model.Address) ? DBNull.Value : (object)model.Address);
                conn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private bool CreateEmployerUser(EmployerRegisterViewModel model)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"INSERT INTO Users (FullName, Email, Password, Phone, Address, Role, CreatedDate, IsActive) 
                                 VALUES (@FullName, @Email, @Password, @Phone, @Address, 'Employer', GETDATE(), 1)";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@FullName", model.FullName);
                cmd.Parameters.AddWithValue("@Email", model.Email);
                cmd.Parameters.AddWithValue("@Password", HashPassword(model.Password));
                cmd.Parameters.AddWithValue("@Phone", string.IsNullOrEmpty(model.Phone) ? DBNull.Value : (object)model.Phone);
                cmd.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(model.Address) ? DBNull.Value : (object)model.Address);
                conn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private User GetUserById(int userId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT * FROM Users WHERE UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new User
                    {
                        UserId = Convert.ToInt32(reader["UserId"]),
                        FullName = reader["FullName"].ToString(),
                        Email = reader["Email"].ToString(),
                        Phone = reader["Phone"]?.ToString(),
                        Address = reader["Address"]?.ToString(),
                        Role = reader["Role"].ToString(),
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : (DateTime?)null,
                        IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"]),
                        MustChangePassword = HasColumn(reader, "MustChangePassword") &&
                            reader["MustChangePassword"] != DBNull.Value &&
                            Convert.ToBoolean(reader["MustChangePassword"]),
                        TwoFactorEnabled = HasColumn(reader, "TwoFactorEnabled") &&
                            reader["TwoFactorEnabled"] != DBNull.Value &&
                            Convert.ToBoolean(reader["TwoFactorEnabled"]),
                        TwoFactorSecret = HasColumn(reader, "TwoFactorSecret")
                            ? reader["TwoFactorSecret"]?.ToString()
                            : null,
                        TwoFactorCreatedAt = HasColumn(reader, "TwoFactorCreatedAt") && reader["TwoFactorCreatedAt"] != DBNull.Value
                            ? Convert.ToDateTime(reader["TwoFactorCreatedAt"])
                            : (DateTime?)null,
                        TwoFactorLastVerifiedAt = HasColumn(reader, "TwoFactorLastVerifiedAt") && reader["TwoFactorLastVerifiedAt"] != DBNull.Value
                            ? Convert.ToDateTime(reader["TwoFactorLastVerifiedAt"])
                            : (DateTime?)null
                    };
                }
            }
            return null;
        }

        private void UpdateUser(User user)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE Users SET FullName = @FullName, Phone = @Phone, Address = @Address WHERE UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@FullName", user.FullName);
                cmd.Parameters.AddWithValue("@Phone", string.IsNullOrEmpty(user.Phone) ? DBNull.Value : (object)user.Phone);
                cmd.Parameters.AddWithValue("@Address", string.IsNullOrEmpty(user.Address) ? DBNull.Value : (object)user.Address);
                cmd.Parameters.AddWithValue("@UserId", user.UserId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private bool CheckCurrentPassword(int userId, string password)
        {
            string hashedPassword = HashPassword(password);
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM Users WHERE UserId = @UserId AND Password = @Password";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);
                conn.Open();
                int count = (int)cmd.ExecuteScalar();
                return count > 0;
            }
        }

        private bool UpdatePassword(int userId, string newPassword)
        {
            string hashedPassword = HashPassword(newPassword);
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE Users SET Password = @Password, MustChangePassword = 0 WHERE UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Password", hashedPassword);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private List<Application> GetUserApplications(int userId)
        {
            var applications = new List<Application>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT a.*, j.Title as JobTitle, j.JobId, c.CompanyName 
                    FROM Applications a
                    INNER JOIN Jobs j ON a.JobId = j.JobId
                    INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                    WHERE a.UserId = @UserId
                    ORDER BY a.ApplyDate DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    applications.Add(new Application
                    {
                        ApplicationId = Convert.ToInt32(reader["ApplicationId"]),
                        JobId = Convert.ToInt32(reader["JobId"]),
                        CVFile = reader["CVFile"]?.ToString(),
                        CoverLetter = reader["CoverLetter"]?.ToString(),
                        Status = reader["Status"]?.ToString(),
                        ApplyDate = reader["ApplyDate"] != DBNull.Value ? Convert.ToDateTime(reader["ApplyDate"]) : (DateTime?)null,
                        Job = new Job
                        {
                            JobId = Convert.ToInt32(reader["JobId"]),
                            Title = reader["JobTitle"]?.ToString(),
                            Company = new Company { CompanyName = reader["CompanyName"]?.ToString() }
                        }
                    });
                }
            }
            return applications;
        }

        private List<Job> GetAllJobs()
        {
            var jobs = new List<Job>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT j.*, c.CompanyName, c.Logo 
                    FROM Jobs j
                    LEFT JOIN Companies c ON j.CompanyId = c.CompanyId
                    WHERE j.IsActive = 1
                    ORDER BY j.CreatedDate DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    jobs.Add(new Job
                    {
                        JobId = Convert.ToInt32(reader["JobId"]),
                        Title = reader["Title"].ToString(),
                        Location = reader["Location"]?.ToString() ?? "",
                        SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                        SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                        JobType = reader["JobType"]?.ToString() ?? "",
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : DateTime.Now,
                        Company = new Company
                        {
                            CompanyName = reader["CompanyName"]?.ToString() ?? "",
                            Logo = reader["Logo"]?.ToString()
                        }
                    });
                }
            }
            return jobs;
        }

        private List<Company> GetFollowedCompanies(int userId)
        {
            var companies = new List<Company>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT c.* FROM Companies c
                    INNER JOIN Follows f ON c.CompanyId = f.CompanyId
                    WHERE f.UserId = @UserId
                    ORDER BY f.FollowedDate DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    companies.Add(new Company
                    {
                        CompanyId = Convert.ToInt32(reader["CompanyId"]),
                        CompanyName = reader["CompanyName"].ToString(),
                        Logo = reader["Logo"]?.ToString(),
                        Address = reader["Address"]?.ToString(),
                        Description = reader["Description"]?.ToString()
                    });
                }
            }
            return companies;
        }

        private bool CheckApplicationOwnership(int applicationId, int userId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM Applications WHERE ApplicationId = @ApplicationId AND UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ApplicationId", applicationId);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                int count = (int)cmd.ExecuteScalar();
                return count > 0;
            }
        }

        private bool WithdrawApplicationFromDb(int applicationId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string updateQuery = @"UPDATE Applications
                                       SET Status = 'Withdrawn',
                                           Notes = COALESCE(Notes + CHAR(13) + CHAR(10), '') + N'Ứng viên đã rút đơn.'
                                       WHERE ApplicationId = @ApplicationId
                                         AND ISNULL(Status, 'Pending') <> 'Withdrawn'";
                SqlCommand deleteCmd = new SqlCommand(updateQuery, conn);
                deleteCmd.Parameters.AddWithValue("@ApplicationId", applicationId);
                conn.Open();
                try
                {
                    int rows = deleteCmd.ExecuteNonQuery();
                    return rows > 0;
                }
                catch (SqlException ex) when (ex.Message.Contains("CHECK constraint") && ex.Message.Contains("Applications"))
                {
                    DropApplicationStatusConstraints(conn);
                    int rows = deleteCmd.ExecuteNonQuery();
                    return rows > 0;
                }
            }
        }

        private bool IsKnownInactiveAccount(string email, SqlConnection existingConnection = null)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(_connectionString))
            {
                return false;
            }

            var ownsConnection = existingConnection == null;
            SqlConnection conn = existingConnection ?? new SqlConnection(_connectionString);
            try
            {
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    conn.Open();
                }

                string query = "SELECT TOP 1 IsActive FROM Users WHERE Email = @Email";
                using SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Email", email.Trim());
                var value = cmd.ExecuteScalar();
                return value != null && value != DBNull.Value && !Convert.ToBoolean(value);
            }
            finally
            {
                if (ownsConnection)
                {
                    conn.Dispose();
                }
            }
        }

        private void LockAccountByEmail(SqlConnection conn, string email)
        {
            string query = "UPDATE Users SET IsActive = 0 WHERE Email = @Email";
            using SqlCommand cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Email", email.Trim());
            cmd.ExecuteNonQuery();
        }

        private void EnsurePasswordResetSchema(SqlConnection conn)
        {
            string query = @"
IF COL_LENGTH('PasswordResets', 'FailedAttempts') IS NULL
BEGIN
    ALTER TABLE PasswordResets ADD FailedAttempts INT NOT NULL DEFAULT 0;
END";
            using SqlCommand cmd = new SqlCommand(query, conn);
            cmd.ExecuteNonQuery();
        }

        private static void DropApplicationStatusConstraints(SqlConnection conn)
        {
            string sql = @"
DECLARE @constraintName NVARCHAR(128);
WHILE 1 = 1
BEGIN
    SELECT TOP 1 @constraintName = cc.name
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID('Applications')
      AND (cc.definition LIKE '%Status%' OR cc.definition LIKE '%[Status]%' OR cc.name LIKE 'CK__Applicati__Statu%');
    IF @constraintName IS NULL BREAK;
    EXEC('ALTER TABLE Applications DROP CONSTRAINT [' + @constraintName + ']');
    SET @constraintName = NULL;
END";
            using var cmd = new SqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }

        private void PrepareTwoFactorSetup(User user)
        {
            if (user == null)
            {
                return;
            }

            ViewBag.TwoFactorEnabled = user.TwoFactorEnabled;
            ViewBag.TwoFactorLastVerifiedAt = user.TwoFactorLastVerifiedAt;

            if (user.TwoFactorEnabled)
            {
                return;
            }

            var protectedSecret = HttpContext.Session.GetString("PendingTwoFactorSetupSecret");
            var secret = UnprotectTwoFactorSecret(protectedSecret);
            if (string.IsNullOrWhiteSpace(secret))
            {
                secret = TwoFactorTotpService.GenerateSecret();
                HttpContext.Session.SetString("PendingTwoFactorSetupSecret", ProtectTwoFactorSecret(secret));
            }

            ViewBag.TwoFactorSetupKey = secret;
            ViewBag.TwoFactorOtpAuthUri = TwoFactorTotpService.BuildOtpAuthUri("JobPortal", user.Email, secret);
        }

        private string ProtectTwoFactorSecret(string secret)
        {
            return _twoFactorProtector.Protect(secret);
        }

        private string UnprotectTwoFactorSecret(string protectedSecret)
        {
            if (string.IsNullOrWhiteSpace(protectedSecret))
            {
                return string.Empty;
            }

            try
            {
                return _twoFactorProtector.Unprotect(protectedSecret);
            }
            catch
            {
                return protectedSecret;
            }
        }

        private void SaveTwoFactorSecret(int userId, string protectedSecret)
        {
            using var conn = new SqlConnection(_connectionString);
            const string query = @"
UPDATE Users
SET TwoFactorEnabled = 1,
    TwoFactorSecret = @Secret,
    TwoFactorCreatedAt = SYSUTCDATETIME(),
    TwoFactorLastVerifiedAt = SYSUTCDATETIME()
WHERE UserId = @UserId";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            cmd.Parameters.AddWithValue("@Secret", protectedSecret);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private void DisableTwoFactorForUser(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            const string query = @"
UPDATE Users
SET TwoFactorEnabled = 0,
    TwoFactorSecret = NULL,
    TwoFactorCreatedAt = NULL,
    TwoFactorLastVerifiedAt = NULL
WHERE UserId = @UserId";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private void UpdateTwoFactorLastVerified(int userId)
        {
            using var conn = new SqlConnection(_connectionString);
            const string query = "UPDATE Users SET TwoFactorLastVerifiedAt = SYSUTCDATETIME() WHERE UserId = @UserId";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
