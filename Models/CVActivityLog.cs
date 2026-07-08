using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTL_2.Models
{
    [Table("CVActivityLogs")]
    public class CVActivityLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string RecruiterId { get; set; } = string.Empty;

        public int CVMetadataId { get; set; }

        public DateTime AccessTime { get; set; } = DateTime.UtcNow;

        [MaxLength(50)]
        public string IPAddress { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [ForeignKey("RecruiterId")]
        public virtual User? Recruiter { get; set; }

        [ForeignKey("CVMetadataId")]
        public virtual CVMetadata? CVMetadata { get; set; }
    }
}