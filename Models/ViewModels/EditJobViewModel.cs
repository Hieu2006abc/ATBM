using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTL_2.Models.ViewModels
{
    public class EditJobViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập ID công việc")]
        public int JobId { get; set; }

        // Không có Required cho các trường khác
        [Display(Name = "Tiêu đề công việc")]
        public string Title { get; set; }

        [Display(Name = "Công ty")]
        public int? CompanyId { get; set; }

        [Display(Name = "Lương tối thiểu")]
        public decimal? SalaryMin { get; set; }

        [Display(Name = "Lương tối đa")]
        public decimal? SalaryMax { get; set; }

        [Display(Name = "Địa điểm")]
        public string Location { get; set; }

        [Display(Name = "Loại công việc")]
        public string JobType { get; set; }

        [Display(Name = "Mô tả công việc")]
        public string Description { get; set; }

        [Display(Name = "Yêu cầu công việc")]
        public string Requirement { get; set; }

        [Display(Name = "Quyền lợi")]
        public string Benefit { get; set; }

        [Display(Name = "Kỹ năng yêu cầu")]
        public string Skills { get; set; }

        [Display(Name = "Kinh nghiệm")]
        public string Experience { get; set; }

        [Display(Name = "Trình độ học vấn")]
        public string Education { get; set; }

        [Display(Name = "Số lượng tuyển")]
        public int? NumberOfRecruits { get; set; }

        [DataType(DataType.Date)]
        [DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
        [Display(Name = "Hạn nộp hồ sơ")]
        public DateTime? Deadline { get; set; }

        [Display(Name = "Hình ảnh công việc")]
        public IFormFile JobImageFile { get; set; }

        public string JobImage { get; set; }

        [Display(Name = "Video giới thiệu")]
        public string VideoUrl { get; set; }

        [Display(Name = "Tags")]
        public string Tags { get; set; }

        [Display(Name = "Tin nổi bật")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Tin khẩn cấp")]
        public bool IsUrgent { get; set; }

        [Display(Name = "Làm việc từ xa")]
        public bool IsRemote { get; set; }

        // Danh sách cho dropdown
        public List<Company> Companies { get; set; } = new List<Company>();
    }
}