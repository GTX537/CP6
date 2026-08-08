using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE12S03PlanningSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Space_PlanningHistoricalDataset_Tenant_Id_Branch_Model_Version",
                table: "Space_PlanningHistoricalDataset",
                columns: new[] { "TenantId", "Id", "BranchId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateTable(
                name: "Space_PlanningSimulationRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefinitionVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    DatasetRequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ResultHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    GeometryBasis = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    CurrencyCode = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    DefaultQuantityCapacity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DefaultConcurrentTaskCapacity = table.Column<int>(type: "int", nullable: false),
                    LocationCapacityOverrideCount = table.Column<int>(type: "int", nullable: false),
                    ThroughputWindowMinutes = table.Column<int>(type: "int", nullable: false),
                    DistanceCostPerMeter = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    LaborCostPerHour = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    CongestionCostPerTaskHour = table.Column<decimal>(type: "decimal(19,6)", precision: 19, scale: 6, nullable: false),
                    TaskCount = table.Column<int>(type: "int", nullable: false),
                    CompletedTaskCount = table.Column<int>(type: "int", nullable: false),
                    CompletedQuantity = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceEligibleTaskCount = table.Column<int>(type: "int", nullable: false),
                    TotalDistanceMeters = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceCoveragePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    PeakConcurrentTasks = table.Column<int>(type: "int", nullable: false),
                    CongestionSeconds = table.Column<long>(type: "bigint", nullable: false),
                    CongestionTaskSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OverloadedLocationCount = table.Column<int>(type: "int", nullable: false),
                    PeakCapacityUtilizationPercent = table.Column<decimal>(type: "decimal(38,4)", precision: 38, scale: 4, nullable: false),
                    AverageCompletedTasksPerHour = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    PeakCompletedTasksPerHour = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    AverageCompletedQuantityPerHour = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    PeakCompletedQuantityPerHour = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    LaborHours = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceCost = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    LaborCost = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    CongestionCost = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningSimulationRun", x => x.Id);
                    table.UniqueConstraint("AK_Space_PlanningSimulationRun_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_Space_PlanningSimulationRun_Tenant_Id_Version", x => new { x.TenantId, x.Id, x.ScenarioVersionId });
                    table.CheckConstraint("CK_Space_PlanningSimulationRun_Invariants", "[ScenarioContentRevision] >= 0 AND [DefaultQuantityCapacity] > 0 AND [DefaultConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [LocationCapacityOverrideCount] BETWEEN 0 AND 10000 AND [ThroughputWindowMinutes] BETWEEN 1 AND 1440 AND [DistanceCostPerMeter] >= 0 AND [LaborCostPerHour] >= 0 AND [CongestionCostPerTaskHour] >= 0 AND [TaskCount] BETWEEN 1 AND 10000 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [CompletedQuantity] >= 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [DistanceCoveragePercent] BETWEEN 0 AND 100 AND [PeakConcurrentTasks] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [OverloadedLocationCount] >= 0 AND [PeakCapacityUtilizationPercent] >= 0 AND [AverageCompletedTasksPerHour] >= 0 AND [PeakCompletedTasksPerHour] >= 0 AND [AverageCompletedQuantityPerHour] >= 0 AND [PeakCompletedQuantityPerHour] >= 0 AND [LaborHours] >= 0 AND [DistanceCost] >= 0 AND [LaborCost] >= 0 AND [CongestionCost] >= 0 AND [TotalCost] >= 0 AND LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([DatasetRequestHash]) = 64 AND [DatasetRequestHash] NOT LIKE '%[^0-9a-f]%' AND LEN([ResultHash]) = 64 AND [ResultHash] NOT LIKE '%[^0-9a-f]%' AND LEN([CurrencyCode]) = 3 AND [CurrencyCode] NOT LIKE '%[^A-Z]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_Branch_Tenant",
                        columns: x => new { x.TenantId, x.BranchId },
                        principalTable: "Space_PlanningScenarioBranch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_Dataset_Tenant",
                        columns: x => new { x.TenantId, x.DatasetId, x.BranchId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_PlanningHistoricalDataset",
                        principalColumns: new[] { "TenantId", "Id", "BranchId", "ModelId", "ScenarioVersionId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_Model_Tenant",
                        columns: x => new { x.TenantId, x.ModelId },
                        principalTable: "Space_Model",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationRun_ScenarioVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelId, x.ScenarioVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "ModelId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_PlanningSimulationLocationResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskCount = table.Column<int>(type: "int", nullable: false),
                    CompletedTaskCount = table.Column<int>(type: "int", nullable: false),
                    TotalQuantity = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    DistanceEligibleTaskCount = table.Column<int>(type: "int", nullable: false),
                    TotalDistanceMeters = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    QuantityCapacity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConcurrentTaskCapacity = table.Column<int>(type: "int", nullable: false),
                    PeakConcurrentTasks = table.Column<int>(type: "int", nullable: false),
                    PeakConcurrentQuantity = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: false),
                    CapacityUtilizationPercent = table.Column<decimal>(type: "decimal(38,4)", precision: 38, scale: 4, nullable: false),
                    CongestionSeconds = table.Column<long>(type: "bigint", nullable: false),
                    CongestionTaskSeconds = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PlanningSimulationLocationResult", x => x.Id);
                    table.CheckConstraint("CK_Space_PlanningSimulationLocationResult_Invariants", "[TaskCount] > 0 AND [CompletedTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalQuantity] > 0 AND [DistanceEligibleTaskCount] BETWEEN 0 AND [TaskCount] AND [TotalDistanceMeters] >= 0 AND [QuantityCapacity] > 0 AND [ConcurrentTaskCapacity] BETWEEN 1 AND 10000 AND [PeakConcurrentTasks] >= 0 AND [PeakConcurrentQuantity] >= 0 AND [CapacityUtilizationPercent] >= 0 AND [CongestionSeconds] >= 0 AND [CongestionTaskSeconds] >= 0 AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationLocation_Location_Tenant",
                        columns: x => new { x.TenantId, x.ScenarioVersionId, x.LocationLogicalId },
                        principalTable: "Space_LocationRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PlanningSimulationLocation_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId, x.ScenarioVersionId },
                        principalTable: "Space_PlanningSimulationRun",
                        principalColumns: new[] { "TenantId", "Id", "ScenarioVersionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationLocationResult_TenantId_RunId_ScenarioVersionId",
                table: "Space_PlanningSimulationLocationResult",
                columns: new[] { "TenantId", "RunId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationLocationResult_TenantId_ScenarioVersionId_LocationLogicalId",
                table: "Space_PlanningSimulationLocationResult",
                columns: new[] { "TenantId", "ScenarioVersionId", "LocationLogicalId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PlanningSimulationLocation_Run_Location",
                table: "Space_PlanningSimulationLocationResult",
                columns: new[] { "TenantId", "RunId", "LocationLogicalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_Branch_Created",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "BranchId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_Dataset_Created",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "DatasetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_TenantId_DatasetId_BranchId_ModelId_ScenarioVersionId",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "DatasetId", "BranchId", "ModelId", "ScenarioVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_PlanningSimulationRun_TenantId_ModelId_ScenarioVersionId",
                table: "Space_PlanningSimulationRun",
                columns: new[] { "TenantId", "ModelId", "ScenarioVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_PlanningSimulationLocationResult");

            migrationBuilder.DropTable(
                name: "Space_PlanningSimulationRun");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Space_PlanningHistoricalDataset_Tenant_Id_Branch_Model_Version",
                table: "Space_PlanningHistoricalDataset");
        }
    }
}
