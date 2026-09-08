using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20260908011000_CrmOidcBrowserSessionFamily")]
public sealed class CrmOidcBrowserSessionFamily : Migration
{
    public const string CreateSql = """
        ALTER TABLE dbo.Sys_Users ADD AuthenticationEpoch uniqueidentifier NOT NULL
            CONSTRAINT DF_Sys_Users_AuthenticationEpoch DEFAULT '00000000-0000-0000-0000-000000000000';
        ALTER TABLE dbo.Sys_RefreshTokens ADD BrowserSessionId uniqueidentifier NULL;
        CREATE INDEX IX_Sys_RefreshTokens_BrowserSessionId ON dbo.Sys_RefreshTokens(BrowserSessionId);
        CREATE TABLE dbo.Sys_BrowserSessions (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_Sys_BrowserSessions PRIMARY KEY,
            UserId uniqueidentifier NOT NULL,
            TenantId uniqueidentifier NOT NULL,
            AuthenticationVersion nvarchar(64) NOT NULL,
            LoggedOutAtUtc datetime2 NULL,
            Creator nvarchar(100) NULL,
            CreateDate datetime2 NOT NULL,
            Modifier nvarchar(100) NULL,
            ModifyDate datetime2 NULL
        );
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(CreateSql);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Browser authentication history is forward-only; disable the bridge to roll back application code.");
}
