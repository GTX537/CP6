using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019073000_PlatformAdminGuardV1")]
public sealed class PlatformAdminGuardV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(PlatformAdminGuardPostgreSqlV1.Install);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Platform administrator protection requires forward recovery; disabling it is not an application rollback.");
}
