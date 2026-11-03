using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103120000_SfsTaskEditingV2")]
public sealed class SfsTaskEditingV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        DO $migration$ DECLARE f text;updated text;BEGIN
          SELECT pg_get_functiondef('cp6_personal_form_v2.runtime_guard()'::regprocedure) INTO f;
          updated=replace(f,'c.operation IN(''SFS_RETURNED_EDIT'',''SFS_RETURNED_RESUBMIT'')',
            'c.operation IN(''SFS_RETURNED_EDIT'',''SFS_RETURNED_RESUBMIT'',''SFS_TASK_DECISION'')');
          IF updated=f THEN RAISE EXCEPTION 'SFS_TASK_RUNTIME_MIGRATION_PRECONDITION';END IF;EXECUTE updated;
          SELECT pg_get_functiondef('cp6_personal_form_v2.complete()'::regprocedure) INTO f;
          updated=replace(f,'c.operation=''FORM_TASK_DECISION''','c.operation IN(''FORM_TASK_DECISION'',''SFS_TASK_DECISION'')');
          IF updated=f THEN RAISE EXCEPTION 'SFS_TASK_COMPLETE_MIGRATION_PRECONDITION';END IF;
          updated=replace(updated,'RETURN NEW;', $body$
            IF c.operation='SFS_TASK_DECISION' AND NOT EXISTS(
              SELECT FROM cp6_personal_form_v2.votes v JOIN public."Wf_FormData" f ON f."Id"=c.form_data_id
              WHERE v.command_id=c.id AND v.task_id=c.task_id AND f."TenantId"=c.tenant_id
                AND v.body::jsonb->>'afterData'=f."DataJson" AND v.body::jsonb->>'formDataVersion'=encode(f."RowVersion",'base64')
                AND v.body::jsonb->>'beforeData' IS NOT NULL AND v.body::jsonb->>'beforeFormDataVersion' IS NOT NULL
                AND c.result_json::jsonb#>>'{result,formDataId}'=f."Id"::text
                AND c.result_json::jsonb#>>'{result,formDataVersion}'=encode(f."RowVersion",'base64'))
              THEN RAISE EXCEPTION 'SFS_TASK_FORM_HISTORY_REQUIRED' USING ERRCODE='23514';END IF;
            RETURN NEW;
          $body$);EXECUTE updated;
          SELECT pg_get_functiondef('cp6_form_files_v2.binding_complete()'::regprocedure) INTO f;
          updated=replace(f,'''SFS_RETURNED_EDIT'',''SFS_RETURNED_RESUBMIT''','''SFS_RETURNED_EDIT'',''SFS_RETURNED_RESUBMIT'',''SFS_TASK_DECISION''');
          IF updated=f THEN RAISE EXCEPTION 'SFS_TASK_FILES_MIGRATION_PRECONDITION';END IF;EXECUTE updated;
          SELECT pg_get_functiondef('cp6_form_files_v2.binding_guard()'::regprocedure) INTO f;
          updated=replace(f,'f.scope_kind=''RETURNED''','f.scope_kind IN(''RETURNED'',''TASK'')');
          IF updated=f THEN RAISE EXCEPTION 'SFS_TASK_FILE_SCOPE_MIGRATION_PRECONDITION';END IF;EXECUTE updated;
        END $migration$;
        ALTER TABLE cp6_form_files_v2.contents DROP CONSTRAINT contents_scope_kind_check;
        ALTER TABLE cp6_form_files_v2.contents ADD CHECK(scope_kind IN('FORM','DRAFT','RETURNED','TASK'));
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("SFS decision originals and form edit history require forward recovery.");
}
