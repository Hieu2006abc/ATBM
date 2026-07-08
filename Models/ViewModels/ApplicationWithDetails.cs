// Models/ViewModels/ApplicationWithDetails.cs
using System;

namespace BTL_2.Models.ViewModels
{
    public class ApplicationWithDetails
    {
        public int ApplicationId { get; set; }
        public int? JobId { get; set; }
        public string JobTitle { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public string UserEmail { get; set; }
        public string OriginalFileName { get; set; }
        public string CoverLetter { get; set; }
        public string Status { get; set; }
        public DateTime? ApplyDate { get; set; }
        public int? CVMetadataId { get; set; }
        public DateTime? CVExpireTime { get; set; }
        public string CVIntegrityStatus { get; set; } = string.Empty;
        public string CVIntegrityDetail { get; set; } = string.Empty;
        public bool CanDownloadSecureCV { get; set; }
    }

    public class JobSummary
    {
        public int JobId { get; set; }
        public string Title { get; set; }
    }
}
