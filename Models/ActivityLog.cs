using System;

namespace BTL_2.Models
{
    public class ActivityLog
    {
        public int LogId { get; set; }
        public string Username { get; set; }
        public string UserRole { get; set; }
        public string Action { get; set; }
        public string Details { get; set; }
        public string TargetUser { get; set; }
        public string TargetEmail { get; set; }
        public string TargetRole { get; set; }
        public int? TargetId { get; set; }
        public string IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
    }
}
