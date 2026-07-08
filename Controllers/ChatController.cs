using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTL_2.Models;
using BTL_2.Models.ViewModels;

namespace BTL_2.Controllers
{
    public class ChatController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public ChatController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        // GET: Chat/Index
        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account");
            }
            return View();
        }

        // GET: Chat/GetCompanies
        [HttpGet]
        public IActionResult GetCompanies()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new List<object>());
            }

            var companies = new List<object>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT c.CompanyId, c.CompanyName, c.Logo, c.UserId as EmployerId,
                           (SELECT COUNT(*) FROM Jobs WHERE CompanyId = c.CompanyId AND IsActive = 1) as JobCount
                    FROM Companies c
                    WHERE c.UserId IS NOT NULL
                    ORDER BY c.CompanyName";

                SqlCommand cmd = new SqlCommand(query, conn);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    companies.Add(new
                    {
                        companyId = Convert.ToInt32(reader["CompanyId"]),
                        companyName = reader["CompanyName"].ToString(),
                        logo = reader["Logo"]?.ToString() ?? "/images/default-company.png",
                        jobCount = Convert.ToInt32(reader["JobCount"]),
                        employerId = reader["EmployerId"] != DBNull.Value ? Convert.ToInt32(reader["EmployerId"]) : (int?)null
                    });
                }
            }
            return Json(companies);
        }

        // POST: Chat/StartChatWithCompany
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult StartChatWithCompany(int companyId)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Vui lòng đăng nhập" });
            }

            try
            {
                int currentUserId = int.Parse(userId);
                int? employerId = null;

                // Lấy EmployerId từ Company
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = "SELECT UserId FROM Companies WHERE CompanyId = @CompanyId";
                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@CompanyId", companyId);
                    conn.Open();
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        employerId = Convert.ToInt32(result);
                    }
                }

                if (!employerId.HasValue)
                {
                    return Json(new { success = false, message = "Không tìm thấy nhà tuyển dụng của công ty này" });
                }

                // Kiểm tra xem đã có conversation chưa
                int? conversationId = null;
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"
                        SELECT ConversationId FROM Conversations 
                        WHERE (User1Id = @UserId1 AND User2Id = @UserId2) 
                           OR (User1Id = @UserId2 AND User2Id = @UserId1)";

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@UserId1", currentUserId);
                    cmd.Parameters.AddWithValue("@UserId2", employerId.Value);
                    conn.Open();
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        conversationId = Convert.ToInt32(result);
                    }
                }

                if (conversationId.HasValue)
                {
                    return Json(new { success = true, conversationId = conversationId.Value, receiverId = employerId.Value });
                }

                // Tạo conversation mới
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"
                        INSERT INTO Conversations (User1Id, User2Id, CreatedAt)
                        VALUES (@User1Id, @User2Id, GETDATE());
                        SELECT SCOPE_IDENTITY();";

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@User1Id", currentUserId);
                    cmd.Parameters.AddWithValue("@User2Id", employerId.Value);
                    conn.Open();
                    int newConversationId = Convert.ToInt32(cmd.ExecuteScalar());

                    return Json(new { success = true, conversationId = newConversationId, receiverId = employerId.Value });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        // GET: Chat/GetConversations
        [HttpGet]
        public IActionResult GetConversations()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new List<object>());
            }

            int currentUserId = int.Parse(userId);
            var conversations = new List<object>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT 
                        c.ConversationId,
                        CASE 
                            WHEN c.User1Id = @UserId THEN c.User2Id
                            ELSE c.User1Id
                        END as OtherUserId,
                        u.FullName as OtherUserName,
                        u.Email as OtherUserEmail,
                        ISNULL(u.Avatar, '/images/default-avatar.png') as OtherUserAvatar,
                        (SELECT TOP 1 Content FROM Messages WHERE ConversationId = c.ConversationId ORDER BY SentAt DESC) as LastMessage,
                        (SELECT TOP 1 SentAt FROM Messages WHERE ConversationId = c.ConversationId ORDER BY SentAt DESC) as LastMessageTime,
                        (SELECT COUNT(*) FROM Messages WHERE ConversationId = c.ConversationId AND ReceiverId = @UserId AND IsRead = 0) as UnreadCount,
                        ISNULL(c.CreatedAt, GETDATE()) as CreatedAt
                    FROM Conversations c
                    INNER JOIN Users u ON (CASE WHEN c.User1Id = @UserId THEN c.User2Id ELSE c.User1Id END) = u.UserId
                    WHERE c.User1Id = @UserId OR c.User2Id = @UserId
                    ORDER BY ISNULL((SELECT TOP 1 SentAt FROM Messages WHERE ConversationId = c.ConversationId ORDER BY SentAt DESC), c.CreatedAt) DESC";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", currentUserId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var lastMessageTime = reader["LastMessageTime"] != DBNull.Value
                        ? Convert.ToDateTime(reader["LastMessageTime"])
                        : (DateTime?)null;

                    conversations.Add(new
                    {
                        conversationId = Convert.ToInt32(reader["ConversationId"]),
                        otherUserId = Convert.ToInt32(reader["OtherUserId"]),
                        otherUserName = reader["OtherUserName"].ToString(),
                        otherUserAvatar = reader["OtherUserAvatar"]?.ToString() ?? "/images/default-avatar.png",
                        lastMessage = reader["LastMessage"]?.ToString() ?? "",
                        timeAgo = lastMessageTime.HasValue ? GetTimeAgo(lastMessageTime.Value) : "Chưa có tin nhắn",
                        unreadCount = Convert.ToInt32(reader["UnreadCount"])
                    });
                }
            }
            return Json(conversations);
        }

        // GET: Chat/GetMessages
        [HttpGet]
        public IActionResult GetMessages(int conversationId)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new List<object>());
            }

            int currentUserId = int.Parse(userId);

            // Đánh dấu đã đọc
            MarkMessagesAsRead(conversationId, currentUserId);

            var messages = new List<object>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT MessageId, SenderId, ReceiverId, Content, SentAt, IsRead
                    FROM Messages
                    WHERE ConversationId = @ConversationId
                    ORDER BY SentAt ASC";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ConversationId", conversationId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    int senderId = Convert.ToInt32(reader["SenderId"]);
                    DateTime sentAt = Convert.ToDateTime(reader["SentAt"]);

                    messages.Add(new
                    {
                        messageId = Convert.ToInt32(reader["MessageId"]),
                        content = reader["Content"].ToString(),
                        isMe = senderId == currentUserId,
                        timeAgo = GetTimeAgo(sentAt),
                        sentAt = sentAt.ToString("HH:mm dd/MM/yyyy")
                    });
                }
            }
            return Json(messages);
        }

        // POST: Chat/SendMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SendMessage(int receiverId, string content)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, error = "Chưa đăng nhập" });
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return Json(new { success = false, error = "Nội dung tin nhắn không được để trống" });
            }

            try
            {
                int senderId = int.Parse(userId);

                // Lấy hoặc tạo conversation
                int conversationId = GetOrCreateConversation(senderId, receiverId);

                // Lưu tin nhắn
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string query = @"
                        INSERT INTO Messages (ConversationId, SenderId, ReceiverId, Content, SentAt, IsRead)
                        VALUES (@ConversationId, @SenderId, @ReceiverId, @Content, GETDATE(), 0)";

                    SqlCommand cmd = new SqlCommand(query, conn);
                    cmd.Parameters.AddWithValue("@ConversationId", conversationId);
                    cmd.Parameters.AddWithValue("@SenderId", senderId);
                    cmd.Parameters.AddWithValue("@ReceiverId", receiverId);
                    cmd.Parameters.AddWithValue("@Content", content);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // Helper methods
        private int? GetConversationId(int userId1, int userId2)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT ConversationId FROM Conversations 
                    WHERE (User1Id = @UserId1 AND User2Id = @UserId2) 
                       OR (User1Id = @UserId2 AND User2Id = @UserId1)";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId1", userId1);
                cmd.Parameters.AddWithValue("@UserId2", userId2);
                conn.Open();
                var result = cmd.ExecuteScalar();

                return result != null ? Convert.ToInt32(result) : (int?)null;
            }
        }

        private int GetOrCreateConversation(int userId1, int userId2)
        {
            var existingId = GetConversationId(userId1, userId2);
            if (existingId.HasValue)
            {
                return existingId.Value;
            }

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    INSERT INTO Conversations (User1Id, User2Id, CreatedAt)
                    VALUES (@User1Id, @User2Id, GETDATE());
                    SELECT SCOPE_IDENTITY();";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@User1Id", userId1);
                cmd.Parameters.AddWithValue("@User2Id", userId2);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private void MarkMessagesAsRead(int conversationId, int userId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    UPDATE Messages 
                    SET IsRead = 1 
                    WHERE ConversationId = @ConversationId AND ReceiverId = @UserId AND IsRead = 0";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@ConversationId", conversationId);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "Vừa xong";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} phút trước";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} giờ trước";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} ngày trước";

            return dateTime.ToString("dd/MM/yyyy");
        }
    }
}