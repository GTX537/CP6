using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261026230000_UomTimeBasisV1")]
public sealed class UomTimeBasisV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_uom_time_v1;
        REVOKE ALL ON SCHEMA cp6_uom_time_v1 FROM PUBLIC;
        CREATE TABLE cp6_uom_time_v1.heads (
          tenant_id uuid NOT NULL,profile_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,profile_id),FOREIGN KEY(tenant_id,profile_id) REFERENCES cp6_uom_profile_v1.heads);
        CREATE TABLE cp6_uom_time_v1.versions (
          tenant_id uuid NOT NULL,profile_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),base_version bigint NOT NULL CHECK(base_version>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 16384),
          body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          actor_id uuid NOT NULL,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,profile_id,revision),
          FOREIGN KEY(tenant_id,profile_id) REFERENCES cp6_uom_time_v1.heads DEFERRABLE INITIALLY DEFERRED,
          FOREIGN KEY(tenant_id,profile_id,base_version) REFERENCES cp6_uom_profile_v1.versions(tenant_id,profile_id,revision));
        ALTER TABLE cp6_uom_time_v1.heads ADD CONSTRAINT time_basis_head_original
          FOREIGN KEY(tenant_id,profile_id,revision) REFERENCES cp6_uom_time_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_uom_time_v1.validate_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;
        BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'profileId' IS DISTINCT FROM NEW.profile_id::text
            OR b->>'version' IS DISTINCT FROM NEW.revision::text OR b#>>'{definition,baseRef,id}' IS DISTINCT FROM NEW.profile_id::text
            OR b#>>'{definition,baseRef,version}' IS DISTINCT FROM NEW.base_version::text
            OR b#>>'{definition,baseRef,owner}' IS DISTINCT FROM 'Main.Erp.UomProfile.v1'
            OR COALESCE(b#>>'{definition,basis}','') NOT IN ('SECOND','MINUTE','HOUR')
            OR jsonb_typeof(b#>'{definition,enabled}') IS DISTINCT FROM 'boolean'
            OR jsonb_typeof(b#>'{definition,reason}') IS DISTINCT FROM 'string'
            OR length(btrim(b#>>'{definition,reason}')) NOT BETWEEN 1 AND 500
            OR NOT EXISTS(SELECT FROM cp6_uom_time_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.profile_id=NEW.profile_id AND h.revision=NEW.revision)
            OR NOT EXISTS(SELECT FROM cp6_uom_profile_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.profile_id=NEW.profile_id
              AND v.revision=NEW.base_version AND v.body_digest=b#>>'{definition,baseRef,digest}'
              AND convert_from(v.body_bytes,'UTF8')::jsonb#>>'{profile,mode}'='BASE') THEN
            RAISE EXCEPTION 'TIME_BASIS_ORIGINAL_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER time_basis_original_valid BEFORE INSERT ON cp6_uom_time_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_uom_time_v1.validate_original();
        CREATE TRIGGER time_basis_head_monotonic BEFORE INSERT OR UPDATE ON cp6_uom_time_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_uom_profile_v1.advance_head();
        CREATE TRIGGER time_basis_head_no_delete BEFORE DELETE ON cp6_uom_time_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_uom_profile_v1.immutable();
        CREATE TRIGGER time_basis_head_no_truncate BEFORE TRUNCATE ON cp6_uom_time_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_uom_profile_v1.immutable();
        CREATE TRIGGER time_basis_original_immutable BEFORE UPDATE OR DELETE ON cp6_uom_time_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_uom_profile_v1.immutable();
        CREATE TRIGGER time_basis_original_no_truncate BEFORE TRUNCATE ON cp6_uom_time_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_uom_profile_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_uom_time_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_uom_time_v1 FROM PUBLIC;
        -- No guessed SEC/HOUR seed, original profile edits or role grants.
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Time basis history requires forward recovery.");
}
