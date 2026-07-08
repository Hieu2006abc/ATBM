// Models/Application.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTL_2.Models
{
    public class Application
    {
        [Key]
        public int ApplicationId { get; set; }

        public int? JobId { get; set; }
        public int? UserId { get; set; }

        [StringLength(500)]
        public string CVFile { get; set; }

        [StringLength(500)]
        public string OriginalFileName { get; set; }

        [StringLength(500)]
        public string EncryptedFileName { get; set; }

        public string CoverLetter { get; set; }

        [StringLength(50)]
        public string Status { get; set; }

        public DateTime? ApplyDate { get; set; }

        public string Notes { get; set; }

        [StringLength(500)]
        public string ApplicationHash { get; set; }

        public int? CVMetadataId { get; set; }

        // Metadata mới cho bảo mật
        public Guid? AccessToken { get; set; }  // Token phiên

        public DateTime? TokenExpireTime { get; set; }  // Thời gian hết hạn token

        [StringLength(200)]
        public string FileChecksum { get; set; }  // Checksum để phát hiện sửa đổi

        [StringLength(100)]
        public string Nonce { get; set; }  // Nonce cho mỗi request

        public int? DownloadCount { get; set; }  // Số lần tải

        public bool IsExpired { get; set; }  // Đánh dấu hết hạn

        [NotMapped]
        public string CVIntegrityStatus { get; set; } = string.Empty;

        [NotMapped]
        public string CVIntegrityDetail { get; set; } = string.Empty;

        [NotMapped]
        public bool CanDownloadSecureCV { get; set; }

        // Navigation properties
        public virtual Job Job { get; set; }
        public virtual User User { get; set; }
    }
}
