using System;

namespace BTL_2.Models.ViewModels
{
    public class CVIntegrityScanItemViewModel
    {
        public int CVMetadataId { get; set; }
        public string CandidateName { get; set; } = string.Empty;
        public string CandidateEmail { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long? ServerFileSize { get; set; }
        public DateTime? ServerLastWriteTime { get; set; }
        public DateTime UploadTime { get; set; }
        public DateTime ExpireTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public bool Passed { get; set; }
    }
}
