using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE11S03DispatchRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_DispatchRecommendation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCode = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefinitionVersion = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    ExaminedTaskCount = table.Column<int>(type: "int", nullable: false),
                    EligibleTaskCount = table.Column<int>(type: "int", nullable: false),
                    ExaminedPersonCount = table.Column<int>(type: "int", nullable: false),
                    EligiblePersonCount = table.Column<int>(type: "int", nullable: false),
                    EligiblePairCount = table.Column<int>(type: "int", nullable: false),
                    MatchableAssignmentCount = table.Column<int>(type: "int", nullable: false),
                    ReturnedAssignmentCount = table.Column<int>(type: "int", nullable: false),
                    IsTruncated = table.Column<bool>(type: "bit", nullable: false),
                    ExclusionSamplesTruncated = table.Column<bool>(type: "bit", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourcesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExclusionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExclusionSamplesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LimitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_Space_DispatchRecommendation", x => x.Id);
                    table.UniqueConstraint("AK_Space_DispatchRecommendation_Tenant_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Counts", "[ExaminedTaskCount] >= 0 AND [EligibleTaskCount] >= 0 AND [ExaminedPersonCount] >= 0 AND [EligiblePersonCount] >= 0 AND [EligiblePairCount] >= 0 AND [MatchableAssignmentCount] >= 0 AND [ReturnedAssignmentCount] >= 0 AND [EligibleTaskCount] <= [ExaminedTaskCount] AND [EligiblePersonCount] <= [ExaminedPersonCount] AND [MatchableAssignmentCount] <= [EligibleTaskCount] AND [MatchableAssignmentCount] <= [EligiblePersonCount] AND [MatchableAssignmentCount] <= [EligiblePairCount] AND [ReturnedAssignmentCount] <= [MatchableAssignmentCount] AND (([IsTruncated] = 1 AND [ReturnedAssignmentCount] < [MatchableAssignmentCount]) OR ([IsTruncated] = 0 AND [ReturnedAssignmentCount] = [MatchableAssignmentCount]))");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Evidence", "[Outcome] IN ('NoAssignment', 'AssignmentsGenerated') AND ISJSON([RequestJson]) = 1 AND ISJSON([SourcesJson]) = 1 AND ISJSON([ExclusionsJson]) = 1 AND ISJSON([ExclusionSamplesJson]) = 1 AND ISJSON([AssignmentsJson]) = 1 AND ISJSON([LimitationsJson]) = 1");
                    table.CheckConstraint("CK_Space_DispatchRecommendation_Immutable", "LEN([RequestHash]) = 64 AND [RequestHash] NOT LIKE '%[^0-9a-f]%' AND [IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_Space_DispatchRecommendation_Version_Tenant",
                        columns: x => new { x.TenantId, x.PublishedVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DispatchRecommendation_Tenant_Site_Generated",
                table: "Space_DispatchRecommendation",
                columns: new[] { "TenantId", "SiteId", "GeneratedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_DispatchRecommendation_TenantId_PublishedVersionId",
                table: "Space_DispatchRecommendation",
                columns: new[] { "TenantId", "PublishedVersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_DispatchRecommendation");
        }
    }
}
