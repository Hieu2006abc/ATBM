using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTL_2.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Users', 'TwoFactorEnabled') IS NULL
BEGIN
    ALTER TABLE Users
    ADD TwoFactorEnabled BIT NOT NULL CONSTRAINT DF_Users_TwoFactorEnabled DEFAULT 0;
END

IF COL_LENGTH('Users', 'TwoFactorSecret') IS NULL
BEGIN
    ALTER TABLE Users
    ADD TwoFactorSecret NVARCHAR(512) NULL;
END

IF COL_LENGTH('Users', 'TwoFactorCreatedAt') IS NULL
BEGIN
    ALTER TABLE Users
    ADD TwoFactorCreatedAt DATETIME2 NULL;
END

IF COL_LENGTH('Users', 'TwoFactorLastVerifiedAt') IS NULL
BEGIN
    ALTER TABLE Users
    ADD TwoFactorLastVerifiedAt DATETIME2 NULL;
END
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Users', 'TwoFactorLastVerifiedAt') IS NOT NULL
BEGIN
    ALTER TABLE Users DROP COLUMN TwoFactorLastVerifiedAt;
END

IF COL_LENGTH('Users', 'TwoFactorCreatedAt') IS NOT NULL
BEGIN
    ALTER TABLE Users DROP COLUMN TwoFactorCreatedAt;
END

IF COL_LENGTH('Users', 'TwoFactorSecret') IS NOT NULL
BEGIN
    ALTER TABLE Users DROP COLUMN TwoFactorSecret;
END

IF COL_LENGTH('Users', 'TwoFactorEnabled') IS NOT NULL
BEGIN
    DECLARE @dfName NVARCHAR(128);
    SELECT @dfName = dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.default_object_id = dc.object_id
    WHERE dc.parent_object_id = OBJECT_ID('Users')
      AND c.name = 'TwoFactorEnabled';

    IF @dfName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE Users DROP CONSTRAINT [' + @dfName + ']');
    END

    ALTER TABLE Users DROP COLUMN TwoFactorEnabled;
END
");
        }
    }
}
