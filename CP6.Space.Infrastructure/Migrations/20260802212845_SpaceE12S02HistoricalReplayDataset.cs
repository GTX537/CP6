using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE12S02HistoricalReplayDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_PlanningHistoricalDataset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HistoricalFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    HistoricalToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReplayStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReplaySpeedFactor = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TaskCount = table.Column<int>(type: "int", nullable: false),
                    SourceDatasetHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    DefinitionVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    DeidentificationVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningHistoricalDataset", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningHistoricalDataset_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_PlanningHistoricalDataset_Invariants", "[HistoricalFromUtc] < [HistoricalToUtc] AND [ReplaySpeedFactor] > 0 AND [ReplaySpeedFactor] <= 1000 AND [TaskCount] BETWEEN 1 AND 10000 AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalDataset_Branch_Tenant",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalTable: "Space_PlanningScenarioBranch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalDataset_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalDataset_ScenarioVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningHistoricalTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    TaskToken = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    WorkerToken = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    TaskType = table.Column<short>(type: "smallint", nullable: false),
                    Outcome = table.Column<short>(type: "smallint", nullable: false),
                    OriginalCreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    OriginalCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReplayCreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ReplayCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    FromLocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToLocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningHistoricalTask", x => x.Id);
                    table.CheckConstraint("CK_Space_PlanningHistoricalTask_Invariants", "[SequenceNo] > 0 AND [Quantity] > 0 AND [OriginalCreatedAtUtc] <= [OriginalCompletedAtUtc] AND [ReplayCreatedAtUtc] <= [ReplayCompletedAtUtc] AND [ToLocationLogicalId] <> '00000000-0000-0000-0000-000000000000' AND ([FromLocationLogicalId] IS NULL OR [FromLocationLogicalId] <> '00000000-0000-0000-0000-000000000000') AND LEN([TaskToken]) = 64 AND [TaskToken] NOT LIKE '%[^0-9a-f]%' AND ([WorkerToken] IS NULL OR (LEN([WorkerToken]) = 64 AND [WorkerToken] NOT LIKE '%[^0-9a-f]%')) AND [TaskType] BETWEEN 0 AND 4 AND [Outcome] BETWEEN 0 AND 2 AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningHistoricalTask_Dataset_Tenant",
                        columns: x => new { x.TenantId, x.DatasetId },
                        principalTable: "Space_PlanningHistoricalDataset",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningHistoricalDataset_Branch_Created",
                table: "Space_PlanningHistoricalDataset",
                columns: new[] { "TenantId", "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningHistoricalDataset_TenantId_ModelId_ScenarioVersionId",
                table: "Space_PlanningHistoricalDataset",
                columns: new[] { "TenantId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningHistoricalTask_Dataset_Sequence",
                table: "Space_PlanningHistoricalTask",
                columns: new[] { "TenantId", "DatasetId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningHistoricalTask_Dataset_Token",
                table: "Space_PlanningHistoricalTask",
                columns: new[] { "TenantId", "DatasetId", "TaskToken" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_PlanningHistoricalTask");

            migrationBuilder.DropTable(
                name: "Space_PlanningHistoricalDataset");
        }
    }
}
