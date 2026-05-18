using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineAndOee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_Machine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MachineType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    WgCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    BaseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PlannedRunMinutesPerDay = table.Column<int>(type: "int", nullable: false),
                    StandardCycleSec = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    CapacityPerHour = table.Column<decimal>(type: "decimal(10,2)", nullable: true),
                    InstallDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMaintenanceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextMaintenanceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActiveFlg = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_Machine", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_MachineDowntime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DowntimeNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MachineCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DowntimeMinutes = table.Column<int>(type: "int", nullable: true),
                    DowntimeType = table.Column<int>(type: "int", nullable: false),
                    ReasonCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RecoveryOperatorCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_MachineDowntime", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "T_OeeDaily",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OeeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MachineCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PlannedRunMinutes = table.Column<int>(type: "int", nullable: false),
                    ActualRunMinutes = table.Column<int>(type: "int", nullable: false),
                    DowntimeMinutes = table.Column<int>(type: "int", nullable: false),
                    GoodQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    DefectQty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    Availability = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                    Performance = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                    Quality = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                    Oee = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_OeeDaily", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_BaseCd",
                table: "M_Machine",
                column: "BaseCd");

            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_MachineCd",
                table: "M_Machine",
                column: "MachineCd",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_ProcessCd_ActiveFlg",
                table: "M_Machine",
                columns: new[] { "ProcessCd", "ActiveFlg" });

            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_Status_ActiveFlg",
                table: "M_Machine",
                columns: new[] { "Status", "ActiveFlg" });

            migrationBuilder.CreateIndex(
                name: "IX_M_Machine_WgCd_ActiveFlg",
                table: "M_Machine",
                columns: new[] { "WgCd", "ActiveFlg" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_DowntimeNo",
                table: "T_MachineDowntime",
                column: "DowntimeNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_DowntimeType_IsDeleted",
                table: "T_MachineDowntime",
                columns: new[] { "DowntimeType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_MachineCd_EndTime",
                table: "T_MachineDowntime",
                columns: new[] { "MachineCd", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_MachineCd_StartTime",
                table: "T_MachineDowntime",
                columns: new[] { "MachineCd", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_T_MachineDowntime_WorkOrderNo",
                table: "T_MachineDowntime",
                column: "WorkOrderNo");

            migrationBuilder.CreateIndex(
                name: "IX_T_OeeDaily_MachineCd_OeeDate",
                table: "T_OeeDaily",
                columns: new[] { "MachineCd", "OeeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_T_OeeDaily_OeeDate_MachineCd",
                table: "T_OeeDaily",
                columns: new[] { "OeeDate", "MachineCd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M_Machine");

            migrationBuilder.DropTable(
                name: "T_MachineDowntime");

            migrationBuilder.DropTable(
                name: "T_OeeDaily");
        }
    }
}
