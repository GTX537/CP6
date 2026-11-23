using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123065000_VersionedWorkCalendarV1")]
public sealed class VersionedWorkCalendarV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_work_calendar_v1;
        CREATE TABLE cp6_work_calendar_v1.revisions (
            tenant_id uuid NOT NULL,
            revision bigint NOT NULL CHECK(revision>0),
            recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            origin text NOT NULL CHECK(origin IN ('CUTOVER','CHANGE')),
            days jsonb NOT NULL CHECK(jsonb_typeof(days)='array'),
            PRIMARY KEY(tenant_id,revision));
        CREATE TABLE cp6_work_calendar_v1.time_zone_rules (
            zone_id text NOT NULL,
            version text NOT NULL CHECK(version ~ '^[0-9a-f]{64}$'),
            rules text NOT NULL CHECK(version=encode(sha256(convert_to(rules,'UTF8')),'hex')),
            PRIMARY KEY(zone_id,version));

        CREATE FUNCTION cp6_work_calendar_v1.snapshot(tenant uuid) RETURNS jsonb
        LANGUAGE sql VOLATILE SET search_path=pg_catalog AS $$
            SELECT coalesce(jsonb_agg(jsonb_build_object('date', "Date"::date, 'isWorkday', "IsWorkday", 'note', "Note")
                ORDER BY "Date"),'[]'::jsonb)
            FROM public."Sys_WorkCalendar" WHERE "TenantId"=tenant
        $$;

        -- 切换时只保存当前可证明的例外。空历的版本 0 表示固定默认周规则。
        INSERT INTO cp6_work_calendar_v1.revisions(tenant_id,revision,origin,days)
        SELECT DISTINCT "TenantId",1,'CUTOVER',cp6_work_calendar_v1.snapshot("TenantId")
        FROM public."Sys_WorkCalendar";

        -- 兼容路由、默认种子与新命令共用实际例外表。每条写语句按租户记录准确变化。
        CREATE FUNCTION cp6_work_calendar_v1.capture() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE tenants uuid[];tenant uuid;before_revision bigint;before_days jsonb;after_days jsonb;
        BEGIN
            IF TG_OP='INSERT' THEN SELECT array_agg(DISTINCT "TenantId") INTO tenants FROM new_days;
            ELSIF TG_OP='DELETE' THEN SELECT array_agg(DISTINCT "TenantId") INTO tenants FROM old_days;
            ELSE
                SELECT array_agg(id) INTO tenants FROM (
                    SELECT "TenantId" id FROM old_days UNION SELECT "TenantId" FROM new_days
                ) affected;
            END IF;
            FOREACH tenant IN ARRAY coalesce(tenants,ARRAY[]::uuid[]) LOOP
                SELECT revision,days INTO before_revision,before_days
                FROM cp6_work_calendar_v1.revisions WHERE tenant_id=tenant ORDER BY revision DESC LIMIT 1;
                after_days=cp6_work_calendar_v1.snapshot(tenant);
                IF after_days IS DISTINCT FROM coalesce(before_days,'[]'::jsonb) THEN
                    INSERT INTO cp6_work_calendar_v1.revisions(tenant_id,revision,origin,days)
                    VALUES(tenant,coalesce(before_revision,0)+1,'CHANGE',after_days);
                END IF;
            END LOOP;
            RETURN NULL;
        END $$;
        CREATE TRIGGER calendar_root BEFORE INSERT OR UPDATE OR DELETE ON public."Sys_WorkCalendar"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER calendar_insert AFTER INSERT ON public."Sys_WorkCalendar" REFERENCING NEW TABLE AS new_days
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_work_calendar_v1.capture();
        CREATE TRIGGER calendar_update AFTER UPDATE ON public."Sys_WorkCalendar" REFERENCING OLD TABLE AS old_days NEW TABLE AS new_days
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_work_calendar_v1.capture();
        CREATE TRIGGER calendar_delete AFTER DELETE ON public."Sys_WorkCalendar" REFERENCING OLD TABLE AS old_days
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_work_calendar_v1.capture();
        CREATE TRIGGER calendar_no_truncate BEFORE TRUNCATE ON public."Sys_WorkCalendar"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_work_calendar_v1.revisions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_work_calendar_v1.time_zone_rules
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();

        CREATE FUNCTION cp6_work_calendar_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT NOT EXISTS (
                SELECT FROM (VALUES ('calendar_root','cp6_authentication_v1.before_change()',30),
                    ('calendar_insert','cp6_work_calendar_v1.capture()',4),('calendar_update','cp6_work_calendar_v1.capture()',16),
                    ('calendar_delete','cp6_work_calendar_v1.capture()',8),('calendar_no_truncate','cp6_scope_authorization_v1.immutable()',34))
                  required(name,fn,kind)
                WHERE NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid='public."Sys_WorkCalendar"'::regclass
                  AND tgname=required.name AND tgfoid=to_regprocedure(required.fn) AND tgtype=required.kind
                  AND tgenabled IN ('O','A') AND NOT tgisinternal))
            AND NOT EXISTS (
                SELECT FROM (VALUES ('revisions'),('time_zone_rules')) a(tbl)
                WHERE NOT EXISTS(SELECT FROM pg_trigger WHERE tgrelid=to_regclass('cp6_work_calendar_v1.'||a.tbl)
                    AND tgname='immutable' AND tgfoid=to_regprocedure('cp6_scope_authorization_v1.immutable()')
                    AND tgtype=58 AND tgenabled IN ('O','A') AND NOT tgisinternal))
        $$;
        REVOKE ALL ON SCHEMA cp6_work_calendar_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_work_calendar_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_work_calendar_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("工作日历历史迁移只支持前滚。");
}
