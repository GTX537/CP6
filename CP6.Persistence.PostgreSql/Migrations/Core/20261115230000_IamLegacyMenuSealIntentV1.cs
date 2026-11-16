using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261115230000_IamLegacyMenuSealIntentV1")]
public sealed class IamLegacyMenuSealIntentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_authentication_v1.legacy_menu_operations (
          operation_id uuid PRIMARY KEY,
          actor_tenant uuid NOT NULL,
          actor_id uuid NOT NULL,
          kind text NOT NULL CHECK (kind IN ('menu-add','menu-edit','menu-delete')),
          menu_ids integer[] NOT NULL CHECK (cardinality(menu_ids)>0),
          desired_menu_key bpchar COLLATE cp6_ci_as_provider_v1,
          request_hash text NOT NULL CHECK (length(request_hash)=64),
          source_hash text NOT NULL CHECK (length(source_hash)=64),
          accepted_at timestamp with time zone NOT NULL DEFAULT clock_timestamp(),
          seal_started boolean NOT NULL DEFAULT false,
          result_hash text CHECK (length(result_hash)=64),
          result_json jsonb,
          CHECK ((result_json IS NULL) = (result_hash IS NULL)),
          CHECK (result_json IS NULL OR seal_started)
        );
        CREATE INDEX legacy_menu_original ON cp6_authentication_v1.legacy_menu_operations
            (actor_tenant,actor_id,kind,request_hash,accepted_at DESC) WHERE result_json IS NOT NULL;
        CREATE INDEX legacy_menu_pending ON cp6_authentication_v1.legacy_menu_operations
            USING gin (menu_ids) WHERE result_json IS NULL;
        REVOKE ALL ON TABLE cp6_authentication_v1.legacy_menu_operations FROM PUBLIC;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
            ON cp6_authentication_v1.legacy_menu_operations FOR EACH STATEMENT
            EXECUTE FUNCTION cp6_authentication_v1.before_change();

        ALTER TABLE cp6_authentication_v1.pending_crm_role_seals
            ADD COLUMN menu_operation_id uuid REFERENCES cp6_authentication_v1.legacy_menu_operations(operation_id),
            DROP CONSTRAINT pending_crm_role_seals_kind_check,
            ADD CONSTRAINT pending_crm_role_seals_kind_check CHECK (
              (menu_operation_id IS NULL AND kind IN ('role-update','role-policy-actions','role-policy-scopes','role-policy-fields','role-inquiry-scopes'))
              OR (menu_operation_id IS NOT NULL AND kind='menu-key-fanout'));
        CREATE INDEX pending_crm_role_seals_menu_operation ON cp6_authentication_v1.pending_crm_role_seals(menu_operation_id)
            WHERE menu_operation_id IS NOT NULL;
        """);

    protected override void Down(MigrationBuilder m)
        => throw new NotSupportedException("Accepted global menu seals require forward recovery.");
}
