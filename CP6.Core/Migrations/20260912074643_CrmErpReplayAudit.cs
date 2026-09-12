using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20260912074643_CrmErpReplayAudit")]
public sealed class CrmErpReplayAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var operation in new ErpIntegration.ErpInboxReplayAudit().UpOperations)
            migrationBuilder.Operations.Add(operation);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("C03 replay audit is forward-only.");
}
