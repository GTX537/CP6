using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261110080000_DeviceHeartbeatNonceV1")]
public sealed class DeviceHeartbeatNonceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_authentication_v1.device_heartbeat_nonces (
          tenant_id uuid NOT NULL,
          device_id text NOT NULL,
          nonce_hash bytea NOT NULL CHECK(octet_length(nonce_hash)=32),
          expires_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,device_id,nonce_hash));
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.device_heartbeat_nonces FOR EACH STATEMENT
          EXECUTE FUNCTION cp6_authentication_v1.before_change();
        REVOKE ALL ON cp6_authentication_v1.device_heartbeat_nonces FROM PUBLIC;

        CREATE OR REPLACE FUNCTION cp6_authentication_v1.track_device() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_authentication_v1.device_versions SET revision=gen_random_uuid(); RETURN NULL;
          END IF;
          IF TG_OP='UPDATE' AND (NEW."FullAuthExpiresAt" IS DISTINCT FROM OLD."FullAuthExpiresAt" OR NEW."QuickSwitchFailureCount">=5) THEN
            UPDATE cp6_authentication_v1.shared_windows SET invalidated_at=COALESCE(invalidated_at,clock_timestamp())
              WHERE tenant_id=OLD."TenantId" AND device_id=OLD."DeviceId";
          END IF;
          IF TG_OP='UPDATE' AND ROW(OLD."TenantId",OLD."DeviceId",OLD."Platform",OLD."Status",OLD."IsDeleted",OLD."PublicKey",OLD."DeviceMode",OLD."WarehouseCd",OLD."AreaCd")
            IS NOT DISTINCT FROM ROW(NEW."TenantId",NEW."DeviceId",NEW."Platform",NEW."Status",NEW."IsDeleted",NEW."PublicKey",NEW."DeviceMode",NEW."WarehouseCd",NEW."AreaCd") THEN RETURN NULL; END IF;
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
        -- Earlier scope edits did not change the registration revision. Existing native
        -- sessions must reauthenticate once; retain every device business field.
        UPDATE cp6_authentication_v1.device_versions SET revision=gen_random_uuid();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Heartbeat nonce authority requires forward recovery.");
}
