using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261115210000_IamCrmRoleSealIntentV1")]
public sealed class IamCrmRoleSealIntentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_authentication_v1.pending_crm_role_seals (
          tenant_id uuid NOT NULL,
          role_id integer NOT NULL,
          operation_id uuid NOT NULL,
          actor_id uuid NOT NULL,
          kind text NOT NULL CHECK (kind IN ('role-update','role-policy-actions','role-policy-scopes','role-policy-fields','role-inquiry-scopes')),
          request_protected text NOT NULL,
          store_id uuid NOT NULL,
          source_version integer NOT NULL CHECK (source_version BETWEEN 1 AND 2147483646),
          source_type text NOT NULL,
          source_hash text NOT NULL CHECK (length(source_hash)=64),
          successor_type text NOT NULL,
          successor_hash text NOT NULL CHECK (length(successor_hash)=64),
          affected_site_forms jsonb,
          seal_started boolean NOT NULL DEFAULT false,
          PRIMARY KEY (tenant_id,role_id), UNIQUE (tenant_id,operation_id),
          CHECK (affected_site_forms IS NULL OR jsonb_typeof(affected_site_forms)='array')
        );
        REVOKE ALL ON TABLE cp6_authentication_v1.pending_crm_role_seals FROM PUBLIC;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
            ON cp6_authentication_v1.pending_crm_role_seals FOR EACH STATEMENT
            EXECUTE FUNCTION cp6_authentication_v1.before_change();
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Pending CRM role seals require forward recovery.");
}
