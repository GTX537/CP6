using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019111000_AuthenticationPolicyGateV1")]
public sealed class AuthenticationPolicyGateV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("AuthenticationPolicyRevision", "Sys_Tenants", type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()");
        migrationBuilder.AddColumn<bool>("QuickSwitchAllowed", "Sys_Tenants", type: "boolean", nullable: true);
        migrationBuilder.AddColumn<Guid>("CredentialRevision", "Sys_Users", type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()");
        migrationBuilder.Sql(AuthenticationPolicyGuardPostgreSqlV1.Install);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Authentication policy and credential revisions require forward recovery.");
}
