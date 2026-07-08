using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTL_2.Models
{
    [Table("CVMetadata")]
    public class CVMetadata
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string CandidateId { get; set; } = string.Empty;

        public int JobId { get; set; }

        [Required]
        [MaxLength(500)]
        public string OriginalFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string StoredFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string FilePath { get; set; } = string.Empty;

        [MaxLength(50)]
        public string FileType { get; set; } = string.Empty;

        public long FileSize { get; set; }

        [Required]
        [MaxLength(100)]
        public string SHA256Hash { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string EncryptionIV { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Nonce { get; set; } = string.Empty;

        public DateTime UploadTime { get; set; } = DateTime.UtcNow;

        public DateTime ExpireTime { get; set; }

        public bool IsDeleted { get; set; } = false;

        [ForeignKey("CandidateId")]
        public virtual User? Candidate { get; set; }

        [ForeignKey("JobId")]
        public virtual Job? Job { get; set; }
    }
}
