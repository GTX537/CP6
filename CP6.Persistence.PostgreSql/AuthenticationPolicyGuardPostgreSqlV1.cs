namespace CP6.Persistence.PostgreSql;

// Reuse the existing root before any identity row lock. Updating the root also rejects a
// stale Repeatable Read / Serializable snapshot. Revisions are authentication, not RBAC epochs.
internal static class AuthenticationPolicyGuardPostgreSqlV1
{
    internal const string Install = """
        CREATE SCHEMA cp6_authentication_v1;
        REVOKE ALL ON SCHEMA cp6_authentication_v1 FROM PUBLIC;
        CREATE FUNCTION cp6_authentication_v1.acquire()
        RETURNS bigint LANGUAGE sql VOLATILE SET search_path = pg_catalog
        AS 'SELECT cp6_platform_admin_guard_v1.acquire()';

        CREATE FUNCTION cp6_authentication_v1.before_change()
        RETURNS trigger LANGUAGE plpgsql VOLATILE SET search_path = pg_catalog AS $cp6$
        BEGIN
          PERFORM cp6_authentication_v1.acquire();
          RETURN NULL;
        END $cp6$;

        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_Users" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_Tenants" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON public."Sys_TenantSsoConfigs" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();

        CREATE FUNCTION cp6_authentication_v1.credential_revision()
        RETURNS trigger LANGUAGE plpgsql VOLATILE SET search_path = pg_catalog AS $cp6$
        BEGIN
          IF ROW(OLD."TenantId", OLD."Enable", OLD."Password", OLD."PasswordChangedAt", OLD."MustChangePassword",
                 OLD."AuthenticationEpoch", OLD."TwoFactorEnabled", OLD."TwoFactorEnrolledAt",
                 CASE WHEN OLD."TwoFactorEnabled" THEN OLD."TwoFactorSecret" END,
                 OLD."ExternalProvider", OLD."ExternalSubject", OLD."AllowPasswordFallback", OLD."QuickPinHash")
             IS DISTINCT FROM
             ROW(NEW."TenantId", NEW."Enable", NEW."Password", NEW."PasswordChangedAt", NEW."MustChangePassword",
                 NEW."AuthenticationEpoch", NEW."TwoFactorEnabled", NEW."TwoFactorEnrolledAt",
                 CASE WHEN NEW."TwoFactorEnabled" THEN NEW."TwoFactorSecret" END,
                 NEW."ExternalProvider", NEW."ExternalSubject", NEW."AllowPasswordFallback", NEW."QuickPinHash") THEN
            NEW."CredentialRevision" := gen_random_uuid();
          ELSE
            NEW."CredentialRevision" := OLD."CredentialRevision";
          END IF;
          RETURN NEW;
        END $cp6$;
        CREATE TRIGGER cp6_authentication_revision_v1 BEFORE UPDATE ON public."Sys_Users"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.credential_revision();

        CREATE FUNCTION cp6_authentication_v1.policy_revision()
        RETURNS trigger LANGUAGE plpgsql VOLATILE SET search_path = pg_catalog AS $cp6$
        BEGIN
          IF ROW(OLD."TwoFactorMode", OLD."QuickSwitchAllowed", OLD."Enable", OLD."ExpireDate")
             IS DISTINCT FROM ROW(NEW."TwoFactorMode", NEW."QuickSwitchAllowed", NEW."Enable", NEW."ExpireDate") THEN
            NEW."AuthenticationPolicyRevision" := gen_random_uuid();
          END IF;
          RETURN NEW;
        END $cp6$;
        CREATE TRIGGER cp6_authentication_revision_v1 BEFORE UPDATE ON public."Sys_Tenants"
          FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.policy_revision();

        CREATE FUNCTION cp6_authentication_v1.sso_revision()
        RETURNS trigger LANGUAGE plpgsql VOLATILE SET search_path = pg_catalog AS $cp6$
        BEGIN
          IF TG_OP = 'TRUNCATE' THEN
            UPDATE public."Sys_Tenants" SET "AuthenticationPolicyRevision" = gen_random_uuid();
            RETURN NULL;
          END IF;
          IF TG_OP != 'INSERT' THEN
            UPDATE public."Sys_Tenants" SET "AuthenticationPolicyRevision" = gen_random_uuid() WHERE "Id" = OLD."TenantId";
          END IF;
          IF TG_OP != 'DELETE' THEN
            UPDATE public."Sys_Tenants" SET "AuthenticationPolicyRevision" = gen_random_uuid() WHERE "Id" = NEW."TenantId";
          END IF;
          RETURN NULL;
        END $cp6$;
        CREATE TRIGGER cp6_authentication_sso_revision_v1 AFTER INSERT OR UPDATE OR DELETE
          ON public."Sys_TenantSsoConfigs" FOR EACH ROW EXECUTE FUNCTION cp6_authentication_v1.sso_revision();
        CREATE TRIGGER cp6_authentication_sso_truncate_v1 AFTER TRUNCATE
          ON public."Sys_TenantSsoConfigs" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.sso_revision();
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_authentication_v1 FROM PUBLIC;
        """;
}
