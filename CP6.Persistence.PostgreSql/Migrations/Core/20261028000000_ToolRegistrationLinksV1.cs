using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261028000000_ToolRegistrationLinksV1")]
public sealed class ToolRegistrationLinksV1:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        CREATE TABLE cp6_tool_instance_v1.registration_links(
          tenant_id uuid NOT NULL,object_id uuid NOT NULL,customer_id uuid,product_id uuid,location_id uuid,warehouse_id uuid,
          customer_cd text,product_cd text,warehouse_cd text,location_cd text,
          original_json text NOT NULL,original_digest text NOT NULL
            CHECK(original_digest=encode(sha256(convert_to(original_json,'UTF8')),'hex')),
          PRIMARY KEY(tenant_id,object_id),
          FOREIGN KEY(tenant_id,object_id) REFERENCES cp6_tool_instance_v1.registrations(tenant_id,object_id),
          CHECK((original_json::jsonb->>'TenantId')::uuid=tenant_id AND (original_json::jsonb->>'ObjectId')::uuid=object_id),
          CHECK(customer_id IS NOT DISTINCT FROM (original_json::jsonb#>>'{Customer,Reference,Id}')::uuid),
          CHECK(product_id IS NOT DISTINCT FROM (original_json::jsonb#>>'{Product,Reference,Id}')::uuid),
          CHECK(location_id IS NOT DISTINCT FROM (original_json::jsonb#>>'{Location,Source,Reference,Id}')::uuid),
          CHECK(warehouse_id IS NOT DISTINCT FROM (original_json::jsonb#>>'{Location,Source,Warehouse,Id}')::uuid),
          CHECK(customer_cd IS NOT DISTINCT FROM (original_json::jsonb#>>'{Customer,Code}')),
          CHECK(product_cd IS NOT DISTINCT FROM (original_json::jsonb#>>'{Product,Code}')),
          CHECK(warehouse_cd IS NOT DISTINCT FROM (original_json::jsonb#>>'{Location,Source,WarehouseCd}')),
          CHECK(location_cd IS NOT DISTINCT FROM (original_json::jsonb#>>'{Location,Source,LocationCd}')));
        CREATE TRIGGER tool_registration_links_immutable BEFORE UPDATE OR DELETE ON cp6_tool_instance_v1.registration_links
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_instance_v1.immutable();
        CREATE TRIGGER tool_registration_links_no_truncate BEFORE TRUNCATE ON cp6_tool_instance_v1.registration_links
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_instance_v1.immutable();
        REVOKE ALL ON cp6_tool_instance_v1.registration_links FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Registration links require forward recovery.");
}
