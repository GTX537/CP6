using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class OaStage1FormEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wf_FormData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FormVersion = table.Column<int>(type: "int", nullable: false),
                    BizId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FormData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FormDef",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FormName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FormDef", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormData_Biz",
                table: "Wf_FormData",
                column: "BizId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormData_FormKey",
                table: "Wf_FormData",
                column: "FormKey");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormDef_FormKey",
                table: "Wf_FormDef",
                column: "FormKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wf_FormData");

            migrationBuilder.DropTable(
                name: "Wf_FormDef");
        }
    }
}
