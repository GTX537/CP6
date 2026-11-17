using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117063000_FinManufacturingCostMasterV1")]
public sealed class FinManufacturingCostMasterV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_fin_mfg_v1.qualifications DROP CONSTRAINT qualifications_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualifications ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release',
                'policy-review','policy-release','rate-basis-review','rate-basis-release'));
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions DROP CONSTRAINT qualification_decisions_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release',
                'policy-review','policy-release','rate-basis-review','rate-basis-release'));

        CREATE TABLE cp6_fin_mfg_v1.cost_master_versions (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, id uuid NOT NULL,
            kind text NOT NULL CHECK (kind IN ('POLICY','RATE_BASIS')),
            root_id uuid NOT NULL, sequence bigint NOT NULL CHECK (sequence>0), predecessor_id uuid,
            maker uuid NOT NULL, product_id uuid, body jsonb NOT NULL,
            submitted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,id),
            FOREIGN KEY (tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers,
            FOREIGN KEY (tenant_id,ledger_id,predecessor_id) REFERENCES cp6_fin_mfg_v1.cost_master_versions,
            CHECK ((sequence=1 AND id=root_id AND predecessor_id IS NULL)
                OR (sequence>1 AND id<>root_id AND predecessor_id IS NOT NULL)),
            CHECK ((kind='RATE_BASIS')=(product_id IS NOT NULL))
        );
        CREATE TABLE cp6_fin_mfg_v1.cost_master_approvals (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, version_id uuid NOT NULL,
            id uuid NOT NULL, reviewer uuid NOT NULL, qualification_id uuid NOT NULL, reason text NOT NULL,
            reviewed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,version_id), UNIQUE (tenant_id,ledger_id,id),
            FOREIGN KEY (tenant_id,ledger_id,version_id) REFERENCES cp6_fin_mfg_v1.cost_master_versions
        );
        CREATE TABLE cp6_fin_mfg_v1.cost_master_releases (
            tenant_id uuid NOT NULL, ledger_id uuid NOT NULL, version_id uuid NOT NULL,
            id uuid NOT NULL, publisher uuid NOT NULL, qualification_id uuid NOT NULL, reason text NOT NULL,
            released_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY (tenant_id,ledger_id,version_id), UNIQUE (tenant_id,ledger_id,id),
            FOREIGN KEY (tenant_id,ledger_id,version_id) REFERENCES cp6_fin_mfg_v1.cost_master_approvals
        );
        CREATE INDEX cost_master_predecessor ON cp6_fin_mfg_v1.cost_master_versions (tenant_id,ledger_id,predecessor_id)
            WHERE predecessor_id IS NOT NULL;

        CREATE TRIGGER cost_master_version_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.cost_master_versions
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER cost_master_approval_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.cost_master_approvals
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER cost_master_release_immutable BEFORE UPDATE OR DELETE ON cp6_fin_mfg_v1.cost_master_releases
            FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER cost_master_versions_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.cost_master_versions
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER cost_master_approvals_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.cost_master_approvals
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();
        CREATE TRIGGER cost_master_releases_keep BEFORE TRUNCATE ON cp6_fin_mfg_v1.cost_master_releases
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source();

        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('cost-master-view','查询成本政策和费率依据'),('policy-submit','登记成本政策'),
            ('policy-review','复核成本政策'),('policy-release','发布成本政策'),
            ('rate-basis-submit','登记费率依据'),('rate-basis-review','复核费率依据'),('rate-basis-release','发布费率依据')) a(code,name)
        WHERE m."MenuKey"='fin-cost' AND NOT EXISTS (
            SELECT 1 FROM public."Sys_MenuAction" e WHERE e."TenantId"=t."Id" AND e."MenuId"=m."MenuId" AND e."ActionCode"=a.code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("制造成本政策与费率依据只支持前向迁移。");
}
