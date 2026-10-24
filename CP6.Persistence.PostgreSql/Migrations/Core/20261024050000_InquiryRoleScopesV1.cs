using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261024050000_InquiryRoleScopesV1")]
public sealed class InquiryRoleScopesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_inquiry_scope_v1;
        CREATE TABLE cp6_inquiry_scope_v1.role_scopes (
          tenant_id uuid NOT NULL CHECK(tenant_id<>'00000000-0000-0000-0000-000000000000'),
          role_id integer NOT NULL, site_id uuid NOT NULL CHECK(site_id<>'00000000-0000-0000-0000-000000000000'),
          form_id uuid NOT NULL CHECK(form_id<>'00000000-0000-0000-0000-000000000000'),
          PRIMARY KEY(tenant_id,role_id,site_id,form_id),
          FOREIGN KEY(tenant_id,role_id) REFERENCES public."Sys_Roles"("TenantId","RoleId") ON DELETE CASCADE);
        -- One final check per role/transaction, irrespective of the number of replaced pairs.
        CREATE TABLE cp6_inquiry_scope_v1.pending_roles (
          transaction_id xid8 NOT NULL, tenant_id uuid NOT NULL, role_id integer NOT NULL,
          PRIMARY KEY(transaction_id,tenant_id,role_id));
        CREATE FUNCTION cp6_inquiry_scope_v1.changed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE tenant uuid; role integer;
        BEGIN
          IF TG_OP='UPDATE' OR TG_OP='TRUNCATE' THEN
            RAISE EXCEPTION 'IAM_INQUIRY_SCOPE_IMMUTABLE_PAIR' USING ERRCODE='55000';
          END IF;
          IF TG_OP='INSERT' THEN tenant=NEW.tenant_id; role=NEW.role_id;
          ELSE tenant=OLD.tenant_id; role=OLD.role_id; END IF;
          PERFORM cp6_authentication_v1.bump_admission_revision(tenant,'role:'||role::text);
          INSERT INTO cp6_inquiry_scope_v1.pending_roles VALUES(pg_current_xact_id(),tenant,role) ON CONFLICT DO NOTHING;
          RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_inquiry_scope_v1.require_snapshot() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE expected jsonb; source jsonb; source_type text; available boolean; present boolean;
        BEGIN
          SELECT EXISTS(SELECT FROM public."Sys_Roles" WHERE "TenantId"=NEW.tenant_id AND "RoleId"=NEW.role_id),
            EXISTS(SELECT FROM public."Sys_Roles" WHERE "TenantId"=NEW.tenant_id AND "RoleId"=NEW.role_id AND "Enable")
            INTO present,available;
          SELECT coalesce(jsonb_agg(jsonb_build_object('siteId',site_id::text,'formId',form_id::text) ORDER BY site_id,form_id),'[]'::jsonb)
            INTO expected FROM cp6_inquiry_scope_v1.role_scopes
            WHERE tenant_id=NEW.tenant_id AND role_id=NEW.role_id AND available;
          SELECT "PayloadJson"::jsonb,"EventType"::text INTO source,source_type FROM crm_identity."Snapshot"
            WHERE "TenantId"=NEW.tenant_id AND "AggregateId"=('permission:role:'||NEW.role_id::text)::bpchar;
          IF source IS NULL OR source_type<>'com.gtx537.platform.permission.changed.v3'
            OR source->'siteFormScopes' IS DISTINCT FROM expected
            OR source->'enabled' IS DISTINCT FROM to_jsonb(available)
            OR coalesce((source->>'deleted')::boolean,false) IS DISTINCT FROM NOT present THEN
            RAISE EXCEPTION 'IAM_INQUIRY_SCOPE_SNAPSHOT_REQUIRED' USING ERRCODE='23514';
          END IF;
          DELETE FROM cp6_inquiry_scope_v1.pending_roles
            WHERE transaction_id=NEW.transaction_id AND tenant_id=NEW.tenant_id AND role_id=NEW.role_id;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_inquiry_scope_v1.role_scopes FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_inquiry_scope_revision_v1 AFTER INSERT OR UPDATE OR DELETE
          ON cp6_inquiry_scope_v1.role_scopes FOR EACH ROW EXECUTE FUNCTION cp6_inquiry_scope_v1.changed();
        CREATE TRIGGER cp6_inquiry_scope_no_truncate_v1 BEFORE TRUNCATE
          ON cp6_inquiry_scope_v1.role_scopes FOR EACH STATEMENT EXECUTE FUNCTION cp6_inquiry_scope_v1.changed();
        CREATE CONSTRAINT TRIGGER cp6_inquiry_scope_commit_v1 AFTER INSERT ON cp6_inquiry_scope_v1.pending_roles
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_inquiry_scope_v1.require_snapshot();
        REVOKE ALL ON SCHEMA cp6_inquiry_scope_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_inquiry_scope_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_inquiry_scope_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Resource scope authority requires forward recovery.");
}
