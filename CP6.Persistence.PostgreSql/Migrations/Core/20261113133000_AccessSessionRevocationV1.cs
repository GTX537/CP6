using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261113133000_AccessSessionRevocationV1")]
public sealed class AccessSessionRevocationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql("ALTER TABLE cp6_authentication_v1.access_sessions ADD COLUMN revoked_at timestamptz;");

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Dropping access-session revocations would restore retired access.");
}
