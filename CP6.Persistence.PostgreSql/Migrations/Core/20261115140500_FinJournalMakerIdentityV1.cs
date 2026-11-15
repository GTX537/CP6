using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261115140500_FinJournalMakerIdentityV1")]
public sealed class FinJournalMakerIdentityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<Guid>(name: "MakerUserId", schema: "public", table: "Fin_JournalEntry", type: "uuid", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Journal maker identity requires forward migration.");
}
