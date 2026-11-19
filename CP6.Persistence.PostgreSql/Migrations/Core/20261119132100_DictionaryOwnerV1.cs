using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261119132100_DictionaryOwnerV1")]
public sealed class DictionaryOwnerV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE SCHEMA cp6_dict_v1;
        CREATE TABLE cp6_dict_v1.revisions (
            type_code text COLLATE public.cp6_ci_as_provider_v1 PRIMARY KEY,
            revision uuid NOT NULL DEFAULT gen_random_uuid()
        );
        INSERT INTO cp6_dict_v1.revisions(type_code)
            SELECT "TypeCode"::text FROM public."Sys_DictTypes"
            UNION ALL SELECT "TypeCode"::text FROM public."Sys_DictDatas"
            ON CONFLICT (type_code) DO NOTHING;

        CREATE FUNCTION cp6_dict_v1.revise() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF TG_OP <> 'INSERT' THEN
                INSERT INTO cp6_dict_v1.revisions(type_code) VALUES (OLD."TypeCode"::text)
                ON CONFLICT (type_code) DO UPDATE SET revision=gen_random_uuid();
            END IF;
            IF TG_OP <> 'DELETE' THEN
                INSERT INTO cp6_dict_v1.revisions(type_code) VALUES (NEW."TypeCode"::text)
                ON CONFLICT (type_code) DO UPDATE SET revision=gen_random_uuid();
            END IF;
            RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_dict_v1.revise_all() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN UPDATE cp6_dict_v1.revisions SET revision=gen_random_uuid(); RETURN NULL; END $$;

        CREATE TRIGGER dict_type_revision AFTER INSERT OR UPDATE OR DELETE ON public."Sys_DictTypes"
            FOR EACH ROW EXECUTE FUNCTION cp6_dict_v1.revise();
        CREATE TRIGGER dict_data_revision AFTER INSERT OR UPDATE OR DELETE ON public."Sys_DictDatas"
            FOR EACH ROW EXECUTE FUNCTION cp6_dict_v1.revise();
        CREATE TRIGGER dict_type_truncate AFTER TRUNCATE ON public."Sys_DictTypes"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_dict_v1.revise_all();
        CREATE TRIGGER dict_data_truncate AFTER TRUNCATE ON public."Sys_DictDatas"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_dict_v1.revise_all();
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("字典版本和原操作只允许前向迁移。");
}
