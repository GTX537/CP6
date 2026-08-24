using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceCadProviderQualificationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DataRegionApproved",
                table: "Space_CadSiteProviderCertification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DeletionRetentionApproved",
                table: "Space_CadSiteProviderCertification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FrozenEnvironmentSha256",
                table: "Space_CadSiteProviderCertification",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoldenDatasetSha256",
                table: "Space_CadSiteProviderCertification",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LicensingApproved",
                table: "Space_CadSiteProviderCertification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "QualificationEvidenceReference",
                table: "Space_CadSiteProviderCertification",
                type: "varchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationRubricVersion",
                table: "Space_CadSiteProviderCertification",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualificationScore",
                table: "Space_CadSiteProviderCertification",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecurityApproved",
                table: "Space_CadSiteProviderCertification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_CadProviderCertification_QualificationScore",
                table: "Space_CadSiteProviderCertification",
                sql: "[QualificationScore] IS NULL OR ([QualificationScore] >= 0 AND [QualificationScore] <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_CadProviderCertification_QualificationScore",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "DataRegionApproved",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "DeletionRetentionApproved",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "FrozenEnvironmentSha256",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "GoldenDatasetSha256",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "LicensingApproved",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "QualificationEvidenceReference",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "QualificationRubricVersion",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "QualificationScore",
                table: "Space_CadSiteProviderCertification");

            migrationBuilder.DropColumn(
                name: "SecurityApproved",
                table: "Space_CadSiteProviderCertification");
        }
    }
}
