using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261115233000_CrmWebFamilyLogoutV1")]
public sealed class CrmWebFamilyLogoutV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_authentication_v1.crm_oidc_family_logouts_v1
          ADD COLUMN web_close_plan jsonb,
          ALTER COLUMN original_grant_id DROP NOT NULL,
          ALTER COLUMN client_id DROP NOT NULL,
          ALTER COLUMN nonce_hash DROP NOT NULL,
          ADD CONSTRAINT crm_oidc_family_logout_source_v1 CHECK (
            (web_close_plan IS NULL AND original_grant_id IS NOT NULL AND client_id IS NOT NULL AND nonce_hash IS NOT NULL)
            OR
            (web_close_plan IS NOT NULL AND jsonb_typeof(web_close_plan)='object'
              AND original_grant_id IS NULL AND client_id IS NULL AND nonce_hash IS NULL)
          );
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Accepted CRM Web browser-family closures require forward recovery.");
}
