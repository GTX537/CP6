using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261019114500_AuthenticationInteractionsV1")]
public sealed class AuthenticationInteractionsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_authentication_v1.interactions (
          completion_id uuid PRIMARY KEY, challenge_hash text UNIQUE NOT NULL,
          proof jsonb NOT NULL, purpose text NOT NULL,
          state text NOT NULL CHECK (state IN ('pending','completing','completed','rejected')),
          lease uuid, preparation_hash text, factor jsonb, created_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_authentication_v1.consumed_totp (
          user_id uuid NOT NULL, secret_hash text NOT NULL, counter bigint NOT NULL,
          completion_id uuid NOT NULL REFERENCES cp6_authentication_v1.interactions(completion_id),
          PRIMARY KEY(user_id,secret_hash,counter));
        CREATE TABLE cp6_authentication_v1.preparation_versions (user_id uuid PRIMARY KEY, revision uuid NOT NULL);
        INSERT INTO cp6_authentication_v1.preparation_versions SELECT "Id", gen_random_uuid() FROM public."Sys_Users";
        CREATE FUNCTION cp6_authentication_v1.track_preparation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' OR NEW."TwoFactorSecret" IS DISTINCT FROM OLD."TwoFactorSecret" THEN
            INSERT INTO cp6_authentication_v1.preparation_versions(user_id,revision) VALUES (NEW."Id",gen_random_uuid())
            ON CONFLICT(user_id) DO UPDATE SET revision=EXCLUDED.revision;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER cp6_authentication_preparation AFTER INSERT OR UPDATE ON public."Sys_Users"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.track_preparation();
        REVOKE ALL ON FUNCTION cp6_authentication_v1.track_preparation() FROM PUBLIC;
        CREATE TABLE cp6_authentication_v1.session_evidence (
          refresh_hash text PRIMARY KEY, access_jti text UNIQUE NOT NULL,
          completion_id uuid NOT NULL REFERENCES cp6_authentication_v1.interactions(completion_id),
          evidence jsonb NOT NULL, rejected_at timestamptz);
        CREATE TABLE cp6_authentication_v1.sso_states (
          state_hash text PRIMARY KEY, policy jsonb NOT NULL, expires_at timestamptz NOT NULL,
          consumed_at timestamptz, client_kind text, client_binding text);
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Authentication receipts require forward recovery.");
}
