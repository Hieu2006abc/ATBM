using BTL_2.Data;
using BTL_2.Models;
using BTL_2.Services;
using JobPortal.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Thêm cấu hình cho phép upload file lớn
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10MB
    options.MemoryBufferThreshold = int.MaxValue;
});

// Add services
builder.Services.AddControllersWithViews();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "DataProtectionKeys")))
    .SetApplicationName("BTL_2");

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Đăng ký DbContext
builder.Services.AddDbContext<JobDatabaseContext>();

// Đăng ký các service cho CV Security
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<ISecureCVService, SecureCVService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();

// Đăng ký background service
builder.Services.AddHostedService<ExpirationService>();

// Add session - QUAN TRỌNG
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Cấu hình email
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddScoped<EmailService>();

// Đăng ký Activity Log Service
builder.Services.AddScoped<ActivityLogService>();

// Thêm Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "Cookie";
    options.DefaultSignInScheme = "Cookie";
    options.DefaultChallengeScheme = "Cookie";
})
.AddCookie("Cookie", options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
});

var app = builder.Build();

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var mustChangePassword = context.Session.GetString("MustChangePassword") == "true";
    var path = context.Request.Path;

    if (mustChangePassword &&
        !path.StartsWithSegments("/Account/ChangePassword") &&
        !path.StartsWithSegments("/Account/Logout") &&
        !path.StartsWithSegments("/Account/Login"))
    {
        context.Response.Redirect("/Account/ChangePassword");
        return;
    }

    await next();
});

// Khởi tạo database
try
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    Console.WriteLine("Đang khởi tạo database...");
    var databaseInitialized = DbInitializer.Initialize(connectionString);
    Console.WriteLine(databaseInitialized
        ? "Khởi tạo database thành công!"
        : "Khởi tạo database không thành công. Vui lòng kiểm tra SQL Server/connection string.");
}
catch (Exception ex)
{
    Console.WriteLine($"Lỗi khởi tạo database: {ex.Message}");
}

// Tạo thư mục uploads cho CV
try
{
    // Tạo thư mục uploads/CVs
    var cvUploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "cvs");
    if (!Directory.Exists(cvUploadFolder))
    {
        Directory.CreateDirectory(cvUploadFolder);
        Console.WriteLine("Đã tạo thư mục wwwroot/uploads/cvs");
    }

    // Tạo avatar mặc định
    Console.WriteLine("Đang kiểm tra file avatar mặc định...");
    var avatarFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images");
    var avatarPath = Path.Combine(avatarFolder, "default-avatar.png");

    if (!Directory.Exists(avatarFolder))
    {
        Directory.CreateDirectory(avatarFolder);
        Console.WriteLine("Đã tạo thư mục wwwroot/images");
    }

    if (!File.Exists(avatarPath))
    {
        Console.WriteLine("Đang tạo file default-avatar.png...");
        string base64Avatar = "iVBORw0KGgoAAAANSUhEUgAAAGQAAABkCAYAAABw4pVUAAAABmJLR0QA/wD/AP+gvaeTAAAA6UlEQVR4nO3aMQ6AIAwF0E7G4O7VzBNxBkM3ExMnC6G0UoW+/pM0afoNtJQWkJmZmZmZmZmZ2V8sA5mBTEFmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgMZAYyA5mBzEBmIDOQGcgM5H28AEgGEa1u3ZnHAAAAAElFTkSuQmCC";
        byte[] imageBytes = Convert.FromBase64String(base64Avatar);
        File.WriteAllBytes(avatarPath, imageBytes);
        Console.WriteLine("Đã tạo file default-avatar.png thành công!");
    }
    else
    {
        Console.WriteLine("File default-avatar.png đã tồn tại");
    }

    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads/jobs");
    if (!Directory.Exists(uploadsFolder))
    {
        Directory.CreateDirectory(uploadsFolder);
        Console.WriteLine("Đã tạo thư mục wwwroot/uploads/jobs");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Lỗi tạo thư mục: {ex.Message}");
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "admin",
    pattern: "Admin",
    defaults: new { controller = "Admin", action = "Index" });

app.Run();
