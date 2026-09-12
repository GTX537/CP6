using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CP6.Core.EFDbContext;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CP6Context))]
    [Migration("20260912071606_CrmErpIntegration")]
    public partial class CrmErpIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Core db-init is the sole migration owner; the queue context only dispatches.
            foreach (var operation in new ErpIntegration.ErpIntegrationQueue().UpOperations)
                migrationBuilder.Operations.Add(operation);
            migrationBuilder.AddColumn<Guid>(
                name: "CrmAccountId",
                table: "T_WebBusinessPartner",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFrozen",
                table: "T_WebBusinessPartner",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedContentSha256",
                table: "T_Quotation",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCd",
                table: "T_Quotation",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerAcceptanceReference",
                table: "T_Quotation",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerAcceptedAtUtc",
                table: "T_Quotation",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerAcceptedBy",
                table: "T_Quotation",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrderDeliveryDate",
                table: "T_Quotation",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderType",
                table: "T_Quotation",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ValidUntilUtc",
                table: "T_Quotation",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrmAccountId",
                table: "T_Order",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrmOpportunityId",
                table: "T_Order",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrmQuotationId",
                table: "T_Order",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CrmRequestId",
                table: "T_Order",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrmRequestSha256",
                table: "T_Order",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CrmRequestVersion",
                table: "T_Order",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WebBusinessPartner_TenantId_CrmAccountId",
                table: "T_WebBusinessPartner",
                columns: new[] { "TenantId", "CrmAccountId" },
                unique: true,
                filter: "[CrmAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_T_Order_TenantId_CrmOpportunityId",
                table: "T_Order",
                columns: new[] { "TenantId", "CrmOpportunityId" },
                unique: true,
                filter: "[CrmOpportunityId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("C03 commerce evidence and order provenance are forward-only.");
    }
}
