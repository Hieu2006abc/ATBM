using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using BTL_2.Models;
using System.Data.SqlClient;
using System;
using System.Collections.Generic;

namespace BTL_2.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public NotificationsController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        [HttpGet]
        public IActionResult GetUnreadCount()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (userId == null) return Json(0);

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT COUNT(*) FROM Notifications 
                               WHERE (TargetRole = 'All' OR TargetRole = (SELECT Role FROM Users WHERE UserId = @UserId))
                               AND IsRead = 0";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                int count = (int)cmd.ExecuteScalar();
                return Json(count);
            }
        }

        [HttpGet]
        public IActionResult GetNotifications()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (userId == null) return Json(new List<Notification>());

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = @"SELECT TOP 10 * FROM Notifications 
                               WHERE (TargetRole = 'All' OR TargetRole = (SELECT Role FROM Users WHERE UserId = @UserId))
                               ORDER BY CreatedAt DESC";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                SqlDataReader reader = cmd.ExecuteReader();

                var notifications = new List<Notification>();
                while (reader.Read())
                {
                    notifications.Add(new Notification
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        Title = reader["Title"].ToString(),
                        Message = reader["Message"].ToString(),
                        Type = reader["Type"].ToString(),
                        CreatedAt = Convert.ToDateTime(reader["CreatedAt"]),
                        IsRead = Convert.ToBoolean(reader["IsRead"]),
                        Link = reader["Link"]?.ToString()
                    });
                }
                return Json(notifications);
            }
        }

        [HttpPost]
        public IActionResult MarkAsRead(int id)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string query = "UPDATE Notifications SET IsRead = 1 WHERE Id = @Id";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@Id", id);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            return Ok();
        }
    }
}