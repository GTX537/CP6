using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PubCommonCodeGen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pub_GenColumn",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenTableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ClrType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    InList = table.Column<bool>(type: "bit", nullable: false),
                    InForm = table.Column<bool>(type: "bit", nullable: false),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pub_GenColumn", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pub_GenTable",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Module = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TableName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SeqBizKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CodeField = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RoutePath = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    MenuName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pub_GenTable", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pub_GenColumn_Table",
                table: "Pub_GenColumn",
                columns: new[] { "GenTableId", "Sort" });

            migrationBuilder.CreateIndex(
                name: "UX_Pub_GenTable_Entity",
                table: "Pub_GenTable",
                column: "EntityName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pub_GenColumn");

            migrationBuilder.DropTable(
                name: "Pub_GenTable");
        }
    }
}
