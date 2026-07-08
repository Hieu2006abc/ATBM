using System;

namespace BTL_2.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string Type { get; set; } // success, info, warning, danger
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public string TargetRole { get; set; } // All, Admin, Employer, Candidate
        public string Link { get; set; }
        public int? RelatedId { get; set; }
    }
}