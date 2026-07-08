using BTL_2.Models;
using BTL_2.Models.ViewModels;
using BTL_2.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTL_2.Controllers
{
    public class JobsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly ISecureCVService _secureCVService;
        private readonly IActivityLogService _activityLogService;

        public JobsController(
            IConfiguration configuration,
            ISecureCVService secureCVService,
            IActivityLogService activityLogService)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _secureCVService = secureCVService;
            _activityLogService = activityLogService;
        }

        // GET: Jobs
        public IActionResult Index(string keyword, string location, string jobType, int page = 1)
        {
            try
            {
                int pageSize = 10;
                var jobs = SearchJobs(keyword, location, jobType, page, pageSize);
                var totalJobs = CountJobs(keyword, location, jobType);

                ViewBag.Keyword = keyword;
                ViewBag.Location = location;
                ViewBag.JobType = jobType;
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = (int)Math.Ceiling((double)totalJobs / pageSize);

                return View(jobs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Index: {ex.Message}");
                return View(new List<Job>());
            }
        }

        // GET: Jobs/Details/5
        public IActionResult Details(int id)
        {
            try
            {
                var job = GetJobById(id);
                if (job == null)
                {
                    return NotFound();
                }

                IncrementJobViews(id);
                var relatedJobs = GetRelatedJobs(job.CompanyId ?? 0, id);

                bool hasApplied = false;
                if (HttpContext.Session.GetString("UserId") != null)
                {
                    var userId = int.Parse(HttpContext.Session.GetString("UserId"));
                    hasApplied = CheckIfApplied(userId, id);
                }

                var viewModel = new JobDetailsViewModel
                {
                    Job = job,
                    RelatedJobs = relatedJobs,
                    HasApplied = hasApplied
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Details: {ex.Message}");
                return NotFound();
            }
        }

        // GET: Jobs/Create
        public IActionResult Create()
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                TempData["ErrorMessage"] = "Chỉ quản trị viên mới có quyền quản lý tin tuyển dụng.";
                return RedirectToAction("Login", "Account");
            }

            try
            {
                var employerIdStr = HttpContext.Session.GetString("UserId");
                if (string.IsNullOrEmpty(employerIdStr))
                {
                    TempData["ErrorMessage"] = "Vui lòng đăng nhập lại";
                    return RedirectToAction("Login", "Account");
                }

                var employerId = int.Parse(employerIdStr);
                var companies = GetEmployerCompanies(employerId);

                var model = new JobViewModel
                {
                    Companies = companies ?? new List<Company>(),
                    Deadline = DateTime.Now.AddDays(30),
                    NumberOfRecruits = 1,
                    JobType = "Toàn thời gian",
                    Experience = "Không yêu cầu",
                    Education = "Không yêu cầu",
                    IsFeatured = false,
                    IsUrgent = false,
                    IsRemote = false
                };

                if (companies == null || companies.Count == 0)
                {
                    TempData["WarningMessage"] = "Bạn chưa có công ty nào. Vui lòng tạo công ty trước khi đăng tin.";
                }

                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Create GET: {ex.Message}");
                var defaultModel = new JobViewModel
                {
                    Companies = new List<Company>(),
                    Deadline = DateTime.Now.AddDays(30),
                    NumberOfRecruits = 1,
                    JobType = "Toàn thời gian"
                };
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                return View(defaultModel);
            }
        }

        // POST: Jobs/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JobViewModel model)
        {
            try
            {
                if (HttpContext.Session.GetString("UserRole") != "Admin")
                {
                    TempData["ErrorMessage"] = "Chỉ quản trị viên mới có quyền quản lý tin tuyển dụng.";
                    return RedirectToAction("Login", "Account");
                }

                var employerIdStr = HttpContext.Session.GetString("UserId");
                if (string.IsNullOrEmpty(employerIdStr))
                {
                    return RedirectToAction("Login", "Account");
                }

                var employerId = int.Parse(employerIdStr);
                model.Companies = GetEmployerCompanies(employerId);

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (!model.CompanyId.HasValue)
                {
                    ModelState.AddModelError("CompanyId", "Vui lòng chọn công ty");
                    return View(model);
                }

                string imagePath = null;
                if (model.JobImageFile != null && model.JobImageFile.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/jobs");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var fileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(model.JobImageFile.FileName);
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.JobImageFile.CopyToAsync(stream);
                    }

                    imagePath = "/uploads/jobs/" + fileName;
                }

                var job = new Job
                {
                    Title = model.Title?.Trim() ?? "",
                    CompanyId = model.CompanyId.Value,
                    SalaryMin = model.SalaryMin,
                    SalaryMax = model.SalaryMax,
                    Location = model.Location?.Trim() ?? "",
                    JobType = model.JobType ?? "",
                    Description = model.Description?.Trim() ?? "",
                    Requirement = model.Requirement?.Trim() ?? "",
                    Benefit = model.Benefit?.Trim() ?? "",
                    Skills = model.Skills?.Trim(),
                    Experience = model.Experience,
                    Education = model.Education,
                    NumberOfRecruits = model.NumberOfRecruits ?? 1,
                    Deadline = model.Deadline ?? DateTime.Now.AddDays(30),
                    JobImage = imagePath,
                    VideoUrl = model.VideoUrl?.Trim(),
                    Tags = model.Tags?.Trim(),
                    IsFeatured = model.IsFeatured,
                    IsUrgent = model.IsUrgent,
                    IsRemote = model.IsRemote,
                    CreatedDate = DateTime.Now,
                    IsActive = true,
                    Views = 0
                };

                int newJobId = CreateJob(job);
                TempData["SuccessMessage"] = "Đăng tin tuyển dụng thành công!";
                return RedirectToAction("Index", "Jobs");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                return View(model);
            }
        }

        // GET: Jobs/Edit/5
        public IActionResult Edit(int id)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                TempData["ErrorMessage"] = "Chỉ quản trị viên mới có quyền chỉnh sửa tin tuyển dụng.";
                return RedirectToAction("Login", "Account");
            }

            try
            {
                var job = GetJobById(id);
                if (job == null)
                {
                    return NotFound();
                }

                var model = new JobViewModel
                {
                    JobId = job.JobId,
                    Title = job.Title,
                    CompanyId = job.CompanyId,
                    SalaryMin = job.SalaryMin,
                    SalaryMax = job.SalaryMax,
                    Location = job.Location,
                    JobType = job.JobType,
                    Description = job.Description,
                    Requirement = job.Requirement,
                    Benefit = job.Benefit,
                    Skills = job.Skills,
                    Experience = job.Experience,
                    Education = job.Education,
                    NumberOfRecruits = job.NumberOfRecruits,
                    Deadline = job.Deadline,
                    JobImage = job.JobImage,
                    VideoUrl = job.VideoUrl,
                    Tags = job.Tags,
                    IsFeatured = job.IsFeatured,
                    IsUrgent = job.IsUrgent,
                    IsRemote = job.IsRemote,
                    Companies = GetEmployerCompanies(job.Company?.EmployerId ?? 0)
                };

                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Edit GET: {ex.Message}");
                return NotFound();
            }
        }

        // POST: Jobs/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, JobViewModel model)
        {
            if (HttpContext.Session.GetString("UserRole") != "Admin")
            {
                TempData["ErrorMessage"] = "Chỉ quản trị viên mới có quyền chỉnh sửa tin tuyển dụng.";
                return RedirectToAction("Login", "Account");
            }

            if (id != model.JobId)
            {
                return NotFound();
            }

            try
            {
                var employerId = int.Parse(HttpContext.Session.GetString("UserId"));
                model.Companies = GetEmployerCompanies(employerId);

                if (ModelState.IsValid)
                {
                    string imagePath = model.JobImage;
                    if (model.JobImageFile != null && model.JobImageFile.Length > 0)
                    {
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/jobs");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var fileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(model.JobImageFile.FileName);
                        var filePath = Path.Combine(uploadsFolder, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await model.JobImageFile.CopyToAsync(stream);
                        }

                        imagePath = "/uploads/jobs/" + fileName;
                    }

                    var job = new Job
                    {
                        JobId = model.JobId.Value,
                        Title = model.Title,
                        CompanyId = model.CompanyId.Value,
                        SalaryMin = model.SalaryMin,
                        SalaryMax = model.SalaryMax,
                        Location = model.Location,
                        JobType = model.JobType,
                        Description = model.Description,
                        Requirement = model.Requirement,
                        Benefit = model.Benefit,
                        Skills = model.Skills,
                        Experience = model.Experience,
                        Education = model.Education,
                        NumberOfRecruits = model.NumberOfRecruits ?? 1,
                        Deadline = model.Deadline ?? DateTime.Now.AddDays(30),
                        JobImage = imagePath,
                        VideoUrl = model.VideoUrl,
                        Tags = model.Tags,
                        IsFeatured = model.IsFeatured,
                        IsUrgent = model.IsUrgent,
                        IsRemote = model.IsRemote
                    };

                    UpdateJob(job);
                    TempData["SuccessMessage"] = "Cập nhật thành công!";
                    return RedirectToAction("Index", "Jobs");
                }

                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Edit POST: {ex.Message}");
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                return View(model);
            }
        }

        // POST: Jobs/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            try
            {
                if (HttpContext.Session.GetString("UserRole") != "Admin")
                {
                    return Json(new { success = false, message = "Không có quyền thực hiện" });
                }

                DeleteJob(id);
                return Json(new { success = true, message = "Xóa thành công" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Delete: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra" });
            }
        }

        // GET: Jobs/Apply/5
        public IActionResult Apply(int id)
        {
            if (HttpContext.Session.GetString("UserId") == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (HttpContext.Session.GetString("UserRole") != "Candidate")
            {
                TempData["ErrorMessage"] = "Chỉ ứng viên mới có thể ứng tuyển";
                return RedirectToAction("Details", new { id });
            }

            try
            {
                var job = GetJobById(id);
                if (job == null)
                {
                    return NotFound();
                }

                var userId = int.Parse(HttpContext.Session.GetString("UserId"));
                bool hasApplied = CheckIfApplied(userId, id);

                if (hasApplied)
                {
                    TempData["ErrorMessage"] = "Bạn đang có hồ sơ ứng tuyển công việc này. Nếu hồ sơ bị từ chối hoặc đã rút, bạn có thể nộp lại.";
                    return RedirectToAction("Details", new { id });
                }

                var viewModel = new ApplicationViewModel
                {
                    JobId = id,
                    JobTitle = job.Title,
                    CompanyName = job.Company?.CompanyName
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Apply GET: {ex.Message}");
                return NotFound();
            }
        }

        // POST: Jobs/Apply/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(int id, ApplicationViewModel model)
        {
            try
            {
                Console.WriteLine($"=== APPLY POST CALLED === JobId: {id}");

                // Kiểm tra ModelState
                if (!ModelState.IsValid)
                {
                    var job = GetJobById(id);
                    if (job != null)
                    {
                        model.JobTitle = job.Title;
                        model.CompanyName = job.Company?.CompanyName;
                    }
                    return View(model);
                }

                // Kiểm tra file CV
                if (model.CVFile == null || model.CVFile.Length == 0)
                {
                    ModelState.AddModelError("CVFile", "Vui lòng chọn file CV");
                    var job = GetJobById(id);
                    if (job != null)
                    {
                        model.JobTitle = job.Title;
                        model.CompanyName = job.Company?.CompanyName;
                    }
                    return View(model);
                }

                // Kiểm tra định dạng file
                var allowedExtensions = new[] { ".pdf", ".doc", ".docx" };
                var fileExtension = Path.GetExtension(model.CVFile.FileName).ToLower();

                if (!allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("CVFile", "Chỉ chấp nhận file PDF, DOC, DOCX");
                    var job = GetJobById(id);
                    if (job != null)
                    {
                        model.JobTitle = job.Title;
                        model.CompanyName = job.Company?.CompanyName;
                    }
                    return View(model);
                }

                // Kiểm tra kích thước file (max 5MB)
                if (model.CVFile.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("CVFile", "File không được vượt quá 5MB");
                    var job = GetJobById(id);
                    if (job != null)
                    {
                        model.JobTitle = job.Title;
                        model.CompanyName = job.Company?.CompanyName;
                    }
                    return View(model);
                }

                // Kiểm tra đăng nhập
                var userIdStr = HttpContext.Session.GetString("UserId");
                if (string.IsNullOrEmpty(userIdStr))
                {
                    return RedirectToAction("Login", "Account");
                }

                var userRole = HttpContext.Session.GetString("UserRole");
                if (userRole != "Candidate")
                {
                    TempData["ErrorMessage"] = "Chỉ ứng viên mới có thể ứng tuyển";
                    return RedirectToAction("Details", new { id });
                }

                if (!int.TryParse(userIdStr, out var userId))
                {
                    HttpContext.Session.Clear();
                    return RedirectToAction("Login", "Account");
                }

                // Kiểm tra đã ứng tuyển chưa
                if (CheckIfApplied(userId, id))
                {
                    TempData["ErrorMessage"] = "Bạn đang có hồ sơ ứng tuyển công việc này. Nếu hồ sơ bị từ chối hoặc đã rút, bạn có thể nộp lại.";
                    return RedirectToAction("Details", new { id });
                }

                var cvMetadata = await _secureCVService.UploadSecureCVAsync(model.CVFile, userIdStr, id);

                // Tạo mã hash
                var applicationHash = CreateApplicationHash(id, userIdStr, DateTime.Now);

                // Lưu vào database
                var application = new Application
                {
                    JobId = id,
                    UserId = userId,
                    CVFile = cvMetadata.StoredFileName,
                    OriginalFileName = model.CVFile.FileName,
                    CoverLetter = model.CoverLetter ?? "",
                    ApplyDate = DateTime.Now,
                    Status = "Pending",
                    ApplicationHash = applicationHash,
                    CVMetadataId = cvMetadata.Id,
                    AccessToken = Guid.NewGuid(),
                    TokenExpireTime = DateTime.UtcNow.AddMinutes(_configuration.GetValue("CVSettings:TokenExpiryMinutes", 5)),
                    FileChecksum = cvMetadata.SHA256Hash,
                    Nonce = cvMetadata.Nonce,
                    DownloadCount = 0,
                    IsExpired = false
                };

                SaveApplication(application);

                TempData["SuccessMessage"] = "Ứng tuyển thành công! Hồ sơ của bạn đã được gửi.";
                return RedirectToAction("MyApplications", "Account");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Apply POST: {ex.Message}");
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;

                var job = GetJobById(id);
                if (job != null)
                {
                    model.JobTitle = job.Title;
                    model.CompanyName = job.Company?.CompanyName;
                }
                return View(model);
            }
        }

        // GET: Jobs/SavedJobs
        [HttpGet]
        public IActionResult SavedJobs()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                TempData["ErrorMessage"] = "Vui lòng đăng nhập để xem việc đã lưu";
                return RedirectToAction("Login", "Account");
            }

            var savedJobs = GetSavedJobs(int.Parse(userId));
            return View(savedJobs);
        }

        // POST: Jobs/SaveJob
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveJob(int jobId)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập" });
            }

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string checkQuery = "SELECT COUNT(*) FROM SavedJobs WHERE UserId = @UserId AND JobId = @JobId";
                    SqlCommand checkCmd = new SqlCommand(checkQuery, conn);
                    checkCmd.Parameters.AddWithValue("@UserId", int.Parse(userId));
                    checkCmd.Parameters.AddWithValue("@JobId", jobId);
                    conn.Open();
                    int count = (int)checkCmd.ExecuteScalar();

                    if (count > 0)
                    {
                        string deleteQuery = "DELETE FROM SavedJobs WHERE UserId = @UserId AND JobId = @JobId";
                        SqlCommand deleteCmd = new SqlCommand(deleteQuery, conn);
                        deleteCmd.Parameters.AddWithValue("@UserId", int.Parse(userId));
                        deleteCmd.Parameters.AddWithValue("@JobId", jobId);
                        deleteCmd.ExecuteNonQuery();
                        return Json(new { success = true, saved = false, message = "Đã bỏ lưu tin tuyển dụng" });
                    }
                    else
                    {
                        string insertQuery = "INSERT INTO SavedJobs (UserId, JobId, SavedDate) VALUES (@UserId, @JobId, GETDATE())";
                        SqlCommand insertCmd = new SqlCommand(insertQuery, conn);
                        insertCmd.Parameters.AddWithValue("@UserId", int.Parse(userId));
                        insertCmd.Parameters.AddWithValue("@JobId", jobId);
                        insertCmd.ExecuteNonQuery();
                        return Json(new { success = true, saved = true, message = "Đã lưu tin tuyển dụng" });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // GET: Jobs/GetSavedJobIds
        [HttpGet]
        public IActionResult GetSavedJobIds()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new List<int>());
            }

            var savedIds = new List<int>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT JobId FROM SavedJobs WHERE UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", int.Parse(userId));
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    savedIds.Add(Convert.ToInt32(reader["JobId"]));
                }
            }
            return Json(savedIds);
        }

        // GET: Jobs/ManageApplications
        [HttpGet]
        public async Task<IActionResult> ManageApplications(int? jobId)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            if (userRole != "Employer" && userRole != "Admin")
            {
                TempData["ErrorMessage"] = "Chỉ nhà tuyển dụng mới có thể xem danh sách ứng viên";
                return RedirectToAction("Index", "Jobs");
            }

            try
            {
                List<ApplicationWithDetails> applications = new List<ApplicationWithDetails>();

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query;
                    SqlCommand cmd;

                    if (userRole == "Admin")
                    {
                        if (jobId.HasValue)
                        {
                            query = @"SELECT a.*, j.Title as JobTitle, u.FullName as UserName, u.Email as UserEmail, m.ExpireTime AS CVExpireTime
                                     FROM Applications a
                                     INNER JOIN Jobs j ON a.JobId = j.JobId
                                     INNER JOIN Users u ON a.UserId = u.UserId
                                     LEFT JOIN CVMetadata m ON a.CVMetadataId = m.Id
                                     WHERE a.JobId = @JobId
                                     ORDER BY a.ApplyDate DESC";
                            cmd = new SqlCommand(query, conn);
                            cmd.Parameters.AddWithValue("@JobId", jobId.Value);
                        }
                        else
                        {
                            query = @"SELECT a.*, j.Title as JobTitle, u.FullName as UserName, u.Email as UserEmail, m.ExpireTime AS CVExpireTime
                                     FROM Applications a
                                     INNER JOIN Jobs j ON a.JobId = j.JobId
                                     INNER JOIN Users u ON a.UserId = u.UserId
                                     LEFT JOIN CVMetadata m ON a.CVMetadataId = m.Id
                                     ORDER BY a.ApplyDate DESC";
                            cmd = new SqlCommand(query, conn);
                        }
                    }
                    else
                    {
                        if (jobId.HasValue)
                        {
                            query = @"SELECT a.*, j.Title as JobTitle, u.FullName as UserName, u.Email as UserEmail, m.ExpireTime AS CVExpireTime
                                     FROM Applications a
                                     INNER JOIN Jobs j ON a.JobId = j.JobId
                                     INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                                     INNER JOIN Users u ON a.UserId = u.UserId
                                     LEFT JOIN CVMetadata m ON a.CVMetadataId = m.Id
                                     WHERE a.JobId = @JobId AND c.EmployerId = @EmployerId
                                     ORDER BY a.ApplyDate DESC";
                            cmd = new SqlCommand(query, conn);
                            cmd.Parameters.AddWithValue("@JobId", jobId.Value);
                            cmd.Parameters.AddWithValue("@EmployerId", int.Parse(userId));
                        }
                        else
                        {
                            query = @"SELECT a.*, j.Title as JobTitle, u.FullName as UserName, u.Email as UserEmail, m.ExpireTime AS CVExpireTime
                                     FROM Applications a
                                     INNER JOIN Jobs j ON a.JobId = j.JobId
                                     INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                                     INNER JOIN Users u ON a.UserId = u.UserId
                                     LEFT JOIN CVMetadata m ON a.CVMetadataId = m.Id
                                     WHERE c.EmployerId = @EmployerId
                                     ORDER BY a.ApplyDate DESC";
                            cmd = new SqlCommand(query, conn);
                            cmd.Parameters.AddWithValue("@EmployerId", int.Parse(userId));
                        }
                    }

                    conn.Open();
                    SqlDataReader reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        applications.Add(new ApplicationWithDetails
                        {
                            ApplicationId = Convert.ToInt32(reader["ApplicationId"]),
                            JobId = reader["JobId"] != DBNull.Value ? Convert.ToInt32(reader["JobId"]) : (int?)null,
                            JobTitle = reader["JobTitle"]?.ToString(),
                            UserId = reader["UserId"] != DBNull.Value ? Convert.ToInt32(reader["UserId"]) : (int?)null,
                            UserName = reader["UserName"]?.ToString(),
                            UserEmail = reader["UserEmail"]?.ToString(),
                            OriginalFileName = reader["OriginalFileName"]?.ToString(),
                            CoverLetter = reader["CoverLetter"]?.ToString(),
                            Status = reader["Status"]?.ToString(),
                            ApplyDate = reader["ApplyDate"] != DBNull.Value ? Convert.ToDateTime(reader["ApplyDate"]) : (DateTime?)null,
                            CVMetadataId = HasColumn(reader, "CVMetadataId") && reader["CVMetadataId"] != DBNull.Value ? Convert.ToInt32(reader["CVMetadataId"]) : (int?)null,
                            CVExpireTime = HasColumn(reader, "CVExpireTime") && reader["CVExpireTime"] != DBNull.Value ? Convert.ToDateTime(reader["CVExpireTime"]) : (DateTime?)null
                        });
                    }
                }

                await AttachCVIntegrityStatusAsync(applications);

                var jobs = userRole == "Admin"
                    ? GetAllJobSummaries()
                    : GetEmployerJobs(int.Parse(userId));
                ViewBag.Jobs = jobs;
                ViewBag.SelectedJobId = jobId;

                return View(applications);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ManageApplications: {ex.Message}");
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                return View(new List<ApplicationWithDetails>());
            }
        }

        [HttpGet]
        public IActionResult CVAccessLogs()
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            if (userRole == "Admin")
            {
                return RedirectToAction("CVAccessLogs", "Admin");
            }

            if (userRole != "Employer")
            {
                TempData["ErrorMessage"] = "Bạn không có quyền xem log truy cập CV";
                return RedirectToAction("Index", "Jobs");
            }

            var logs = new List<CVAccessLogViewModel>();
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT TOP 100 l.Id, l.RecruiterId, l.CVMetadataId, l.AccessTime, l.IPAddress, l.Status, l.ErrorMessage, l.UserAgent,
                       u.FullName AS RecruiterName, u.Email AS RecruiterEmail,
                       m.OriginalFileName, m.CandidateId, m.JobId,
                       j.Title AS JobTitle
                FROM CVActivityLogs l
                INNER JOIN CVMetadata m ON l.CVMetadataId = m.Id
                INNER JOIN Jobs j ON m.JobId = j.JobId
                INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                LEFT JOIN Users u ON TRY_CONVERT(INT, l.RecruiterId) = u.UserId
                WHERE c.EmployerId = @EmployerId AND ISNULL(m.IsDeleted, 0) = 0
                ORDER BY l.AccessTime DESC, l.Id DESC", conn);
            cmd.Parameters.AddWithValue("@EmployerId", int.Parse(userId));

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

            return View("~/Views/Admin/CVAccessLogs.cshtml", logs);
        }

        // GET: Jobs/DownloadDecryptedCV
        // Hỗ trợ link cũ/dán URL trực tiếp để tránh lỗi 405. Giao diện mới vẫn dùng POST + anti-forgery.
        [HttpGet]
        public Task<IActionResult> DownloadDecryptedCV(int applicationId)
        {
            return DownloadDecryptedCVCore(applicationId);
        }

        // POST: Jobs/DownloadDecryptedCV
        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName("DownloadDecryptedCV")]
        public Task<IActionResult> DownloadDecryptedCVPost(int applicationId)
        {
            return DownloadDecryptedCVCore(applicationId);
        }

        private async Task<IActionResult> DownloadDecryptedCVCore(int applicationId)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            if (userRole != "Employer" && userRole != "Admin")
            {
                TempData["ErrorMessage"] = "Bạn không có quyền tải CV này";
                return RedirectToAction("Index", "Jobs");
            }

            try
            {
                var application = GetApplicationById(applicationId);
                if (application == null)
                {
                    return NotFound("Không tìm thấy đơn ứng tuyển");
                }

                if (userRole == "Employer")
                {
                    var job = GetJobById(application.JobId ?? 0);
                    if (job == null || job.Company?.EmployerId != int.Parse(userId))
                    {
                        TempData["ErrorMessage"] = "Bạn không có quyền truy cập CV này";
                        return RedirectToAction("ManageApplications");
                    }
                }

                if (!application.CVMetadataId.HasValue)
                {
                    TempData["ErrorMessage"] = "CV này chưa có metadata bảo mật";
                    return RedirectToAction("ManageApplications");
                }

                var integrity = await _secureCVService.CheckCVIntegrityAsync(application.CVMetadataId.Value);
                if (!integrity.isValid || !string.Equals(integrity.status, "Valid", StringComparison.OrdinalIgnoreCase))
                {
                    TempData["ErrorMessage"] = integrity.status == "DecryptFailed"
                        ? $"CV #{application.CVMetadataId.Value} đang quét ra DecryptFailed nên không thể tải: {integrity.detail}"
                        : $"CV #{application.CVMetadataId.Value} chưa đạt trạng thái Valid ({integrity.status}) nên không thể tải: {integrity.detail}";
                    return RedirectToAction("ManageApplications");
                }

                var token = await _secureCVService.GenerateDownloadTokenAsync(application.CVMetadataId.Value, int.Parse(userId));
                var downloadUrl = Url.Action(
                    "DownloadSecureCV",
                    "Jobs",
                    new { cvId = application.CVMetadataId.Value, token },
                    Request.Scheme);

                TempData["SuccessMessage"] = "Tạo token tải CV thành công. Link chỉ dùng một lần, ràng buộc với tài khoản và phiên đăng nhập hiện tại.";
                TempData["DownloadUrl"] = downloadUrl;
                TempData["DownloadToken"] = token;
                return RedirectToAction("ManageApplications");
            }
            catch (UnauthorizedAccessException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("ManageApplications");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating CV download token: {ex.Message}");
                TempData["ErrorMessage"] = "Có lỗi khi tạo token tải CV: " + ex.Message;
                return RedirectToAction("ManageApplications");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadSecureCV(int cvId, string token)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }

            if (userRole != "Employer" && userRole != "Admin")
            {
                return CVDownloadUnavailable(
                    403,
                    "Không có quyền tải CV",
                    "CV này chỉ được tải bởi nhà tuyển dụng đang quản lý tin tuyển dụng tương ứng.",
                    "Tài khoản hiện tại không có quyền truy cập hồ sơ này.",
                    "fa-user-lock",
                    "Không đủ quyền");
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = Request.Headers["User-Agent"].ToString();
            var recruiterId = int.Parse(userId);

            try
            {
                var isValidToken = await _secureCVService.ValidateAndConsumeTokenAsync(token, cvId, recruiterId);
                if (!isValidToken)
                {
                    await _activityLogService.LogCVAccessAsync(userId, cvId, ipAddress, "Failed_InvalidToken", userAgent, "Token invalid or expired");
                    return CVDownloadUnavailable(
                        403,
                        "Link tải CV không còn hiệu lực",
                        "Token tải CV không hợp lệ, đã hết hạn, đã được sử dụng hoặc không thuộc phiên đăng nhập hiện tại.",
                        "Vui lòng quay lại trang quản lý hồ sơ ứng tuyển và tạo link tải mới.",
                        "fa-link-slash",
                        "Token không hợp lệ");
                }

                var result = await _secureCVService.DownloadSecureCVAsync(cvId, recruiterId, ipAddress, userAgent);
                var contentType = GetContentType(result.metadata.FileType);
                IncrementSecureCVDownloadCount(cvId);

                return File(result.fileData, contentType, result.metadata.OriginalFileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                await _activityLogService.LogCVAccessAsync(userId, cvId, ipAddress, "Failed_Unauthorized", userAgent, ex.Message);
                var isPermissionError = ex.Message.Contains("không có quyền", StringComparison.OrdinalIgnoreCase);
                return CVDownloadUnavailable(
                    403,
                    isPermissionError ? "Không có quyền tải CV" : "CV không thể tải xuống",
                    isPermissionError
                        ? "CV này chỉ được tải bởi nhà tuyển dụng đang quản lý tin tuyển dụng tương ứng."
                        : "Link tải hoặc thời hạn tải CV không còn hợp lệ.",
                    ex.Message,
                    isPermissionError ? "fa-user-lock" : "fa-clock",
                    isPermissionError ? "Không đủ quyền" : "Hết hiệu lực");
            }
            catch (FileNotFoundException ex)
            {
                await _activityLogService.LogCVAccessAsync(userId, cvId, ipAddress, "Failed_NotFound", userAgent, ex.Message);
                return CVDownloadUnavailable(
                    404,
                    "Không tìm thấy CV",
                    "File CV hoặc metadata bảo mật không còn tồn tại trong hệ thống.",
                    ex.Message,
                    "fa-file-circle-question",
                    "Không tìm thấy");
            }
            catch (InvalidDataException ex)
            {
                await _activityLogService.LogCVAccessAsync(userId, cvId, ipAddress, "Failed_Integrity", userAgent, ex.Message);
                return CVDownloadUnavailable(
                    403,
                    "CV chưa đạt kiểm tra bảo mật",
                    "Hệ thống đã chặn lượt tải vì file CV không vượt qua kiểm tra toàn vẹn.",
                    ex.Message,
                    "fa-file-shield",
                    "Bị chặn");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading secure CV: {ex.Message}");
                await _activityLogService.LogCVAccessAsync(userId, cvId, ipAddress, "Failed_Error", userAgent, ex.Message);
                TempData["ErrorMessage"] = "Có lỗi khi tải CV: " + ex.Message;
                return RedirectToAction("ManageApplications");
            }
        }

        private IActionResult CVDownloadUnavailable(
            int statusCode,
            string title,
            string message,
            string? detail,
            string iconClass,
            string badgeText)
        {
            Response.StatusCode = statusCode;

            return View("CVAccessDenied", new CVDownloadAccessViewModel
            {
                StatusCode = statusCode,
                Title = title,
                Message = message,
                Detail = detail,
                IconClass = iconClass,
                BadgeText = badgeText,
                PrimaryActionText = "Về quản lý hồ sơ",
                PrimaryActionUrl = Url.Action("ManageApplications", "Jobs") ?? "/Jobs/ManageApplications",
                SecondaryActionText = "Xem việc làm",
                SecondaryActionUrl = Url.Action("Index", "Jobs") ?? "/Jobs",
                SupportText = "Nếu bạn cần tải lại CV, hãy tạo link tải mới từ đúng hồ sơ ứng tuyển."
            });
        }

        // POST: Jobs/UpdateApplicationStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateApplicationStatus(int applicationId, string status)
        {
            var userRole = HttpContext.Session.GetString("UserRole");
            var userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId) || (userRole != "Employer" && userRole != "Admin"))
            {
                return Json(new { success = false, message = "Không có quyền thực hiện" });
            }

            try
            {
                var allowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Pending", "Reviewed", "Interview", "Approved", "Rejected", "Accepted"
                };

                if (!allowedStatuses.Contains(status ?? ""))
                {
                    return Json(new { success = false, message = "Trạng thái không hợp lệ" });
                }

                if (string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase))
                {
                    status = "Approved";
                }

                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query;
                    if (userRole == "Admin")
                    {
                        query = @"UPDATE Applications SET Status = @Status WHERE ApplicationId = @ApplicationId";
                    }
                    else
                    {
                        query = @"UPDATE a
                                  SET a.Status = @Status
                                  FROM Applications a
                                  INNER JOIN Jobs j ON a.JobId = j.JobId
                                  INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                                  WHERE a.ApplicationId = @ApplicationId AND c.EmployerId = @EmployerId";
                    }

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@ApplicationId", applicationId);
                    if (userRole != "Admin")
                    {
                        cmd.Parameters.AddWithValue("@EmployerId", int.Parse(userId));
                    }

                    conn.Open();
                    int rowsAffected = cmd.ExecuteNonQuery();

                    if (rowsAffected > 0)
                    {
                        return Json(new { success = true, message = "Cập nhật trạng thái thành công" });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Không tìm thấy đơn ứng tuyển" });
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // GET: Jobs/TestCompanies
        public IActionResult TestCompanies()
        {
            var employerIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(employerIdStr))
            {
                return Content("❌ Vui lòng đăng nhập với tài khoản Employer");
            }

            var employerId = int.Parse(employerIdStr);
            var companies = GetEmployerCompanies(employerId);

            var html = $@"
            <html>
            <head>
                <title>Test Companies</title>
                <style>
                    body {{ font-family: Arial; margin: 20px; }}
                    table {{ border-collapse: collapse; width: 100%; }}
                    th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
                    th {{ background-color: #4CAF50; color: white; }}
                </style>
            </head>
            <body>
                <h2>Danh sách công ty</h2>
                <p><strong>Employer ID:</strong> {employerId}</p>
                <p><strong>Số lượng:</strong> {companies.Count}</p>
                <table>
                    <tr><th>ID</th><th>Tên công ty</th><th>Logo</th></tr>";
            foreach (var c in companies)
            {
                html += $"<tr><td>{c.CompanyId}</td><td>{c.CompanyName}</td><td>{c.Logo ?? "Không"}</td></tr>";
            }
            html += @"</table><br/><a href='/Jobs/Create'>Đăng tin mới</a></body></html>";
            return Content(html, "text/html");
        }

        // ==================== HELPER METHODS ====================

        private List<Job> SearchJobs(string keyword, string location, string jobType, int page, int pageSize)
        {
            var jobs = new List<Job>();
            int offset = (page - 1) * pageSize;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT j.*, c.CompanyName, c.Logo 
                               FROM Jobs j 
                               LEFT JOIN Companies c ON j.CompanyId = c.CompanyId 
                               WHERE j.IsActive = 1 
                               AND (@Keyword IS NULL OR j.Title LIKE '%' + @Keyword + '%' OR j.Description LIKE '%' + @Keyword + '%')
                               AND (@Location IS NULL OR j.Location LIKE '%' + @Location + '%')
                               AND (@JobType IS NULL OR j.JobType = @JobType)
                               ORDER BY j.CreatedDate DESC
                               OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Keyword", string.IsNullOrEmpty(keyword) ? (object)DBNull.Value : keyword);
                cmd.Parameters.AddWithValue("@Location", string.IsNullOrEmpty(location) ? (object)DBNull.Value : location);
                cmd.Parameters.AddWithValue("@JobType", string.IsNullOrEmpty(jobType) ? (object)DBNull.Value : jobType);
                cmd.Parameters.AddWithValue("@Offset", offset);
                cmd.Parameters.AddWithValue("@PageSize", pageSize);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var job = new Job
                    {
                        JobId = Convert.ToInt32(reader["JobId"]),
                        Title = reader["Title"].ToString(),
                        Location = reader["Location"].ToString(),
                        SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                        SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                        JobType = reader["JobType"]?.ToString(),
                        Description = reader["Description"]?.ToString(),
                        CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                        JobImage = reader["JobImage"]?.ToString(),
                        IsFeatured = reader["IsFeatured"] != DBNull.Value && Convert.ToBoolean(reader["IsFeatured"]),
                        IsUrgent = reader["IsUrgent"] != DBNull.Value && Convert.ToBoolean(reader["IsUrgent"]),
                        IsRemote = reader["IsRemote"] != DBNull.Value && Convert.ToBoolean(reader["IsRemote"]),
                        Company = new Company
                        {
                            CompanyName = reader["CompanyName"]?.ToString() ?? "",
                            Logo = reader["Logo"]?.ToString()
                        }
                    };
                    jobs.Add(job);
                }
            }
            return jobs;
        }

        private int CountJobs(string keyword, string location, string jobType)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT COUNT(*) 
                               FROM Jobs j 
                               WHERE j.IsActive = 1 
                               AND (@Keyword IS NULL OR j.Title LIKE '%' + @Keyword + '%')
                               AND (@Location IS NULL OR j.Location LIKE '%' + @Location + '%')
                               AND (@JobType IS NULL OR j.JobType = @JobType)";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Keyword", string.IsNullOrEmpty(keyword) ? (object)DBNull.Value : keyword);
                cmd.Parameters.AddWithValue("@Location", string.IsNullOrEmpty(location) ? (object)DBNull.Value : location);
                cmd.Parameters.AddWithValue("@JobType", string.IsNullOrEmpty(jobType) ? (object)DBNull.Value : jobType);

                conn.Open();
                return (int)cmd.ExecuteScalar();
            }
        }

        private Job GetJobById(int id)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT j.*, c.CompanyName, c.Logo, c.Description as CompanyDescription, 
                                        c.Website, c.Address as CompanyAddress, c.Phone as CompanyPhone,
                                        c.Email as CompanyEmail, c.EmployerId
                               FROM Jobs j 
                               LEFT JOIN Companies c ON j.CompanyId = c.CompanyId 
                               WHERE j.JobId = @JobId";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@JobId", id);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    return new Job
                    {
                        JobId = Convert.ToInt32(reader["JobId"]),
                        Title = reader["Title"].ToString(),
                        Location = reader["Location"].ToString(),
                        SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                        SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                        JobType = reader["JobType"]?.ToString(),
                        Description = reader["Description"]?.ToString(),
                        Requirement = reader["Requirement"]?.ToString(),
                        Benefit = reader["Benefit"]?.ToString(),
                        Skills = reader["Skills"]?.ToString(),
                        Experience = reader["Experience"]?.ToString(),
                        Education = reader["Education"]?.ToString(),
                        NumberOfRecruits = reader["NumberOfRecruits"] != DBNull.Value ? Convert.ToInt32(reader["NumberOfRecruits"]) : (int?)null,
                        Deadline = reader["Deadline"] != DBNull.Value ? Convert.ToDateTime(reader["Deadline"]) : (DateTime?)null,
                        JobImage = reader["JobImage"]?.ToString(),
                        VideoUrl = reader["VideoUrl"]?.ToString(),
                        Tags = reader["Tags"]?.ToString(),
                        IsFeatured = reader["IsFeatured"] != DBNull.Value && Convert.ToBoolean(reader["IsFeatured"]),
                        IsUrgent = reader["IsUrgent"] != DBNull.Value && Convert.ToBoolean(reader["IsUrgent"]),
                        IsRemote = reader["IsRemote"] != DBNull.Value && Convert.ToBoolean(reader["IsRemote"]),
                        CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                        Views = Convert.ToInt32(reader["Views"]),
                        CompanyId = reader["CompanyId"] != DBNull.Value ? Convert.ToInt32(reader["CompanyId"]) : (int?)null,
                        Company = new Company
                        {
                            CompanyId = reader["CompanyId"] != DBNull.Value ? Convert.ToInt32(reader["CompanyId"]) : 0,
                            CompanyName = reader["CompanyName"]?.ToString() ?? "",
                            Logo = reader["Logo"]?.ToString(),
                            Description = reader["CompanyDescription"]?.ToString(),
                            Website = reader["Website"]?.ToString(),
                            Address = reader["CompanyAddress"]?.ToString(),
                            Phone = reader["CompanyPhone"]?.ToString(),
                            Email = reader["CompanyEmail"]?.ToString(),
                            EmployerId = reader["EmployerId"] != DBNull.Value ? Convert.ToInt32(reader["EmployerId"]) : (int?)null
                        }
                    };
                }
            }
            return null;
        }

        private List<Job> GetRelatedJobs(int companyId, int currentJobId)
        {
            var jobs = new List<Job>();
            if (companyId == 0) return jobs;

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT TOP 3 j.*, c.CompanyName, c.Logo 
                               FROM Jobs j 
                               LEFT JOIN Companies c ON j.CompanyId = c.CompanyId 
                               WHERE j.CompanyId = @CompanyId 
                               AND j.JobId != @CurrentJobId 
                               AND j.IsActive = 1
                               ORDER BY j.CreatedDate DESC";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);
                cmd.Parameters.AddWithValue("@CurrentJobId", currentJobId);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var job = new Job
                    {
                        JobId = Convert.ToInt32(reader["JobId"]),
                        Title = reader["Title"].ToString(),
                        Location = reader["Location"]?.ToString() ?? "",
                        SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                        SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                        JobType = reader["JobType"]?.ToString() ?? "",
                        JobImage = reader["JobImage"]?.ToString(),
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : (DateTime?)null,
                        Company = new Company
                        {
                            CompanyName = reader["CompanyName"]?.ToString() ?? "",
                            Logo = reader["Logo"]?.ToString()
                        }
                    };
                    jobs.Add(job);
                }
            }
            return jobs;
        }

        private void IncrementJobViews(int jobId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = "UPDATE Jobs SET Views = Views + 1 WHERE JobId = @JobId";
                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@JobId", jobId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error incrementing views: {ex.Message}");
            }
        }

        private bool CheckIfApplied(int userId, int jobId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"SELECT COUNT(*) FROM Applications
                                     WHERE UserId = @UserId
                                       AND JobId = @JobId
                                       AND ISNULL(Status, 'Pending') NOT IN ('Rejected', 'Withdrawn')";
                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@JobId", jobId);
                    conn.Open();
                    int count = (int)cmd.ExecuteScalar();
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking application: {ex.Message}");
                return false;
            }
        }

        private List<Company> GetEmployerCompanies(int employerId)
        {
            var companies = new List<Company>();
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"SELECT CompanyId, CompanyName, Logo, Address, Description
                                    FROM Companies WHERE EmployerId = @EmployerId ORDER BY CompanyName";
                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@EmployerId", employerId);
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            return companies;
        }

        private List<JobSummary> GetEmployerJobs(int employerId)
        {
            var jobs = new List<JobSummary>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT j.JobId, j.Title FROM Jobs j
                                 INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                                 WHERE c.EmployerId = @EmployerId AND j.IsActive = 1
                                 ORDER BY j.CreatedDate DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@EmployerId", employerId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    jobs.Add(new JobSummary
                    {
                        JobId = Convert.ToInt32(reader["JobId"]),
                        Title = reader["Title"]?.ToString()
                    });
                }
            }
            return jobs;
        }

        private List<JobSummary> GetAllJobSummaries()
        {
            var jobs = new List<JobSummary>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT JobId, Title FROM Jobs WHERE IsActive = 1 ORDER BY CreatedDate DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    jobs.Add(new JobSummary
                    {
                        JobId = Convert.ToInt32(reader["JobId"]),
                        Title = reader["Title"]?.ToString()
                    });
                }
            }
            return jobs;
        }

        private int CreateJob(Job job)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"INSERT INTO Jobs 
                        (Title, CompanyId, SalaryMin, SalaryMax, Location, 
                         JobType, Description, Requirement, Benefit, Skills,
                         Experience, Education, NumberOfRecruits, Deadline, 
                         JobImage, VideoUrl, Tags, IsFeatured, IsUrgent, IsRemote,
                         CreatedDate, IsActive, Views)
                       OUTPUT INSERTED.JobId
                       VALUES 
                        (@Title, @CompanyId, @SalaryMin, @SalaryMax, @Location, 
                         @JobType, @Description, @Requirement, @Benefit, @Skills,
                         @Experience, @Education, @NumberOfRecruits, @Deadline, 
                         @JobImage, @VideoUrl, @Tags, @IsFeatured, @IsUrgent, @IsRemote,
                         @CreatedDate, @IsActive, @Views)";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Title", job.Title ?? "");
                cmd.Parameters.AddWithValue("@CompanyId", job.CompanyId);
                cmd.Parameters.AddWithValue("@SalaryMin", job.SalaryMin ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@SalaryMax", job.SalaryMax ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Location", job.Location ?? "");
                cmd.Parameters.AddWithValue("@JobType", job.JobType ?? "");
                cmd.Parameters.AddWithValue("@Description", job.Description ?? "");
                cmd.Parameters.AddWithValue("@Requirement", job.Requirement ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Benefit", job.Benefit ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Skills", job.Skills ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Experience", job.Experience ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Education", job.Education ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@NumberOfRecruits", job.NumberOfRecruits ?? 1);
                cmd.Parameters.AddWithValue("@Deadline", job.Deadline ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@JobImage", job.JobImage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@VideoUrl", job.VideoUrl ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Tags", job.Tags ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@IsFeatured", job.IsFeatured);
                cmd.Parameters.AddWithValue("@IsUrgent", job.IsUrgent);
                cmd.Parameters.AddWithValue("@IsRemote", job.IsRemote);
                cmd.Parameters.AddWithValue("@CreatedDate", DateTime.Now);
                cmd.Parameters.AddWithValue("@IsActive", true);
                cmd.Parameters.AddWithValue("@Views", 0);
                conn.Open();
                return (int)cmd.ExecuteScalar();
            }
        }

        private void UpdateJob(Job job)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"UPDATE Jobs SET Title=@Title, CompanyId=@CompanyId,
                               SalaryMin=@SalaryMin, SalaryMax=@SalaryMax, Location=@Location,
                               JobType=@JobType, Description=@Description, Requirement=@Requirement,
                               Benefit=@Benefit, Skills=@Skills, Experience=@Experience,
                               Education=@Education, NumberOfRecruits=@NumberOfRecruits,
                               Deadline=@Deadline, JobImage=@JobImage, VideoUrl=@VideoUrl,
                               Tags=@Tags, IsFeatured=@IsFeatured, IsUrgent=@IsUrgent,
                               IsRemote=@IsRemote WHERE JobId=@JobId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Title", job.Title ?? "");
                cmd.Parameters.AddWithValue("@CompanyId", job.CompanyId);
                cmd.Parameters.AddWithValue("@SalaryMin", job.SalaryMin ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@SalaryMax", job.SalaryMax ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Location", job.Location ?? "");
                cmd.Parameters.AddWithValue("@JobType", job.JobType ?? "");
                cmd.Parameters.AddWithValue("@Description", job.Description ?? "");
                cmd.Parameters.AddWithValue("@Requirement", job.Requirement ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Benefit", job.Benefit ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Skills", job.Skills ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Experience", job.Experience ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Education", job.Education ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@NumberOfRecruits", job.NumberOfRecruits ?? 1);
                cmd.Parameters.AddWithValue("@Deadline", job.Deadline ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@JobImage", job.JobImage ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@VideoUrl", job.VideoUrl ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Tags", job.Tags ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@IsFeatured", job.IsFeatured);
                cmd.Parameters.AddWithValue("@IsUrgent", job.IsUrgent);
                cmd.Parameters.AddWithValue("@IsRemote", job.IsRemote);
                cmd.Parameters.AddWithValue("@JobId", job.JobId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void DeleteJob(int jobId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE Jobs SET IsActive = 0 WHERE JobId = @JobId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@JobId", jobId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void SaveApplication(Application application)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"INSERT INTO Applications
                                (JobId, UserId, CVFile, OriginalFileName, CoverLetter, Status, ApplyDate, ApplicationHash,
                                 CVMetadataId, AccessToken, TokenExpireTime, FileChecksum, Nonce, DownloadCount, IsExpired)
                                VALUES
                                (@JobId, @UserId, @CVFile, @OriginalFileName, @CoverLetter, @Status, @ApplyDate, @ApplicationHash,
                                 @CVMetadataId, @AccessToken, @TokenExpireTime, @FileChecksum, @Nonce, @DownloadCount, @IsExpired);
                                SELECT SCOPE_IDENTITY();";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@JobId", application.JobId);
                cmd.Parameters.AddWithValue("@UserId", application.UserId);
                cmd.Parameters.AddWithValue("@CVFile", application.CVFile ?? "");
                cmd.Parameters.AddWithValue("@OriginalFileName", application.OriginalFileName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@CoverLetter", application.CoverLetter ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Status", application.Status ?? "Pending");
                cmd.Parameters.AddWithValue("@ApplyDate", application.ApplyDate ?? DateTime.Now);
                cmd.Parameters.AddWithValue("@ApplicationHash", application.ApplicationHash ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@CVMetadataId", application.CVMetadataId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@AccessToken", application.AccessToken ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@TokenExpireTime", application.TokenExpireTime ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@FileChecksum", application.FileChecksum ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Nonce", application.Nonce ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@DownloadCount", application.DownloadCount ?? 0);
                cmd.Parameters.AddWithValue("@IsExpired", application.IsExpired);
                conn.Open();
                application.ApplicationId = Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private Application GetApplicationById(int applicationId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT * FROM Applications WHERE ApplicationId = @ApplicationId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ApplicationId", applicationId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new Application
                    {
                        ApplicationId = Convert.ToInt32(reader["ApplicationId"]),
                        JobId = reader["JobId"] != DBNull.Value ? Convert.ToInt32(reader["JobId"]) : (int?)null,
                        UserId = reader["UserId"] != DBNull.Value ? Convert.ToInt32(reader["UserId"]) : (int?)null,
                        CVFile = reader["CVFile"]?.ToString(),
                        OriginalFileName = reader["OriginalFileName"]?.ToString(),
                          CoverLetter = reader["CoverLetter"]?.ToString(),
                          Status = reader["Status"]?.ToString(),
                          ApplyDate = reader["ApplyDate"] != DBNull.Value ? Convert.ToDateTime(reader["ApplyDate"]) : (DateTime?)null,
                          ApplicationHash = reader["ApplicationHash"]?.ToString(),
                          CVMetadataId = HasColumn(reader, "CVMetadataId") && reader["CVMetadataId"] != DBNull.Value ? Convert.ToInt32(reader["CVMetadataId"]) : (int?)null,
                          AccessToken = HasColumn(reader, "AccessToken") && reader["AccessToken"] != DBNull.Value ? Guid.Parse(reader["AccessToken"].ToString()) : (Guid?)null,
                          TokenExpireTime = HasColumn(reader, "TokenExpireTime") && reader["TokenExpireTime"] != DBNull.Value ? Convert.ToDateTime(reader["TokenExpireTime"]) : (DateTime?)null,
                          FileChecksum = HasColumn(reader, "FileChecksum") ? reader["FileChecksum"]?.ToString() : null,
                          Nonce = HasColumn(reader, "Nonce") ? reader["Nonce"]?.ToString() : null,
                          DownloadCount = HasColumn(reader, "DownloadCount") && reader["DownloadCount"] != DBNull.Value ? Convert.ToInt32(reader["DownloadCount"]) : 0,
                          IsExpired = HasColumn(reader, "IsExpired") && reader["IsExpired"] != DBNull.Value && Convert.ToBoolean(reader["IsExpired"])
                      };
                  }
              }
              return null;
          }

        private void IncrementSecureCVDownloadCount(int cvMetadataId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"UPDATE Applications
                                 SET DownloadCount = ISNULL(DownloadCount, 0) + 1
                                 WHERE CVMetadataId = @CVMetadataId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@CVMetadataId", cvMetadataId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private async Task AttachCVIntegrityStatusAsync(IEnumerable<ApplicationWithDetails> applications)
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

        private static bool HasColumn(SqlDataReader reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetContentType(string fileExtension)
        {
            return fileExtension?.ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => "application/octet-stream"
            };
        }

        private string CreateApplicationHash(int jobId, string userEmail, DateTime appliedDate)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var input = $"{jobId}-{userEmail}-{appliedDate:yyyyMMddHHmmss}-{Guid.NewGuid()}";
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(hash);
            }
        }

        private List<SavedJobsViewModel> GetSavedJobs(int userId)
        {
            var savedJobs = new List<SavedJobsViewModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT sj.SavedJobId, sj.JobId, sj.SavedDate, j.Title, j.Location, j.JobType, 
                                        j.SalaryMin, j.SalaryMax, j.Deadline, j.IsActive, c.CompanyName, c.Logo as CompanyLogo
                                 FROM SavedJobs sj
                                 INNER JOIN Jobs j ON sj.JobId = j.JobId
                                 INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                                 WHERE sj.UserId = @UserId ORDER BY sj.SavedDate DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    savedJobs.Add(new SavedJobsViewModel
                    {
                        SavedJobId = Convert.ToInt32(reader["SavedJobId"]),
                        JobId = Convert.ToInt32(reader["JobId"]),
                        JobTitle = reader["Title"].ToString(),
                        CompanyName = reader["CompanyName"].ToString(),
                        CompanyLogo = reader["CompanyLogo"]?.ToString(),
                        Location = reader["Location"]?.ToString(),
                        JobType = reader["JobType"]?.ToString(),
                        SalaryMin = reader["SalaryMin"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMin"]) : (decimal?)null,
                        SalaryMax = reader["SalaryMax"] != DBNull.Value ? Convert.ToDecimal(reader["SalaryMax"]) : (decimal?)null,
                        Deadline = reader["Deadline"] != DBNull.Value ? Convert.ToDateTime(reader["Deadline"]) : (DateTime?)null,
                        SavedDate = Convert.ToDateTime(reader["SavedDate"]),
                        IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"])
                    });
                }
            }
            return savedJobs;
        }
    }
}
