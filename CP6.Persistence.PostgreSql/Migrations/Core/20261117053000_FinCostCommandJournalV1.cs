using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117053000_FinCostCommandJournalV1")]
public sealed class FinCostCommandJournalV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- The immutable ledger identity resolves the complete Finance scope.
        CREATE TABLE cp6_fin_mfg_v1.finance_command_bindings (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, command_id uuid NOT NULL,
            original_principal text NOT NULL CHECK (char_length(original_principal) BETWEEN 1 AND 128
                AND original_principal !~ '[[:cntrl:]]'),
            method text NOT NULL CHECK (method IN ('POST','IN_PROCESS')), canonical_route text NOT NULL,
            body_digest bytea NOT NULL, expected_digest bytea NOT NULL, accepted_at timestamptz,
            PRIMARY KEY (tenant_id,ledger_id,command_id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers,
            CHECK (octet_length(body_digest)=32 AND octet_length(expected_digest)=32)
        );

        -- The original native receipt only proves its own digest and recorded time.
        INSERT INTO cp6_fin_mfg_v1.finance_command_bindings
            (tenant_id,ledger_id,command_id,original_principal,method,canonical_route,body_digest,expected_digest)
        SELECT tenant_id,ledger_id,command_id,principal::text,method,route,body_digest,expected_digest
            FROM cp6_fin_mfg_v1.standard_document_commands;

        ALTER TABLE cp6_fin_mfg_v1.standard_document_commands
            ADD COLUMN r2_bound boolean NOT NULL DEFAULT false,
            ADD FOREIGN KEY (tenant_id,ledger_id,command_id) REFERENCES cp6_fin_mfg_v1.finance_command_bindings;

        -- Previous application versions still reserve the same slot through their existing insert.
        CREATE FUNCTION cp6_fin_mfg_v1.bind_native_standard_command() RETURNS trigger
            LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            INSERT INTO cp6_fin_mfg_v1.finance_command_bindings
                (tenant_id,ledger_id,command_id,original_principal,method,canonical_route,body_digest,expected_digest)
            VALUES (NEW.tenant_id,NEW.ledger_id,NEW.command_id,NEW.principal::text,
                NEW.method,NEW.route,NEW.body_digest,NEW.expected_digest);
            RETURN NEW;
        END; $$;
        CREATE TRIGGER bind_native_standard_command BEFORE INSERT ON cp6_fin_mfg_v1.standard_document_commands
            FOR EACH ROW WHEN (NOT NEW.r2_bound) EXECUTE FUNCTION cp6_fin_mfg_v1.bind_native_standard_command();

        CREATE TABLE cp6_fin_mfg_v1.finance_command_receipts (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, command_id uuid NOT NULL,
            receipt bytea NOT NULL, result bytea NOT NULL,
            PRIMARY KEY (tenant_id,ledger_id,command_id),
            FOREIGN KEY (tenant_id,ledger_id,command_id) REFERENCES cp6_fin_mfg_v1.finance_command_bindings
        );
        CREATE TRIGGER finance_command_binding_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.finance_command_bindings
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER finance_command_receipt_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.finance_command_receipts
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER finance_command_bindings_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.finance_command_bindings
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER finance_command_receipts_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.finance_command_receipts
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("制造成本命令登记只支持前向迁移。");
}
