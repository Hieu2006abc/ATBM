using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using BTL_2.Models;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BTL_2.Controllers
{
    public class CompaniesController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public CompaniesController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        // GET: Companies
        public IActionResult Index()
        {
            var companies = new List<Company>();

            try
            {
                companies = GetAllCompanies();
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Companies page database error: {ex.Message}");
                ViewBag.ErrorMessage = "Không thể kết nối cơ sở dữ liệu. Danh sách công ty tạm thời chưa hiển thị.";
            }

            return View(companies);
        }

        // GET: Companies/Details/5
        public IActionResult Details(int id)
        {
            Company company;

            try
            {
                company = GetCompanyById(id);
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Company details database error: {ex.Message}");
                TempData["ErrorMessage"] = "Không thể kết nối cơ sở dữ liệu. Vui lòng thử lại sau.";
                return RedirectToAction(nameof(Index));
            }

            if (company == null)
            {
                return NotFound();
            }

            // Lấy danh sách công việc của công ty
            var jobs = new List<Job>();
            try
            {
                jobs = GetJobsByCompanyId(id);
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Company jobs database error: {ex.Message}");
                ViewBag.ErrorMessage = "Không thể tải danh sách việc làm của công ty.";
            }
            ViewBag.Jobs = jobs;

            return View(company);
        }

        private List<Company> GetAllCompanies()
        {
            var companies = new List<Company>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT c.*, 
                           (SELECT COUNT(*) FROM Jobs WHERE CompanyId = c.CompanyId AND IsActive = 1) as JobCount
                    FROM Companies c
                    ORDER BY c.CompanyName";

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
                        Website = reader["Website"]?.ToString(),
                        Phone = reader["Phone"]?.ToString(),
                        Email = reader["Email"]?.ToString(),
                        JobCount = reader["JobCount"] != DBNull.Value ? Convert.ToInt32(reader["JobCount"]) : 0
                    };

                    companies.Add(company);
                }
            }
            return companies;
        }

        private Company GetCompanyById(int id)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT * FROM Companies WHERE CompanyId = @CompanyId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@CompanyId", id);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    var companyName = reader["CompanyName"].ToString();
                    return new Company
                    {
                        CompanyId = Convert.ToInt32(reader["CompanyId"]),
                        CompanyName = companyName,
                        Logo = reader["Logo"]?.ToString(),
                        Address = CleanCompanyAddress(companyName, reader["Address"]?.ToString()),
                        Description = CleanCompanyDescription(companyName, reader["Description"]?.ToString()),
                        Website = reader["Website"]?.ToString(),
                        Phone = reader["Phone"]?.ToString(),
                        Email = reader["Email"]?.ToString()
                    };
                }
            }
            return null;
        }

        private List<Job> GetJobsByCompanyId(int companyId)
        {
            var jobs = new List<Job>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT j.*, c.CompanyName, c.Logo 
                    FROM Jobs j
                    INNER JOIN Companies c ON j.CompanyId = c.CompanyId
                    WHERE j.CompanyId = @CompanyId AND j.IsActive = 1
                    ORDER BY j.CreatedDate DESC";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@CompanyId", companyId);

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
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : DateTime.Now,
                        Company = new Company
                        {
                            CompanyName = reader["CompanyName"].ToString(),
                            Logo = reader["Logo"]?.ToString()
                        }
                    };
                    jobs.Add(job);
                }
            }
            return jobs;
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
