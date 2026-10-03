using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class CrmIdentityTenantGenerationV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmIdentityTenantGenerations",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmIdentityTenantGenerations", x => x.TenantId);
                    table.CheckConstraint("CK_CrmIdentityTenantGenerations_Generation", "[Generation] >= 0");
                });
            migrationBuilder.Sql(CP6.Core.Persistence.IdentityGenerationSqlServerV1.SeedSql);
            migrationBuilder.Sql(CP6.Core.Persistence.IdentityGenerationSqlServerV1.TriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Identity tenant-generation migrations are forward-only.");
    }
}
