using System.Threading.Tasks;

namespace BTL_2.Services
{
    public interface IEncryptionService
    {
        Task<(byte[] encryptedData, string iv, string hash)> EncryptFileAsync(byte[] fileData);
        Task<(byte[] decryptedData, bool integrityValid)> DecryptAndVerifyAsync(byte[] encryptedData, string iv, string expectedHash);
        string ComputeSHA256Hash(byte[] data);
        string ComputeSHA256HashFromFile(string filePath);
    }
}