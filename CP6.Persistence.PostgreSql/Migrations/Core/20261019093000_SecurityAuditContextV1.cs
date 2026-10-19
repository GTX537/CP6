using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019093000_SecurityAuditContextV1")]
public sealed class SecurityAuditContextV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "ContextJson", table: "Sys_SecurityLogs", type: "jsonb", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Security audit context requires forward recovery; historical evidence must be preserved.");
}
