using System.Data;
using System.Text;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed record TenantLoginV1(Guid TenantId, string Login);

/// <summary>One control-owned directory for the explicitly supported shared-schema adapters.</summary>
internal static class TenantLoginDirectoryV1
{
    internal const string HelperOwner = "cp6_tenant_lookup_v1";
    internal const string DirectorySchema = "cp6_tenancy_v1";
    internal const int DirectoryLockNamespace = 1129330260;
    internal static void ValidateArguments(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> bindings)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(bindings);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || bindings.Count == 0 || bindings.Any(x => x is null || x.TenantId == Guid.Empty
                || string.IsNullOrWhiteSpace(x.Login) || x.Login.Any(char.IsControl) || Encoding.UTF8.GetByteCount(x.Login) > 63)
            || bindings.Select(x => x.TenantId).Distinct().Count() != bindings.Count
            || bindings.Select(x => x.Login).Distinct(StringComparer.Ordinal).Count() != bindings.Count)
            throw TenantStorageBindingV1.Rejected();
    }

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> bindings, CancellationToken ct)
    {
        ValidateArguments(connection, transaction, bindings);
        // CREATE deliberately rejects any pre-existing schema or role; never adopt unknown objects.
        await using var command = new NpgsqlCommand(InstallSql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        foreach (var binding in bindings)
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO cp6_tenancy_v1.login_tenant(role_oid, role_name, tenant_id)
                SELECT oid, rolname, @tenant FROM pg_catalog.pg_roles WHERE rolname = @role
                """, connection, transaction);
            insert.Parameters.AddWithValue("tenant", binding.TenantId);
            insert.Parameters.AddWithValue("role", binding.Login);
            if (await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("ERP tenant login disappeared during installation; roll back.");
        }

    }

    // A known v1 directory can be shared; unknown or modified objects are never repaired here.
    // The control caller owns rollback on rejection, just as for first installation.
    internal static async Task ValidateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> bindings, CancellationToken ct)
    {
        ValidateArguments(connection, transaction, bindings);
        await using (var gate = new NpgsqlCommand($"SELECT pg_catalog.pg_advisory_xact_lock({DirectoryLockNamespace}, 1); LOCK TABLE cp6_tenancy_v1.login_tenant IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            WITH helper AS (SELECT oid FROM pg_catalog.pg_roles WHERE rolname='cp6_tenant_lookup_v1'
                AND NOT (rolcanlogin OR rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication OR rolinherit)),
            directory AS (SELECT c.* FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='cp6_tenancy_v1' AND c.relname='login_tenant' AND c.relkind='r'
                AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity),
            allowed AS (SELECT oid FROM pg_catalog.pg_roles WHERE rolname=ANY(@roles))
            SELECT (SELECT count(*) FROM helper)=1 AND (SELECT count(*) FROM directory)=1
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members WHERE member IN (SELECT oid FROM helper) OR roleid IN (SELECT oid FROM helper))
              AND (SELECT count(*) FROM pg_catalog.pg_namespace WHERE nspname='cp6_tenancy_v1' AND nspowner=current_user::regrole)=1
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_namespace n, LATERAL pg_catalog.aclexplode(n.nspacl) a
                WHERE n.nspname='cp6_tenancy_v1' AND a.grantee<>n.nspowner
                  AND (a.is_grantable OR a.privilege_type<>'USAGE' OR a.grantee NOT IN (SELECT oid FROM allowed UNION SELECT oid FROM helper)))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='cp6_tenancy_v1' AND c.oid NOT IN (SELECT oid FROM directory)
                  AND c.oid NOT IN (SELECT conindid FROM pg_catalog.pg_constraint WHERE conrelid IN (SELECT oid FROM directory) AND contype IN ('p','u')))
              AND (SELECT count(*) FROM pg_catalog.pg_attribute WHERE attrelid IN (SELECT oid FROM directory) AND attnum>0)=3
              AND (SELECT count(*) FROM pg_catalog.pg_attribute WHERE attrelid IN (SELECT oid FROM directory) AND attnum>0
                AND NOT attisdropped AND attnotnull AND NOT atthasdef AND attidentity='' AND attgenerated='' AND attacl IS NULL
                AND (attname,atttypid) IN (('role_oid','oid'::regtype),('role_name','name'::regtype),('tenant_id','uuid'::regtype)))=3
              AND (SELECT array_agg(pg_catalog.pg_get_constraintdef(oid) ORDER BY pg_catalog.pg_get_constraintdef(oid))
                FROM pg_catalog.pg_constraint WHERE conrelid IN (SELECT oid FROM directory) AND contype<>'n') = ARRAY[
                  'CHECK ((tenant_id <> ''00000000-0000-0000-0000-000000000000''::uuid))',
                  'PRIMARY KEY (role_oid)', 'UNIQUE (role_name)', 'UNIQUE (tenant_id)']
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_constraint WHERE conrelid IN (SELECT oid FROM directory)
                AND (NOT convalidated OR condeferrable OR condeferred))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_index WHERE indrelid IN (SELECT oid FROM directory)
                AND (NOT indisvalid OR NOT indisready OR NOT indisunique OR indpred IS NOT NULL OR indexprs IS NOT NULL))
              AND NOT EXISTS (SELECT FROM directory c, LATERAL pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner
                AND (a.grantee NOT IN (SELECT oid FROM helper) OR a.privilege_type<>'SELECT' OR a.is_grantable))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy WHERE polrelid IN (SELECT oid FROM directory))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_rewrite WHERE ev_class IN (SELECT oid FROM directory))
              AND (SELECT count(*) FROM pg_catalog.pg_trigger WHERE tgrelid IN (SELECT oid FROM directory) AND NOT tgisinternal)=2
              AND (SELECT count(*) FROM pg_catalog.pg_trigger WHERE tgrelid IN (SELECT oid FROM directory) AND NOT tgisinternal
                AND tgfoid='cp6_tenancy_v1.lock_login_tenant_v1()'::regprocedure AND tgenabled='O' AND tgnargs=0
                AND tgqual IS NULL AND tgattr=''::int2vector AND NOT tgdeferrable AND NOT tginitdeferred
                AND ((tgname='login_tenant_write_v1' AND tgtype=30) OR (tgname='login_tenant_no_truncate_v1' AND tgtype=34)))=2
              AND (SELECT count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname='cp6_tenancy_v1')=2
              AND (SELECT count(*) FROM pg_catalog.pg_proc p WHERE p.oid IN
                ('cp6_tenancy_v1.current_tenant_id()'::regprocedure,'cp6_tenancy_v1.lock_login_tenant_v1()'::regprocedure)
                AND p.prolang=(SELECT oid FROM pg_catalog.pg_language WHERE lanname='plpgsql')
                AND p.prokind='f' AND p.provolatile='v' AND p.proparallel='u' AND NOT p.proleakproof
                AND NOT p.proisstrict AND NOT p.proretset AND p.pronargs=0 AND p.pronargdefaults=0
                AND p.proconfig=ARRAY['search_path=pg_catalog, cp6_tenancy_v1, pg_temp']
                AND ((p.proname='current_tenant_id' AND p.prosecdef AND p.proowner IN (SELECT oid FROM helper)
                    AND p.prorettype='uuid'::regtype AND btrim(replace(p.prosrc, chr(13), ''), chr(32)||chr(9)||chr(10))=@helper)
                  OR (p.proname='lock_login_tenant_v1' AND NOT p.prosecdef AND p.proowner=current_user::regrole
                    AND p.prorettype='trigger'::regtype AND btrim(replace(p.prosrc, chr(13), ''), chr(32)||chr(9)||chr(10))=@lock))
                AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(coalesce(p.proacl,pg_catalog.acldefault('f',p.proowner))) a
                    WHERE a.grantee<>p.proowner AND (p.proname<>'current_tenant_id' OR a.grantee NOT IN (SELECT oid FROM allowed)
                        OR a.privilege_type<>'EXECUTE' OR a.is_grantable)))=2
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_roles r WHERE r.rolname=ANY(@roles)
                AND (NOT r.rolcanlogin OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                  OR EXISTS (SELECT FROM pg_catalog.pg_auth_members m WHERE m.member=r.oid OR m.roleid=r.oid)))
              AND (SELECT count(*) FROM cp6_tenancy_v1.login_tenant)=cardinality(@roles)
              AND (SELECT count(*) FROM cp6_tenancy_v1.login_tenant m JOIN pg_catalog.pg_roles r ON r.oid=m.role_oid AND r.rolname=m.role_name
                JOIN unnest(@roles::text[],@tenants::uuid[]) b(login,tenant) ON b.login=r.rolname::text AND b.tenant=m.tenant_id)=cardinality(@roles)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_database WHERE datdba IN (SELECT oid FROM helper))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_namespace WHERE nspowner IN (SELECT oid FROM helper))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class WHERE relowner IN (SELECT oid FROM helper))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_proc WHERE proowner IN (SELECT oid FROM helper)
                AND oid<>'cp6_tenancy_v1.current_tenant_id()'::regprocedure)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_namespace n, helper h WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                AND (pg_catalog.has_schema_privilege(h.oid,n.oid,'CREATE')
                  OR (n.nspname<>'cp6_tenancy_v1' AND pg_catalog.has_schema_privilege(h.oid,n.oid,'USAGE'))))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace, helper h
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND c.relkind IN ('r','v','m','f','p')
                  AND ((c.oid NOT IN (SELECT oid FROM directory) AND pg_catalog.has_any_column_privilege(h.oid,c.oid,'SELECT'))
                    OR pg_catalog.has_table_privilege(h.oid,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege(h.oid,c.oid,'INSERT,UPDATE,REFERENCES')))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_database d, helper h
                WHERE d.datname=current_database() AND pg_catalog.has_database_privilege(h.oid,d.oid,'CREATE,TEMP'))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c, helper h WHERE c.relkind='S'
                AND pg_catalog.has_sequence_privilege(h.oid,c.oid,'USAGE,SELECT,UPDATE'))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace, helper h
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                  AND p.oid<>'cp6_tenancy_v1.current_tenant_id()'::regprocedure
                  AND pg_catalog.has_function_privilege(h.oid,p.oid,'EXECUTE'))
              AND pg_catalog.has_schema_privilege('cp6_tenant_lookup_v1','cp6_tenancy_v1','USAGE')
              AND pg_catalog.has_table_privilege('cp6_tenant_lookup_v1','cp6_tenancy_v1.login_tenant','SELECT')
            """, connection, transaction);
        command.Parameters.AddWithValue("roles", bindings.Select(x => x.Login).ToArray());
        command.Parameters.AddWithValue("tenants", bindings.Select(x => x.TenantId).ToArray());
        command.Parameters.AddWithValue("helper", HelperBody.Replace("\r", "", StringComparison.Ordinal).Trim());
        command.Parameters.AddWithValue("lock", LockBody.Replace("\r", "", StringComparison.Ordinal).Trim());
        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
            throw new InvalidOperationException("Known tenant directory v1 validation rejected.");
    }

    private static string InstallSql => $$"""
            CREATE ROLE cp6_tenant_lookup_v1 NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
            CREATE SCHEMA cp6_tenancy_v1;
            REVOKE ALL ON SCHEMA cp6_tenancy_v1 FROM PUBLIC;
            CREATE TABLE cp6_tenancy_v1.login_tenant (
                role_oid oid PRIMARY KEY,
                role_name name NOT NULL UNIQUE,
                tenant_id uuid NOT NULL UNIQUE CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid)
            );
            REVOKE ALL ON cp6_tenancy_v1.login_tenant FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_tenancy_v1 TO cp6_tenant_lookup_v1;
            GRANT SELECT ON cp6_tenancy_v1.login_tenant TO cp6_tenant_lookup_v1;
            CREATE FUNCTION cp6_tenancy_v1.lock_login_tenant_v1() RETURNS trigger
            LANGUAGE plpgsql SET search_path = pg_catalog, cp6_tenancy_v1, pg_temp
            AS $cp6_directory$
            BEGIN
                IF TG_OP = 'TRUNCATE' THEN
                    RAISE EXCEPTION 'Tenant directory truncate is not supported' USING ERRCODE = '42501';
                END IF;
                PERFORM pg_catalog.pg_advisory_xact_lock({{DirectoryLockNamespace}}, 1);
                RETURN NULL;
            END $cp6_directory$;
            REVOKE ALL ON FUNCTION cp6_tenancy_v1.lock_login_tenant_v1() FROM PUBLIC;
            CREATE TRIGGER login_tenant_write_v1 BEFORE INSERT OR UPDATE OR DELETE
                ON cp6_tenancy_v1.login_tenant FOR EACH STATEMENT EXECUTE FUNCTION cp6_tenancy_v1.lock_login_tenant_v1();
            CREATE TRIGGER login_tenant_no_truncate_v1 BEFORE TRUNCATE
                ON cp6_tenancy_v1.login_tenant FOR EACH STATEMENT EXECUTE FUNCTION cp6_tenancy_v1.lock_login_tenant_v1();
            CREATE FUNCTION cp6_tenancy_v1.current_tenant_id() RETURNS uuid
            LANGUAGE plpgsql VOLATILE SECURITY DEFINER
            SET search_path = pg_catalog, cp6_tenancy_v1, pg_temp
            AS $cp6_tenant$
            DECLARE result uuid;
            BEGIN
                -- Pin this directory until the runtime transaction ends. VOLATILE obtains a fresh
                -- snapshot for the SELECT after waiting for a prior control writer's exclusive lock.
                PERFORM pg_catalog.pg_advisory_xact_lock_shared({{DirectoryLockNamespace}}, 1);
                SELECT m.tenant_id INTO result
                FROM cp6_tenancy_v1.login_tenant AS m
                JOIN pg_catalog.pg_roles AS r ON r.oid = m.role_oid AND r.rolname = m.role_name
                WHERE r.rolname = session_user AND r.rolcanlogin
                  AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members a WHERE a.member = r.oid);
                IF result IS NULL THEN
                    RAISE EXCEPTION 'Tenant login binding rejected' USING ERRCODE = '42501';
                END IF;
                RETURN result;
            END $cp6_tenant$;
            ALTER FUNCTION cp6_tenancy_v1.current_tenant_id() OWNER TO cp6_tenant_lookup_v1;
            REVOKE ALL ON FUNCTION cp6_tenancy_v1.current_tenant_id() FROM PUBLIC;
            """;

    private static string LockBody => $$"""
            BEGIN
                IF TG_OP = 'TRUNCATE' THEN
                    RAISE EXCEPTION 'Tenant directory truncate is not supported' USING ERRCODE = '42501';
                END IF;
                PERFORM pg_catalog.pg_advisory_xact_lock({{DirectoryLockNamespace}}, 1);
                RETURN NULL;
            END
            """;
    private static string HelperBody => $$"""
            DECLARE result uuid;
            BEGIN
                -- Pin this directory until the runtime transaction ends. VOLATILE obtains a fresh
                -- snapshot for the SELECT after waiting for a prior control writer's exclusive lock.
                PERFORM pg_catalog.pg_advisory_xact_lock_shared({{DirectoryLockNamespace}}, 1);
                SELECT m.tenant_id INTO result
                FROM cp6_tenancy_v1.login_tenant AS m
                JOIN pg_catalog.pg_roles AS r ON r.oid = m.role_oid AND r.rolname = m.role_name
                WHERE r.rolname = session_user AND r.rolcanlogin
                  AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members a WHERE a.member = r.oid);
                IF result IS NULL THEN
                    RAISE EXCEPTION 'Tenant login binding rejected' USING ERRCODE = '42501';
                END IF;
                RETURN result;
            END
            """;
}
