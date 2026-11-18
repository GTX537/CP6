using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261117223000_FileDispositionV1")]
public sealed class FileDispositionV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE TABLE cp6_file_content_v1.dispositions (
          tenant_id uuid NOT NULL,file_id uuid NOT NULL,operation_id uuid NOT NULL,
          initial_state text NOT NULL CHECK(initial_state IN ('UNLINKED','DISPOSAL_PENDING')),storage_id uuid,
          PRIMARY KEY(tenant_id,file_id),UNIQUE(tenant_id,operation_id),UNIQUE(tenant_id,file_id,operation_id,initial_state),
          FOREIGN KEY(tenant_id,file_id) REFERENCES cp6_file_content_v1.contents,
          CHECK((storage_id IS NOT NULL)=(initial_state='DISPOSAL_PENDING')));
        CREATE TABLE cp6_file_content_v1.disposition_attempts (
          tenant_id uuid NOT NULL,file_id uuid NOT NULL,operation_id uuid NOT NULL,started_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,file_id,operation_id),
          initial_state text NOT NULL DEFAULT 'DISPOSAL_PENDING' CHECK(initial_state='DISPOSAL_PENDING'),
          FOREIGN KEY(tenant_id,file_id,operation_id,initial_state)
            REFERENCES cp6_file_content_v1.dispositions(tenant_id,file_id,operation_id,initial_state));
        CREATE TABLE cp6_file_content_v1.disposition_completions (
          tenant_id uuid NOT NULL,file_id uuid NOT NULL,operation_id uuid NOT NULL,deleted_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,file_id),
          FOREIGN KEY(tenant_id,file_id,operation_id) REFERENCES cp6_file_content_v1.disposition_attempts(tenant_id,file_id,operation_id));
        CREATE TRIGGER file_dispositions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_file_content_v1.dispositions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER file_disposition_attempts_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_file_content_v1.disposition_attempts
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER file_disposition_completions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_file_content_v1.disposition_completions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_file_content_v1.workflow_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS (SELECT FROM (VALUES
            ('draft_sets','file_draft_advance','cp6_file_content_v1.guard_draft_set()',19),
            ('draft_items','file_draft_items_guard','cp6_file_content_v1.guard_draft_item()',15),
            ('draft_items','file_draft_items_no_update','cp6_scope_authorization_v1.immutable()',50),
            ('draft_sets','file_draft_sets_permanent','cp6_scope_authorization_v1.immutable()',42),
            ('draft_bindings','file_draft_bindings_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('dispositions','file_dispositions_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('disposition_attempts','file_disposition_attempts_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('disposition_completions','file_disposition_completions_immutable','cp6_scope_authorization_v1.immutable()',58)
          ) expected(table_name,trigger_name,trigger_fn,kind)
          WHERE NOT EXISTS(SELECT FROM pg_trigger t
            WHERE t.tgrelid=to_regclass('cp6_file_content_v1.'||expected.table_name)
              AND t.tgname=expected.trigger_name AND t.tgfoid=to_regprocedure(expected.trigger_fn)
              AND t.tgtype=expected.kind AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON FUNCTION cp6_file_content_v1.workflow_ready() FROM PUBLIC;
        REVOKE ALL ON TABLE cp6_file_content_v1.dispositions,cp6_file_content_v1.disposition_attempts,cp6_file_content_v1.disposition_completions FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'delete','处置受控文件',3,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='file-content' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='delete');
        """);

    protected override void Down(MigrationBuilder migration) =>
        throw new NotSupportedException("文件处置墓碑及原回执需要前向恢复。");
}
