using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261117110000_IdentitySourcePublicationV1")]
public sealed class IdentitySourcePublicationV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_identity_source_v1;
        CREATE TABLE cp6_identity_source_v1.registrations(
          tenant_id uuid PRIMARY KEY, revision bigint NOT NULL CHECK(revision>=0),
          binding_bytes bytea NOT NULL, binding_hash bytea NOT NULL CHECK(binding_hash=sha256(binding_bytes)));
        CREATE TABLE cp6_identity_source_v1.publications(
          tenant_id uuid NOT NULL REFERENCES cp6_identity_source_v1.registrations,
          revision bigint NOT NULL CHECK(revision>0), operation_hash bytea NOT NULL,
          operation_bytes bytea NOT NULL, input_bytes bytea NOT NULL,
          actor_id uuid NOT NULL, actor_tenant uuid NOT NULL, result_bytes bytea NOT NULL,
          PRIMARY KEY(tenant_id,operation_hash), UNIQUE(tenant_id,revision),
          CHECK(operation_hash=sha256(operation_bytes)));
        CREATE TABLE cp6_identity_source_v1.retired_browser_sessions(
          tenant_id uuid NOT NULL, family_id uuid NOT NULL, revision bigint NOT NULL,
          PRIMARY KEY(tenant_id,family_id),
          FOREIGN KEY(tenant_id,revision) REFERENCES cp6_identity_source_v1.publications(tenant_id,revision) DEFERRABLE INITIALLY DEFERRED);
        CREATE FUNCTION cp6_identity_source_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'C02_SOURCE_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_identity_source_v1.publications
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_identity_source_v1.publications
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_identity_source_v1.immutable();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_identity_source_v1.retired_browser_sessions
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_identity_source_v1.retired_browser_sessions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_identity_source_v1.immutable();
        CREATE TRIGGER no_delete BEFORE DELETE ON cp6_identity_source_v1.registrations
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_identity_source_v1.registrations
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_identity_source_v1.immutable();
        CREATE FUNCTION cp6_identity_source_v1.advance() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>0 THEN RAISE EXCEPTION 'C02_SOURCE_REVISION_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF NEW.tenant_id<>OLD.tenant_id OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'C02_SOURCE_REVISION_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER advance BEFORE INSERT OR UPDATE ON cp6_identity_source_v1.registrations
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.advance();
        -- 受限目录登录只能观察自己租户的来源摘要；控制连接仍由原 HTTP/Owner 权限门授权。
        CREATE FUNCTION cp6_identity_source_v1.read_hash(tenant uuid) RETURNS bytea LANGUAGE plpgsql
          SECURITY DEFINER SET search_path=pg_catalog AS $$
        DECLARE mapped uuid; owner_name name; result bytea; issuer_login boolean := false;
        BEGIN
          SELECT pg_get_userbyid(relowner) INTO owner_name FROM pg_class
            WHERE oid='cp6_identity_source_v1.registrations'::regclass;
          IF session_user<>owner_name THEN
            IF to_regclass('cp6_service_revocation_v1.installation') IS NOT NULL THEN
              EXECUTE 'SELECT EXISTS(SELECT FROM cp6_service_revocation_v1.installation WHERE issuer_name=$1 AND issuer_oid=$2)'
                INTO issuer_login USING session_user,session_user::regrole::oid;
            END IF;
            IF NOT issuer_login AND to_regclass('cp6_tenancy_v1.login_tenant') IS NOT NULL THEN
              EXECUTE 'SELECT cp6_tenancy_v1.current_tenant_id()' INTO mapped;
            END IF;
            IF NOT issuer_login AND mapped IS DISTINCT FROM tenant THEN
              RAISE EXCEPTION 'C02_SOURCE_READER_FORBIDDEN' USING ERRCODE='42501';
            END IF;
          END IF;
          SELECT binding_hash INTO result FROM cp6_identity_source_v1.registrations WHERE tenant_id=tenant FOR SHARE;
          RETURN result;
        END $$;
        CREATE FUNCTION cp6_identity_source_v1.read_publication(tenant uuid, version bigint) RETURNS bytea LANGUAGE plpgsql
          SECURITY DEFINER SET search_path=pg_catalog AS $$
        DECLARE result bytea;
        BEGIN
          PERFORM cp6_identity_source_v1.read_hash(tenant);
          SELECT result_bytes INTO result FROM cp6_identity_source_v1.publications
            WHERE tenant_id=tenant AND revision=version;
          RETURN result;
        END $$;
        -- 已批准切换后，旧宿主不能绕过来源握手再写新身份事实或签发凭据。
        CREATE FUNCTION cp6_identity_source_v1.require_writer() RETURNS trigger LANGUAGE plpgsql
          SECURITY DEFINER SET search_path=pg_catalog AS $$
        DECLARE tenant uuid; current_hash bytea; current_revision bigint; family uuid;
        BEGIN
          tenant := CASE WHEN TG_TABLE_NAME='CrmOidcGrant' THEN (to_jsonb(NEW)->>'OrganizationId')::uuid
                         ELSE (to_jsonb(NEW)->>'TenantId')::uuid END;
          SELECT binding_hash,revision INTO current_hash,current_revision
            FROM cp6_identity_source_v1.registrations WHERE tenant_id=tenant FOR SHARE;
          IF current_revision>0 AND current_setting('cp6_identity_source.t_'||replace(tenant::text,'-',''),true)
              IS DISTINCT FROM encode(current_hash,'hex') THEN
            RAISE EXCEPTION 'C02_SOURCE_NOT_APPROVED' USING ERRCODE='23514';
          END IF;
          IF current_revision>0 AND TG_TABLE_NAME='CrmOidcGrant' THEN
            SELECT r."BrowserSessionId" INTO family FROM public."Sys_RefreshTokens" r
              WHERE r."TenantId"=tenant AND r."UserId"=(to_jsonb(NEW)->>'SubjectId')::uuid
                AND r."TokenHash"::text=to_jsonb(NEW)->>'SourceRefreshHash';
            IF family IS NULL OR EXISTS(SELECT FROM cp6_identity_source_v1.retired_browser_sessions
                WHERE tenant_id=tenant AND family_id=family) THEN
              RAISE EXCEPTION 'C02_SOURCE_REAUTHENTICATE_REQUIRED' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER source_writer BEFORE INSERT OR UPDATE ON crm_identity."Snapshot"
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.require_writer();
        CREATE TRIGGER source_writer BEFORE INSERT ON crm_identity."ServiceToken"
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.require_writer();
        CREATE TRIGGER source_writer BEFORE INSERT ON public."CrmOidcGrant"
          FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.require_writer();
        CREATE FUNCTION cp6_identity_source_v1.require_publication() RETURNS trigger LANGUAGE plpgsql
          SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.revision>0 AND NOT EXISTS(SELECT FROM cp6_identity_source_v1.publications p
              WHERE p.tenant_id=NEW.tenant_id AND p.revision=NEW.revision
                AND convert_from(p.result_bytes,'UTF8')::jsonb->>'currentSourceSha256'=encode(NEW.binding_hash,'hex')
                AND convert_from(p.result_bytes,'UTF8')::jsonb->'current'=convert_from(NEW.binding_bytes,'UTF8')::jsonb) THEN
            RAISE EXCEPTION 'C02_SOURCE_PUBLICATION_REQUIRED' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER publication_required AFTER INSERT OR UPDATE ON cp6_identity_source_v1.registrations
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_identity_source_v1.require_publication();
        REVOKE ALL ON SCHEMA cp6_identity_source_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_identity_source_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_identity_source_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)
        => throw new NotSupportedException("身份来源版本、批准原件与原目录仅支持前向迁移。");
}
