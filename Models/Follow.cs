using System;

namespace BTL_2.Models
{
    public class Follow
    {
        public int FollowId { get; set; }
        public int UserId { get; set; }
        public int CompanyId { get; set; }
        public DateTime FollowedDate { get; set; }

        // Navigation properties
        public virtual User User { get; set; }
        public virtual Company Company { get; set; }
    }
}