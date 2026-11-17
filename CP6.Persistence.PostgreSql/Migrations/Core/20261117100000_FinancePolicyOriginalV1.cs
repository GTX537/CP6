using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117100000_FinancePolicyOriginalV1")]
public sealed class FinancePolicyOriginalV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_fin_mfg_v1.qualifications DROP CONSTRAINT qualifications_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualifications ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release',
                'policy-review','policy-release','rate-basis-review','rate-basis-release','accounting-policy-review',
                'accounting-policy-release','source-contract-source-review','source-contract-gl-review','source-contract-release'));
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions DROP CONSTRAINT qualification_decisions_action_check;
        ALTER TABLE cp6_fin_mfg_v1.qualification_decisions ADD CHECK
            (action IN ('rate-review','rate-release','center-review','center-release','basis-review','basis-release',
                'policy-review','policy-release','rate-basis-review','rate-basis-release','accounting-policy-review',
                'accounting-policy-release','source-contract-source-review','source-contract-gl-review','source-contract-release'));
        CREATE SCHEMA cp6_fin_policy_v1;
        CREATE TABLE cp6_fin_policy_v1.versions (
            tenant_id uuid NOT NULL,ledger_id uuid NOT NULL,id uuid NOT NULL,root_id uuid NOT NULL,
            version bigint NOT NULL CHECK(version>0),previous_id uuid,
            kind text NOT NULL CHECK(kind IN ('ACCOUNTING_POLICY','SOURCE_CONTRACT')),
            maker uuid NOT NULL,body jsonb NOT NULL,evidence jsonb NOT NULL,reason text NOT NULL,
            valid_from date NOT NULL,to_exclusive date,CHECK(to_exclusive IS NULL OR to_exclusive>valid_from),
            PRIMARY KEY(tenant_id,ledger_id,id),UNIQUE(tenant_id,ledger_id,root_id,version),
            FOREIGN KEY(tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers(tenant_id,id),
            FOREIGN KEY(tenant_id,ledger_id,previous_id) REFERENCES cp6_fin_policy_v1.versions
        );
        CREATE UNIQUE INDEX finance_policy_successor ON cp6_fin_policy_v1.versions(tenant_id,ledger_id,previous_id) WHERE previous_id IS NOT NULL;
        CREATE TABLE cp6_fin_policy_v1.controls (
            tenant_id uuid NOT NULL,ledger_id uuid NOT NULL,id uuid NOT NULL,
            state text NOT NULL CHECK(state IN ('SUBMITTED','APPROVED','PUBLISHED','REVOKED')),revision bigint NOT NULL CHECK(revision>0),
            PRIMARY KEY(tenant_id,ledger_id,id),FOREIGN KEY(tenant_id,ledger_id,id) REFERENCES cp6_fin_policy_v1.versions
        );
        CREATE TABLE cp6_fin_policy_v1.approvals (
            tenant_id uuid NOT NULL,ledger_id uuid NOT NULL,id uuid NOT NULL,role text NOT NULL CHECK(role IN ('SOURCE','GL')),
            actor uuid NOT NULL,qualification_id uuid NOT NULL,qualification_original text NOT NULL,evidence_original text NOT NULL,
            reason text NOT NULL,PRIMARY KEY(tenant_id,ledger_id,id,role),
            UNIQUE(tenant_id,ledger_id,id,actor),FOREIGN KEY(tenant_id,ledger_id,id) REFERENCES cp6_fin_policy_v1.versions
        );
        CREATE TABLE cp6_fin_policy_v1.originals (
            tenant_id uuid NOT NULL,ledger_id uuid NOT NULL,id uuid NOT NULL,reference jsonb NOT NULL,original text NOT NULL,
            publisher uuid NOT NULL,qualification_id uuid NOT NULL,
            PRIMARY KEY(tenant_id,ledger_id,id),FOREIGN KEY(tenant_id,ledger_id,id) REFERENCES cp6_fin_policy_v1.versions
        );
        CREATE TABLE cp6_fin_policy_v1.commands (
            tenant_id uuid NOT NULL,ledger_id uuid NOT NULL,command_id uuid NOT NULL,actor uuid NOT NULL,
            route text NOT NULL,request bytea NOT NULL,result text NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,ledger_id,command_id),FOREIGN KEY(tenant_id,ledger_id) REFERENCES cp6_fin_mfg_v1.ledgers(tenant_id,id)
        );
        DO $block$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['versions','approvals','originals','commands'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_fin_policy_v1.%I FOR EACH ROW EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source()',t);
            EXECUTE format('CREATE TRIGGER keep BEFORE TRUNCATE ON cp6_fin_policy_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_fin_mfg_v1.immutable_source()',t);
          END LOOP;
        END $block$;
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('accounting-source-view','查询会计政策和来源合同'),
            ('accounting-policy-submit','送审会计政策'),('accounting-policy-review','专业复核会计政策'),
            ('accounting-policy-release','发布会计政策'),('accounting-policy-revoke','撤销会计政策'),
            ('source-contract-submit','送审来源合同'),('source-contract-source-review','来源方复核合同'),
            ('source-contract-gl-review','GL方复核合同'),('source-contract-release','发布双方来源合同'),
            ('source-contract-revoke','撤销来源合同')) a(code,name)
        WHERE m."MenuKey"='fin-cost' AND NOT EXISTS (
            SELECT 1 FROM public."Sys_MenuAction" e WHERE e."TenantId"=t."Id" AND e."MenuId"=m."MenuId" AND e."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("专业会计政策及来源合同原件只支持前向迁移。");
}
