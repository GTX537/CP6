using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261027010000_ToolOrderLinkV1")]
public sealed class ToolOrderLinkV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tool_order_link_v1;
        REVOKE ALL ON SCHEMA cp6_tool_order_link_v1 FROM PUBLIC;
        CREATE TABLE cp6_tool_order_link_v1.links (
          tenant_id uuid NOT NULL,tool_id uuid NOT NULL,order_id uuid NOT NULL,line_id uuid NOT NULL,
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 16384),
          body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          PRIMARY KEY(tenant_id,tool_id),UNIQUE(tenant_id,line_id),
          FOREIGN KEY(tenant_id,tool_id) REFERENCES cp6_plate_mold_source_v1.heads(tenant_id,object_id));
        CREATE FUNCTION cp6_tool_order_link_v1.validate_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;
        BEGIN
          IF b->>'TenantId' IS DISTINCT FROM NEW.tenant_id::text OR b#>>'{Tool,Id}' IS DISTINCT FROM NEW.tool_id::text
            OR b#>>'{Order,OrderId}' IS DISTINCT FROM NEW.order_id::text OR b#>>'{Order,LineId}' IS DISTINCT FROM NEW.line_id::text
            OR b#>>'{Tool,Owner}' IS DISTINCT FROM 'Main.Erp.PlateMoldSource.v1'
            OR b->>'SourceKind' IS DISTINCT FROM 'NATIVE_ORDER_LINK_ONLY' OR b->>'PeState' IS DISTINCT FROM 'NOT_EXECUTED'
            OR COALESCE(b->>'Effect','') NOT IN ('ORDER_CREATED','NATIVE_LINK_RECORDED')
            OR NOT EXISTS(SELECT FROM cp6_plate_mold_source_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.object_id=NEW.tool_id
                AND v.revision::text=b#>>'{Tool,Version}' AND v.source_digest=b#>>'{Tool,Digest}')
            OR NOT EXISTS(SELECT FROM public."T_PlateMold" p JOIN public."T_OrderDetail" d
                ON d."TenantId"=p."TenantId" AND d."HaibaiNo1"=p."ArrangeNo"
                JOIN public."T_Order" o ON o."TenantId"=d."TenantId" AND o."WebOrderNo"=d."WebOrderNo"
                WHERE p."TenantId"=NEW.tenant_id AND p."Id"=NEW.tool_id AND d."Id"=NEW.line_id AND o."Id"=NEW.order_id
                AND NOT p."IsDeleted" AND NOT d."IsDeleted" AND NOT o."IsDeleted"
                AND encode(p."RowVersion",'base64')=b->>'NativeToolRowVersion'
                AND encode(o."RowVersion",'base64')=b#>>'{Order,OrderRowVersion}'
                AND encode(d."RowVersion",'base64')=b#>>'{Order,LineRowVersion}') THEN
            RAISE EXCEPTION 'TOOL_ORDER_LINK_ORIGINAL_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER tool_order_link_valid BEFORE INSERT ON cp6_tool_order_link_v1.links FOR EACH ROW EXECUTE FUNCTION cp6_tool_order_link_v1.validate_original();
        CREATE TRIGGER tool_order_link_immutable BEFORE UPDATE OR DELETE ON cp6_tool_order_link_v1.links FOR EACH ROW EXECUTE FUNCTION cp6_uom_profile_v1.immutable();
        CREATE TRIGGER tool_order_link_no_truncate BEFORE TRUNCATE ON cp6_tool_order_link_v1.links FOR EACH STATEMENT EXECUTE FUNCTION cp6_uom_profile_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tool_order_link_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tool_order_link_v1 FROM PUBLIC;
        -- No guessed association, retroactive original, Sales schedule or permission grant.
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Tool order originals require forward recovery.");
}
