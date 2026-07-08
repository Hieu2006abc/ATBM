using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using BTL_2.Models;

namespace BTL_2.Controllers
{
    public class NotificationController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public NotificationController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public JsonResult GetUnreadCount()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (userId == null) return Json(0);

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "SELECT COUNT(*) FROM UserNotifications WHERE UserId = @UserId AND IsRead = 0";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                conn.Open();
                int count = (int)cmd.ExecuteScalar();
                return Json(count);
            }
        }

        [HttpGet]
        public JsonResult GetNotifications()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (userId == null) return Json(new List<object>());

            var notifications = new List<object>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"
                    SELECT TOP 10 n.*, 
                           CASE WHEN n.AnnouncementId IS NOT NULL THEN a.Title ELSE n.Title END as DisplayTitle,
                           CASE WHEN n.AnnouncementId IS NOT NULL THEN a.Content ELSE n.Content END as DisplayContent,
                           a.Type as AnnouncementType
                    FROM UserNotifications n
                    LEFT JOIN Announcements a ON n.AnnouncementId = a.Id
                    WHERE n.UserId = @UserId
                    ORDER BY n.CreatedAt DESC";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var notif = new
                    {
                        id = reader["Id"],
                        title = reader["DisplayTitle"],
                        content = reader["DisplayContent"],
                        type = reader["Type"],
                        isRead = reader["IsRead"],
                        createdAt = reader["CreatedAt"],
                        timeAgo = GetTimeAgo(Convert.ToDateTime(reader["CreatedAt"])),
                        iconClass = GetIconClass(reader["Type"].ToString()),
                        linkUrl = reader["LinkUrl"]
                    };
                    notifications.Add(notif);
                }
            }

            return Json(notifications);
        }

        [HttpPost]
        public IActionResult MarkAsRead(int id)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (userId == null) return Json(new { success = false });

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE UserNotifications SET IsRead = 1 WHERE Id = @Id AND UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                cmd.Parameters.AddWithValue("@UserId", userId);

                conn.Open();
                cmd.ExecuteNonQuery();
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult MarkAllAsRead()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (userId == null) return Json(new { success = false });

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE UserNotifications SET IsRead = 1 WHERE UserId = @UserId";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                conn.Open();
                cmd.ExecuteNonQuery();
            }

            return Json(new { success = true });
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var span = DateTime.Now - dateTime;
            if (span.TotalMinutes < 1) return "Vài giây trước";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} phút trước";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} giờ trước";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} ngày trước";
            return dateTime.ToString("dd/MM/yyyy");
        }

        private string GetIconClass(string type)
        {
            return type switch
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