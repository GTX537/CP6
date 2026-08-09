using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE05S02RackLevelSpecification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_RackLevelRevision_Dimensions",
                table: "Space_RackLevelRevision");

            migrationBuilder.AddColumn<int>(
                name: "BeamHeight",
                table: "Space_RackLevelRevision",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_RackLevelRevision_Dimensions",
                table: "Space_RackLevelRevision",
                sql: "[LevelNo] > 0 AND [BottomZ] >= 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND [BeamHeight] >= 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_RackLevelRevision_Dimensions",
                table: "Space_RackLevelRevision");

            migrationBuilder.DropColumn(
                name: "BeamHeight",
                table: "Space_RackLevelRevision");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_RackLevelRevision_Dimensions",
                table: "Space_RackLevelRevision",
                sql: "[LevelNo] > 0 AND [ClearHeight] > 0 AND [BinCount] > 0 AND [DepthCount] > 0 AND [CellWidth] > 0 AND [CellDepth] > 0 AND ([MaxLoad] IS NULL OR [MaxLoad] >= 0)");
        }
    }
}
