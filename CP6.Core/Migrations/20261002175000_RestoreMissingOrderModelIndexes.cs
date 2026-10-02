using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20261002175000_RestoreMissingOrderModelIndexes")]
public sealed class RestoreMissingOrderModelIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("BUG139's historical index repair is SQL Server only.");
        foreach (var statement in OrderModelIndexRepairSql.CreateStatements())
            migrationBuilder.Sql(statement);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Query-index restoration is forward-only; reconcile conflicting objects explicitly.");
}
