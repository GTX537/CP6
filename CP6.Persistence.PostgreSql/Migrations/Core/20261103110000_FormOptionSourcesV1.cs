using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261103110000_FormOptionSourcesV1")]
public sealed class FormOptionSourcesV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_form_options_v1;
        -- Shared reference catalog: serialize source mutation with a consumer's final membership decision.
        -- BEFORE STATEMENT acquires the gate before row locks, including inserts and truncates.
        CREATE FUNCTION cp6_form_options_v1.writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN PERFORM pg_advisory_xact_lock(817287,1);RETURN NULL;END $$;
        CREATE TRIGGER cp6_form_options_writer BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_DictTypes"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_form_options_v1.writer();
        CREATE TRIGGER cp6_form_options_writer BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_DictDatas"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_form_options_v1.writer();
        CREATE FUNCTION cp6_form_options_v1.read_source() RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          PERFORM pg_advisory_xact_lock_shared(817287,1);
          RETURN (SELECT count(*)=2 FROM pg_trigger WHERE tgname='cp6_form_options_writer'
            AND tgrelid IN ('public."Sys_DictTypes"'::regclass,'public."Sys_DictDatas"'::regclass)
            AND tgfoid='cp6_form_options_v1.writer()'::regprocedure AND tgtype=62
            AND tgenabled IN ('O','A') AND tgqual IS NULL AND tgnargs=0);
        END $$;
        """);
    protected override void Down(MigrationBuilder m) => m.Sql("""
        DROP TRIGGER cp6_form_options_writer ON public."Sys_DictTypes";
        DROP TRIGGER cp6_form_options_writer ON public."Sys_DictDatas";
        DROP SCHEMA cp6_form_options_v1 CASCADE;
        """);
}
