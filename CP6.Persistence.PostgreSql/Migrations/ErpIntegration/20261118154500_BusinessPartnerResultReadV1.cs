using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.ErpIntegration;

[DbContext(typeof(ErpIntegrationContext))]
[Migration("20261118154500_BusinessPartnerResultReadV1")]
public sealed class BusinessPartnerResultReadV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE erp_integration."BusinessPartnerResultReadV1" (
            tenant_id uuid NOT NULL, client_id varchar(128) NOT NULL, subject_id uuid NOT NULL,
            authentication_epoch uuid NOT NULL, source_epoch varchar(128) NOT NULL,
            account_id uuid NOT NULL, request_id uuid NOT NULL, request_version integer NOT NULL CHECK (request_version > 0),
            original_message_id varchar(128) NOT NULL, event_id varchar(128) NOT NULL,
            result_version integer NOT NULL CHECK (result_version > 0),
            payload_sha256 varchar(64) NOT NULL CHECK (payload_sha256 ~ '^[0-9a-f]{64}$'),
            valid_from timestamptz NOT NULL, valid_until timestamptz NOT NULL,
            revoked boolean NOT NULL DEFAULT false, CHECK (valid_from < valid_until),
            PRIMARY KEY (tenant_id, subject_id, authentication_epoch, event_id)
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("BusinessPartnerResultReadV1", "erp_integration");
}
