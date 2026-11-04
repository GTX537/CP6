using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261104063000_PersonalFormLegacyAdapterV1")]
public sealed class PersonalFormLegacyAdapterV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_personal_form_v2.legacy_commands(
          command_id uuid PRIMARY KEY REFERENCES cp6_personal_form_v2.commands(id),
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
          operation text COLLATE "C" NOT NULL,resource_key text COLLATE "C" NOT NULL,
          legacy_key text COLLATE "C" NOT NULL CHECK(length(legacy_key) BETWEEN 1 AND 100),
          mapping_version text NOT NULL CHECK(mapping_version='FORM-LEGACY-ADAPTER/1'),
          request_hash text NOT NULL,raw_request bytea NOT NULL,
          UNIQUE(tenant_id,environment_id,actor_id,operation,resource_key,legacy_key));
        CREATE FUNCTION cp6_personal_form_v2.legacy_command_guard() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.command_id;
          IF c.id IS NULL OR c.completed OR c.writer_xid<>pg_current_xact_id()
            OR (c.tenant_id,c.environment_id,c.actor_id,c.operation,c.resource_key)
              IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.actor_id,NEW.operation,NEW.resource_key)
            THEN RAISE EXCEPTION 'FORM_LEGACY_COMMAND_WRITER_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER personal_legacy_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.legacy_commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_legacy_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.legacy_commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_legacy_guard BEFORE INSERT ON cp6_personal_form_v2.legacy_commands
          FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.legacy_command_guard();
        CREATE FUNCTION cp6_personal_form_v2.legacy_adapter_ready() RETURNS boolean
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('personal_legacy_root','cp6_authentication_v1.before_change()',62),
            ('personal_legacy_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('personal_legacy_guard','cp6_personal_form_v2.legacy_command_guard()',7)) a(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_personal_form_v2.legacy_commands'::regclass
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ
              AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgdeferrable));
        $$;
        REVOKE ALL ON cp6_personal_form_v2.legacy_commands FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.legacy_command_guard(),cp6_personal_form_v2.legacy_adapter_ready() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Legacy command mappings require forward recovery.");
}
