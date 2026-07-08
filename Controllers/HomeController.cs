using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;
using BTL_2.Models;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace JobPortal.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public IActionResult Index()
        {
            var featuredJobs = new List<Job>();
            var latestJobs = new List<Job>();
            var featuredCompanies = new List<Company>();
            var announcements = new List<Announcement>();
            var locations = GetDefaultLocations();
            var jobTitles = GetDefaultJobTitles();

            try
            {
                featuredJobs = GetFeaturedJobs();
                latestJobs = GetLatestJobs();
                featuredCompanies = GetFeaturedCompanies();
                announcements = GetActiveAnnouncements();
                locations = GetLocations();
                jobTitles = GetJobTitles();
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Home page database error: {ex.Message}");
                ViewBag.ErrorMessage = "Không thể kết nối cơ sở dữ liệu. Trang đang hiển thị dữ liệu mặc định.";
            }

            ViewBag.FeaturedJobs = featuredJobs;
            ViewBag.LatestJobs = latestJobs;
            ViewBag.FeaturedCompanies = featuredCompanies;
            ViewBag.Announcements = announcements;
            ViewBag.Locations = locations;
            ViewBag.JobTitles = jobTitles;

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View("~/Views/Shared/Error.cshtml", new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }

        private List<Job> GetFeaturedJobs()
        {
            var jobs = new List<Job>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT TOP 6 j.*, c.CompanyName, c.Logo 
                    FROM Jobs j
                    LEFT JOIN Companies c ON j.CompanyId = c.CompanyId
                    WHERE j.IsActive = 1
                    ORDER BY j.Views DESC";

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
                        Description = reader["Description"]?.ToString(),
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : DateTime.Now,
                        IsFeatured = reader["IsFeatured"] != DBNull.Value && Convert.ToBoolean(reader["IsFeatured"]),
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

        private List<Job> GetLatestJobs()
        {
            var jobs = new List<Job>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT TOP 6 j.*, c.CompanyName, c.Logo 
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
                        Description = reader["Description"]?.ToString(),
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

        private List<Company> GetFeaturedCompanies()
        {
            var companies = new List<Company>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT TOP 6 c.*, 
                           (SELECT COUNT(*) FROM Jobs WHERE CompanyId = c.CompanyId AND IsActive = 1) as JobCount
                    FROM Companies c
                    ORDER BY JobCount DESC";

                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var companyName = reader["CompanyName"].ToString();
                    var company = new Company
                    {
                        CompanyId = Convert.ToInt32(reader["CompanyId"]),
                        CompanyName = companyName,
                        Logo = reader["Logo"]?.ToString(),
                        Address = CleanCompanyAddress(companyName, reader["Address"]?.ToString()),
                        Description = CleanCompanyDescription(companyName, reader["Description"]?.ToString()),
                        JobCount = reader["JobCount"] != DBNull.Value ? Convert.ToInt32(reader["JobCount"]) : 0
                    };

                    companies.Add(company);
                }
            }

            return companies;
        }

        private List<Announcement> GetActiveAnnouncements()
        {
            var announcements = new List<Announcement>();
            string userRole = HttpContext.Session.GetString("UserRole") ?? "Guest";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT * FROM Announcements 
                    WHERE IsActive = 1 
                    AND (TargetRole IS NULL OR TargetRole = 'All' OR TargetRole = @UserRole)
                    AND (ExpiryDate IS NULL OR ExpiryDate > GETDATE())
                    ORDER BY CreatedAt DESC";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserRole", userRole);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    announcements.Add(new Announcement
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        Title = reader["Title"].ToString(),
                        Content = reader["Content"].ToString(),
                        Type = reader["Type"]?.ToString() ?? "info",
                        TargetRole = reader["TargetRole"]?.ToString(),
                        CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                        ExpiryDate = reader["ExpiryDate"] != DBNull.Value ? Convert.ToDateTime(reader["ExpiryDate"]) : (DateTime?)null,
                        IsActive = reader["IsActive"] != DBNull.Value && Convert.ToBoolean(reader["IsActive"]),
                        ImageUrl = reader["ImageUrl"]?.ToString(),
                        LinkUrl = reader["LinkUrl"]?.ToString()
                    });
                }
            }
            return announcements;
        }

        private List<string> GetLocations()
        {
            var locations = new List<string>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT DISTINCT Location 
                    FROM Jobs 
                    WHERE Location IS NOT NULL AND Location != '' AND IsActive = 1
                    ORDER BY Location";

                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    locations.Add(reader["Location"].ToString());
                }
            }

            // Nếu không có dữ liệu, trả về danh sách mặc định
            if (locations.Count == 0)
            {
                locations = GetDefaultLocations();
            }

            return locations;
        }

        private List<string> GetJobTitles()
        {
            var titles = new List<string>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT DISTINCT Title 
                    FROM Jobs 
                    WHERE Title IS NOT NULL AND Title != '' AND IsActive = 1
                    ORDER BY Title";

                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    titles.Add(reader["Title"].ToString());
                }
            }

            // Nếu không có dữ liệu, trả về danh sách mặc định
            if (titles.Count == 0)
            {
                titles = GetDefaultJobTitles();
            }

            return titles;
        }

        private static List<string> GetDefaultLocations()
        {
            return new List<string> {
                "Hà Nội", "Hồ Chí Minh", "Đà Nẵng", "Hải Phòng", "Cần Thơ", "Remote"
            };
        }

        private static List<string> GetDefaultJobTitles()
        {
            return new List<string> {
                ".NET Developer", "Java Developer", "Frontend Developer", "Backend Developer",
                "Fullstack Developer", "Mobile Developer", "DevOps Engineer", "Data Scientist",
                "QA Tester", "Business Analyst", "Project Manager", "Product Manager",
                "UI/UX Designer", "Marketing Executive", "Sales Executive", "HR Manager"
            };
        }

        private static string CleanCompanyDescription(string companyName, string value)
        {
            if (companyName.Contains("FPT", StringComparison.OrdinalIgnoreCase))
                return "Công ty phần mềm hàng đầu Việt Nam";
            if (companyName.Contains("Shopee", StringComparison.OrdinalIgnoreCase) ||
                companyName.Contains("Tiki", StringComparison.OrdinalIgnoreCase))
                return "Sàn thương mại điện tử";
            if (companyName.Contains("VNG", StringComparison.OrdinalIgnoreCase))
                return "Công ty công nghệ";
            if (companyName.Contains("Viettel", StringComparison.OrdinalIgnoreCase))
                return "Tập đoàn viễn thông";
            if (companyName.Contains("Tech", StringComparison.OrdinalIgnoreCase))
                return "Công ty công nghệ hàng đầu";

            return string.IsNullOrWhiteSpace(value) ? "Doanh nghiệp đang tuyển dụng" : value;
        }

        private static string CleanCompanyAddress(string companyName, string value)
        {
            if (companyName.Contains("FPT", StringComparison.OrdinalIgnoreCase) ||
                companyName.Contains("Viettel", StringComparison.OrdinalIgnoreCase))
                return "Hà Nội";
            if (companyName.Contains("Shopee", StringComparison.OrdinalIgnoreCase) ||
                companyName.Contains("Tiki", StringComparison.OrdinalIgnoreCase) ||
                companyName.Contains("VNG", StringComparison.OrdinalIgnoreCase) ||
                companyName.Contains("Tech", StringComparison.OrdinalIgnoreCase))
                return "Hồ Chí Minh";

            return string.IsNullOrWhiteSpace(value) ? "Việt Nam" : value;
        }
    }
}
