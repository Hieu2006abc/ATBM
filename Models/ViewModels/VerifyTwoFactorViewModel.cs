using System.ComponentModel.DataAnnotations;

namespace BTL_2.Models.ViewModels
{
    public class VerifyTwoFactorViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập mã xác thực")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Mã xác thực gồm 6 chữ số")]
        public string Code { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }
}
