using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13RackGenerationProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_RackGenerationProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackGenerationProfile", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackGenerationProfile_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_RackGenerationProfile_ScopeOwner", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                });

            migrationBuilder.CreateTable(
                name: "Space_RackGenerationProfileVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    RackWidthMillimeters = table.Column<int>(type: "int", nullable: false),
                    RackDepthMillimeters = table.Column<int>(type: "int", nullable: false),
                    RackHeightMillimeters = table.Column<int>(type: "int", nullable: false),
                    LevelsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LocationCount = table.Column<long>(type: "bigint", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_RackGenerationProfileVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_RackGenerationProfileVersion_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_Dimensions", "[RackWidthMillimeters] > 0 AND [RackDepthMillimeters] > 0 AND [RackHeightMillimeters] > 0");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_LocationCount", "[LocationCount] > 0 AND [LocationCount] <= 10000000");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_ScopeOwner", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_Space_RackGenerationProfileVersion_VersionNo", "[VersionNo] > 0");
                    table.ForeignKey(
                        name: "FK_Space_RackGenerationProfileVersion_Profile_Scope_Owner",
                        columns: x => new { x.Scope, x.OwnerTenantId, x.ProfileId },
                        principalTable: "Space_RackGenerationProfile",
                        principalColumns: new[] { "Scope", "OwnerTenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackGenerationProfile_Scope_Owner_Code_Active",
                table: "Space_RackGenerationProfile",
                columns: new[] { "Scope", "OwnerTenantId", "ProfileCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_RackGenerationProfileVersion_Scope_Owner_Profile_VersionNo",
                table: "Space_RackGenerationProfileVersion",
                columns: new[] { "Scope", "OwnerTenantId", "ProfileId", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_RackGenerationProfileVersion");

            migrationBuilder.DropTable(
                name: "Space_RackGenerationProfile");
        }
    }
}
