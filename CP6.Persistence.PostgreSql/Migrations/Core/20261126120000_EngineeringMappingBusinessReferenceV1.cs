using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261126120000_EngineeringMappingBusinessReferenceV1")]
public sealed class EngineeringMappingBusinessReferenceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- 从不可变的映射原件按经营产品原引用查找，保留既有原件与角色授权。
        CREATE FUNCTION cp6_engineering_product_v1.mapping_business_reference(original bytea) RETURNS jsonb
        LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE SET search_path=pg_catalog AS $$
          SELECT convert_from(original,'UTF8')::jsonb #> '{candidate,businessRef}';
        $$;
        CREATE INDEX engineering_mapping_business_reference ON cp6_engineering_product_v1.mapping_versions
          (tenant_id,cp6_engineering_product_v1.mapping_business_reference(body_bytes));
        REVOKE ALL ON FUNCTION cp6_engineering_product_v1.mapping_business_reference(bytea) FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("经营映射引用索引通过前向迁移修复。");
}
