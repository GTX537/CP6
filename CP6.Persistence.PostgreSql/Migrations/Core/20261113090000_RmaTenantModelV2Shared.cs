using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261113090000_RmaTenantModelV2Shared")]
public sealed class RmaTenantModelV2Shared : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_rma_model_v2;
        REVOKE ALL ON SCHEMA cp6_rma_model_v2 FROM PUBLIC;
        CREATE TABLE cp6_rma_model_v2.tenant_migrations (
            tenant_id uuid PRIMARY KEY,
            installation_id uuid NOT NULL,
            operation_id uuid NOT NULL,
            script_sha256 text NOT NULL,
            result_json text NOT NULL
        );
        REVOKE ALL ON cp6_rma_model_v2.tenant_migrations FROM PUBLIC;

        CREATE TABLE cp6_rma_model_v2.writer_transactions (
            tenant_id uuid NOT NULL,
            writer_xid xid8 NOT NULL,
            PRIMARY KEY (tenant_id, writer_xid)
        );
        REVOKE ALL ON cp6_rma_model_v2.writer_transactions FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("RMA storage models require forward migration.");
}
