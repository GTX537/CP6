using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WfsFlowTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wf_FlowTrigger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TriggerType = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StarterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NextDueUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFiredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowTrigger", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_TriggerFire",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriggerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FiredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_TriggerFire", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTrigger_Event",
                table: "Wf_FlowTrigger",
                columns: new[] { "TenantId", "EventKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTrigger_Flow",
                table: "Wf_FlowTrigger",
                columns: new[] { "TenantId", "FlowKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTrigger_Scan",
                table: "Wf_FlowTrigger",
                columns: new[] { "Enabled", "TriggerType", "NextDueUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Wf_TriggerFire_Idem",
                table: "Wf_TriggerFire",
                columns: new[] { "TenantId", "TriggerId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wf_FlowTrigger");

            migrationBuilder.DropTable(
                name: "Wf_TriggerFire");
        }
    }
}
