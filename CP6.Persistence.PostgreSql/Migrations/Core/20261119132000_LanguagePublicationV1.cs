using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261119132000_LanguagePublicationV1")]
public sealed class LanguagePublicationV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        CREATE SCHEMA cp6_lang_v1;
        ALTER TABLE public."Sys_Langs"
            ADD COLUMN "LanguageRevision" uuid NOT NULL DEFAULT gen_random_uuid(),
            ADD COLUMN "EverReviewed" boolean NOT NULL DEFAULT false;
        UPDATE public."Sys_Langs" SET "EverReviewed" = true WHERE "Status" = 'reviewed';

        CREATE TABLE cp6_lang_v1.tenant_entries (
            "TenantId" uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),
            "Id" integer GENERATED ALWAYS AS IDENTITY,
            "LangKey" varchar(200) NOT NULL,
            "Status" varchar(20) NOT NULL CHECK ("Status" IN ('draft', 'reviewed')),
            "ZhCN" varchar(500), "ZhTW" varchar(500), "En" varchar(500), "Ja" varchar(500), "Ko" varchar(500),
            "LanguageRevision" uuid NOT NULL DEFAULT gen_random_uuid(),
            "EverReviewed" boolean NOT NULL DEFAULT false,
            PRIMARY KEY ("TenantId", "Id"),
            UNIQUE ("TenantId", "LangKey")
        );

        CREATE FUNCTION cp6_lang_v1.revise_entry() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            NEW."LanguageRevision" := gen_random_uuid();
            IF TG_OP = 'INSERT' THEN
                NEW."EverReviewed" := NEW."Status" = 'reviewed';
            ELSE
                NEW."EverReviewed" := OLD."EverReviewed" OR OLD."Status" = 'reviewed' OR NEW."Status" = 'reviewed';
            END IF;
            RETURN NEW;
        END $$;
        CREATE TRIGGER lang_global_revision BEFORE INSERT OR UPDATE ON public."Sys_Langs"
            FOR EACH ROW EXECUTE FUNCTION cp6_lang_v1.revise_entry();
        CREATE TRIGGER lang_tenant_revision BEFORE INSERT OR UPDATE ON cp6_lang_v1.tenant_entries
            FOR EACH ROW EXECUTE FUNCTION cp6_lang_v1.revise_entry();

        CREATE TRIGGER lang_global_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_Langs"
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER lang_tenant_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_lang_v1.tenant_entries
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();

        CREATE TABLE cp6_lang_v1.entry_events (
            event_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            tenant_id uuid, actor_id uuid NOT NULL, action text NOT NULL,
            entry_id integer NOT NULL, entry_revision uuid NOT NULL, reason text NOT NULL,
            recorded_at_utc timestamptz NOT NULL DEFAULT clock_timestamp()
        );

        CREATE TABLE cp6_lang_v1.releases (
            version text PRIMARY KEY, manifest bytea NOT NULL
        );
        CREATE TABLE cp6_lang_v1.pointer (
            singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
            revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
            version text REFERENCES cp6_lang_v1.releases(version)
        );
        INSERT INTO cp6_lang_v1.pointer DEFAULT VALUES;

        CREATE TRIGGER lang_entry_audit_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_lang_v1.entry_events
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER lang_release_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_lang_v1.releases
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER lang_pointer_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_lang_v1.pointer
            FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        """);

    protected override void Down(MigrationBuilder migration)
        => throw new NotSupportedException("语言词条版本、发布和审计账本只允许前向迁移。");
}
