using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261126160000_EngineeringRevisionLineageV1")]
public sealed class EngineeringRevisionLineageV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_engineering_revision_v1.rebases (
          tenant_id uuid NOT NULL,revision_id uuid NOT NULL,revision_version bigint NOT NULL CHECK(revision_version>1),
          operation_id uuid NOT NULL,journal text NOT NULL DEFAULT 'Main.Plm.EngineeringRevision.v1' CHECK(journal='Main.Plm.EngineeringRevision.v1'),
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 8388608),
          digest text COLLATE "C" NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          CHECK(convert_from(body,'UTF8')::jsonb->>'operationId' IS NOT DISTINCT FROM operation_id::text),
          PRIMARY KEY(tenant_id,revision_id,revision_version),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,revision_id,revision_version) REFERENCES cp6_engineering_revision_v1.working_versions,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TRIGGER engineering_rebase_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_revision_v1.rebases
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_engineering_revision_v1.immutable();

        CREATE TABLE cp6_engineering_revision_v1.legacy_references (
          tenant_id uuid NOT NULL,id uuid NOT NULL,product_id uuid NOT NULL,
          source_repo_sha text COLLATE "C" NOT NULL CHECK(source_repo_sha ~ '^[0-9a-f]{40}$'),
          source_record_ref text COLLATE "C" NOT NULL CHECK(octet_length(source_record_ref) BETWEEN 1 AND 1024),
          operation_id uuid NOT NULL,journal text NOT NULL DEFAULT 'Main.Plm.EngineeringLegacyReference.v1' CHECK(journal='Main.Plm.EngineeringLegacyReference.v1'),
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 131072),
          digest text COLLATE "C" NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          CHECK(convert_from(body,'UTF8')::jsonb#>>'{registration,operationId}' IS NOT DISTINCT FROM operation_id::text),
          CHECK(convert_from(body,'UTF8')::jsonb#>>'{registration,product,id}' IS NOT DISTINCT FROM product_id::text),
          CHECK(convert_from(body,'UTF8')::jsonb#>>'{registration,sourceRepositorySha}' IS NOT DISTINCT FROM source_repo_sha),
          CHECK(convert_from(body,'UTF8')::jsonb#>>'{registration,sourceRecordRef}' IS NOT DISTINCT FROM source_record_ref),
          PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,operation_id),UNIQUE(tenant_id,product_id,source_repo_sha,source_record_ref),
          FOREIGN KEY(tenant_id,product_id) REFERENCES cp6_engineering_product_v1.heads(tenant_id,product_id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TRIGGER engineering_legacy_reference_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_revision_v1.legacy_references
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_engineering_revision_v1.immutable();

        -- 仅登记动作定义。角色授权仍由当前租户管理员分配。
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'legacy-register','登记历史工程来源',3,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='plm-engineering-revision' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='legacy-register');
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("工程修订谱系和历史登记通过前向迁移修复。");
}
