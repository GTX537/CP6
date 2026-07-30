using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE01S06FileSafetyRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContentDeletedAtUtc",
                table: "Space_File",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionRequestedAtUtc",
                table: "Space_File",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetainUntilUtc",
                table: "Space_File",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [Space_File]
                SET [DeletionRequestedAtUtc] =
                    COALESCE([ModifiedAtUtc], [CreatedAtUtc], SYSUTCDATETIME())
                WHERE ([State] = 5 OR [IsDeleted] = 1)
                  AND [DeletionRequestedAtUtc] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Space_File_Tenant_PendingObjectDeletion",
                table: "Space_File",
                columns: new[] { "TenantId", "DeletionRequestedAtUtc", "ContentDeletedAtUtc" },
                filter: "[State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [ContentDeletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_File_Tenant_Retention",
                table: "Space_File",
                columns: new[] { "TenantId", "RetainUntilUtc", "State" },
                filter: "[RetainUntilUtc] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_File_ContentDeletion",
                table: "Space_File",
                sql: "[ContentDeletedAtUtc] IS NULL OR ([State] = 5 AND [DeletionRequestedAtUtc] IS NOT NULL AND [IsDeleted] = 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Space_File_Tenant_PendingObjectDeletion",
                table: "Space_File");

            migrationBuilder.DropIndex(
                name: "IX_Space_File_Tenant_Retention",
                table: "Space_File");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_File_ContentDeletion",
                table: "Space_File");

            migrationBuilder.DropColumn(
                name: "ContentDeletedAtUtc",
                table: "Space_File");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAtUtc",
                table: "Space_File");

            migrationBuilder.DropColumn(
                name: "RetainUntilUtc",
                table: "Space_File");
        }
    }
}
