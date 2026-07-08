using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using BTL_2.Data;
using BTL_2.Models;

namespace BTL_2.Services
{
    public class ActivityLogService : IActivityLogService
    {
        private readonly JobDatabaseContext _context;
        private readonly ILogger<ActivityLogService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ActivityLogService(
            JobDatabaseContext context,
            ILogger<ActivityLogService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogCVAccessAsync(string recruiterId, int cvMetadataId, string ipAddress,
            string status, string? userAgent = null, string? errorMessage = null)
        {
            try
            {
                var log = new CVActivityLog
                {
                    RecruiterId = recruiterId,
                    CVMetadataId = cvMetadataId,
                    AccessTime = DateTime.UtcNow,
                    IPAddress = ipAddress,
                    Status = status,
                    UserAgent = userAgent ?? _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"],
                    ErrorMessage = errorMessage
                };

                _context.CVActivityLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log CV access activity");
            }
        }

        public async Task<CVActivityLog?> GetLastAccessLogAsync(int cvMetadataId)
        {
            return await _context.CVActivityLogs
                .Where(l => l.CVMetadataId == cvMetadataId)
                .OrderByDescending(l => l.AccessTime)
                .FirstOrDefaultAsync();
        }
    }
}