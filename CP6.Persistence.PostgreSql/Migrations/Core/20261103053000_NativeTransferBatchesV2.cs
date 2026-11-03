using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103053000_NativeTransferBatchesV2")]
public sealed class NativeTransferBatchesV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_native_tasks_v2.transfer_batches(
          id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
          command_key text COLLATE "C" NOT NULL,digest text COLLATE "C" NOT NULL,snapshot_json text NOT NULL,
          UNIQUE(tenant_id,environment_id,actor_id,command_key),
          CHECK(jsonb_array_length(snapshot_json::jsonb->'items') BETWEEN 0 AND 500),
          CHECK((snapshot_json::jsonb->>'batchId')::uuid=id));
        CREATE TABLE cp6_native_tasks_v2.transfer_rejections(
          batch_id uuid NOT NULL REFERENCES cp6_native_tasks_v2.transfer_batches(id),task_id uuid NOT NULL REFERENCES public."Wf_FlowTask"("Id"),
          error_code text NOT NULL,PRIMARY KEY(batch_id,task_id));
        CREATE FUNCTION cp6_native_tasks_v2.transfer_rejection_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND (OLD.batch_id,OLD.task_id) IS DISTINCT FROM (NEW.batch_id,NEW.task_id)
            OR NOT EXISTS(SELECT FROM cp6_native_tasks_v2.transfer_batches b,jsonb_array_elements(b.snapshot_json::jsonb->'items') i
              WHERE b.id=NEW.batch_id AND (i->>'taskId')::uuid=NEW.task_id)
            THEN RAISE EXCEPTION 'BATCH_ITEM_REQUIRED' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER transfer_batch_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.transfer_batches FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER transfer_batch_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.transfer_batches FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER transfer_rejection_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.transfer_rejections FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER transfer_rejection_guard BEFORE INSERT OR UPDATE ON cp6_native_tasks_v2.transfer_rejections FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.transfer_rejection_guard();
        CREATE FUNCTION cp6_native_tasks_v2.batch_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_native_tasks_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('transfer_batches','transfer_batch_root','cp6_authentication_v1.before_change()',62),
            ('transfer_batches','transfer_batch_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('transfer_rejections','transfer_rejection_root','cp6_authentication_v1.before_change()',62),
            ('transfer_rejections','transfer_rejection_guard','cp6_native_tasks_v2.transfer_rejection_guard()',23)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_native_tasks_v2.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgdeferrable));
        $$;
        REVOKE ALL ON cp6_native_tasks_v2.transfer_batches,cp6_native_tasks_v2.transfer_rejections FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_native_tasks_v2.transfer_rejection_guard(),cp6_native_tasks_v2.batch_ready() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward-only native transfer batch history.");
}
