namespace BTL_2.Models.ViewModels
{
    public class CVDownloadAccessViewModel
    {
        public int StatusCode { get; set; }
        public string Title { get; set; } = "Không thể tải CV";
        public string Message { get; set; } = "Yêu cầu tải CV chưa được chấp nhận.";
        public string? Detail { get; set; }
        public string IconClass { get; set; } = "fa-shield-alt";
        public string BadgeText { get; set; } = "Bị từ chối";
        public string PrimaryActionText { get; set; } = "Về quản lý hồ sơ";
        public string PrimaryActionUrl { get; set; } = "/Jobs/ManageApplications";
        public string SecondaryActionText { get; set; } = "Xem việc làm";
        public string SecondaryActionUrl { get; set; } = "/Jobs";
        public string SupportText { get; set; } = "Nếu bạn cần tải lại CV, hãy tạo link tải mới từ đúng hồ sơ ứng tuyển.";
    }
}
