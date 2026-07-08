using System;

namespace BTL_2.Models
{
    public class UserNotification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Type { get; set; } // 'application_approved', 'application_rejected', 'message', 'system'
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public string LinkUrl { get; set; }
        public int? RelatedId { get; set; }
        public int? AnnouncementId { get; set; }

        // Navigation properties
        public virtual User User { get; set; }
        public virtual Announcement Announcement { get; set; }

        // Helper property for time display
        public string TimeAgo
        {
            get
            {
                var span = DateTime.Now - CreatedAt;
                if (span.TotalMinutes < 1) return "Vài giây trước";
                if (span.TotalHours < 1) return $"{span.Minutes} phút trước";
                if (span.TotalHours < 24) return $"{span.Hours} giờ trước";
                if (span.TotalDays < 30) return $"{span.Days} ngày trước";
                return CreatedAt.ToString("dd/MM/yyyy");
            }
        }

        // Helper property for icon
        public string IconClass
        {
            get
            {
                return Type switch
                {
                    "application_approved" => "fa-check-circle text-success",
                    "application_rejected" => "fa-times-circle text-danger",
                    "message" => "fa-envelope text-primary",
                    "system" => "fa-info-circle text-info",
                    _ => "fa-bell text-secondary"
                };
            }
        }
    }
}