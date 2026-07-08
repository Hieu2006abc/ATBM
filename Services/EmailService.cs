using System;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace BTL_2.Services
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateOtpCode()
        {
            Random random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        public string GetResetPasswordEmailBody(string otpCode)
        {
            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <title>Khôi phục mật khẩu</title>
                </head>
                <body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                    <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e0e0e0; border-radius: 10px;'>
                        <h2 style='color: #2563eb; text-align: center;'>JobPortal - Khôi phục mật khẩu</h2>
                        
                        <p>Xin chào,</p>
                        
                        <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>
                        
                        <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 10px; text-align: center; margin: 20px 0;'>
                            <h3 style='margin: 0 0 10px 0;'>Mã xác thực OTP của bạn là:</h3>
                            <h1 style='font-size: 48px; letter-spacing: 10px; margin: 10px 0;'>{otpCode}</h1>
                            <p style='margin: 10px 0 0 0;'>Mã có hiệu lực trong 15 phút</p>
                        </div>
                        
                        <p>Vui lòng nhập mã OTP này trên trang xác thực để tiếp tục đặt lại mật khẩu.</p>
                        
                        <p>Nếu bạn không yêu cầu khôi phục mật khẩu, vui lòng bỏ qua email này.</p>
                        
                        <hr style='border: 1px solid #e0e0e0; margin: 20px 0;'>
                        
                        <p style='color: #666; font-size: 12px; text-align: center;'>
                            Đây là email tự động, vui lòng không trả lời email này.<br>
                            &copy; 2024 JobPortal. Tất cả các quyền được bảo lưu.
                        </p>
                    </div>
                </body>
                </html>";
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                toEmail = toEmail?.Trim();

                Console.WriteLine($"=== SENDING EMAIL ===");
                Console.WriteLine($"To: {toEmail}");
                Console.WriteLine($"Subject: {subject}");

                // Kiểm tra tham số đầu vào
                if (string.IsNullOrEmpty(toEmail))
                {
                    Console.WriteLine("❌ Email recipient is null or empty");
                    return false;
                }

                if (string.IsNullOrEmpty(subject))
                {
                    Console.WriteLine("❌ Email subject is null or empty");
                    return false;
                }

                if (string.IsNullOrEmpty(body))
                {
                    Console.WriteLine("❌ Email body is null or empty");
                    return false;
                }

                // Lấy cấu hình SMTP
                var smtpSettings = _configuration.GetSection("SmtpSettings");

                string host = smtpSettings["Host"];
                string portStr = smtpSettings["Port"];
                string enableSslStr = smtpSettings["EnableSsl"];
                string username = smtpSettings["Username"];
                string password = smtpSettings["Password"]?.Replace(" ", "");
                string fromEmail = smtpSettings["FromEmail"];
                string fromName = smtpSettings["FromName"];

                // Kiểm tra cấu hình
                if (string.IsNullOrEmpty(host))
                {
                    Console.WriteLine("❌ SMTP Host is not configured");
                    return false;
                }

                if (string.IsNullOrEmpty(portStr))
                {
                    Console.WriteLine("❌ SMTP Port is not configured");
                    return false;
                }

                if (string.IsNullOrEmpty(username))
                {
                    Console.WriteLine("❌ SMTP Username is not configured");
                    return false;
                }

                if (string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("❌ SMTP Password is not configured");
                    return false;
                }

                if (string.IsNullOrEmpty(fromEmail))
                {
                    Console.WriteLine("❌ SMTP FromEmail is not configured");
                    return false;
                }

                int port = int.Parse(portStr);
                bool enableSsl = !string.IsNullOrEmpty(enableSslStr) && bool.Parse(enableSslStr);

                Console.WriteLine($"SMTP Host: {host}");
                Console.WriteLine($"SMTP Port: {port}");
                Console.WriteLine($"SMTP SSL: {enableSsl}");
                Console.WriteLine($"SMTP Username: {username}");
                Console.WriteLine($"SMTP From: {fromEmail}");

                using (var client = new SmtpClient(host, port))
                {
                    client.EnableSsl = enableSsl;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(username, password);
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;

                    var mailMessage = new MailMessage
                    {
                        From = new MailAddress(fromEmail, fromName ?? "JobPortal"),
                        Subject = subject,
                        Body = body,
                        IsBodyHtml = true,
                        SubjectEncoding = Encoding.UTF8,
                        BodyEncoding = Encoding.UTF8,
                        HeadersEncoding = Encoding.UTF8,
                        Priority = MailPriority.Normal,
                        DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure
                    };
                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                    Console.WriteLine($"✅ Email sent successfully at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    return true;
                }
            }
            catch (SmtpException smtpEx)
            {
                Console.WriteLine($"❌ SMTP Error: {smtpEx.Message}");
                Console.WriteLine($"Status Code: {smtpEx.StatusCode}");
                return false;
            }
            catch (ArgumentNullException argEx)
            {
                Console.WriteLine($"❌ Argument Null Error: {argEx.Message}");
                Console.WriteLine($"Parameter: {argEx.ParamName}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Email Error: {ex.Message}");
                return false;
            }
        }
    }
}
