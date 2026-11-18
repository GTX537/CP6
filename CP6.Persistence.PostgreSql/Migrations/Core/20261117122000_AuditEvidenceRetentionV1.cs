using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117122000_AuditEvidenceRetentionV1")]
public sealed class AuditEvidenceRetentionV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        ALTER TABLE public."Sys_OperLogs" ADD "AuditAtUtc" timestamptz, ADD "AuditEvidenceJson" jsonb;
        ALTER TABLE public."Sys_SecurityLogs" ADD "AuditAtUtc" timestamptz, ADD "AuditEvidenceJson" jsonb;
        ALTER TABLE public."Sys_FieldAuditLogs" ADD "AuditAtUtc" timestamptz, ADD "AuditEvidenceJson" jsonb;
        CREATE INDEX "IX_Sys_OperLogs_AuditOrder" ON public."Sys_OperLogs" ("TenantId", "CreateDate" DESC, "Id" DESC);
        CREATE INDEX "IX_Sys_SecurityLogs_AuditOrder" ON public."Sys_SecurityLogs" ("TenantId", "CreatedAt" DESC, "Id" DESC);
        CREATE INDEX "IX_Sys_FieldAuditLogs_AuditOrder" ON public."Sys_FieldAuditLogs" ("TenantId", "ChangedAt" DESC, "Id" DESC);

        CREATE SCHEMA cp6_audit_v1;
        CREATE FUNCTION cp6_audit_v1.define_capabilities(target_tenant uuid) RETURNS void LANGUAGE sql AS $seed$
          INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
          SELECT gen_random_uuid(),m."MenuId",c.action,c.title,0,target_tenant,LOCALTIMESTAMP
          FROM public."Sys_Menus" m
          CROSS JOIN (VALUES ('read-content','查看审计内容'),('export','导出审计'),('retention-manage','管理审计保留')) c(action,title)
          WHERE m."MenuKey" IN ('operlog','sys-security-log','sys-field-audit')
            AND (c.action!='retention-manage' OR m."MenuKey"='operlog')
          ON CONFLICT ("TenantId","MenuId","ActionCode") DO NOTHING;
        $seed$;
        SELECT cp6_audit_v1.define_capabilities("Id") FROM public."Sys_Tenants";
        CREATE FUNCTION cp6_audit_v1.define_new_tenant_capabilities() RETURNS trigger LANGUAGE plpgsql AS $seed$
        BEGIN
          PERFORM cp6_audit_v1.define_capabilities(NEW."Id");
          RETURN NEW;
        END
        $seed$;
        CREATE TRIGGER cp6_audit_v1_define_new_tenant_capabilities AFTER INSERT ON public."Sys_Tenants"
          FOR EACH ROW EXECUTE FUNCTION cp6_audit_v1.define_new_tenant_capabilities();
        CREATE TABLE cp6_audit_v1.policies (
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL, retention_class text NOT NULL CHECK (retention_class IN
            ('TECH_TELEMETRY','SECURITY_EVENT','FIELD_HISTORY','OWNER_RESULT_EVIDENCE','REPLAY_AUDIT','TECHNICAL_CONTENT_EVIDENCE','PRIVACY_EXECUTION_EVIDENCE')),
          version uuid NOT NULL UNIQUE, policy_ref uuid NOT NULL, basis_ref uuid NOT NULL, owner_id uuid NOT NULL,
          enabled boolean NOT NULL, worker_enabled boolean NOT NULL, duration_days integer NOT NULL CHECK (duration_days BETWEEN 1 AND 36500),
          legacy_zone text CHECK (legacy_zone IS NULL OR retention_class = 'TECH_TELEMETRY'),
          disposal_method text NOT NULL CHECK (disposal_method IN ('DELETE','ANONYMIZE')),
          redactable_fields text[] NOT NULL DEFAULT ARRAY[]::text[],
          retention_anchor text NOT NULL DEFAULT 'AUDIT_AT' CHECK (retention_anchor = 'AUDIT_AT'),
          CHECK ((disposal_method = 'ANONYMIZE') = (cardinality(redactable_fields) > 0)),
          at timestamptz NOT NULL DEFAULT clock_timestamp()
        );
        CREATE INDEX policy_current ON cp6_audit_v1.policies(tenant_id, retention_class, sequence DESC);
        CREATE TABLE cp6_audit_v1.safeguards (
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL, retention_class text NOT NULL, native_id text NOT NULL, reference_id uuid NOT NULL,
          kind text NOT NULL CHECK (kind IN ('HOLD','UNKNOWN','PROCESSING','RESCHEDULING','DISPUTE','REQUIRED_REFERENCE')),
          active boolean NOT NULL, version uuid NOT NULL UNIQUE, owner_id uuid NOT NULL, at timestamptz NOT NULL DEFAULT clock_timestamp()
        );
        CREATE INDEX safeguard_current ON cp6_audit_v1.safeguards(tenant_id, retention_class, native_id, kind, reference_id, sequence DESC);
        CREATE TABLE cp6_audit_v1.plans (
          tenant_id uuid NOT NULL, id uuid NOT NULL, retention_class text NOT NULL,
          actor_id uuid, actual_actor_id uuid, service text,
          mode text NOT NULL CHECK (mode IN ('EXPLICIT','LEGACY','WORKER')),
          policy_version uuid NOT NULL REFERENCES cp6_audit_v1.policies(version),
          cutoff timestamptz NOT NULL, planned_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
          safeguard_sequence bigint NOT NULL, privacy_sequence bigint NOT NULL, candidates jsonb NOT NULL,
          PRIMARY KEY(tenant_id,id), CHECK ((actor_id IS NULL) <> (service IS NULL))
        );
        CREATE UNIQUE INDEX legacy_original ON cp6_audit_v1.plans(tenant_id,actor_id,actual_actor_id) NULLS NOT DISTINCT WHERE mode='LEGACY';
        CREATE INDEX worker_original ON cp6_audit_v1.plans(tenant_id,retention_class,planned_at DESC) WHERE mode='WORKER';
        CREATE TABLE cp6_audit_v1.receipts (
          tenant_id uuid NOT NULL, plan_id uuid NOT NULL, state text NOT NULL CHECK (state IN ('COMPLETED','BLOCKED','EXPIRED')),
          reason text NOT NULL, affected integer NOT NULL CHECK (affected>=0), at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,plan_id), FOREIGN KEY(tenant_id,plan_id) REFERENCES cp6_audit_v1.plans(tenant_id,id)
        );
        CREATE TABLE cp6_audit_v1.exports (
          tenant_id uuid NOT NULL, id uuid NOT NULL, actor_id uuid NOT NULL, purpose_ref uuid NOT NULL,
          evidence_class text NOT NULL, policy_version text NOT NULL, permission_version text NOT NULL,
          expires_at timestamptz NOT NULL, native_keys jsonb NOT NULL, sha256 text NOT NULL,
          PRIMARY KEY(tenant_id,id)
        );
        REVOKE ALL ON SCHEMA cp6_audit_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_audit_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_audit_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder m)
        => throw new NotSupportedException("审计原证据和处置回执只允许前向迁移。");
}
