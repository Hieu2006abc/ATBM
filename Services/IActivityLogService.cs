using System.Threading.Tasks;
using BTL_2.Models;

namespace BTL_2.Services
{
    public interface IActivityLogService
    {
        Task LogCVAccessAsync(string recruiterId, int cvMetadataId, string ipAddress,
            string status, string? userAgent = null, string? errorMessage = null);
        Task<CVActivityLog?> GetLastAccessLogAsync(int cvMetadataId);
    }
}