using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE12S01PlanningScenarioBranches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "Purpose",
                table: "Space_ModelVersion",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "Space_PlanningScenarioBranch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BasePublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CloneJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefinitionVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningScenarioBranch", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningScenarioBranch_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PlanningScenarioBranch_Immutable", "[BasePublishedVersionId] <> [ScenarioVersionId] AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_BaseVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.BasePublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_CloneJob_Tenant",
                        columns: x => new { x.TenantId, x.CloneJobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningScenarioBranch_ScenarioVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ModelVersion_Purpose",
                table: "Space_ModelVersion",
                sql: "[Purpose] IN (0, 1) AND ([Purpose] = 0 OR ([Status] NOT IN (3, 4, 5, 6) AND [PublishedAtUtc] IS NULL AND [PublishedBy] IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningScenarioBranch_Site_Created",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "SiteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningScenarioBranch_TenantId_ModelId_BasePublishedVersionId",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "ModelId", "BasePublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningScenarioBranch_TenantId_ModelId_ScenarioVersionId",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningScenarioBranch_CloneJob",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "CloneJobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningScenarioBranch_ScenarioVersion",
                table: "Space_PlanningScenarioBranch",
                columns: new[] { "TenantId", "ScenarioVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_PlanningScenarioBranch");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_ModelVersion_Purpose",
                table: "Space_ModelVersion");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Space_ModelVersion");
        }
    }
}
