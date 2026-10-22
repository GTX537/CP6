using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261022061500_MainOwnerOperationsV1")]
public sealed class MainOwnerOperationsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_main_owner_v1;
        REVOKE ALL ON SCHEMA cp6_main_owner_v1 FROM PUBLIC;
        CREATE TABLE cp6_main_owner_v1.operations (
          tenant_id uuid NOT NULL, owner text COLLATE "C" NOT NULL,
          operation_id uuid NOT NULL, actor_id uuid NOT NULL, contract text COLLATE "C" NOT NULL,
          state text NOT NULL CHECK(state IN ('COMMITTED','CLOSED')),
          input_bytes bytea, input_sha256 text COLLATE "C", result_bytes bytea NOT NULL,
          object_ids uuid[] NOT NULL, audit_json jsonb NOT NULL,
          committed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,owner,operation_id),
          CHECK(operation_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          CHECK(actor_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          CHECK(tenant_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          CHECK((state='CLOSED' AND input_bytes IS NULL AND input_sha256 IS NULL)
            OR (state='COMMITTED' AND input_bytes IS NOT NULL AND input_sha256 ~ '^[0-9a-f]{64}$')));
        CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_main_owner_v1.operations FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_main_owner_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'MAIN_ORIGINAL_OPERATION_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_main_owner_v1.operations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_main_owner_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_main_owner_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_main_owner_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Original operations, deduplication and audit require forward recovery.");
}
