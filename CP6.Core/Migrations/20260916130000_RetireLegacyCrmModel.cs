using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20260916130000_RetireLegacyCrmModel")]
public sealed class RetireLegacyCrmModel : Migration
{
    // The current EF model no longer owns the old business tables. The physical
    // tables, source-control fences and historical migrations remain intact.
    // Any later physical cleanup requires its own explicitly approved migration.
    protected override void Up(MigrationBuilder migrationBuilder) { }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("C04B model retirement is forward-only; legacy writers cannot be re-enabled by rollback.");
}
