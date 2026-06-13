using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PubCommonAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Pub_Attachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BizType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BizId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DraftToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoreName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StorePath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    FileHash = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Uploader = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pub_Attachment", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pub_Attachment_Biz",
                table: "Pub_Attachment",
                columns: new[] { "BizType", "BizId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pub_Attachment_Draft",
                table: "Pub_Attachment",
                column: "DraftToken");

            migrationBuilder.CreateIndex(
                name: "IX_Pub_Attachment_Hash",
                table: "Pub_Attachment",
                column: "FileHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pub_Attachment");
        }
    }
}
