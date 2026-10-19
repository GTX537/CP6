using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019101500_RefreshAuthenticationStateV1")]
public sealed class RefreshAuthenticationStateV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("AuthenticationVersion", "Sys_RefreshTokens", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTime>("CompletionRejectedAtUtc", "Sys_RefreshTokens", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<bool>("CompletionPending", "Sys_RefreshTokens", type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Authentication evidence and completion rejection require forward recovery.");
}
