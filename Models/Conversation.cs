using System;
using System.Collections.Generic;

namespace BTL_2.Models
{
    public class Conversation
    {
        public int ConversationId { get; set; }
        public int User1Id { get; set; }
        public int User2Id { get; set; }
        public DateTime LastMessageTime { get; set; }
        public string LastMessageContent { get; set; }
        public bool User1Deleted { get; set; }
        public bool User2Deleted { get; set; }

        // Navigation properties
        public virtual User User1 { get; set; }
        public virtual User User2 { get; set; }
        public virtual ICollection<Message> Messages { get; set; }
    }
}