using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261103013500_NativeQueryAvailabilityV2")]
public sealed class NativeQueryAvailabilityV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        -- Query membership time, not reconstructed business submission/creation time.
        -- Existing rows become available when this query capability is installed; new rows
        -- acquire their watermark inside the same native writer transaction.
        ALTER TABLE cp6_personal_form_v2.submissions ADD COLUMN query_available_at timestamptz NOT NULL DEFAULT clock_timestamp();
        ALTER TABLE cp6_personal_form_v2.instances ADD COLUMN query_available_at timestamptz NOT NULL DEFAULT clock_timestamp();
        ALTER TABLE cp6_approval_start_v2.starts ADD COLUMN query_available_at timestamptz NOT NULL DEFAULT clock_timestamp();
        ALTER TABLE cp6_approval_runtime_v2.children ADD COLUMN query_available_at timestamptz NOT NULL DEFAULT clock_timestamp();
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Query snapshot membership uses forward migration.");
}
