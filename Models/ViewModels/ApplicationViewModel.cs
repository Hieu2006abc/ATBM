using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace BTL_2.Models.ViewModels
{
    public class ApplicationViewModel
    {
        public int JobId { get; set; }

        public string JobTitle { get; set; }

        public string CompanyName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập thư xin việc")]
        [Display(Name = "Thư xin việc")]
        public string CoverLetter { get; set; }

        [Required(ErrorMessage = "Vui lòng tải lên CV")]
        [Display(Name = "CV của bạn")]
        public IFormFile CVFile { get; set; }
    }
}