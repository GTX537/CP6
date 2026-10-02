using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Core.Migrations;

[DbContext(typeof(CP6Context))]
[Migration("20261002184500_RestoreMissingOrderModelForeignKeys")]
public sealed class RestoreMissingOrderModelForeignKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            throw new NotSupportedException("BUG141's historical FK repair is SQL Server only.");
        foreach (var statement in OrderModelForeignKeyRepairSql.CreateStatements())
            migrationBuilder.Sql(statement);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Order FK restoration is forward-only; reconcile conflicting objects explicitly.");
}
