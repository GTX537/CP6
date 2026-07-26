using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class OaP0FoundationExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FormDefVersionId",
                table: "Wf_FormData",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Wf_FormData",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Wf_FormData",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionKey",
                table: "Wf_FormData",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "Wf_FormData",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedBy",
                table: "Wf_FormData",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FlowDefVersionId",
                table: "Wf_FlowInstance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormDataId",
                table: "Wf_FlowInstance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormDefVersionId",
                table: "Wf_FlowInstance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FormKey",
                table: "Wf_FlowDef",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "DetailRoute",
                table: "Wf_ApprovalBinding",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Wf_FlowDefVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowDefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FlowNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowDefVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wf_FlowDefVersion_Wf_FlowDef_FlowDefId",
                        column: x => x.FlowDefId,
                        principalTable: "Wf_FlowDef",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FormDefVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FormNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FormDefVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wf_FormDefVersion_Wf_FormDef_FormDefId",
                        column: x => x.FormDefId,
                        principalTable: "Wf_FormDef",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FormFlowBinding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowDefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FormFlowBinding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wf_FormFlowBinding_Wf_FlowDef_FlowDefId",
                        column: x => x.FlowDefId,
                        principalTable: "Wf_FlowDef",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Wf_FormFlowBinding_Wf_FormDef_FormDefId",
                        column: x => x.FormDefId,
                        principalTable: "Wf_FormDef",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FlowDefVersionDependency",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowDefVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DependencyType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetFlowDefVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowDefVersionDependency", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wf_FlowDefVersionDependency_Wf_FlowDefVersion_FlowDefVersionId",
                        column: x => x.FlowDefVersionId,
                        principalTable: "Wf_FlowDefVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Wf_FlowDefVersionDependency_Wf_FlowDefVersion_TargetFlowDefVersionId",
                        column: x => x.TargetFlowDefVersionId,
                        principalTable: "Wf_FlowDefVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FormDraft",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDefVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedFormDataId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LegacyFlowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RebasedFromVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FormDraft", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wf_FormDraft_Wf_FormDefVersion_FormDefVersionId",
                        column: x => x.FormDefVersionId,
                        principalTable: "Wf_FormDefVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Wf_FormDraft_Wf_FormDef_FormDefId",
                        column: x => x.FormDefId,
                        principalTable: "Wf_FormDef",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormData_VersionSubmitted",
                table: "Wf_FormData",
                columns: new[] { "TenantId", "FormDefVersionId", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormData_SubmissionKey",
                table: "Wf_FormData",
                columns: new[] { "TenantId", "SubmissionKey" },
                unique: true,
                filter: "[SubmissionKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowInstance_ActiveBusiness",
                table: "Wf_FlowInstance",
                columns: new[] { "TenantId", "BizType", "BizId" },
                unique: true,
                filter: "[BizType] IS NOT NULL AND [BizId] IS NOT NULL AND [Status] IN (0, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowFormTo_ActualParticipant",
                table: "Wf_FlowFormTo",
                columns: new[] { "TenantId", "ActualHandlerId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowFormTo_ExpectedParticipant",
                table: "Wf_FlowFormTo",
                columns: new[] { "TenantId", "ExpectedHandlerId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowFormTo_OnBehalfParticipant",
                table: "Wf_FlowFormTo",
                columns: new[] { "TenantId", "OnBehalfOfId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowCc_Participant",
                table: "Wf_FlowCc",
                columns: new[] { "TenantId", "RecipientId", "InstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowDefVersion_FlowDefId",
                table: "Wf_FlowDefVersion",
                column: "FlowDefId");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDefVersion",
                table: "Wf_FlowDefVersion",
                columns: new[] { "TenantId", "FlowDefId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDefVersion_OneDraft",
                table: "Wf_FlowDefVersion",
                columns: new[] { "TenantId", "FlowDefId", "Status" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowDefVersionDependency_FlowDefVersionId",
                table: "Wf_FlowDefVersionDependency",
                column: "FlowDefVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowDefVersionDependency_Target",
                table: "Wf_FlowDefVersionDependency",
                columns: new[] { "TenantId", "TargetFlowDefVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowDefVersionDependency_TargetFlowDefVersionId",
                table: "Wf_FlowDefVersionDependency",
                column: "TargetFlowDefVersionId");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDefVersionDependency",
                table: "Wf_FlowDefVersionDependency",
                columns: new[] { "TenantId", "FlowDefVersionId", "NodeId", "DependencyType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormDefVersion_FormDefId",
                table: "Wf_FormDefVersion",
                column: "FormDefId");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormDefVersion",
                table: "Wf_FormDefVersion",
                columns: new[] { "TenantId", "FormDefId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormDefVersion_OneDraft",
                table: "Wf_FormDefVersion",
                columns: new[] { "TenantId", "FormDefId", "Status" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormDraft_FormDefId",
                table: "Wf_FormDraft",
                column: "FormDefId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormDraft_FormDefVersionId",
                table: "Wf_FormDraft",
                column: "FormDefVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormDraft_Owner",
                table: "Wf_FormDraft",
                columns: new[] { "TenantId", "OwnerUserId", "Status", "ModifyDate" });

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormDraft_Legacy",
                table: "Wf_FormDraft",
                columns: new[] { "TenantId", "LegacyFlowInstanceId" },
                unique: true,
                filter: "[LegacyFlowInstanceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormFlowBinding_FlowDefId",
                table: "Wf_FormFlowBinding",
                column: "FlowDefId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FormFlowBinding_FormDefId",
                table: "Wf_FormFlowBinding",
                column: "FormDefId");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FormFlowBinding_Active",
                table: "Wf_FormFlowBinding",
                columns: new[] { "TenantId", "FormDefId" },
                unique: true,
                filter: "[Enable] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wf_FlowDefVersionDependency");

            migrationBuilder.DropTable(
                name: "Wf_FormDraft");

            migrationBuilder.DropTable(
                name: "Wf_FormFlowBinding");

            migrationBuilder.DropTable(
                name: "Wf_FlowDefVersion");

            migrationBuilder.DropTable(
                name: "Wf_FormDefVersion");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FormData_VersionSubmitted",
                table: "Wf_FormData");

            migrationBuilder.DropIndex(
                name: "UX_Wf_FormData_SubmissionKey",
                table: "Wf_FormData");

            migrationBuilder.DropIndex(
                name: "UX_Wf_FlowInstance_ActiveBusiness",
                table: "Wf_FlowInstance");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowFormTo_ActualParticipant",
                table: "Wf_FlowFormTo");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowFormTo_ExpectedParticipant",
                table: "Wf_FlowFormTo");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowFormTo_OnBehalfParticipant",
                table: "Wf_FlowFormTo");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowCc_Participant",
                table: "Wf_FlowCc");

            migrationBuilder.DropColumn(
                name: "FormDefVersionId",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "SubmissionKey",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "SubmittedAtUtc",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "FlowDefVersionId",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "FormDataId",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "FormDefVersionId",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "DetailRoute",
                table: "Wf_ApprovalBinding");

            migrationBuilder.AlterColumn<string>(
                name: "FormKey",
                table: "Wf_FlowDef",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
