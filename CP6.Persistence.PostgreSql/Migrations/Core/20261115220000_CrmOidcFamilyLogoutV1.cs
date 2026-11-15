using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261115220000_CrmOidcFamilyLogoutV1")]
public sealed class CrmOidcFamilyLogoutV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_authentication_v1.crm_oidc_family_logouts_v1 (
          tenant_id uuid NOT NULL,
          family_id uuid NOT NULL REFERENCES public."Sys_BrowserSessions"("Id"),
          subject_id uuid NOT NULL,
          store_id uuid NOT NULL,
          issuer text COLLATE "C" NOT NULL,
          original_grant_id uuid NOT NULL,
          client_id text COLLATE "C" NOT NULL,
          nonce_hash text COLLATE "C" NOT NULL CHECK(nonce_hash ~ '^[0-9A-F]{64}$'),
          request_hash text COLLATE "C" NOT NULL CHECK(request_hash ~ '^[0-9A-F]{64}$'),
          grant_plan jsonb NOT NULL CHECK(jsonb_typeof(grant_plan)='array'),
          accepted_at_utc timestamp with time zone NOT NULL DEFAULT clock_timestamp(),
          completed_at_utc timestamp with time zone,
          PRIMARY KEY (tenant_id, family_id)
        );
        CREATE INDEX crm_oidc_family_logouts_pending_v1 ON cp6_authentication_v1.crm_oidc_family_logouts_v1(family_id)
          WHERE completed_at_utc IS NULL;
        REVOKE ALL ON TABLE cp6_authentication_v1.crm_oidc_family_logouts_v1 FROM PUBLIC;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.crm_oidc_family_logouts_v1 FOR EACH STATEMENT
          EXECUTE FUNCTION cp6_authentication_v1.before_change();

        ALTER TABLE public."CrmOidcLogout"
          ADD COLUMN "SourceRequestHash" text COLLATE "C" CHECK("SourceRequestHash" ~ '^[0-9A-F]{64}$'),
          ADD COLUMN "ResponseTicketProtected" text,
          ADD CONSTRAINT "CK_CrmOidcLogout_ProtectedReplay" CHECK(("SourceRequestHash" IS NULL)=("ResponseTicketProtected" IS NULL));
        CREATE UNIQUE INDEX "UX_CrmOidcLogout_SourceRequestHash" ON public."CrmOidcLogout"("SourceRequestHash")
          WHERE "SourceRequestHash" IS NOT NULL;
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Accepted CRM browser-family logout requires forward recovery.");
}
