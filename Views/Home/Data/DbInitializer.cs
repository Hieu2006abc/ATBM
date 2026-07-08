using BTL_2.Models;
using BTL_2.Data;
using System.Data.SqlClient;
using System;
using System.Security.Cryptography;
using System.Text;

namespace JobPortal.Data
{
    public static class DbInitializer
    {
        public static bool Initialize(string connectionString)
        {
            try
            {
                // Kiểm tra kết nối database
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Kiểm tra xem bảng Users có tồn tại không
                    string checkTableQuery = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'Users'";

                    using (SqlCommand cmd = new SqlCommand(checkTableQuery, conn))
                    {
                        int tableCount = (int)cmd.ExecuteScalar();

                        // Nếu chưa có bảng Users, tạo database
                        if (tableCount == 0)
                        {
                            CreateDatabase(conn);
                        }
                    }

                    EnsureCoreSchema(conn);
                    EnsureSecureCVSchema(conn);

                    // Kiểm tra và tạo tài khoản Admin nếu chưa có
                    CreateAdminIfNotExists(conn);

                    // Kiểm tra và tạo dữ liệu mẫu
                    CreateSampleDataIfNeeded(conn);
                }

                Console.WriteLine("✅ Database initialized successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Database initialization error: {ex.Message}");
                return false;
            }
        }

