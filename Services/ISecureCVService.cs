using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using BTL_2.Models;

namespace BTL_2.Services
{
    public interface ISecureCVService
    {
        Task<CVMetadata> UploadSecureCVAsync(IFormFile file, string candidateId, int jobId);
        Task<(byte[] fileData, CVMetadata metadata, bool isValid)> DownloadSecureCVAsync(
            int cvMetadataId, int recruiterId, string ipAddress, string userAgent);
        Task<(bool isValid, string status, string detail)> CheckCVIntegrityAsync(int cvMetadataId);
        Task<string> GenerateDownloadTokenAsync(int cvMetadataId, int recruiterId);
        Task<bool> ValidateAndConsumeTokenAsync(string token, int cvMetadataId, int recruiterId);
        Task<bool> HasRecruiterPermissionAsync(int recruiterId, int jobId);
    }
}
