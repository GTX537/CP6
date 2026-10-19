namespace CP6.Persistence.PostgreSql;

// PostgreSQL-only, forward-installed protection for the enabled platform administrator set.
// Revision is a transaction serialization token, not the full IAM authority/authentication epoch.
internal static class PlatformAdminGuardPostgreSqlV1
{
    internal const string Install = """
        CREATE SCHEMA cp6_platform_admin_guard_v1;
        REVOKE ALL ON SCHEMA cp6_platform_admin_guard_v1 FROM PUBLIC;
        CREATE TABLE cp6_platform_admin_guard_v1.root (
          singleton boolean PRIMARY KEY CHECK (singleton),
          revision bigint NOT NULL CHECK (revision >= 0)
        );
        INSERT INTO cp6_platform_admin_guard_v1.root VALUES (true, 0);

        CREATE FUNCTION cp6_platform_admin_guard_v1.acquire()
        RETURNS bigint LANGUAGE plpgsql VOLATILE
        SET search_path = pg_catalog AS $cp6$
        DECLARE current_revision bigint;
        BEGIN
          UPDATE cp6_platform_admin_guard_v1.root SET revision = revision + 1
          WHERE singleton RETURNING revision INTO current_revision;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'PLATFORM_ADMIN_GUARD_UNAVAILABLE' USING ERRCODE = 'P0001';
          END IF;
          RETURN current_revision;
        END $cp6$;

        CREATE FUNCTION cp6_platform_admin_guard_v1.before_change()
        RETURNS trigger LANGUAGE plpgsql VOLATILE
        SET search_path = pg_catalog AS $cp6$
        BEGIN
          PERFORM cp6_platform_admin_guard_v1.acquire();
          IF TG_OP = 'TRUNCATE' AND EXISTS (
            SELECT 1 FROM public."Sys_Users" WHERE "Enable" AND "IsPlatformAdmin") THEN
            RAISE EXCEPTION 'E-SEC-037' USING ERRCODE = '23514',
              CONSTRAINT = 'cp6_platform_admin_survivor_v1';
          END IF;
          IF TG_LEVEL = 'ROW' THEN RETURN NEW; END IF;
          RETURN NULL;
        END $cp6$;

        CREATE FUNCTION cp6_platform_admin_guard_v1.after_change()
        RETURNS trigger LANGUAGE plpgsql VOLATILE
        SET search_path = pg_catalog AS $cp6$
        BEGIN
          IF EXISTS (SELECT 1 FROM cp6_old_platform_users WHERE "Enable" AND "IsPlatformAdmin")
            AND NOT EXISTS (SELECT 1 FROM public."Sys_Users" WHERE "Enable" AND "IsPlatformAdmin") THEN
            RAISE EXCEPTION 'E-SEC-037' USING ERRCODE = '23514',
              CONSTRAINT = 'cp6_platform_admin_survivor_v1';
          END IF;
          RETURN NULL;
        END $cp6$;

        CREATE TRIGGER cp6_platform_admin_update_gate_v1
          BEFORE UPDATE OF "Enable", "IsPlatformAdmin" ON public."Sys_Users"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_platform_admin_guard_v1.before_change();
        CREATE TRIGGER cp6_platform_admin_delete_gate_v1
          BEFORE DELETE OR TRUNCATE ON public."Sys_Users"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_platform_admin_guard_v1.before_change();
        CREATE TRIGGER cp6_platform_admin_insert_gate_v1
          BEFORE INSERT ON public."Sys_Users" FOR EACH ROW
          WHEN (NEW."Enable" AND NEW."IsPlatformAdmin")
          EXECUTE FUNCTION cp6_platform_admin_guard_v1.before_change();
        CREATE TRIGGER cp6_platform_admin_update_check_v1
          AFTER UPDATE ON public."Sys_Users" REFERENCING OLD TABLE AS cp6_old_platform_users
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_platform_admin_guard_v1.after_change();
        CREATE TRIGGER cp6_platform_admin_delete_check_v1
          AFTER DELETE ON public."Sys_Users" REFERENCING OLD TABLE AS cp6_old_platform_users
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_platform_admin_guard_v1.after_change();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_platform_admin_guard_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_platform_admin_guard_v1 FROM PUBLIC;
        """;
}
