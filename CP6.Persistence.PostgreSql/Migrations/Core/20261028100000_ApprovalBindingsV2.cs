using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028100000_ApprovalBindingsV2")]
public sealed class ApprovalBindingsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_approval_binding_v2;
        CREATE TABLE cp6_approval_binding_v2.registrations (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,biz_type text NOT NULL,subject_kind text NOT NULL,
          owner text NOT NULL CHECK(length(owner) BETWEEN 1 AND 100),revision uuid NOT NULL,
          snapshot_schema text NOT NULL,snapshot_fields text NOT NULL,enabled boolean NOT NULL,
          service_principal uuid NOT NULL,audience text NOT NULL,fixed_route text NOT NULL,evidence_mode text NOT NULL CHECK(evidence_mode IN('REAL','DEMO')),
          PRIMARY KEY(tenant_id,environment_id,biz_type,subject_kind));
        CREATE TABLE cp6_approval_binding_v2.heads (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,biz_type text NOT NULL,subject_kind text NOT NULL,
          owner text NOT NULL,owner_id uuid NOT NULL,owner_dept_id uuid,row_version bigint NOT NULL CHECK(row_version>0),
          timeline_id uuid NOT NULL,revision_id uuid NOT NULL,revision_number integer NOT NULL CHECK(revision_number>0),
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,biz_type,subject_kind));
        CREATE TABLE cp6_approval_binding_v2.revisions (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,binding_id uuid NOT NULL,id uuid NOT NULL,
          revision_number integer NOT NULL,registration_revision uuid NOT NULL,body text NOT NULL,actor_id uuid NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,binding_id,id),UNIQUE(tenant_id,environment_id,binding_id,revision_number),
          FOREIGN KEY(tenant_id,environment_id,binding_id) REFERENCES cp6_approval_binding_v2.heads(tenant_id,environment_id,id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE cp6_approval_binding_v2.timelines (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,binding_id uuid NOT NULL,id uuid NOT NULL,
          timeline_no bigint NOT NULL,previous_id uuid,body text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,binding_id,id),UNIQUE(tenant_id,environment_id,binding_id,timeline_no),
          FOREIGN KEY(tenant_id,environment_id,binding_id) REFERENCES cp6_approval_binding_v2.heads(tenant_id,environment_id,id) DEFERRABLE INITIALLY DEFERRED,
          FOREIGN KEY(tenant_id,environment_id,binding_id,previous_id) REFERENCES cp6_approval_binding_v2.timelines(tenant_id,environment_id,binding_id,id));
        CREATE TABLE cp6_approval_binding_v2.intervals (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,binding_id uuid NOT NULL,timeline_id uuid NOT NULL,
          starts_at timestamptz NOT NULL,ends_at timestamptz,revision_id uuid,state text NOT NULL,
          CHECK(ends_at IS NULL OR ends_at>starts_at),CHECK((state='ACTIVE' AND revision_id IS NOT NULL) OR (state='DISABLED' AND revision_id IS NULL)),
          PRIMARY KEY(tenant_id,environment_id,binding_id,timeline_id,starts_at),
          FOREIGN KEY(tenant_id,environment_id,binding_id,timeline_id) REFERENCES cp6_approval_binding_v2.timelines(tenant_id,environment_id,binding_id,id),
          FOREIGN KEY(tenant_id,environment_id,binding_id,revision_id) REFERENCES cp6_approval_binding_v2.revisions(tenant_id,environment_id,binding_id,id));
        ALTER TABLE cp6_approval_binding_v2.heads ADD FOREIGN KEY(tenant_id,environment_id,id,timeline_id)
          REFERENCES cp6_approval_binding_v2.timelines(tenant_id,environment_id,binding_id,id) DEFERRABLE INITIALLY DEFERRED,
          ADD FOREIGN KEY(tenant_id,environment_id,id,revision_id) REFERENCES cp6_approval_binding_v2.revisions(tenant_id,environment_id,binding_id,id) DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_approval_binding_v2.commands (
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,command_id uuid NOT NULL,
          binding_id uuid NOT NULL,operation text NOT NULL,digest text NOT NULL,raw_body bytea NOT NULL,result_json text NOT NULL,evidence_json text NOT NULL,recorded_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,actor_id,command_id),
          FOREIGN KEY(tenant_id,environment_id,binding_id) REFERENCES cp6_approval_binding_v2.heads(tenant_id,environment_id,id));
        CREATE FUNCTION cp6_approval_binding_v2.head_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'BINDING_HISTORY_REQUIRED' USING ERRCODE='23514'; END IF;
          IF TG_OP='UPDATE' AND (NEW.row_version<>OLD.row_version+1 OR ROW(NEW.tenant_id,NEW.environment_id,NEW.id,NEW.biz_type,NEW.subject_kind,NEW.owner,NEW.owner_id,NEW.owner_dept_id)
            IS DISTINCT FROM ROW(OLD.tenant_id,OLD.environment_id,OLD.id,OLD.biz_type,OLD.subject_kind,OLD.owner,OLD.owner_id,OLD.owner_dept_id)) THEN
            RAISE EXCEPTION 'BINDING_CAS_REQUIRED' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT FROM cp6_approval_binding_v2.timelines t WHERE t.tenant_id=NEW.tenant_id AND t.environment_id=NEW.environment_id
              AND t.binding_id=NEW.id AND t.id=NEW.timeline_id AND t.timeline_no=NEW.row_version
              AND (TG_OP='INSERT' AND t.previous_id IS NULL OR TG_OP='UPDATE' AND t.previous_id=OLD.timeline_id)) THEN
            RAISE EXCEPTION 'BINDING_TIMELINE_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER head_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_approval_binding_v2.heads FOR EACH ROW EXECUTE FUNCTION cp6_approval_binding_v2.head_guard();
        CREATE TRIGGER head_no_truncate BEFORE TRUNCATE ON cp6_approval_binding_v2.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_approval_binding_v2.head_guard();
        CREATE FUNCTION cp6_approval_binding_v2.interval_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_approval_binding_v2.heads h WHERE h.tenant_id=NEW.tenant_id AND h.environment_id=NEW.environment_id AND h.id=NEW.binding_id AND h.timeline_id=NEW.timeline_id)
            OR EXISTS(SELECT FROM cp6_approval_binding_v2.intervals i WHERE i.tenant_id=NEW.tenant_id AND i.environment_id=NEW.environment_id AND i.binding_id=NEW.binding_id AND i.timeline_id=NEW.timeline_id
              AND tstzrange(i.starts_at,i.ends_at,'[)') && tstzrange(NEW.starts_at,NEW.ends_at,'[)'))
            OR NOT EXISTS(SELECT FROM cp6_approval_binding_v2.timelines t CROSS JOIN LATERAL jsonb_array_elements(t.body::jsonb->'intervals') v
              WHERE t.tenant_id=NEW.tenant_id AND t.environment_id=NEW.environment_id AND t.binding_id=NEW.binding_id AND t.id=NEW.timeline_id
                AND (v->>'from')::timestamptz=NEW.starts_at AND (v->>'to')::timestamptz IS NOT DISTINCT FROM NEW.ends_at
                AND (v->>'revisionId')::uuid IS NOT DISTINCT FROM NEW.revision_id AND v->>'state'=NEW.state) THEN
            RAISE EXCEPTION 'BINDING_INTERVAL_INVALID' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER interval_root BEFORE INSERT ON cp6_approval_binding_v2.intervals FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER interval_guard BEFORE INSERT ON cp6_approval_binding_v2.intervals FOR EACH ROW EXECUTE FUNCTION cp6_approval_binding_v2.interval_guard();
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['registrations','heads'] LOOP
            EXECUTE format('CREATE TRIGGER binding_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_binding_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER binding_scope BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_binding_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change()',tbl);
          END LOOP;
          FOREACH tbl IN ARRAY ARRAY['revisions','timelines','intervals','commands'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_binding_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_approval_binding_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_workflow_authoring_v2.activation_ready() AND NOT EXISTS(SELECT FROM (
            SELECT tbl,name,fn,typ FROM (VALUES('registrations'),('heads')) a(tbl) CROSS JOIN (VALUES
              ('binding_root','cp6_authentication_v1.before_change()',62),('binding_scope','cp6_scope_authorization_v1.before_authority_change()',62)) b(name,fn,typ)
            UNION ALL SELECT tbl,'immutable','cp6_scope_authorization_v1.immutable()',58 FROM (VALUES('revisions'),('timelines'),('intervals'),('commands')) a(tbl)
            UNION ALL SELECT 'heads','head_guard','cp6_approval_binding_v2.head_guard()',31
            UNION ALL SELECT 'heads','head_no_truncate','cp6_approval_binding_v2.head_guard()',34
            UNION ALL SELECT 'intervals','interval_root','cp6_authentication_v1.before_change()',6
            UNION ALL SELECT 'intervals','interval_guard','cp6_approval_binding_v2.interval_guard()',7
          ) e WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_binding_v2.'||e.tbl) AND t.tgname=e.name
            AND t.tgfoid=to_regprocedure(e.fn) AND t.tgtype=e.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgisinternal))
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_approval_binding_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_approval_binding_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Approval binding history requires forward recovery.");
}
