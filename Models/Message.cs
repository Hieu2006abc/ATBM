using System;

namespace BTL_2.Models
{
    public class Message
    {
        public int MessageId { get; set; }
        public int ConversationId { get; set; }
        public int SenderId { get; set; }
        public string Content { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }

        // Navigation properties
        public virtual Conversation Conversation { get; set; }
        public virtual User Sender { get; set; }

        // Helper property
        public string TimeAgo
        {
            get
            {
                var span = DateTime.Now - SentAt;
                if (span.TotalMinutes < 1) return "Vài giây trước";
                if (span.TotalHours < 1) return $"{span.Minutes} phút trước";
                if (span.TotalHours < 24) return $"{span.Hours} giờ trước";
                if (span.TotalDays < 7) return $"{span.Days} ngày trước";
                return SentAt.ToString("dd/MM/yyyy");
            }
        }
    }
}