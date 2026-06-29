using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceP5ConnectorCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TravelSecPerFloor",
                table: "Space_Connector",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WaitSec",
                table: "Space_Connector",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE [Space_Connector] SET [WaitSec]=20,[TravelSecPerFloor]=6  WHERE [ConnectorType]=1;");
            migrationBuilder.Sql("UPDATE [Space_Connector] SET [WaitSec]=0, [TravelSecPerFloor]=15 WHERE [ConnectorType]=2;");
            migrationBuilder.Sql("UPDATE [Space_Connector] SET [WaitSec]=0, [TravelSecPerFloor]=10 WHERE [ConnectorType]=3;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TravelSecPerFloor",
                table: "Space_Connector");

            migrationBuilder.DropColumn(
                name: "WaitSec",
                table: "Space_Connector");
        }
    }
}
