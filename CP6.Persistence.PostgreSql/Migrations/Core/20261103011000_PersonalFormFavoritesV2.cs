using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261103011000_PersonalFormFavoritesV2")]
public sealed class PersonalFormFavoritesV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_personal_form_v2.favorites(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,form_key text COLLATE "C" NOT NULL,
          id uuid NOT NULL UNIQUE,head_id uuid NOT NULL,updated_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,actor_id,form_key),
          FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_form_authoring_v2.heads(tenant_id,environment_id,id));
        CREATE FUNCTION cp6_personal_form_v2.favorite_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r cp6_personal_form_v2.favorites;c cp6_personal_form_v2.commands;BEGIN
          IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'FAVORITE_ASSIGNMENT_REQUIRED' USING ERRCODE='23514';END IF;
          r=CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE tenant_id=r.tenant_id AND environment_id=r.environment_id
            AND actor_id=r.actor_id AND resource_key=r.form_key AND operation='FORM_FAVORITE_SET'
            AND writer_xid=pg_current_xact_id() AND NOT completed AND NOT closed;
          IF c.id IS NULL OR (convert_from(c.raw_body,'UTF8')::jsonb->'body'->>'on')::boolean IS DISTINCT FROM (TG_OP='INSERT')
            OR NOT EXISTS(SELECT FROM cp6_form_authoring_v2.heads h WHERE h.tenant_id=r.tenant_id AND h.environment_id=r.environment_id AND h.id=r.head_id AND h.form_key=r.form_key)
            THEN RAISE EXCEPTION 'FAVORITE_COMMAND_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN r;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.favorite_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands; f cp6_personal_form_v2.favorites; b jsonb;desired boolean;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.id;
          IF c.operation<>'FORM_FAVORITE_SET' OR c.closed THEN RETURN NULL;END IF;
          b=c.result_json::jsonb;desired=(convert_from(c.raw_body,'UTF8')::jsonb->'body'->>'on')::boolean;
          SELECT * INTO f FROM cp6_personal_form_v2.favorites WHERE tenant_id=c.tenant_id AND environment_id=c.environment_id AND actor_id=c.actor_id AND form_key=c.resource_key;
          IF NOT c.completed OR b->>'outcome' IS DISTINCT FROM 'SUCCEEDED' OR b->'result'->>'formKey' IS DISTINCT FROM c.resource_key
            OR (b->'result'->>'favorite')::boolean IS DISTINCT FROM desired OR desired IS DISTINCT FROM (f.id IS NOT NULL)
            OR desired AND ((b->>'resourceId')::uuid IS DISTINCT FROM f.id OR (b->'result'->>'updatedAtUtc')::timestamptz IS DISTINCT FROM f.updated_at)
            THEN RAISE EXCEPTION 'FAVORITE_RESULT_MISMATCH' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER favorite_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.favorites FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER favorite_no_truncate BEFORE TRUNCATE ON cp6_personal_form_v2.favorites FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER favorite_writer BEFORE INSERT OR UPDATE OR DELETE ON cp6_personal_form_v2.favorites FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.favorite_writer();
        CREATE CONSTRAINT TRIGGER favorite_complete AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.favorite_complete();
        CREATE FUNCTION cp6_personal_form_v2.favorites_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('favorites','favorite_root','cp6_authentication_v1.before_change()',62,false),
            ('favorites','favorite_no_truncate','cp6_scope_authorization_v1.immutable()',34,false),
            ('favorites','favorite_writer','cp6_personal_form_v2.favorite_writer()',31,false),
            ('commands','favorite_complete','cp6_personal_form_v2.favorite_complete()',21,true)) e(tab,name,fn,kind,deferred)
          WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_personal_form_v2.'||e.tab) AND t.tgname=e.name
            AND t.tgfoid=to_regprocedure(e.fn) AND t.tgenabled='O' AND t.tgtype=e.kind AND t.tgdeferrable=e.deferred AND t.tginitdeferred=e.deferred));
        $$;
        REVOKE ALL ON cp6_personal_form_v2.favorites FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.favorite_writer(),cp6_personal_form_v2.favorite_complete(),cp6_personal_form_v2.favorites_ready() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Favorite commands retain original results; use forward recovery.");
}
