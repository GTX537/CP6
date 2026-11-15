using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261115213000_IamCrmUserSealIntentV1")]
public sealed class IamCrmUserSealIntentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_authentication_v1.pending_crm_user_seals (
          tenant_id uuid NOT NULL,
          user_id uuid NOT NULL,
          operation_id uuid NOT NULL,
          actor_id uuid NOT NULL,
          kind text NOT NULL CHECK (kind IN ('user-update','user-roles','user-org')),
          request_protected text NOT NULL,
          store_id uuid NOT NULL,
          source_seals jsonb NOT NULL CHECK (jsonb_typeof(source_seals)='array' AND jsonb_array_length(source_seals)>0),
          seal_started boolean NOT NULL DEFAULT false,
          PRIMARY KEY (tenant_id,user_id), UNIQUE (tenant_id,operation_id)
        );
        REVOKE ALL ON TABLE cp6_authentication_v1.pending_crm_user_seals FROM PUBLIC;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
            ON cp6_authentication_v1.pending_crm_user_seals FOR EACH STATEMENT
            EXECUTE FUNCTION cp6_authentication_v1.before_change();
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Pending CRM user seals require forward recovery.");
}
