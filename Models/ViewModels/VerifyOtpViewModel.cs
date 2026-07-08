using System.ComponentModel.DataAnnotations;

namespace BTL_2.Models.ViewModels
{
    public class VerifyOtpViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập email")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã OTP")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 ký tự")]
        [RegularExpression("^[0-9]*$", ErrorMessage = "Mã OTP chỉ được chứa số")]
        public string OtpCode { get; set; }
    }
}