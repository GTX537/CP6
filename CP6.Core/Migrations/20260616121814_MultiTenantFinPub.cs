using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantFinPub : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Pub_GenTable",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Pub_GenColumn",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Pub_DocSequence",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Pub_Attachment",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_TaxCode",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_Sequence",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_Receipt",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_PostingRuleLine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_PostingRule",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_Payment",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_JournalLine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_JournalEntry",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_GlAccount",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_FiscalPeriod",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_CostSheetLine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_CostSheet",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_CostCenter",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_BankAccount",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_ArSettlement",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_ArInvoiceLine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_ArInvoice",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_ApSettlement",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_ApInvoiceLine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Fin_ApInvoice",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量数据可见
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Pub_GenTable");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Pub_GenColumn");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Pub_DocSequence");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Pub_Attachment");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_TaxCode");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_Sequence");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_Receipt");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_PostingRuleLine");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_PostingRule");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_Payment");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_JournalLine");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_JournalEntry");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_GlAccount");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_FiscalPeriod");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_CostSheetLine");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_CostSheet");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_CostCenter");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_BankAccount");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_ArSettlement");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_ArInvoiceLine");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_ArInvoice");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_ApSettlement");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_ApInvoiceLine");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Fin_ApInvoice");
        }
    }
}
