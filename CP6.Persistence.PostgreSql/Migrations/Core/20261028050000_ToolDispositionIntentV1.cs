using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261028050000_ToolDispositionIntentV1")]
public sealed class ToolDispositionIntentV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tool_disposition_v1;
        CREATE TABLE cp6_tool_disposition_v1.facts (
          sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          tenant_id uuid NOT NULL, tool_id uuid NOT NULL, record_id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN ('REQUEST','BLOCK')), body_json text NOT NULL,
          body_digest text NOT NULL CHECK(body_digest ~ '^[0-9a-f]{64}$'),
          PRIMARY KEY(tenant_id,record_id), UNIQUE(tenant_id,tool_id,kind)
        );
        CREATE FUNCTION cp6_tool_disposition_v1.guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'tool disposition originals are immutable'; END $$;
        CREATE TRIGGER tool_disposition_immutable BEFORE UPDATE OR DELETE ON cp6_tool_disposition_v1.facts
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_disposition_v1.guard();
        CREATE TRIGGER tool_disposition_no_truncate BEFORE TRUNCATE ON cp6_tool_disposition_v1.facts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_disposition_v1.guard();
        CREATE FUNCTION cp6_tool_disposition_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb:=NEW.body_json::jsonb; r jsonb; rd text;
        BEGIN
          IF (b->>'tenantId') IS DISTINCT FROM NEW.tenant_id::text OR (b->>'toolId') IS DISTINCT FROM NEW.tool_id::text
             OR (b->>'recordId') IS DISTINCT FROM NEW.record_id::text OR (b->>'kind') IS DISTINCT FROM NEW.kind
             OR encode(sha256(convert_to(NEW.body_json,'UTF8')),'hex')<>NEW.body_digest THEN RAISE EXCEPTION 'tool disposition identity mismatch'; END IF;
          IF NEW.kind='REQUEST' AND b->'request' IS DISTINCT FROM 'null'::jsonb THEN RAISE EXCEPTION 'request cannot replace another request'; END IF;
          IF NEW.kind='BLOCK' THEN
            SELECT body_json::jsonb,body_digest INTO r,rd FROM cp6_tool_disposition_v1.facts
              WHERE tenant_id=NEW.tenant_id AND tool_id=NEW.tool_id AND kind='REQUEST';
            IF r IS NULL OR (b->'request'->>'owner') IS DISTINCT FROM 'Main.Wms.ToolDisposition.v1'
              OR (b->'request'->>'id') IS DISTINCT FROM (r->>'recordId') OR (b->'request'->>'version') IS DISTINCT FROM '1'
              OR (b->'request'->>'digest') IS DISTINCT FROM rd THEN RAISE EXCEPTION 'exact disposition request required'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER tool_disposition_validate BEFORE INSERT ON cp6_tool_disposition_v1.facts
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_disposition_v1.validate();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Immutable disposition facts require an explicitly reviewed forward recovery.");
}
