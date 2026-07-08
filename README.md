
# JobPortal - ASP.NET Core MVC Recruitment Website

JobPortal là website tuyển dụng được xây dựng bằng ASP.NET Core MVC, Razor Views và SQL Server. Hệ thống hỗ trợ ba nhóm người dùng chính: Admin, Employer và Candidate. Ngoài các chức năng tuyển dụng thông thường, dự án có module bảo mật CV theo hướng Secure System Upgrade Challenge: CV được mã hóa, gắn metadata, tải bằng token dùng một lần, kiểm tra toàn vẹn file và ghi log truy cập.

## Mục Lục

- [Tổng quan chức năng](#tổng-quan-chức-năng)
- [Công nghệ sử dụng](#công-nghệ-sử-dụng)
- [Cấu trúc dự án](#cấu-trúc-dự-án)
- [Yêu cầu môi trường](#yêu-cầu-môi-trường)
- [Cài đặt và chạy dự án](#cài-đặt-và-chạy-dự-án)
- [Tài khoản mẫu](#tài-khoản-mẫu)
- [Phân quyền người dùng](#phân-quyền-người-dùng)
- [Luồng nghiệp vụ chính](#luồng-nghiệp-vụ-chính)
- [Bảo mật CV](#bảo-mật-cv)
- [Kiểm thử bảo mật bắt buộc](#kiểm-thử-bảo-mật-bắt-buộc)
- [Cấu hình quan trọng](#cấu-hình-quan-trọng)
- [Log và giám sát](#log-và-giám-sát)
- [Ghi chú vận hành](#ghi-chú-vận-hành)

## Tổng Quan Chức Năng

JobPortal cung cấp các chức năng chính:

- Hiển thị danh sách việc làm, công ty và tin tuyển dụng nổi bật.
- Tìm kiếm việc làm theo từ khóa, địa điểm và loại công việc.
- Xem chi tiết công việc, mô tả, yêu cầu, quyền lợi, deadline và thông tin công ty.
- Đăng ký, đăng nhập, đăng xuất, đổi mật khẩu, cập nhật hồ sơ cá nhân.
- Quên mật khẩu bằng OTP email.
- Ứng tuyển công việc bằng CV.
- Lưu việc làm yêu thích.
- Theo dõi công ty.
- Chat giữa người dùng và công ty.
- Nhận thông báo trong hệ thống.
- Admin quản lý người dùng, công việc, nhà tuyển dụng, thông báo và hồ sơ ứng tuyển.
- Employer xem và xử lý hồ sơ ứng tuyển thuộc công ty của mình.
- Module tải CV bảo mật với mã hóa, token, metadata, kiểm tra toàn vẹn và log truy cập.

## Công Nghệ Sử Dụng

- ASP.NET Core MVC, target framework `net10.0`.
- Razor Views.
- SQL Server.
- Entity Framework Core SQL Server.
- ADO.NET với `System.Data.SqlClient` ở một số controller và initializer.
- Bootstrap 5.
- jQuery.
- Font Awesome.
- MailKit và MimeKit để gửi email OTP.
- ASP.NET Core Cookie Authentication.
- ASP.NET Core Session.
- AES-256 CBC và SHA-256 cho module bảo mật CV.

## Cấu Trúc Dự Án

```text
BTL_2/
  Controllers/
    AccountController.cs          Quản lý đăng nhập, đăng ký, hồ sơ, OTP
    AdminController.cs            Dashboard và các chức năng quản trị
    JobsController.cs             Việc làm, ứng tuyển, lưu việc, tải CV bảo mật
    CVController.cs               Upload/download CV bảo mật độc lập
    CompaniesController.cs        Danh sách và chi tiết công ty
    ChatController.cs             Tin nhắn giữa người dùng
    NotificationController.cs     Thông báo người dùng
    TestSecurityController.cs     Kiểm thử bảo mật CV

  Data/
    JobDatabaseContext.cs         DbContext chính

  Models/
    Application.cs                Hồ sơ ứng tuyển
    CVMetadata.cs                 Metadata của CV mã hóa
    DownloadToken.cs              Token tải CV dùng một lần
    CVActivityLog.cs              Log truy cập CV
    User.cs, Job.cs, Company.cs   Model nghiệp vụ tuyển dụng

  Services/
    SecureCVService.cs            Upload, mã hóa, token, tải và kiểm tra CV
    EncryptionService.cs          AES và SHA-256
    ActivityLogService.cs         Ghi log truy cập CV
    ExpirationService.cs          Dọn token hết hạn và đánh dấu CV cũ
    EmailService.cs               Gửi email OTP

  Views/
    Shared/                       Layout chung
    Home/                         Trang chủ
    Jobs/                         Việc làm và ứng tuyển
    Admin/                        Trang quản trị
    Account/                      Tài khoản người dùng
    CV/                           Upload CV bảo mật
    TestSecurity/                 Kiểm thử bảo mật CV

  wwwroot/
    css/site.css                  Giao diện chung, dark mode
    js/site.js                    Script chung
    uploads/cvs/                  File CV đã mã hóa
    uploads/jobs/                 Ảnh bài tuyển dụng
```

## Yêu Cầu Môi Trường

- Windows hoặc môi trường có thể chạy .NET SDK.
- .NET SDK tương thích với target framework `net10.0`.
- SQL Server local.
- Visual Studio, Visual Studio Code hoặc terminal PowerShell.
- Kết nối mạng nếu cần restore package NuGet lần đầu.

Connection string mặc định:

```json
"DefaultConnection": "Server=.;Database=JobDatabase;Trusted_Connection=True;TrustServerCertificate=True;"
```

## Cài Đặt Và Chạy Dự Án

Từ thư mục gốc solution:

```powershell
cd C:\C#_BaiTap_LyThuyet\ASP_NET\BTL_2
dotnet restore
dotnet build BTL_2.slnx
dotnet run --project BTL_2\BTL_2.csproj
```

Khi ứng dụng khởi động:

- `DbInitializer` kiểm tra và tạo database schema nếu thiếu.
- Các bảng chính như `Users`, `Companies`, `Jobs`, `Applications`, `CVMetadata`, `DownloadTokens`, `CVActivityLogs`, `Messages`, `Notifications` được tạo hoặc bổ sung cột cần thiết.
- Thư mục upload CV và ảnh job được tạo tự động nếu chưa tồn tại.
- Data mẫu được thêm vào database nếu database mới.

Sau khi chạy, mở URL hiển thị trong terminal, thường là:

```text
https://localhost:<port>
```

hoặc

```text
http://localhost:<port>
```

## Tài Khoản Mẫu

Khi database mới được khởi tạo, hệ thống có thể tạo các tài khoản mẫu:

| Vai trò | Email | Mật khẩu |
| --- | --- | --- |
| Admin | `admin@jobportal.com` | `admin` |
| Employer | `hr@techcorp.com` | `employer123` |
| Candidate | `john@email.com` | `candidate123` |

Nếu không đăng nhập được, kiểm tra lại dữ liệu seed trong `Views/Home/Data/DbInitializer.cs` và database `JobDatabase`.

## Phân Quyền Người Dùng

### Candidate

Candidate là ứng viên tìm việc.

Chức năng chính:

- Xem danh sách việc làm.
- Tìm kiếm việc làm.
- Xem chi tiết công việc.
- Ứng tuyển bằng CV.
- Gửi thư xin việc.
- Lưu việc làm yêu thích.
- Theo dõi công ty.
- Xem danh sách hồ sơ đã ứng tuyển.
- Xóa hoặc rút hồ sơ trong phạm vi cho phép.
- Cập nhật hồ sơ cá nhân.

### Employer

Employer là nhà tuyển dụng hoặc người đại diện công ty.

Chức năng chính:

- Xem các hồ sơ ứng tuyển vào job thuộc công ty của mình.
- Lọc hồ sơ theo công việc.
- Xem thông tin ứng viên.
- Duyệt hoặc từ chối hồ sơ.
- Tạo token tải CV bảo mật.
- Tải CV đã giải mã nếu token hợp lệ.
- Xem log truy cập CV liên quan đến mình.
- Truy cập trang kiểm thử bảo mật CV.

Employer không được tải CV của job thuộc công ty khác. Mọi hành vi tải sai quyền đều bị chặn và ghi log.

### Admin

Admin quản trị toàn hệ thống.

Chức năng chính:

- Xem dashboard.
- Quản lý người dùng.
- Tạo, sửa, khóa hoặc mở tài khoản.
- Quản lý employer và phân công công ty.
- Quản lý tin tuyển dụng.
- Quản lý thông báo/tin nổi bật.
- Xem và xử lý toàn bộ hồ sơ ứng tuyển.
- Xem toàn bộ log truy cập CV.
- Chạy bộ kiểm thử bảo mật CV.

## Luồng Nghiệp Vụ Chính

### Tìm Việc

1. Người dùng vào trang danh sách việc làm.
2. Có thể tìm kiếm theo từ khóa, địa điểm, loại công việc.
3. Chọn một job để xem chi tiết.
4. Nếu là Candidate đã đăng nhập, có thể lưu job hoặc ứng tuyển.

### Ứng Tuyển

1. Candidate mở trang chi tiết job.
2. Chọn ứng tuyển.
3. Upload CV định dạng `.pdf`, `.doc`, `.docx`.
4. File tối đa 5 MB.
5. Nhập thư xin việc nếu cần.
6. Hệ thống kiểm tra dữ liệu, mã hóa CV, tạo metadata và lưu hồ sơ ứng tuyển.
7. Candidate được chuyển về trang hồ sơ ứng tuyển.

### Duyệt Hồ Sơ

1. Employer hoặc Admin vào trang quản lý hồ sơ.
2. Xem danh sách ứng viên.
3. Kiểm tra trạng thái CV bảo mật.
4. Tạo token tải CV nếu cần xem file.
5. Duyệt hoặc từ chối hồ sơ.
6. Hệ thống ghi lại log các thao tác tải CV.

### Tải CV Bảo Mật

1. Employer/Admin bấm `Tạo token & tải`.
2. Hệ thống tạo token dùng một lần, ràng buộc với tài khoản và session hiện tại.
3. Giao diện hiển thị link tải CV đã giải mã.
4. Người dùng bấm tải.
5. Hệ thống kiểm tra token, quyền truy cập, hạn CV, file mã hóa và hash toàn vẹn.
6. Nếu hợp lệ, file gốc được trả về.
7. Nếu không hợp lệ, yêu cầu bị từ chối và ghi log.

## Bảo Mật CV

Module bảo mật CV nằm chủ yếu trong:

- `Services/SecureCVService.cs`
- `Services/EncryptionService.cs`
- `Services/ActivityLogService.cs`
- `Models/CVMetadata.cs`
- `Models/DownloadToken.cs`
- `Models/CVActivityLog.cs`
- `Controllers/JobsController.cs`
- `Controllers/CVController.cs`
- `Controllers/TestSecurityController.cs`

### Metadata CV

Mỗi CV bảo mật có metadata:

- `candidate_id`: ID ứng viên.
- `job_id`: ID công việc.
- `upload_time`: thời gian upload.
- `expire_time`: thời gian hết hạn.
- `nonce`: giá trị ngẫu nhiên cho từng CV.
- `sha256_hash`: hash SHA-256 của file gốc.
- `encryption_iv`: IV dùng khi mã hóa AES.
- `stored_file_name`: tên file mã hóa trên server.
- `file_path`: đường dẫn file mã hóa.

### Mã Hóa

Khi upload CV:

1. File gốc được đọc vào bộ nhớ.
2. Hệ thống tính SHA-256 trên file gốc.
3. Tạo nonce ngẫu nhiên.
4. Tạo envelope `cv-secure-v2` chứa metadata và nội dung file dạng base64.
5. Envelope được mã hóa bằng AES-256 CBC.
6. File mã hóa được lưu với đuôi `.enc`.
7. Metadata được lưu vào database.

Điểm quan trọng: metadata không chỉ nằm trong database mà còn được đóng gói trong bản mã. Khi tải, hệ thống giải mã envelope và đối chiếu metadata trong file với metadata trong database. Nếu file hoặc metadata bị sửa, quá trình tải bị từ chối.

### Token Tải CV

Token tải CV có các đặc điểm:

- Sinh ngẫu nhiên bằng cryptographic RNG.
- Có mã xác thực tạm thời đi kèm.
- Chỉ dùng một lần.
- Hết hạn sau thời gian ngắn, mặc định 5 phút.
- Ràng buộc với `RecruiterId`.
- Ràng buộc với session đăng nhập hiện tại.
- Không xác thực chỉ dựa vào IP.

Token được lưu trong bảng `DownloadTokens`.

### Kiểm Tra Quyền

Khi tải CV:

- Admin được phép tải nếu token hợp lệ.
- Employer chỉ được tải CV của job thuộc công ty do mình quản lý.
- Candidate không được tải CV qua endpoint dành cho nhà tuyển dụng.
- Người chưa đăng nhập bị chuyển về trang đăng nhập.
- Token sai, hết hạn, đã dùng hoặc sai session đều bị từ chối.

### Kiểm Tra Toàn Vẹn

Hệ thống phát hiện CV bị sửa bằng:

- SHA-256 hash của file gốc.
- Metadata trong envelope mã hóa.
- So sánh fixed-time để tránh rò rỉ timing không cần thiết.
- Bắt lỗi giải mã hoặc sai padding khi bản mã bị sửa byte.

Nếu hash không khớp hoặc envelope không hợp lệ, hệ thống trả lỗi và ghi log `Failed_Integrity`.

## Kiểm Thử Bảo Mật Bắt Buộc

Trang kiểm thử nằm tại:

```text
/TestSecurity
```

Truy cập từ:

- Admin: menu duyệt hồ sơ CV hoặc dashboard.
- Employer: trang quản lý ứng viên.

Bộ test bắt buộc gồm:

| Test | Mục tiêu |
| --- | --- |
| Ứng viên hợp lệ gửi CV | Kiểm tra có CV bảo mật với metadata đầy đủ |
| Truy cập bằng token không hợp lệ | Token giả phải bị từ chối |
| Sửa file CV | File mã hóa bị sửa byte phải bị phát hiện |
| Quét toàn vẹn CV thật | CV thật phải giải mã được và hash khớp |
| Tải CV sau thời gian hết hạn | CV hết hạn phải bị từ chối |
| Nhà tuyển dụng không có quyền tải CV | Employer khác công ty không được tải |

Kết quả được hiển thị dạng PASS/FAIL để dễ kiểm tra khi chấm bài hoặc khi nâng cấp hệ thống.

## Cấu Hình Quan Trọng

File cấu hình chính:

```text
BTL_2/appsettings.json
```

### Database

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.;Database=JobDatabase;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

### Email OTP

```json
"SmtpSettings": {
  "Host": "smtp.gmail.com",
  "Port": "587",
  "EnableSsl": true,
  "Username": "...",
  "Password": "...",
  "FromEmail": "...",
  "FromName": "JobPortal"
}
```

Lưu ý: không nên commit SMTP password thật khi đưa dự án lên GitHub hoặc môi trường public.

### Khóa Mã Hóa

```json
"EncryptionSettings": {
  "AESKey": "BTL2SecretKey2024VeryStrong12345678",
  "KeyRotationDays": 30
}
```

Khóa AES hiện được đọc từ cấu hình. Khi triển khai thật, nên chuyển khóa sang Secret Manager, biến môi trường hoặc hệ thống quản lý secret.

### Cấu Hình CV

```json
"CVSettings": {
  "MaxFileSizeMB": 5,
  "AllowedExtensions": [ ".pdf", ".doc", ".docx" ],
  "TokenExpiryMinutes": 5,
  "CVRetentionDays": 90,
  "UploadPath": "wwwroot/uploads/cvs/"
}
```

Ý nghĩa:

- `MaxFileSizeMB`: dung lượng CV tối đa.
- `AllowedExtensions`: định dạng file cho phép.
- `TokenExpiryMinutes`: thời gian sống của token tải CV.
- `CVRetentionDays`: số ngày CV còn hiệu lực.
- `UploadPath`: thư mục lưu file CV mã hóa.

### Cấu Hình Bảo Mật

```json
"SecuritySettings": {
  "EnableAuditLogging": true,
  "EnableIntegrityCheck": true,
  "RequireOneTimeToken": true
}
```

## Log Và Giám Sát

Log truy cập CV được lưu trong bảng `CVActivityLogs`.

Các trạng thái thường gặp:

| Trạng thái | Ý nghĩa |
| --- | --- |
| `Token_Created` | Employer/Admin tạo token tải CV |
| `Success` | Tải CV thành công |
| `Failed_InvalidToken` | Token sai, hết hạn, đã dùng hoặc sai session |
| `Failed_Expired` | CV đã hết hạn |
| `Failed_Permission` | Người tải không có quyền |
| `Failed_Unauthorized` | Request không được phép |
| `Failed_FileMissing` | File mã hóa không tồn tại trên server |
| `Failed_Integrity` | File bị sửa, hash sai hoặc envelope không hợp lệ |
| `Failed_Error` | Lỗi khác khi tải CV |

Trang log hiển thị:

- Thời gian truy cập.
- Người tải.
- CV metadata ID.
- Tên file gốc.
- Job liên quan.
- IP.
- User-Agent.
- Trạng thái.
- Chi tiết lỗi.

## Giao Diện

Website sử dụng layout Razor chung và Bootstrap. Giao diện có dark mode. Phần dark mode đã được xử lý để tránh lỗi nháy trắng khi chuyển trang hoặc bấm chức năng:

- Theme tối được áp sớm trên `html`.
- CSS preload dark mode phủ nền, card, modal, form và dropdown.
- Focus ring trong dark mode không còn dùng nền trắng.
- Một số animation vào trang bị tắt trong dark mode để giảm nhấp nháy.

Các file giao diện chính:

- `Views/Shared/_Layout.cshtml`
- `Views/Shared/_Layout_Admin.cshtml`
- `wwwroot/css/site.css`
- `Views/Home/Index.cshtml`
- `Views/Jobs/Index.cshtml`
- `Views/Admin/ManageApplications.cshtml`
- `Views/CV/Upload.cshtml`
- `Views/TestSecurity/Index.cshtml`

## Database Chính

Một số bảng quan trọng:

| Bảng | Chức năng |
| --- | --- |
| `Users` | Người dùng hệ thống |
| `Companies` | Công ty tuyển dụng |
| `Jobs` | Tin tuyển dụng |
| `Applications` | Hồ sơ ứng tuyển |
| `CVMetadata` | Metadata CV bảo mật |
| `DownloadTokens` | Token tải CV |
| `CVActivityLogs` | Log truy cập CV |
| `SavedJobs` | Việc làm đã lưu |
| `Follows` | Theo dõi công ty |
| `PasswordResets` | OTP quên mật khẩu |
| `Announcements` | Thông báo/tin nổi bật |
| `Conversations` | Cuộc trò chuyện |
| `Messages` | Tin nhắn |

## Các Route Quan Trọng

| Route | Chức năng |
| --- | --- |
| `/` | Trang chủ |
| `/Jobs` | Danh sách việc làm |
| `/Jobs/Details/{id}` | Chi tiết việc làm |
| `/Jobs/Apply/{id}` | Ứng tuyển |
| `/Jobs/SavedJobs` | Việc đã lưu |
| `/Jobs/ManageApplications` | Employer/Admin quản lý hồ sơ ứng tuyển |
| `/Jobs/CVAccessLogs` | Log CV theo employer |
| `/Admin/Dashboard` | Dashboard Admin |
| `/Admin/ManageUsers` | Quản lý người dùng |
| `/Admin/ManageJobs` | Quản lý tin tuyển dụng |
| `/Admin/ManageApplications` | Admin duyệt hồ sơ |
| `/Admin/CVAccessLogs` | Admin xem log CV |
| `/CV/Upload` | Upload CV bảo mật độc lập |
| `/TestSecurity` | Kiểm thử bảo mật CV |
| `/Account/Login` | Đăng nhập |
| `/Account/Register` | Đăng ký |
| `/Account/Profile` | Hồ sơ cá nhân |
| `/Chat` | Tin nhắn |

## Kiểm Tra Build

Chạy:

```powershell
dotnet build BTL_2.slnx
```

Build hiện có thể xuất hiện nhiều warning cũ:

- `MailKit` có advisory mức moderate.
- `System.Data.SqlClient` obsolete, nên cân nhắc chuyển sang `Microsoft.Data.SqlClient`.
- Nullable warning ở một số model/viewmodel/controller.

Các warning này không nhất thiết làm ứng dụng không chạy, nhưng nên xử lý nếu muốn nâng cấp lên chuẩn production.

## Ghi Chú Vận Hành

- Không đưa SMTP password thật và AES key thật lên repository public.
- Nên đổi `EncryptionSettings:AESKey` trước khi chạy production.
- File trong `wwwroot/uploads/cvs` là file mã hóa, không mở trực tiếp như CV gốc.
- Nếu mất AES key, các CV đã mã hóa bằng key cũ sẽ không giải mã được.
- Token tải CV chỉ dùng một lần; nếu đã bấm tải hoặc hết hạn, cần tạo token mới.
- CV hết hạn theo `CVRetentionDays` sẽ bị chặn tải.
- `ExpirationService` chạy nền để dọn token hết hạn và xử lý CV cũ.
- Khi đổi schema thủ công, kiểm tra lại `DbInitializer` để tránh lệch database.
- Khi deploy thật, nên cấu hình HTTPS bắt buộc và `CookieSecurePolicy.Always`.
# Ý tưởng phát triển đề tài:

Nâng cấp bảo mật CV: mã hóa dữ liệu, token tải một lần, kiểm tra toàn vẹn file
Tích hợp AI để gợi ý việc làm và hỗ trợ đánh giá CV ứng viên
Phát triển thêm chat realtime, dashboard thống kê và quản lý tuyển dụng
Chuẩn hóa kiến trúc hệ thống, triển khai cloud và tối ưu cho production
Hướng tới xây dựng nền tảng tuyển dụng an toàn, hiện đại và chuyên nghiệp 


## Hướng Nâng Cấp Đề Xuất

Các hướng nâng cấp để dự án chuyên nghiệp hơn:

- Chuyển toàn bộ ADO.NET cũ sang EF Core hoặc repository/service thống nhất.
- Tách dashboard Admin và Employer rõ hơn ở controller/service.
- Thêm migration chính thức thay cho initializer tự sửa schema.
- Lưu file CV mã hóa ngoài `wwwroot` để tránh public static path.
- Dùng Data Protection hoặc Key Vault để quản lý khóa mã hóa.
- Thêm rate limiting cho đăng nhập, OTP và tạo token tải CV.
- Thêm audit log cho thay đổi trạng thái hồ sơ.
- Thêm unit test/integration test tự động cho module CV security.
- Chuẩn hóa role `Employer`, `Admin`, `Candidate` bằng policy thay vì kiểm tra session thủ công.
- Bổ sung phân trang và tìm kiếm nâng cao cho log CV.

# Ý tưởng phát triển đề tài:

Nâng cấp bảo mật CV: mã hóa dữ liệu, token tải một lần, kiểm tra toàn vẹn file
Tích hợp AI để gợi ý việc làm và hỗ trợ đánh giá CV ứng viên
Phát triển thêm chat realtime, dashboard thống kê và quản lý tuyển dụng
Chuẩn hóa kiến trúc hệ thống, triển khai cloud và tối ưu cho production
Hướng tới xây dựng nền tảng tuyển dụng an toàn, hiện đại và chuyên nghiệp 
