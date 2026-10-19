using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019125500_AuthenticationContinuityV1")]
public sealed class AuthenticationContinuityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_authentication_v1.session_evidence ADD COLUMN parent_refresh_hash text
          REFERENCES cp6_authentication_v1.session_evidence(refresh_hash);
        CREATE INDEX session_evidence_parent ON cp6_authentication_v1.session_evidence(parent_refresh_hash);
        CREATE TABLE cp6_authentication_v1.access_sessions (
          access_jti text PRIMARY KEY, refresh_hash text NOT NULL REFERENCES cp6_authentication_v1.session_evidence(refresh_hash));
        INSERT INTO cp6_authentication_v1.access_sessions SELECT access_jti,refresh_hash FROM cp6_authentication_v1.session_evidence;
        CREATE TABLE cp6_authentication_v1.device_versions (
          tenant_id uuid NOT NULL, device_id text NOT NULL, revision uuid NOT NULL, PRIMARY KEY(tenant_id,device_id));
        CREATE TABLE cp6_authentication_v1.device_policy_versions (tenant_id uuid PRIMARY KEY, revision uuid NOT NULL);
        INSERT INTO cp6_authentication_v1.device_versions
          SELECT "TenantId","DeviceId",gen_random_uuid() FROM public."T_ClientDevice";
        INSERT INTO cp6_authentication_v1.device_policy_versions
          SELECT "Id",gen_random_uuid() FROM public."Sys_Tenants" ON CONFLICT DO NOTHING;
        CREATE TABLE cp6_authentication_v1.shared_windows (
          tenant_id uuid NOT NULL, device_id text NOT NULL, window_id uuid NOT NULL UNIQUE,
          completion_id uuid NOT NULL REFERENCES cp6_authentication_v1.interactions(completion_id),
          device_revision text NOT NULL, policy_version text NOT NULL, expires_at timestamptz NOT NULL, invalidated_at timestamptz,
          PRIMARY KEY(tenant_id,device_id));

        CREATE FUNCTION cp6_authentication_v1.track_device() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.device_versions SET revision=gen_random_uuid(); RETURN NULL;
          END IF;
          IF TG_OP='UPDATE' AND (NEW."FullAuthExpiresAt" IS DISTINCT FROM OLD."FullAuthExpiresAt" OR NEW."QuickSwitchFailureCount">=5) THEN
            UPDATE cp6_authentication_v1.shared_windows SET invalidated_at=COALESCE(invalidated_at,clock_timestamp())
              WHERE tenant_id=OLD."TenantId" AND device_id=OLD."DeviceId";
          END IF;
          IF TG_OP='UPDATE' AND ROW(OLD."TenantId",OLD."DeviceId",OLD."Platform",OLD."Status",OLD."IsDeleted",OLD."PublicKey",OLD."DeviceMode")
            IS NOT DISTINCT FROM ROW(NEW."TenantId",NEW."DeviceId",NEW."Platform",NEW."Status",NEW."IsDeleted",NEW."PublicKey",NEW."DeviceMode") THEN RETURN NULL; END IF;
          IF TG_OP<>'INSERT' THEN
            INSERT INTO cp6_authentication_v1.device_versions VALUES(OLD."TenantId",OLD."DeviceId",gen_random_uuid())
              ON CONFLICT(tenant_id,device_id) DO UPDATE SET revision=EXCLUDED.revision;
          END IF;
          IF TG_OP<>'DELETE' THEN
            INSERT INTO cp6_authentication_v1.device_versions VALUES(NEW."TenantId",NEW."DeviceId",gen_random_uuid())
              ON CONFLICT(tenant_id,device_id) DO UPDATE SET revision=EXCLUDED.revision;
          END IF;
          RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_authentication_v1.track_device_policy() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.device_policy_versions SET revision=gen_random_uuid(); RETURN NULL;
          END IF;
          IF TG_OP='UPDATE' AND ROW(OLD."TenantId",OLD."ProductionMoveEnabled",OLD."IsDeleted")
            IS NOT DISTINCT FROM ROW(NEW."TenantId",NEW."ProductionMoveEnabled",NEW."IsDeleted") THEN RETURN NULL; END IF;
          IF TG_OP<>'INSERT' THEN
            INSERT INTO cp6_authentication_v1.device_policy_versions VALUES(OLD."TenantId",gen_random_uuid())
              ON CONFLICT(tenant_id) DO UPDATE SET revision=EXCLUDED.revision;
          END IF;
          IF TG_OP<>'DELETE' THEN
            INSERT INTO cp6_authentication_v1.device_policy_versions VALUES(NEW."TenantId",gen_random_uuid())
              ON CONFLICT(tenant_id) DO UPDATE SET revision=EXCLUDED.revision;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_authentication_device_revision AFTER INSERT OR UPDATE OR DELETE ON public."T_ClientDevice"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.track_device();
        CREATE TRIGGER cp6_authentication_device_truncate AFTER TRUNCATE ON public."T_ClientDevice"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.track_device();
        CREATE TRIGGER cp6_authentication_device_policy_revision AFTER INSERT OR UPDATE OR DELETE ON public."T_WmsFeatureFlag"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.track_device_policy();
        CREATE TRIGGER cp6_authentication_device_policy_truncate AFTER TRUNCATE ON public."T_WmsFeatureFlag"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.track_device_policy();
        CREATE FUNCTION cp6_authentication_v1.close_revoked_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW."RevokedAt" IS NOT NULL AND OLD."RevokedAt" IS NULL
             AND NEW."ReplacedByTokenHash" IS NULL AND NEW."CompletionRejectedAtUtc" IS NULL THEN
            UPDATE cp6_authentication_v1.session_evidence SET rejected_at=COALESCE(rejected_at,clock_timestamp())
              WHERE completion_id=(SELECT completion_id FROM cp6_authentication_v1.session_evidence WHERE refresh_hash=NEW."TokenHash");
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER cp6_authentication_origin_revocation AFTER UPDATE ON public."Sys_RefreshTokens"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.close_revoked_origin();

        DO $$ DECLARE table_name text; BEGIN
          FOREACH table_name IN ARRAY ARRAY['T_ClientDevice','T_WmsFeatureFlag','Sys_RefreshTokens','Sys_BrowserSessions'] LOOP
            EXECUTE format('CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',table_name);
          END LOOP;
          FOREACH table_name IN ARRAY ARRAY['interactions','session_evidence','access_sessions','sso_states','consumed_totp','preparation_versions','device_versions','device_policy_versions','shared_windows'] LOOP
            EXECUTE format('CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_authentication_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',table_name);
          END LOOP;
        END $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Authentication continuity requires forward recovery.");
}
