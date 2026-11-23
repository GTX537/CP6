using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123132700_LegacyFormHistoryOriginV2")]
public sealed class LegacyFormHistoryOriginV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        -- 表重建或清空会更换存储代际。逻辑归档中保留的来源标记据此与目标库区分。
        CREATE FUNCTION public.legacy_form_history_origin_v2() RETURNS text
        LANGUAGE sql STABLE SET search_path = pg_catalog AS $origin$
            SELECT c.system_identifier::text || '/' || d.oid::text || '/' ||
                   'public."Wf_FlowInstance"'::regclass::oid::text || '/' ||
                   pg_relation_filenode('public."Wf_FlowInstance"'::regclass)::text
            FROM pg_control_system() AS c, pg_database AS d
            WHERE d.datname = current_database()
        $origin$;

        ALTER TABLE public."Wf_FlowInstance" ADD COLUMN legacy_query_insert_origin text;
        ALTER TABLE public."Wf_FlowInstance" ALTER COLUMN legacy_query_insert_xid DROP DEFAULT;

        -- 所有正常写入（含 COPY）记录当前来源与顶层事务。原行状态更新保留其写入标记。
        CREATE FUNCTION public.legacy_form_history_stamp_v2() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $stamp$
        BEGIN
            NEW.legacy_query_insert_origin := public.legacy_form_history_origin_v2();
            NEW.legacy_query_insert_xid := pg_current_xact_id();
            RETURN NEW;
        END;
        $stamp$;
        CREATE TRIGGER legacy_form_history_stamp_v2
        BEFORE INSERT ON public."Wf_FlowInstance"
        FOR EACH ROW EXECUTE FUNCTION public.legacy_form_history_stamp_v2();
        """);

    protected override void Down(MigrationBuilder m) => m.Sql("""
        DROP TRIGGER legacy_form_history_stamp_v2 ON public."Wf_FlowInstance";
        DROP FUNCTION public.legacy_form_history_stamp_v2();
        ALTER TABLE public."Wf_FlowInstance" DROP COLUMN legacy_query_insert_origin;
        ALTER TABLE public."Wf_FlowInstance" ALTER COLUMN legacy_query_insert_xid SET DEFAULT pg_current_xact_id();
        DROP FUNCTION public.legacy_form_history_origin_v2();
        """);
}
