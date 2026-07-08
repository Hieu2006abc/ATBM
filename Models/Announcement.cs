using System;

namespace BTL_2.Models
{
    public class Announcement
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Type { get; set; } // 'info', 'success', 'warning', 'danger'
        public string TargetRole { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public bool IsActive { get; set; }
        public int? CreatedBy { get; set; }
        public string ImageUrl { get; set; }
        public string LinkUrl { get; set; }
    }
}