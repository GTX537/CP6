using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117030000_FinStandardCostDocumentV1")]
public sealed class FinStandardCostDocumentV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_fin_mfg_v1.standard_cost_documents (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL, work_order_id uuid NOT NULL,
            current_version bigint CHECK (current_version>0), created_by uuid NOT NULL,
            created_at timestamptz NOT NULL DEFAULT clock_timestamp(), reason text NOT NULL,
            PRIMARY KEY (tenant_id,ledger_id,id),
            UNIQUE (tenant_id,ledger_id,work_order_id), UNIQUE (tenant_id,ledger_id,id,work_order_id),
            FOREIGN KEY (tenant_id,ledger_id,work_order_id) REFERENCES cp6_fin_mfg_v1.work_orders (tenant_id,ledger_id,work_order_id)
        );
        CREATE TABLE cp6_fin_mfg_v1.standard_document_versions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, document_id uuid NOT NULL, work_order_id uuid NOT NULL,
            version bigint NOT NULL CHECK (version>0), standard_basis_id uuid NOT NULL, plan_id uuid NOT NULL,
            accounting_date date NOT NULL, frozen_by uuid NOT NULL, frozen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            reason text NOT NULL,
            PRIMARY KEY (tenant_id,ledger_id,document_id,version),
            UNIQUE (tenant_id,ledger_id,document_id,standard_basis_id), UNIQUE (tenant_id,ledger_id,document_id,plan_id),
            FOREIGN KEY (tenant_id,ledger_id,document_id,work_order_id) REFERENCES cp6_fin_mfg_v1.standard_cost_documents
                (tenant_id,ledger_id,id,work_order_id),
            FOREIGN KEY (tenant_id,ledger_id,work_order_id,standard_basis_id) REFERENCES cp6_fin_mfg_v1.standard_basis_versions
        );
        ALTER TABLE cp6_fin_mfg_v1.standard_cost_documents ADD FOREIGN KEY (tenant_id,ledger_id,id,current_version)
            REFERENCES cp6_fin_mfg_v1.standard_document_versions;

        CREATE TABLE cp6_fin_mfg_v1.standard_collection_plans (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, document_id uuid NOT NULL, id uuid NOT NULL,
            work_order_id uuid NOT NULL, expected_version bigint, standard_basis_id uuid NOT NULL,
            accounting_date date NOT NULL, actor_id uuid NOT NULL, expires_at timestamptz NOT NULL, reason text NOT NULL,
            PRIMARY KEY (tenant_id,ledger_id,document_id,id),
            UNIQUE (tenant_id,ledger_id,document_id,id,standard_basis_id),
            FOREIGN KEY (tenant_id,ledger_id,document_id,work_order_id) REFERENCES cp6_fin_mfg_v1.standard_cost_documents
                (tenant_id,ledger_id,id,work_order_id),
            FOREIGN KEY (tenant_id,ledger_id,document_id,expected_version) REFERENCES cp6_fin_mfg_v1.standard_document_versions,
            FOREIGN KEY (tenant_id,ledger_id,work_order_id,standard_basis_id) REFERENCES cp6_fin_mfg_v1.standard_basis_versions
        );
        ALTER TABLE cp6_fin_mfg_v1.standard_document_versions
            ADD FOREIGN KEY (tenant_id,ledger_id,document_id,plan_id,standard_basis_id)
                REFERENCES cp6_fin_mfg_v1.standard_collection_plans (tenant_id,ledger_id,document_id,id,standard_basis_id);

        CREATE TABLE cp6_fin_mfg_v1.standard_document_commands (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, command_id uuid NOT NULL, receipt_id uuid NOT NULL,
            principal uuid NOT NULL, method text NOT NULL CHECK (method='POST'), route text NOT NULL, body_digest bytea NOT NULL,
            expected_digest bytea NOT NULL, operation text NOT NULL CHECK (operation IN ('CREATE','PREPARE','FREEZE')),
            document_id uuid NOT NULL, plan_id uuid, version bigint, recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,command_id), UNIQUE (tenant_id,ledger_id,receipt_id),
            FOREIGN KEY (tenant_id,ledger_id,document_id) REFERENCES cp6_fin_mfg_v1.standard_cost_documents,
            FOREIGN KEY (tenant_id,ledger_id,document_id,plan_id) REFERENCES cp6_fin_mfg_v1.standard_collection_plans,
            FOREIGN KEY (tenant_id,ledger_id,document_id,version) REFERENCES cp6_fin_mfg_v1.standard_document_versions,
            CHECK (octet_length(body_digest)=32 AND octet_length(expected_digest)=32),
            CHECK ((operation='CREATE' AND plan_id IS NULL AND version IS NULL)
                OR (operation='PREPARE' AND plan_id IS NOT NULL AND version IS NULL)
                OR (operation='FREEZE' AND plan_id IS NOT NULL AND version IS NOT NULL))
        );

        CREATE FUNCTION cp6_fin_mfg_v1.guard_standard_document_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP<>'UPDATE' OR (to_jsonb(NEW)-'current_version') IS DISTINCT FROM (to_jsonb(OLD)-'current_version')
                OR NEW.current_version IS DISTINCT FROM coalesce(OLD.current_version,0)+1 THEN
                RAISE EXCEPTION 'STANDARD_DOCUMENT_HEAD_MISMATCH' USING ERRCODE='55000';
            END IF;
            RETURN NEW;
        END; $$;
        CREATE TRIGGER standard_document_head_transition BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.standard_cost_documents
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.guard_standard_document_head();

        CREATE TRIGGER standard_document_version_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.standard_document_versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER standard_document_plan_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.standard_collection_plans
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER standard_document_command_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.standard_document_commands
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER standard_documents_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.standard_cost_documents
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER standard_document_versions_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.standard_document_versions
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER standard_document_plans_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.standard_collection_plans
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER standard_document_commands_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.standard_document_commands
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("独立标准成本单据只支持前向迁移。");
}
