using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTL_2.Models
{
    [Table("DownloadTokens")]
    public class DownloadToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Token { get; set; } = string.Empty;

        public int CVMetadataId { get; set; }

        [Required]
        [MaxLength(450)]
        public string RecruiterId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public bool IsUsed { get; set; }

        public bool IsRevoked { get; set; }

        [MaxLength(128)]
        public string? SessionId { get; set; }

        [MaxLength(128)]
        public string? VerificationCodeHash { get; set; }

        [ForeignKey("CVMetadataId")]
        public virtual CVMetadata? CVMetadata { get; set; }

        [ForeignKey("RecruiterId")]
        public virtual User? Recruiter { get; set; }
    }
}
