using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceCadTenantMappingProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_LayerMappingProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Space_LayerMappingProfile", x => x.Id);
                    table.UniqueConstraint("AK_Space_LayerMappingProfile_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_LayerMappingProfile_CurrentVersion", "[CurrentVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Space_LayerMappingProfileVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefinitionHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BasedOnProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BasedOnVersion = table.Column<int>(type: "int", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_LayerMappingProfileVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_LayerMappingProfileVersion_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_LayerMappingProfileVersion_Base", "([BasedOnProfileId] IS NULL AND [BasedOnVersion] IS NULL) OR ([BasedOnProfileId] IS NOT NULL AND [BasedOnVersion] > 0)");
                    table.CheckConstraint("CK_Space_LayerMappingProfileVersion_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_Space_LayerMappingProfileVersion_Profile_Tenant",
                        columns: x => new { x.TenantId, x.ProfileId },
                        principalTable: "Space_LayerMappingProfile",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LayerMappingProfile_CurrentName",
                table: "Space_LayerMappingProfile",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_LayerMappingProfileVersion_DefinitionHash",
                table: "Space_LayerMappingProfileVersion",
                columns: new[] { "TenantId", "DefinitionHash" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_LayerMappingProfileVersion_Profile_Version",
                table: "Space_LayerMappingProfileVersion",
                columns: new[] { "TenantId", "ProfileId", "Version" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_LayerMappingProfileVersion");

            migrationBuilder.DropTable(
                name: "Space_LayerMappingProfile");
        }
    }
}
