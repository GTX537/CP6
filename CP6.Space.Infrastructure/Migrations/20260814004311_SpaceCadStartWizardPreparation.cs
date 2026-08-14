using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceCadStartWizardPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_CadParsePreparation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedUnit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ConfirmedScaleToMillimeters = table.Column<decimal>(type: "decimal(18,9)", precision: 18, scale: 9, nullable: false),
                    CoordinateMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CoordinateTransformSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    MappingProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingProfileVersion = table.Column<int>(type: "int", nullable: false),
                    MappingDefinitionSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    MappingPreviewSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SemanticPreviewSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ReadyForParsing = table.Column<bool>(type: "bit", nullable: false),
                    BaseContentRevision = table.Column<long>(type: "bigint", nullable: false),
                    BaseContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CadParsePreparation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_CadParsePreparation_Source_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_CadParsePreparation_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_CadParsePreparation_Source_Expiry",
                table: "Space_CadParsePreparation",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_CadParsePreparation");
        }
    }
}
