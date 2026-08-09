using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE12S04PlanningComparisonDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Space_PlanningSimulationRun_Tenant_Id_Branch_Version",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "Id", "BranchId", "ScenarioVersionId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Space_PlanningSimulationRun_Tenant_Id_Site",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "Id", "SiteId" });

            migrationBuilder.CreateTable(
                name: "Space_PlanningComparison",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BasePublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefinitionVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ComparisonHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SourceDatasetHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    HistoricalFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    HistoricalToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RunCount = table.Column<int>(type: "int", nullable: false),
                    MinimumDistanceCoveragePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaximumPeakCapacityUtilizationPercent = table.Column<decimal>(type: "decimal(38,4)", precision: 38, scale: 4, nullable: false),
                    MaximumCongestionTaskHours = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    MaximumTotalCost = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningComparison", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningComparison_Tenant_Id_Site", x => new { x.TenantId, x.Id, x.SiteId });
                    table.CheckConstraint("CK_Space_PlanningComparison_Invariants", "[RunCount] BETWEEN 2 AND 10 AND [HistoricalFromUtc] < [HistoricalToUtc] AND [MinimumDistanceCoveragePercent] BETWEEN 0 AND 100 AND [MaximumPeakCapacityUtilizationPercent] >= 0 AND [MaximumCongestionTaskHours] >= 0 AND ([MaximumTotalCost] IS NULL OR [MaximumTotalCost] >= 0) AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([SourceDatasetHash]) = 64 AND [SourceDatasetHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparison_BaseVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.BasePublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparison_BaselineRun_Tenant",
                        columns: x => new { x.TenantId, x.BaselineRunId, x.SiteId },
                        principalTable: "Space_PlanningSimulationRun",
                        principalColumns: new[] { "TenantId", "Id", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparison_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningComparisonEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    RunName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RunResultHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    IsBaseline = table.Column<bool>(type: "bit", nullable: false),
                    DistanceCoveragePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    TotalDistanceMeters = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    CongestionTaskSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OverloadedLocationCount = table.Column<int>(type: "int", nullable: false),
                    PeakCapacityUtilizationPercent = table.Column<decimal>(type: "decimal(38,4)", precision: 38, scale: 4, nullable: false),
                    AverageCompletedTasksPerHour = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    PeakCompletedTasksPerHour = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceDeltaMeters = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    CongestionTaskSecondsDelta = table.Column<long>(type: "bigint", nullable: false),
                    OverloadedLocationCountDelta = table.Column<int>(type: "int", nullable: false),
                    PeakCapacityUtilizationDeltaPercentagePoints = table.Column<decimal>(type: "decimal(38,4)", precision: 38, scale: 4, nullable: false),
                    AverageCompletedTasksPerHourDelta = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    TotalCostDelta = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    RiskCount = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningComparisonEntry", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningComparisonEntry_Comparison_Id_Run", x => new { x.TenantId, x.ComparisonId, x.Id, x.RunId });
                    table.UniqueConstraint("AK_Space_PlanningComparisonEntry_Comparison_Run", x => new { x.TenantId, x.ComparisonId, x.RunId });
                    table.CheckConstraint("CK_Space_PlanningComparisonEntry_Invariants", "[SequenceNo] BETWEEN 1 AND 10 AND [ScenarioContentRevision] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [TotalDistanceMeters] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [TotalCost] >= 0 AND [RiskCount] BETWEEN 0 AND 10 AND LEN([RunResultHash]) = 64 AND [RunResultHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparisonEntry_Comparison_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SiteId },
                        principalTable: "Space_PlanningComparison",
                        principalColumns: new[] { "TenantId", "Id", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparisonEntry_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId, x.BranchId, x.ScenarioVersionId },
                        principalTable: "Space_PlanningSimulationRun",
                        principalColumns: new[] { "TenantId", "Id", "BranchId", "ScenarioVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningComparisonRisk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningComparisonRisk", x => x.Id);
                    table.CheckConstraint("CK_Space_PlanningComparisonRisk_Invariants", "[Severity] BETWEEN 1 AND 3 AND LEN([Code]) BETWEEN 1 AND 100 AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningComparisonRisk_Entry_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.EntryId, x.RunId },
                        principalTable: "Space_PlanningComparisonEntry",
                        principalColumns: new[] { "TenantId", "ComparisonId", "Id", "RunId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningDecisionRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SelectedRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersedesDecisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Rationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ComparisonHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    DefinitionVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningDecisionRecord", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningDecisionRecord_Comparison_Id", x => new { x.TenantId, x.ComparisonId, x.Id });
                    table.CheckConstraint("CK_Space_PlanningDecisionRecord_Invariants", "[Outcome] BETWEEN 1 AND 3 AND (([Outcome] = 1 AND [SelectedRunId] IS NOT NULL) OR ([Outcome] IN (2, 3) AND [SelectedRunId] IS NULL)) AND ([SupersedesDecisionId] IS NULL OR [SupersedesDecisionId] <> [Id]) AND LEN([Rationale]) BETWEEN 1 AND 2000 AND LEN([ComparisonHash]) = 64 AND [ComparisonHash] NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningDecisionRecord_Comparison_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SiteId },
                        principalTable: "Space_PlanningComparison",
                        principalColumns: new[] { "TenantId", "Id", "SiteId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningDecisionRecord_SelectedRun_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SelectedRunId },
                        principalTable: "Space_PlanningComparisonEntry",
                        principalColumns: new[] { "TenantId", "ComparisonId", "RunId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningDecisionRecord_Supersedes_Tenant",
                        columns: x => new { x.TenantId, x.ComparisonId, x.SupersedesDecisionId },
                        principalTable: "Space_PlanningDecisionRecord",
                        principalColumns: new[] { "TenantId", "ComparisonId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparison_Site_Created",
                table: "Space_PlanningComparison",
                columns: new[] { "TenantId", "SiteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparison_TenantId_BaselineRunId_SiteId",
                table: "Space_PlanningComparison",
                columns: new[] { "TenantId", "BaselineRunId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparison_TenantId_ModelId_BasePublishedVersionId",
                table: "Space_PlanningComparison",
                columns: new[] { "TenantId", "ModelId", "BasePublishedVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparisonEntry_TenantId_ComparisonId_SiteId",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "ComparisonId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparisonEntry_TenantId_RunId_BranchId_ScenarioVersionId",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "RunId", "BranchId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningComparisonEntry_Comparison_Baseline",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "ComparisonId", "IsBaseline" },
                unique: true,
                filter: "[IsBaseline] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningComparisonEntry_Comparison_Sequence",
                table: "Space_PlanningComparisonEntry",
                columns: new[] { "TenantId", "ComparisonId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningComparisonRisk_TenantId_ComparisonId_EntryId_RunId",
                table: "Space_PlanningComparisonRisk",
                columns: new[] { "TenantId", "ComparisonId", "EntryId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningComparisonRisk_Entry_Code",
                table: "Space_PlanningComparisonRisk",
                columns: new[] { "TenantId", "EntryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningDecisionRecord_Comparison_Created",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningDecisionRecord_TenantId_ComparisonId_SelectedRunId",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "SelectedRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningDecisionRecord_TenantId_ComparisonId_SiteId",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningDecisionRecord_Supersedes",
                table: "Space_PlanningDecisionRecord",
                columns: new[] { "TenantId", "ComparisonId", "SupersedesDecisionId" },
                unique: true,
                filter: "[SupersedesDecisionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_PlanningComparisonRisk");

            migrationBuilder.DropTable(
                name: "Space_PlanningDecisionRecord");

            migrationBuilder.DropTable(
                name: "Space_PlanningComparisonEntry");

            migrationBuilder.DropTable(
                name: "Space_PlanningComparison");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Space_PlanningSimulationRun_Tenant_Id_Branch_Version",
                table: "Space_PlanningSimulationRun");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Space_PlanningSimulationRun_Tenant_Id_Site",
                table: "Space_PlanningSimulationRun");
        }
    }
}
