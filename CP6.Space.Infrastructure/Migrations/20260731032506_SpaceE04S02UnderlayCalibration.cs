using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE04S02UnderlayCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UnderlayCalibrationId",
                table: "Space_FloorRevision",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_UnderlayCalibration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    PixelWidth = table.Column<int>(type: "int", nullable: false),
                    PixelHeight = table.Column<int>(type: "int", nullable: false),
                    Point1PixelX = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Point1PixelY = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Point1WorldX = table.Column<int>(type: "int", nullable: false),
                    Point1WorldY = table.Column<int>(type: "int", nullable: false),
                    Point2PixelX = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Point2PixelY = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Point2WorldX = table.Column<int>(type: "int", nullable: false),
                    Point2WorldY = table.Column<int>(type: "int", nullable: false),
                    ValidationPixelX = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    ValidationPixelY = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    ValidationWorldX = table.Column<int>(type: "int", nullable: false),
                    ValidationWorldY = table.Column<int>(type: "int", nullable: false),
                    MillimetersPerPixel = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    OffsetX = table.Column<int>(type: "int", nullable: false),
                    OffsetY = table.Column<int>(type: "int", nullable: false),
                    RotationZ = table.Column<decimal>(type: "decimal(9,4)", nullable: false),
                    ValidationErrorMillimeters = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ErrorThresholdMillimeters = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_UnderlayCalibration", x => x.Id);
                    table.UniqueConstraint("AK_Space_UnderlayCalibration_Tenant_Version_Floor_Source_Id", x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId, x.SourceId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_UnderlayCalibration_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_LogicalId_UnderlaySourceId_UnderlayCalibrationId",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "LogicalId", "UnderlaySourceId", "UnderlayCalibrationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_UnderlayCalibration_Version_Floor_Created",
                table: "Space_UnderlayCalibration",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_UnderlayCalibration_Version_Source",
                table: "Space_UnderlayCalibration",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Space_FloorRevision_UnderlayCalibration_Tenant_Version_Floor_Source",
                table: "Space_FloorRevision",
                columns: new[] { "TenantId", "ModelVersionId", "LogicalId", "UnderlaySourceId", "UnderlayCalibrationId" },
                principalTable: "Space_UnderlayCalibration",
                principalColumns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "SourceId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_FloorRevision_UnderlayCalibration_Tenant_Version_Floor_Source",
                table: "Space_FloorRevision");

            migrationBuilder.DropTable(
                name: "Space_UnderlayCalibration");

            migrationBuilder.DropIndex(
                name: "IX_Space_FloorRevision_TenantId_ModelVersionId_LogicalId_UnderlaySourceId_UnderlayCalibrationId",
                table: "Space_FloorRevision");

            migrationBuilder.DropColumn(
                name: "UnderlayCalibrationId",
                table: "Space_FloorRevision");
        }
    }
}
