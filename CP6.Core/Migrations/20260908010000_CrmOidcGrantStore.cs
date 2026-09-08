using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

/// <summary>Operational OIDC grant ledger, deliberately outside tenant-filtered business EF entities.</summary>
[DbContext(typeof(CP6Context))]
[Migration("20260908010000_CrmOidcGrantStore")]
public sealed class CrmOidcGrantStore : Migration
{
    public const string CreateSql = """
        CREATE TABLE dbo.CrmOidcGrant (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_CrmOidcGrant PRIMARY KEY,
            CodeHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ClientId varchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
            RedirectUri nvarchar(2048) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Challenge varchar(43) COLLATE Latin1_General_100_BIN2 NOT NULL,
            Nonce nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SubjectId uniqueidentifier NOT NULL,
            OrganizationId uniqueidentifier NOT NULL,
            SourceJti varchar(100) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SourceRefreshHash nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
            SecurityStamp varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ExpiresAtUtc datetime2 NOT NULL,
            SourceExpiresAtUtc datetime2 NOT NULL,
            AccessExpiresAtUtc datetime2 NOT NULL,
            Consumed bit NOT NULL,
            Revoked bit NOT NULL,
            CONSTRAINT UX_CrmOidcGrant_CodeHash UNIQUE (CodeHash)
        );
        CREATE INDEX IX_CrmOidcGrant_AccessExpiresAtUtc ON dbo.CrmOidcGrant(AccessExpiresAtUtc);
        CREATE TABLE dbo.CrmOidcLogout (
            TicketHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT PK_CrmOidcLogout PRIMARY KEY,
            GrantId uniqueidentifier NOT NULL,
            RedirectUri nvarchar(2048) COLLATE Latin1_General_100_BIN2 NOT NULL,
            ExpiresAtUtc datetime2 NOT NULL
        );
        """;
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(CreateSql);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("OIDC ledger migrations are forward-only; disable the bridge to roll back application code.");
}