        private static void CreateDatabase(SqlConnection conn)
        {
            // Tạo bảng Users
            string createUsersTable = @"
                CREATE TABLE Users (
                    UserId INT PRIMARY KEY IDENTITY(1,1),
                    FullName NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(100) UNIQUE NOT NULL,
                    Password NVARCHAR(255) NOT NULL,
                    Role NVARCHAR(20) CHECK (Role IN ('Admin', 'Employer', 'Candidate')) NOT NULL,
                    Phone NVARCHAR(20),
                    Address NVARCHAR(255),
                    CreatedDate DATETIME DEFAULT GETDATE(),
                    IsActive BIT DEFAULT 1,
                    MustChangePassword BIT NOT NULL DEFAULT 0,
                    TwoFactorEnabled BIT NOT NULL DEFAULT 0,
                    TwoFactorSecret NVARCHAR(512) NULL,
                    TwoFactorCreatedAt DATETIME2 NULL,
                    TwoFactorLastVerifiedAt DATETIME2 NULL
                )";

            using (SqlCommand cmd = new SqlCommand(createUsersTable, conn))
            {
                cmd.ExecuteNonQuery();
            }

            // Tạo bảng Companies
            string createCompaniesTable = @"
                CREATE TABLE Companies (
                    CompanyId INT PRIMARY KEY IDENTITY(1,1),
                    CompanyName NVARCHAR(200) NOT NULL,
                    Logo NVARCHAR(500),
                    Address NVARCHAR(255),
                    Description NVARCHAR(MAX),
                    Website NVARCHAR(200),
                    Phone NVARCHAR(20),
                    Email NVARCHAR(100),
                    CreatedDate DATETIME DEFAULT GETDATE(),
                    EmployerId INT FOREIGN KEY REFERENCES Users(UserId)
                )";

            using (SqlCommand cmd = new SqlCommand(createCompaniesTable, conn))
            {
                cmd.ExecuteNonQuery();
            }

            // Tạo bảng Jobs
            string createJobsTable = @"
                CREATE TABLE Jobs (
                    JobId INT PRIMARY KEY IDENTITY(1,1),
                    Title NVARCHAR(200) NOT NULL,
                    CompanyId INT FOREIGN KEY REFERENCES Companies(CompanyId),
                    SalaryMin DECIMAL(10,2),
                    SalaryMax DECIMAL(10,2),
                    Location NVARCHAR(200),
                    JobType NVARCHAR(50) CHECK (JobType IN ('Full-time', 'Part-time', 'Remote', 'Intern')),
                    Description NVARCHAR(MAX),
                    Requirement NVARCHAR(MAX),
                    Benefit NVARCHAR(MAX),
                    Deadline DATETIME,
                    CreatedDate DATETIME DEFAULT GETDATE(),
                    IsActive BIT DEFAULT 1,
                    Views INT DEFAULT 0
                )";

            using (SqlCommand cmd = new SqlCommand(createJobsTable, conn))
            {
                cmd.ExecuteNonQuery();
            }

            // Tạo bảng Applications
            string createApplicationsTable = @"
                CREATE TABLE Applications (
                    ApplicationId INT PRIMARY KEY IDENTITY(1,1),
                    JobId INT FOREIGN KEY REFERENCES Jobs(JobId),
                    UserId INT FOREIGN KEY REFERENCES Users(UserId),
                    CVFile NVARCHAR(500),
                    CoverLetter NVARCHAR(MAX),
                    Status NVARCHAR(20) CHECK (Status IN ('Pending', 'Approved', 'Rejected')) DEFAULT 'Pending',
                    ApplyDate DATETIME DEFAULT GETDATE(),
                    Notes NVARCHAR(MAX)
                )";

            using (SqlCommand cmd = new SqlCommand(createApplicationsTable, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void EnsureSecureCVSchema(SqlConnection conn)
        {
            string schemaScript = @"
DECLARE @constraintName NVARCHAR(128);
WHILE 1 = 1
BEGIN
    SELECT TOP 1 @constraintName = cc.name
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID('Applications')
      AND (
          cc.definition LIKE '%Status%'
          OR cc.definition LIKE '%[Status]%'
          OR cc.name LIKE 'CK__Applicati__Statu%'
          OR cc.name LIKE '%Applications%Status%'
      );

    IF @constraintName IS NULL BREAK;
    EXEC('ALTER TABLE Applications DROP CONSTRAINT [' + @constraintName + ']');
    SET @constraintName = NULL;
END
IF COL_LENGTH('Applications', 'Status') IS NOT NULL
    ALTER TABLE Applications ALTER COLUMN Status NVARCHAR(50) NULL;
IF COL_LENGTH('Applications', 'OriginalFileName') IS NULL
    ALTER TABLE Applications ADD OriginalFileName NVARCHAR(500) NULL;
IF COL_LENGTH('Applications', 'ApplicationHash') IS NULL
    ALTER TABLE Applications ADD ApplicationHash NVARCHAR(500) NULL;
IF COL_LENGTH('Applications', 'CVMetadataId') IS NULL
    ALTER TABLE Applications ADD CVMetadataId INT NULL;
IF COL_LENGTH('Applications', 'AccessToken') IS NULL
    ALTER TABLE Applications ADD AccessToken UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('Applications', 'TokenExpireTime') IS NULL
    ALTER TABLE Applications ADD TokenExpireTime DATETIME NULL;
IF COL_LENGTH('Applications', 'FileChecksum') IS NULL
    ALTER TABLE Applications ADD FileChecksum NVARCHAR(200) NULL;
IF COL_LENGTH('Applications', 'Nonce') IS NULL
    ALTER TABLE Applications ADD Nonce NVARCHAR(100) NULL;
IF COL_LENGTH('Applications', 'DownloadCount') IS NULL
    ALTER TABLE Applications ADD DownloadCount INT NOT NULL CONSTRAINT DF_Applications_DownloadCount DEFAULT 0;
IF COL_LENGTH('Applications', 'IsExpired') IS NULL
    ALTER TABLE Applications ADD IsExpired BIT NOT NULL CONSTRAINT DF_Applications_IsExpired DEFAULT 0;

IF OBJECT_ID('Jobs', 'U') IS NOT NULL AND COL_LENGTH('Jobs', 'AttachmentFile') IS NULL
    ALTER TABLE Jobs ADD AttachmentFile NVARCHAR(500) NULL;

IF OBJECT_ID('CVMetadata', 'U') IS NULL
BEGIN
    CREATE TABLE CVMetadata (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        CandidateId NVARCHAR(450) NOT NULL,
        JobId INT NOT NULL,
        OriginalFileName NVARCHAR(500) NOT NULL,
        StoredFileName NVARCHAR(500) NOT NULL,
        FilePath NVARCHAR(1000) NOT NULL,
        FileType NVARCHAR(50) NULL,
        FileSize BIGINT NOT NULL,
        SHA256Hash NVARCHAR(100) NOT NULL,
        EncryptionIV NVARCHAR(100) NOT NULL,
        Nonce NVARCHAR(100) NOT NULL,
        UploadTime DATETIME2 NOT NULL,
        ExpireTime DATETIME2 NOT NULL,
        IsDeleted BIT NOT NULL DEFAULT 0
    );
END
ELSE
BEGIN
    IF COL_LENGTH('CVMetadata', 'CandidateId') IS NULL
        ALTER TABLE CVMetadata ADD CandidateId NVARCHAR(450) NULL;
    IF COL_LENGTH('CVMetadata', 'CandidateId') IS NOT NULL AND COL_LENGTH('CVMetadata', 'UserId') IS NOT NULL
        EXEC('UPDATE CVMetadata SET CandidateId = COALESCE(CandidateId, CONVERT(NVARCHAR(450), UserId), ''0'') WHERE CandidateId IS NULL');
    IF COL_LENGTH('CVMetadata', 'CandidateId') IS NOT NULL AND COL_LENGTH('CVMetadata', 'UserId') IS NULL
        EXEC('UPDATE CVMetadata SET CandidateId = COALESCE(CandidateId, ''0'') WHERE CandidateId IS NULL');

    IF COL_LENGTH('CVMetadata', 'JobId') IS NULL
        ALTER TABLE CVMetadata ADD JobId INT NULL;

    IF COL_LENGTH('CVMetadata', 'OriginalFileName') IS NULL
        ALTER TABLE CVMetadata ADD OriginalFileName NVARCHAR(500) NULL;
    IF COL_LENGTH('CVMetadata', 'OriginalFileName') IS NOT NULL AND COL_LENGTH('CVMetadata', 'FileName') IS NOT NULL
        EXEC('UPDATE CVMetadata SET OriginalFileName = COALESCE(OriginalFileName, FileName, ''CV'') WHERE OriginalFileName IS NULL');
    IF COL_LENGTH('CVMetadata', 'OriginalFileName') IS NOT NULL AND COL_LENGTH('CVMetadata', 'FileName') IS NULL
        EXEC('UPDATE CVMetadata SET OriginalFileName = COALESCE(OriginalFileName, ''CV'') WHERE OriginalFileName IS NULL');

    IF COL_LENGTH('CVMetadata', 'StoredFileName') IS NULL
        ALTER TABLE CVMetadata ADD StoredFileName NVARCHAR(500) NULL;
    IF COL_LENGTH('CVMetadata', 'StoredFileName') IS NOT NULL AND COL_LENGTH('CVMetadata', 'FileName') IS NOT NULL
        EXEC('UPDATE CVMetadata SET StoredFileName = COALESCE(StoredFileName, FileName, ''CV'') WHERE StoredFileName IS NULL');
    IF COL_LENGTH('CVMetadata', 'StoredFileName') IS NOT NULL AND COL_LENGTH('CVMetadata', 'FileName') IS NULL
        EXEC('UPDATE CVMetadata SET StoredFileName = COALESCE(StoredFileName, ''CV'') WHERE StoredFileName IS NULL');

    IF COL_LENGTH('CVMetadata', 'FilePath') IS NULL
        ALTER TABLE CVMetadata ADD FilePath NVARCHAR(1000) NULL;

    IF COL_LENGTH('CVMetadata', 'FileType') IS NULL
        ALTER TABLE CVMetadata ADD FileType NVARCHAR(50) NULL;
    IF COL_LENGTH('CVMetadata', 'FileType') IS NOT NULL AND COL_LENGTH('CVMetadata', 'MimeType') IS NOT NULL
        EXEC('UPDATE CVMetadata SET FileType = COALESCE(FileType, MimeType) WHERE FileType IS NULL');

    IF COL_LENGTH('CVMetadata', 'FileSize') IS NULL
        ALTER TABLE CVMetadata ADD FileSize BIGINT NOT NULL CONSTRAINT DF_CVMetadata_FileSize DEFAULT 0;

    IF COL_LENGTH('CVMetadata', 'SHA256Hash') IS NULL
        ALTER TABLE CVMetadata ADD SHA256Hash NVARCHAR(100) NULL;

    IF COL_LENGTH('CVMetadata', 'EncryptionIV') IS NULL
        ALTER TABLE CVMetadata ADD EncryptionIV NVARCHAR(100) NULL;

    IF COL_LENGTH('CVMetadata', 'Nonce') IS NULL
        ALTER TABLE CVMetadata ADD Nonce NVARCHAR(100) NULL;
    IF COL_LENGTH('CVMetadata', 'Nonce') IS NOT NULL
        EXEC('UPDATE CVMetadata SET Nonce = CONVERT(NVARCHAR(100), NEWID()) WHERE Nonce IS NULL');

    IF COL_LENGTH('CVMetadata', 'UploadTime') IS NULL
        ALTER TABLE CVMetadata ADD UploadTime DATETIME2 NULL;
    IF COL_LENGTH('CVMetadata', 'UploadTime') IS NOT NULL AND COL_LENGTH('CVMetadata', 'UploadedAt') IS NOT NULL
        EXEC('UPDATE CVMetadata SET UploadTime = COALESCE(UploadTime, UploadedAt, GETDATE()) WHERE UploadTime IS NULL');
    IF COL_LENGTH('CVMetadata', 'UploadTime') IS NOT NULL AND COL_LENGTH('CVMetadata', 'UploadedAt') IS NULL
        EXEC('UPDATE CVMetadata SET UploadTime = COALESCE(UploadTime, GETDATE()) WHERE UploadTime IS NULL');

    IF COL_LENGTH('CVMetadata', 'ExpireTime') IS NULL
        ALTER TABLE CVMetadata ADD ExpireTime DATETIME2 NULL;
    IF COL_LENGTH('CVMetadata', 'ExpireTime') IS NOT NULL
        EXEC('UPDATE CVMetadata SET ExpireTime = DATEADD(day, 90, COALESCE(UploadTime, GETDATE())) WHERE ExpireTime IS NULL');

    IF COL_LENGTH('CVMetadata', 'IsDeleted') IS NULL
        ALTER TABLE CVMetadata ADD IsDeleted BIT NOT NULL CONSTRAINT DF_CVMetadata_IsDeleted DEFAULT 0;
END

IF OBJECT_ID('DownloadTokens', 'U') IS NULL
BEGIN
    CREATE TABLE DownloadTokens (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Token NVARCHAR(100) NOT NULL UNIQUE,
        CVMetadataId INT NOT NULL,
        RecruiterId NVARCHAR(450) NOT NULL,
        CreatedAt DATETIME2 NOT NULL,
        ExpiresAt DATETIME2 NOT NULL,
        IsUsed BIT NOT NULL DEFAULT 0,
        IsRevoked BIT NOT NULL DEFAULT 0,
        SessionId NVARCHAR(128) NULL,
        VerificationCodeHash NVARCHAR(128) NULL,
        CONSTRAINT FK_DownloadTokens_CVMetadata FOREIGN KEY (CVMetadataId) REFERENCES CVMetadata(Id) ON DELETE CASCADE
    );
END
ELSE
BEGIN
    IF COL_LENGTH('DownloadTokens', 'CVMetadataId') IS NULL
        ALTER TABLE DownloadTokens ADD CVMetadataId INT NULL;
    IF COL_LENGTH('DownloadTokens', 'RecruiterId') IS NULL
        ALTER TABLE DownloadTokens ADD RecruiterId NVARCHAR(450) NULL;
    IF COL_LENGTH('DownloadTokens', 'SessionId') IS NULL
        ALTER TABLE DownloadTokens ADD SessionId NVARCHAR(128) NULL;
    IF COL_LENGTH('DownloadTokens', 'VerificationCodeHash') IS NULL
        ALTER TABLE DownloadTokens ADD VerificationCodeHash NVARCHAR(128) NULL;
END

IF OBJECT_ID('CVActivityLogs', 'U') IS NULL
BEGIN
    CREATE TABLE CVActivityLogs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        RecruiterId NVARCHAR(450) NOT NULL,
        CVMetadataId INT NOT NULL,
        AccessTime DATETIME2 NOT NULL,
        IPAddress NVARCHAR(50) NULL,
        Status NVARCHAR(50) NULL,
        ErrorMessage NVARCHAR(500) NULL,
        UserAgent NVARCHAR(500) NULL
    );
END
ELSE
BEGIN
    IF COL_LENGTH('CVActivityLogs', 'RecruiterId') IS NULL
        ALTER TABLE CVActivityLogs ADD RecruiterId NVARCHAR(450) NULL;
    IF COL_LENGTH('CVActivityLogs', 'CVMetadataId') IS NULL
        ALTER TABLE CVActivityLogs ADD CVMetadataId INT NULL;
    IF COL_LENGTH('CVActivityLogs', 'AccessTime') IS NULL
        ALTER TABLE CVActivityLogs ADD AccessTime DATETIME2 NULL;
    IF COL_LENGTH('CVActivityLogs', 'IPAddress') IS NULL
        ALTER TABLE CVActivityLogs ADD IPAddress NVARCHAR(50) NULL;
    IF COL_LENGTH('CVActivityLogs', 'Status') IS NULL
        ALTER TABLE CVActivityLogs ADD Status NVARCHAR(50) NULL;
    IF COL_LENGTH('CVActivityLogs', 'ErrorMessage') IS NULL
        ALTER TABLE CVActivityLogs ADD ErrorMessage NVARCHAR(500) NULL;
    IF COL_LENGTH('CVActivityLogs', 'UserAgent') IS NULL
        ALTER TABLE CVActivityLogs ADD UserAgent NVARCHAR(500) NULL;
END";

            using (SqlCommand cmd = new SqlCommand(schemaScript, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void EnsureCoreSchema(SqlConnection conn)
        {
            string schemaScript = @"
IF OBJECT_ID('Categories', 'U') IS NULL
BEGIN
    CREATE TABLE Categories (
        CategoryID INT IDENTITY(1,1) PRIMARY KEY,
        CategoryName NVARCHAR(100) NULL,
        Description NVARCHAR(255) NULL
    );
END

IF OBJECT_ID('JobCategories', 'U') IS NULL
BEGIN
    CREATE TABLE JobCategories (
        JobID INT NOT NULL,
        CategoryID INT NOT NULL,
        CONSTRAINT PK_JobCategories PRIMARY KEY (JobID, CategoryID),
        CONSTRAINT FK_JobCategories_Jobs FOREIGN KEY (JobID) REFERENCES Jobs(JobID),
        CONSTRAINT FK_JobCategories_Categories FOREIGN KEY (CategoryID) REFERENCES Categories(CategoryID)
    );
END

IF COL_LENGTH('Users', 'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE Users ADD MustChangePassword BIT NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT 0;
END

IF COL_LENGTH('Users', 'TwoFactorEnabled') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorEnabled BIT NOT NULL CONSTRAINT DF_Users_TwoFactorEnabled DEFAULT 0;
END

IF COL_LENGTH('Users', 'TwoFactorSecret') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorSecret NVARCHAR(512) NULL;
END

IF COL_LENGTH('Users', 'TwoFactorCreatedAt') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorCreatedAt DATETIME2 NULL;
END

IF COL_LENGTH('Users', 'TwoFactorLastVerifiedAt') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorLastVerifiedAt DATETIME2 NULL;
END

IF OBJECT_ID('SavedJobs', 'U') IS NULL
BEGIN
    CREATE TABLE SavedJobs (
        SavedJobId INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        JobId INT NOT NULL,
        SavedDate DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_SavedJobs_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
        CONSTRAINT FK_SavedJobs_Jobs FOREIGN KEY (JobId) REFERENCES Jobs(JobId)
    );
END

IF OBJECT_ID('Follows', 'U') IS NULL
BEGIN
    CREATE TABLE Follows (
        FollowId INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        CompanyId INT NOT NULL,
        FollowedDate DATETIME NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_Follows_Users FOREIGN KEY (UserId) REFERENCES Users(UserId),
        CONSTRAINT FK_Follows_Companies FOREIGN KEY (CompanyId) REFERENCES Companies(CompanyId)
    );
END

IF OBJECT_ID('PasswordResets', 'U') IS NULL
BEGIN
    CREATE TABLE PasswordResets (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Email NVARCHAR(100) NOT NULL,
        Token NVARCHAR(200) NULL,
        OtpCode NVARCHAR(20) NOT NULL,
        ExpiryTime DATETIME NOT NULL,
        IsUsed BIT NOT NULL DEFAULT 0,
        FailedAttempts INT NOT NULL DEFAULT 0,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END

IF COL_LENGTH('PasswordResets', 'FailedAttempts') IS NULL
BEGIN
    ALTER TABLE PasswordResets ADD FailedAttempts INT NOT NULL DEFAULT 0;
END

IF OBJECT_ID('Announcements', 'U') IS NULL
BEGIN
    CREATE TABLE Announcements (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        Title NVARCHAR(200) NOT NULL,
        Content NVARCHAR(MAX) NULL,
        Type NVARCHAR(50) NULL,
        TargetRole NVARCHAR(50) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        ExpiryDate DATETIME NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedBy INT NULL,
        ImageUrl NVARCHAR(500) NULL,
        LinkUrl NVARCHAR(500) NULL
    );
END

IF OBJECT_ID('UserNotifications', 'U') IS NULL
BEGIN
    CREATE TABLE UserNotifications (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        Content NVARCHAR(MAX) NULL,
        Type NVARCHAR(50) NULL,
        IsRead BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        LinkUrl NVARCHAR(500) NULL,
        RelatedId INT NULL,
        AnnouncementId INT NULL,
        CONSTRAINT FK_UserNotifications_Users FOREIGN KEY (UserId) REFERENCES Users(UserId)
    );
END

IF OBJECT_ID('Conversations', 'U') IS NULL
BEGIN
    CREATE TABLE Conversations (
        ConversationId INT IDENTITY(1,1) PRIMARY KEY,
        User1Id INT NOT NULL,
        User2Id INT NOT NULL,
        LastMessageTime DATETIME NOT NULL DEFAULT GETDATE(),
        LastMessageContent NVARCHAR(MAX) NULL,
        User1Deleted BIT NOT NULL DEFAULT 0,
        User2Deleted BIT NOT NULL DEFAULT 0
    );
END

IF OBJECT_ID('Messages', 'U') IS NULL
BEGIN
    CREATE TABLE Messages (
        MessageId INT IDENTITY(1,1) PRIMARY KEY,
        ConversationId INT NOT NULL,
        SenderId INT NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        SentAt DATETIME NOT NULL DEFAULT GETDATE(),
        IsRead BIT NOT NULL DEFAULT 0,
        CONSTRAINT FK_Messages_Conversations FOREIGN KEY (ConversationId) REFERENCES Conversations(ConversationId),
        CONSTRAINT FK_Messages_Users FOREIGN KEY (SenderId) REFERENCES Users(UserId)
    );
END

IF OBJECT_ID('ActivityLogs', 'U') IS NULL
BEGIN
    CREATE TABLE ActivityLogs (
        LogId INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(100) NOT NULL,
        UserRole NVARCHAR(50) NOT NULL,
        Action NVARCHAR(100) NOT NULL,
        Details NVARCHAR(500) NOT NULL,
        TargetUser NVARCHAR(200) NULL,
        TargetEmail NVARCHAR(200) NULL,
        TargetRole NVARCHAR(100) NULL,
        TargetId INT NULL,
        IpAddress NVARCHAR(50) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
        Status NVARCHAR(50) NULL
    );
END

IF OBJECT_ID('CVDownloadLogs', 'U') IS NULL
BEGIN
    CREATE TABLE CVDownloadLogs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ApplicationId INT NULL,
        CVMetadataId INT NULL,
        RecruiterId INT NULL,
        DownloadedAt DATETIME NOT NULL DEFAULT GETDATE(),
        IpAddress NVARCHAR(50) NULL,
        Status NVARCHAR(50) NULL
    );
END
ELSE
BEGIN
    IF COL_LENGTH('CVDownloadLogs', 'ApplicationId') IS NULL
        ALTER TABLE CVDownloadLogs ADD ApplicationId INT NULL;
    IF COL_LENGTH('CVDownloadLogs', 'CVMetadataId') IS NULL
        ALTER TABLE CVDownloadLogs ADD CVMetadataId INT NULL;
    IF COL_LENGTH('CVDownloadLogs', 'RecruiterId') IS NULL
        ALTER TABLE CVDownloadLogs ADD RecruiterId INT NULL;
    IF COL_LENGTH('CVDownloadLogs', 'DownloadedAt') IS NULL
        ALTER TABLE CVDownloadLogs ADD DownloadedAt DATETIME NULL;
    IF COL_LENGTH('CVDownloadLogs', 'IpAddress') IS NULL
        ALTER TABLE CVDownloadLogs ADD IpAddress NVARCHAR(50) NULL;
    IF COL_LENGTH('CVDownloadLogs', 'Status') IS NULL
        ALTER TABLE CVDownloadLogs ADD Status NVARCHAR(50) NULL;
END";

            using (SqlCommand cmd = new SqlCommand(schemaScript, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void CreateAdminIfNotExists(SqlConnection conn)
        {
            // Kiểm tra xem đã có Admin chưa
            string checkAdminQuery = "SELECT COUNT(*) FROM Users WHERE Role = 'Admin'";
            using (SqlCommand cmd = new SqlCommand(checkAdminQuery, conn))
            {
                int adminCount = (int)cmd.ExecuteScalar();

                if (adminCount == 0)
                {
                    // Tạo tài khoản Admin mặc định
                    string insertAdmin = @"
                        INSERT INTO Users (FullName, Email, Password, Role, CreatedDate, IsActive) 
                        VALUES (@FullName, @Email, @Password, @Role, GETDATE(), 1)";

                    using (SqlCommand insertCmd = new SqlCommand(insertAdmin, conn))
                    {
                        insertCmd.Parameters.AddWithValue("@FullName", "System Administrator");
                        insertCmd.Parameters.AddWithValue("@Email", "admin@jobportal.com");
                        insertCmd.Parameters.AddWithValue("@Password", HashPassword("admin123"));
                        insertCmd.Parameters.AddWithValue("@Role", "Admin");

                        insertCmd.ExecuteNonQuery();
                        Console.WriteLine("✅ Admin account created: admin@jobportal.com / admin123");
                    }
                }
            }
        }

        private static void CreateSampleDataIfNeeded(SqlConnection conn)
        {
            // Kiểm tra số lượng users
            string checkUsersQuery = "SELECT COUNT(*) FROM Users";
            using (SqlCommand cmd = new SqlCommand(checkUsersQuery, conn))
            {
                int userCount = (int)cmd.ExecuteScalar();

                // Nếu chỉ có 1 user (Admin) thì tạo thêm dữ liệu mẫu
                if (userCount == 1)
                {
                    // Tạo Employer mẫu
                    string insertEmployer = @"
                        INSERT INTO Users (FullName, Email, Password, Role, CreatedDate, IsActive) 
                        VALUES ('Tech Corp HR', 'hr@techcorp.com', @Password, 'Employer', GETDATE(), 1);
                        SELECT SCOPE_IDENTITY();";

                    int employerId;
                    using (SqlCommand cmd2 = new SqlCommand(insertEmployer, conn))
                    {
                        cmd2.Parameters.AddWithValue("@Password", HashPassword("employer123"));
                        employerId = Convert.ToInt32(cmd2.ExecuteScalar());
                    }

                    // Tạo Candidate mẫu
                    string insertCandidate = @"
                        INSERT INTO Users (FullName, Email, Password, Role, CreatedDate, IsActive) 
                        VALUES ('John Candidate', 'john@email.com', @Password, 'Candidate', GETDATE(), 1)";

                    using (SqlCommand cmd2 = new SqlCommand(insertCandidate, conn))
                    {
                        cmd2.Parameters.AddWithValue("@Password", HashPassword("candidate123"));
                        cmd2.ExecuteNonQuery();
                    }

                    // Tạo Company mẫu
                    string insertCompany = @"
                        INSERT INTO Companies (CompanyName, Address, Description, Website, EmployerId) 
                        VALUES ('Tech Corporation', 'Ho Chi Minh City', 'Leading technology company', 'https://techcorp.com', @EmployerId)";

                    using (SqlCommand cmd2 = new SqlCommand(insertCompany, conn))
                    {
                        cmd2.Parameters.AddWithValue("@EmployerId", employerId);
                        cmd2.ExecuteNonQuery();
                    }

                    Console.WriteLine("✅ Sample data created successfully!");
                }
            }
        }

        private static string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
