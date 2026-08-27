using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceV1UnifiedDraftCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "CreationSource",
                table: "Space_ModelVersion",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<string>(
                name: "SourceTemplateContentHash",
                table: "Space_ModelVersion",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTemplateId",
                table: "Space_ModelVersion",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceTemplateVersionId",
                table: "Space_ModelVersion",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [Space_ModelVersion]
                SET [CreationSource] = 1
                WHERE [BasedOnVersionId] IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ModelVersion_CreationSource",
                table: "Space_ModelVersion",
                sql: "[CreationSource] IN (0, 1, 2, 3) AND (((([CreationSource] = 0 AND [BasedOnVersionId] IS NULL) OR ([CreationSource] = 1 AND [BasedOnVersionId] IS NOT NULL)) AND [SourceTemplateId] IS NULL AND [SourceTemplateVersionId] IS NULL AND [SourceTemplateContentHash] IS NULL) OR ([CreationSource] IN (2, 3) AND [BasedOnVersionId] IS NULL AND [SourceTemplateId] IS NOT NULL AND [SourceTemplateVersionId] IS NOT NULL AND [SourceTemplateContentHash] IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_ModelVersion_CreationSource",
                table: "Space_ModelVersion");

            migrationBuilder.DropColumn(
                name: "CreationSource",
                table: "Space_ModelVersion");

            migrationBuilder.DropColumn(
                name: "SourceTemplateContentHash",
                table: "Space_ModelVersion");

            migrationBuilder.DropColumn(
                name: "SourceTemplateId",
                table: "Space_ModelVersion");

            migrationBuilder.DropColumn(
                name: "SourceTemplateVersionId",
                table: "Space_ModelVersion");
        }
    }
}
