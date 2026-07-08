using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BTL_2.Data;

namespace BTL_2.Services
{
    public class ExpirationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExpirationService> _logger;

        public ExpirationService(IServiceProvider serviceProvider, ILogger<ExpirationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    await CleanExpiredData();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi dọn dẹp dữ liệu hết hạn");
                }
            }
        }

        private async Task CleanExpiredData()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<JobDatabaseContext>();

            try
            {
                // Xóa token hết hạn
                var expiredTokens = await context.DownloadTokens
                    .Where(t => t.ExpiresAt < DateTime.UtcNow || t.IsUsed == true)
                    .ToListAsync();

                if (expiredTokens.Any())
                {
                    context.DownloadTokens.RemoveRange(expiredTokens);
                    _logger.LogInformation($"Đã xóa {expiredTokens.Count} token hết hạn");
                }

                // Xóa CV cũ (quá 90 ngày)
                var oldCVs = await context.CVMetadata
                    .Where(c => c.ExpireTime < DateTime.UtcNow && c.IsDeleted == false)
                    .ToListAsync();

                foreach (var cv in oldCVs)
                {
                    cv.IsDeleted = true;

                    // Xóa file vật lý nếu tồn tại
                    if (System.IO.File.Exists(cv.FilePath))
                    {
                        try
                        {
                            System.IO.File.Delete(cv.FilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Không thể xóa file CV: {cv.FilePath}");
                        }
                    }
                }

                if (oldCVs.Any())
                {
                    await context.SaveChangesAsync();
                    _logger.LogInformation($"Đã đánh dấu xóa {oldCVs.Count} CV cũ");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi dọn dẹp dữ liệu");
            }
        }
    }
}