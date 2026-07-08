using BTL_2.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace BTL_2.Data;

public partial class JobDatabaseContext : DbContext
{
    public JobDatabaseContext()
    {
    }

    public JobDatabaseContext(DbContextOptions<JobDatabaseContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Application> Applications { get; set; }
    public virtual DbSet<Category> Categories { get; set; }
    public virtual DbSet<Company> Companies { get; set; }
    public virtual DbSet<Job> Jobs { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<CVMetadata> CVMetadata { get; set; }
    public virtual DbSet<DownloadToken> DownloadTokens { get; set; }
    public virtual DbSet<CVActivityLog> CVActivityLogs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=JobDatabase;Trusted_Connection=True;TrustServerCertificate=True;");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Cấu hình cho Application
        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.ApplicationId).HasName("PK__Applicat__C93A4F79C586CF96");
            entity.Property(e => e.ApplicationId).HasColumnName("ApplicationID");
            entity.Property(e => e.ApplyDate).HasDefaultValueSql("(getdate())").HasColumnType("datetime");
            entity.Property(e => e.CVFile).HasMaxLength(500).HasColumnName("CVFile");
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Pending");
            entity.Property(e => e.UserId).HasColumnName("UserID");

            entity.HasOne(d => d.Job)
                .WithMany(p => p.Applications)
                .HasForeignKey(d => d.JobId)
                .HasConstraintName("FK__Applicati__JobID__47DBAE45");

            entity.HasOne(d => d.User)
                .WithMany(p => p.Applications)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__Applicati__UserI__48CFD27E");
        });

        // Cấu hình cho Category
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__Categori__19093A2B0BB3FA7A");
            entity.Property(e => e.CategoryId).HasColumnName("CategoryID");
            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(255);
        });

        // Cấu hình cho Company
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(e => e.CompanyId).HasName("PK__Companie__2D971C4CE1A10AE6");
            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.CompanyName).HasMaxLength(200);
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())").HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.EmployerId).HasColumnName("EmployerID");
            entity.Property(e => e.Logo).HasMaxLength(500);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Website).HasMaxLength(200);

            entity.HasOne(d => d.Employer)
                .WithMany(p => p.Companies)
                .HasForeignKey(d => d.EmployerId)
                .HasConstraintName("FK__Companies__Emplo__3E52440B");
        });

        // Cấu hình cho Job
        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.JobId).HasName("PK__Jobs__056690E2F2798B2E");
            entity.Property(e => e.JobId).HasColumnName("JobID");
            entity.Property(e => e.CompanyId).HasColumnName("CompanyID");
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())").HasColumnType("datetime");
            entity.Property(e => e.Deadline).HasColumnType("datetime");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.JobType).HasMaxLength(50);
            entity.Property(e => e.Location).HasMaxLength(200);
            entity.Property(e => e.SalaryMax).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.SalaryMin).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Views).HasDefaultValue(0);

            entity.HasOne(d => d.Company)
                .WithMany(p => p.Jobs)
                .HasForeignKey(d => d.CompanyId)
                .HasConstraintName("FK__Jobs__CompanyID__412EB0B6");

            entity.HasMany(d => d.Categories)
                .WithMany(p => p.Jobs)
                .UsingEntity<Dictionary<string, object>>(
                    "JobCategory",
                    r => r.HasOne<Category>().WithMany().HasForeignKey("CategoryId"),
                    l => l.HasOne<Job>().WithMany().HasForeignKey("JobId"),
                    j =>
                    {
                        j.HasKey("JobId", "CategoryId");
                        j.ToTable("JobCategories");
                        j.IndexerProperty<int>("JobId").HasColumnName("JobID");
                        j.IndexerProperty<int>("CategoryId").HasColumnName("CategoryID");
                    });
        });

        // Cấu hình cho User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CCAC7FE5F331");
            entity.HasIndex(e => e.Email, "UQ__Users__A9D10534F0E34D60").IsUnique();
            entity.Property(e => e.UserId).HasColumnName("UserID");
            entity.Property(e => e.Address).HasMaxLength(255);
            entity.Property(e => e.CreatedDate).HasDefaultValueSql("(getdate())").HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MustChangePassword).HasDefaultValue(false);
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.TwoFactorSecret).HasMaxLength(512);
        });

        // ==================== CẤU HÌNH CHO CV SECURITY ====================

        // Cấu hình cho CVMetadata
        modelBuilder.Entity<CVMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CandidateId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.OriginalFileName).HasMaxLength(500);
            entity.Property(e => e.StoredFileName).HasMaxLength(500);
            entity.Property(e => e.FilePath).HasMaxLength(1000);
            entity.Property(e => e.FileType).HasMaxLength(50);
            entity.Property(e => e.SHA256Hash).HasMaxLength(100);
            entity.Property(e => e.EncryptionIV).HasMaxLength(100);
            entity.Property(e => e.Nonce).HasMaxLength(100);
            entity.HasIndex(e => e.CandidateId);
            entity.HasIndex(e => e.JobId);
            entity.HasIndex(e => e.ExpireTime);
            entity.Ignore(e => e.Candidate);
            entity.Ignore(e => e.Job);
            entity.ToTable("CVMetadata");
        });

        // Cấu hình cho DownloadToken
        modelBuilder.Entity<DownloadToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(100);
            entity.Property(e => e.RecruiterId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.SessionId).HasMaxLength(128);
            entity.Property(e => e.VerificationCodeHash).HasMaxLength(128);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasOne(d => d.CVMetadata)
                .WithMany()
                .HasForeignKey(d => d.CVMetadataId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Ignore(e => e.Recruiter);
            entity.ToTable("DownloadTokens");
        });

        // Cấu hình cho CVActivityLog
        modelBuilder.Entity<CVActivityLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RecruiterId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.IPAddress).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.HasIndex(e => e.AccessTime);
            entity.HasIndex(e => e.Status);
            entity.Ignore(e => e.Recruiter);
            entity.Ignore(e => e.CVMetadata);
            entity.ToTable("CVActivityLogs");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
