using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20260912085508_CrmErpDeliveryReplayAudit")]
public sealed class CrmErpDeliveryReplayAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var operation in new ErpIntegration.ErpDeliveryReplayAudit().UpOperations)
            migrationBuilder.Operations.Add(operation);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("C03 delivery replay audit is forward-only.");
}
