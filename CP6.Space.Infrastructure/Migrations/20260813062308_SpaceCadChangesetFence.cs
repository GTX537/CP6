using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceCadChangesetFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangesetSha256",
                table: "Space_ElementCommandBatch",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedContentHash",
                table: "Space_ElementCommandBatch",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExpectedContentRevision",
                table: "Space_ElementCommandBatch",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangesetSha256",
                table: "Space_ElementCommandBatch");

            migrationBuilder.DropColumn(
                name: "ExpectedContentHash",
                table: "Space_ElementCommandBatch");

            migrationBuilder.DropColumn(
                name: "ExpectedContentRevision",
                table: "Space_ElementCommandBatch");
        }
    }
}
