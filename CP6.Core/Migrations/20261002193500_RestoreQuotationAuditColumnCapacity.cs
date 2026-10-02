using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20261002193500_RestoreQuotationAuditColumnCapacity")]
public sealed class RestoreQuotationAuditColumnCapacity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("BUG143's historical quotation audit capacity repair is SQL Server only.");
        foreach (var statement in QuotationAuditColumnCapacityRepairSql.CreateStatements())
            migrationBuilder.Sql(statement);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Quotation audit capacity restoration is forward-only; reconcile conflicting definitions or data explicitly.");
}
