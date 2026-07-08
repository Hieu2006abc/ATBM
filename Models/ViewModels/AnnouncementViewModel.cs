using Microsoft.AspNetCore.Http;
using System;
using System.ComponentModel.DataAnnotations;

namespace BTL_2.Models.ViewModels
{
    public class AnnouncementViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tiêu đề")]
        [StringLength(200, ErrorMessage = "Tiêu đề không quá 200 ký tự")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập nội dung")]
        public string Content { get; set; }

        public string Type { get; set; } = "info";

        public string TargetRole { get; set; } = "All";

        public DateTime? ExpiryDate { get; set; }

        // KHÔNG có [Required] cho ImageUrl
        public string ImageUrl { get; set; }

        public string LinkUrl { get; set; }

        // File upload
        public IFormFile ImageFile { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}