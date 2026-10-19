using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019141000_BrowserAuthenticationRecoveryV1")]
public sealed class BrowserAuthenticationRecoveryV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_authentication_v1.browser_closures (
          challenge_hash text PRIMARY KEY, closed_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_authentication_v1.browser_rotations (
          challenge_hash text PRIMARY KEY, refresh_hash text UNIQUE NOT NULL REFERENCES cp6_authentication_v1.session_evidence(refresh_hash));
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_authentication_v1.browser_closures
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_authentication_v1.browser_rotations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_authentication_v1.require_open_browser_attempt() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT 1 FROM cp6_authentication_v1.browser_closures WHERE challenge_hash=NEW.challenge_hash)
            OR (TG_TABLE_NAME='interactions' AND EXISTS(SELECT 1 FROM cp6_authentication_v1.browser_rotations WHERE challenge_hash=NEW.challenge_hash))
            OR (TG_TABLE_NAME='browser_rotations' AND EXISTS(SELECT 1 FROM cp6_authentication_v1.interactions WHERE challenge_hash=NEW.challenge_hash)) THEN
            RAISE EXCEPTION 'Closed or already bound authentication attempt' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER cp6_browser_attempt_open BEFORE INSERT ON cp6_authentication_v1.interactions
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.require_open_browser_attempt();
        CREATE TRIGGER cp6_browser_attempt_open BEFORE INSERT ON cp6_authentication_v1.browser_rotations
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.require_open_browser_attempt();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Browser authentication closure requires forward recovery.");
}
