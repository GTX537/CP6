using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Msbb.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class V1_BaseTables_And_SeedData_Fix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "InsUsrID",
                table: "T_SYS_MENU",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DelFlg",
                table: "M_GENERAL",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InsDate",
                table: "M_GENERAL",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "InsUsrID",
                table: "M_GENERAL",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdDate",
                table: "M_GENERAL",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdUsrID",
                table: "M_GENERAL",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "M_GENERAL",
                keyColumns: new[] { "ClassCode", "Code" },
                keyValues: new object[] { "UNIT", "01" },
                columns: new[] { "DelFlg", "InsDate", "InsUsrID", "UpdDate", "UpdUsrID" },
                values: new object[] { false, new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8696), "SYSTEM", null, null });

            migrationBuilder.UpdateData(
                table: "M_GENERAL",
                keyColumns: new[] { "ClassCode", "Code" },
                keyValues: new object[] { "UNIT", "02" },
                columns: new[] { "DelFlg", "InsDate", "InsUsrID", "UpdDate", "UpdUsrID" },
                values: new object[] { false, new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8699), "SYSTEM", null, null });

            migrationBuilder.UpdateData(
                table: "M_GENERAL",
                keyColumns: new[] { "ClassCode", "Code" },
                keyValues: new object[] { "UNIT", "03" },
                columns: new[] { "DelFlg", "InsDate", "InsUsrID", "UpdDate", "UpdUsrID" },
                values: new object[] { false, new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8700), "SYSTEM", null, null });

            migrationBuilder.InsertData(
                table: "M_GENERAL",
                columns: new[] { "ClassCode", "Code", "DelFlg", "DisplayOrder", "InsDate", "InsUsrID", "Name", "NumValue1", "UpdDate", "UpdUsrID", "Value1" },
                values: new object[,]
                {
                    { "DATA_STATUS", "0", false, 10, new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8701), "SYSTEM", "未登録/新規", null, null, null, null },
                    { "DATA_STATUS", "1", false, 20, new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8702), "SYSTEM", "承認待ち", null, null, null, null },
                    { "DATA_STATUS", "9", false, 30, new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8703), "SYSTEM", "承認済/確定", null, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "T_SYS_MENU",
                columns: new[] { "FunctionNO", "MajorCategoryNO", "DelFlg", "DisplayOrder", "FunctionID", "FunctionName", "InsDate", "InsUsrID", "MajorCategoryName", "UpdDate", "UpdUsrID", "Url" },
                values: new object[,]
                {
                    { 10, 10, false, 10, "MSBBPA010", "見積計算書入力", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8575), "SYSTEM", "見積・报价", null, null, "/estimate/input" },
                    { 20, 10, false, 20, "MSBBPA020", "見積計算書一覧照会", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8577), "SYSTEM", "見積・报价", null, null, "/estimate/list" },
                    { 30, 10, false, 30, "MSBBPA030", "御見積書登録・発行", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8579), "SYSTEM", "見積・报价", null, null, "/quote/input" },
                    { 40, 10, false, 40, "MSBBPA040", "御見積書一覧照会", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8581), "SYSTEM", "見積・报价", null, null, "/quote/list" },
                    { 10, 20, false, 10, "MSBBPA070", "受注入力", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8583), "SYSTEM", "受注管理", null, null, "/order/input" },
                    { 20, 20, false, 20, "MSBBPA080", "受注一覧照会", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8584), "SYSTEM", "受注管理", null, null, "/order/list" },
                    { 30, 20, false, 30, "MSBBPA090", "単価訂正", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8586), "SYSTEM", "受注管理", null, null, "/order/price-correction" },
                    { 10, 30, false, 10, "MSBBPA050", "製品マスタ", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8588), "SYSTEM", "マスタ管理", null, null, "/master/product-input" },
                    { 20, 30, false, 20, "MSBBPA060", "製品マスタ一覧照会", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8589), "SYSTEM", "マスタ管理", null, null, "/master/product-list" },
                    { 30, 30, false, 30, "MSBBPA110", "取引先マスタ", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8591), "SYSTEM", "マスタ管理", null, null, "/master/bp-input" },
                    { 40, 30, false, 40, "MSBBPA120", "取引先マスタ一覧照会", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8593), "SYSTEM", "マスタ管理", null, null, "/master/bp-list" },
                    { 50, 30, false, 50, "MSBBPA130", "シート単価マスタ", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8594), "SYSTEM", "マスタ管理", null, null, "/master/sheet-price" },
                    { 60, 30, false, 60, "MSBBPA140", "版型・木型マスタ", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8596), "SYSTEM", "マスタ管理", null, null, "/master/die-input" },
                    { 70, 30, false, 70, "MSBBPA150", "版型・木型マスタ一覧照会", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8598), "SYSTEM", "マスタ管理", null, null, "/master/die-list" },
                    { 10, 99, false, 10, "MSBBPA100", "FSCチェックシート出力", new DateTime(2025, 11, 27, 22, 45, 16, 924, DateTimeKind.Local).AddTicks(8599), "SYSTEM", "帳票出力", null, null, "/report/fsc-output" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "M_GENERAL",
                keyColumns: new[] { "ClassCode", "Code" },
                keyValues: new object[] { "DATA_STATUS", "0" });

            migrationBuilder.DeleteData(
                table: "M_GENERAL",
                keyColumns: new[] { "ClassCode", "Code" },
                keyValues: new object[] { "DATA_STATUS", "1" });

            migrationBuilder.DeleteData(
                table: "M_GENERAL",
                keyColumns: new[] { "ClassCode", "Code" },
                keyValues: new object[] { "DATA_STATUS", "9" });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 10, 10 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 20, 10 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 30, 10 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 40, 10 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 10, 20 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 20, 20 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 30, 20 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 10, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 20, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 30, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 40, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 50, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 60, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 70, 30 });

            migrationBuilder.DeleteData(
                table: "T_SYS_MENU",
                keyColumns: new[] { "FunctionNO", "MajorCategoryNO" },
                keyValues: new object[] { 10, 99 });

            migrationBuilder.DropColumn(
                name: "DelFlg",
                table: "M_GENERAL");

            migrationBuilder.DropColumn(
                name: "InsDate",
                table: "M_GENERAL");

            migrationBuilder.DropColumn(
                name: "InsUsrID",
                table: "M_GENERAL");

            migrationBuilder.DropColumn(
                name: "UpdDate",
                table: "M_GENERAL");

            migrationBuilder.DropColumn(
                name: "UpdUsrID",
                table: "M_GENERAL");

            migrationBuilder.AlterColumn<string>(
                name: "InsUsrID",
                table: "T_SYS_MENU",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);
        }
    }
}
