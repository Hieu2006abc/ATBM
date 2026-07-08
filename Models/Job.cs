using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTL_2.Models
{
    public class Job
    {
        [Key]
        public int JobId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tiêu đề công việc")]
        [StringLength(200, ErrorMessage = "Tiêu đề không được vượt quá 200 ký tự")]
        [Display(Name = "Tiêu đề")]
        public string Title { get; set; }

        [Display(Name = "Công ty")]
        public int? CompanyId { get; set; }

        [Display(Name = "Mức lương tối thiểu")]
        [Range(0, double.MaxValue, ErrorMessage = "Mức lương không hợp lệ")]
        public decimal? SalaryMin { get; set; }

        [Display(Name = "Mức lương tối đa")]
        [Range(0, double.MaxValue, ErrorMessage = "Mức lương không hợp lệ")]
        public decimal? SalaryMax { get; set; }

        [Display(Name = "Địa điểm")]
        [StringLength(200)]
        public string Location { get; set; }

        [Display(Name = "Loại công việc")]
        [StringLength(50)]
        public string JobType { get; set; }

        [Display(Name = "Mô tả công việc")]
        public string Description { get; set; }

        [Display(Name = "Yêu cầu")]
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
        [Range(1, 100)]
        public int? NumberOfRecruits { get; set; }

        [Display(Name = "Hạn nộp hồ sơ")]
        [DataType(DataType.Date)]
        public DateTime? Deadline { get; set; }

        [Display(Name = "Ngày đăng")]
        [DataType(DataType.Date)]
        public DateTime? CreatedDate { get; set; }

        [Display(Name = "Ngày cập nhật")]
        [DataType(DataType.Date)]
        public DateTime? UpdatedDate { get; set; }

        [Display(Name = "Trạng thái")]
        public bool? IsActive { get; set; }

        [Display(Name = "Lượt xem")]
        public int? Views { get; set; }

        [Display(Name = "Hình ảnh công việc")]
        public string JobImage { get; set; }

        [Display(Name = "File đính kèm")]
        [NotMapped]
        public string AttachmentFile { get; set; }

        [Display(Name = "Video giới thiệu")]
        public string VideoUrl { get; set; }

        [Display(Name = "Tags")]
        public string Tags { get; set; }

        [Display(Name = "Nổi bật")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Khẩn cấp")]
        public bool IsUrgent { get; set; }

        [Display(Name = "Làm việc từ xa")]
        public bool IsRemote { get; set; }

        // Navigation properties
        public virtual Company Company { get; set; }
        public virtual ICollection<Application> Applications { get; set; } = new List<Application>();
        public virtual ICollection<Category> Categories { get; set; } = new List<Category>();
    }

    // Enum cho loại công việc
    public enum JobTypeEnum
    {
        [Display(Name = "Toàn thời gian")]
        FullTime,
        [Display(Name = "Bán thời gian")]
        PartTime,
        [Display(Name = "Làm việc từ xa")]
        Remote,
        [Display(Name = "Thực tập")]
        Intern,
        [Display(Name = "Freelance")]
        Freelance,
        [Display(Name = "Hợp đồng")]
        Contract,
        [Display(Name = "Theo ca")]
        Shift
    }

    // Enum cho kinh nghiệm
    public enum ExperienceEnum
    {
        [Display(Name = "Không yêu cầu kinh nghiệm")]
        NoExperience,
        [Display(Name = "Dưới 1 năm")]
        Under1Year,
        [Display(Name = "1 - 2 năm")]
        OneToTwoYears,
        [Display(Name = "2 - 3 năm")]
        TwoToThreeYears,
        [Display(Name = "3 - 5 năm")]
        ThreeToFiveYears,
        [Display(Name = "Trên 5 năm")]
        OverFiveYears
    }

    // Enum cho trình độ học vấn
    public enum EducationEnum
    {
        [Display(Name = "Không yêu cầu")]
        NoRequirement,
        [Display(Name = "Trung cấp")]
        Intermediate,
        [Display(Name = "Cao đẳng")]
        College,
        [Display(Name = "Đại học")]
        University,
        [Display(Name = "Thạc sĩ")]
        Master,
        [Display(Name = "Tiến sĩ")]
        Doctor
    }
}
