using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE09S03ExternalPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_FieldPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AudienceType = table.Column<short>(type: "smallint", nullable: false),
                    CanExport = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    PolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_FieldPolicy", x => x.Id);
                    table.UniqueConstraint("AK_Space_FieldPolicy_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_FieldPolicy_AudienceType", "[AudienceType] >= 0 AND [AudienceType] <= 2");
                    table.CheckConstraint("CK_Space_FieldPolicy_Status", "[Status] >= 0 AND [Status] <= 1");
                    table.CheckConstraint("CK_Space_FieldPolicy_Version", "[PolicyVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_FieldPolicyField",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceType = table.Column<short>(type: "smallint", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedFieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MaskingRule = table.Column<short>(type: "smallint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_FieldPolicyField", x => x.Id);
                    table.UniqueConstraint("AK_Space_FieldPolicyField_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_FieldPolicyField_MaskingRule", "[MaskingRule] >= 0 AND [MaskingRule] <= 3");
                    table.CheckConstraint("CK_Space_FieldPolicyField_ResourceType", "[ResourceType] >= 0 AND [ResourceType] <= 2");
                    table.ForeignKey(
                        name: "FK_Space_FieldPolicyField_Policy_Tenant",
                        columns: x => new { x.TenantId, x.PolicyId },
                        principalTable: "Space_FieldPolicy",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalGrant_TenantId_FieldPolicyId",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "FieldPolicyId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_FieldPolicy_CurrentName",
                table: "Space_FieldPolicy",
                columns: new[] { "TenantId", "AudienceType", "NormalizedName" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_FieldPolicyField_Current",
                table: "Space_FieldPolicyField",
                columns: new[] { "TenantId", "PolicyId", "ResourceType", "NormalizedFieldName" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ExternalGrant_FieldPolicy_Tenant",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "FieldPolicyId" },
                principalTable: "Space_FieldPolicy",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_ExternalGrant_FieldPolicy_Tenant",
                table: "Space_ExternalGrant");

            migrationBuilder.DropTable(
                name: "Space_FieldPolicyField");

            migrationBuilder.DropTable(
                name: "Space_FieldPolicy");

            migrationBuilder.DropIndex(
                name: "IX_Space_ExternalGrant_TenantId_FieldPolicyId",
                table: "Space_ExternalGrant");
        }
    }
}
