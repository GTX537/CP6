using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE04S03ElementCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_ElementCommandBatch",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedFloorRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultFloorRevision = table.Column<long>(type: "bigint", nullable: true),
                    ResultVersionContentRevision = table.Column<long>(type: "bigint", nullable: true),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppliedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementCommandBatch", x => x.Id);
                    table.UniqueConstraint("AK_Space_ElementCommandBatch_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ElementCommandBatch_Result", "([ResultFloorRevision] IS NULL AND [ResultVersionContentRevision] IS NULL AND [ResponseJson] IS NULL) OR ([ResultFloorRevision] IS NOT NULL AND [ResultVersionContentRevision] IS NOT NULL AND [ResponseJson] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Space_ElementCommandBatch_Floor_Tenant_Version_Logical",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.FloorLogicalId },
                        principalTable: "Space_FloorRevision",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "LogicalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ElementCommandBatch_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ElementCommandRecord",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ElementCommandRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_ElementCommandRecord_Batch_Tenant",
                        columns: x => new { x.TenantId, x.CommandBatchId },
                        principalTable: "Space_ElementCommandBatch",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementCommandBatch_Floor_Applied",
                table: "Space_ElementCommandBatch",
                columns: new[] { "TenantId", "ModelVersionId", "FloorLogicalId", "AppliedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ElementCommandRecord_Batch_Sequence",
                table: "Space_ElementCommandRecord",
                columns: new[] { "TenantId", "CommandBatchId", "SequenceNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_ElementCommandRecord");

            migrationBuilder.DropTable(
                name: "Space_ElementCommandBatch");
        }
    }
}
