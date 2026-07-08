using System;

namespace BTL_2.Models.ViewModels
{
    public class CVAccessLogViewModel
    {
        public int Id { get; set; }
        public string RecruiterName { get; set; } = string.Empty;
        public string RecruiterEmail { get; set; } = string.Empty;
        public int CVMetadataId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string CandidateId { get; set; } = string.Empty;
        public int JobId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public DateTime AccessTime { get; set; }
        public string IPAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string? UserAgent { get; set; }
    }
}
