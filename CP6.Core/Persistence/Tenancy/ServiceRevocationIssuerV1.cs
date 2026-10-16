using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

// Issuance is outside the tenant directory. Do not broaden the existing catalog seal to include it.
internal static class ServiceRevocationIssuerV1
{
    internal static async Task<uint> ValidateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string login, uint? expectedOid, bool installed, CancellationToken ct)
    {
        await using var role = new NpgsqlCommand("""
            SELECT r.oid FROM pg_catalog.pg_roles r WHERE r.rolname=@login
              AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
              AND r.rolcanlogin AND r.rolconnlimit<>0 AND (r.rolvaliduntil IS NULL OR r.rolvaliduntil>CURRENT_TIMESTAMP)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_db_role_setting WHERE setrole=r.oid)
            """, connection, transaction);
        role.Parameters.AddWithValue("login", login);
        if (await role.ExecuteScalarAsync(ct).ConfigureAwait(false) is not uint oid || expectedOid is { } expected && oid != expected)
            throw TenantStorageBindingV1.Rejected();

        await using var rights = new NpgsqlCommand("""
            WITH db AS (SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()),
            allowed(classid,objid,privilege_type) AS (
              SELECT 'pg_database'::regclass,oid,'CONNECT' FROM db
              UNION ALL SELECT 'pg_namespace'::regclass,oid,'USAGE' FROM pg_catalog.pg_namespace
                WHERE @installed AND nspname IN ('public','crm_identity','cp6_storage_core')
              UNION ALL SELECT 'pg_class'::regclass,c.oid,p.privilege_type
                FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace,
                  (VALUES ('SELECT'),('INSERT')) p(privilege_type)
                WHERE @installed AND n.nspname='crm_identity' AND c.relname='ServiceToken'
              UNION ALL SELECT 'pg_class'::regclass,c.oid,'USAGE'
                FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE @installed AND n.nspname='cp6_storage_core' AND c.relname='rowversion_v1'
              UNION ALL SELECT 'pg_proc'::regclass,p.oid,'EXECUTE'
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE @installed AND ((n.nspname='public' AND p.proname='cp6_cp936_length_v1' AND p.proargtypes='25'::oidvector)
                  OR (n.nspname='cp6_storage_core' AND p.proname='set_rowversion_v1' AND p.pronargs=0))
            ), grants(classid,objid,objsubid,grantee,privilege_type,is_grantable) AS (
              SELECT 'pg_database'::regclass,d.oid,0,a.grantee,a.privilege_type,a.is_grantable
                FROM pg_catalog.pg_database d,LATERAL pg_catalog.aclexplode(d.datacl) a
              UNION ALL SELECT 'pg_namespace'::regclass,n.oid,0,a.grantee,a.privilege_type,a.is_grantable
                FROM pg_catalog.pg_namespace n,LATERAL pg_catalog.aclexplode(n.nspacl) a
              UNION ALL SELECT 'pg_class'::regclass,c.oid,0,a.grantee,a.privilege_type,a.is_grantable
                FROM pg_catalog.pg_class c,LATERAL pg_catalog.aclexplode(c.relacl) a
              UNION ALL SELECT 'pg_class'::regclass,c.attrelid,c.attnum,a.grantee,a.privilege_type,a.is_grantable
                FROM pg_catalog.pg_attribute c,LATERAL pg_catalog.aclexplode(c.attacl) a
              UNION ALL SELECT 'pg_proc'::regclass,p.oid,0,a.grantee,a.privilege_type,a.is_grantable
                FROM pg_catalog.pg_proc p,LATERAL pg_catalog.aclexplode(p.proacl) a
            )
            SELECT NOT EXISTS(SELECT FROM grants g WHERE g.grantee=@oid AND (g.objsubid<>0 OR g.is_grantable
              OR NOT EXISTS(SELECT FROM allowed a WHERE (a.classid,a.objid,a.privilege_type)=(g.classid,g.objid,g.privilege_type))))
              AND (NOT @installed OR NOT EXISTS(SELECT FROM allowed a WHERE NOT EXISTS(
                SELECT FROM grants g WHERE g.grantee=@oid AND g.objsubid=0
                  AND (g.classid,g.objid,g.privilege_type)=(a.classid,a.objid,a.privilege_type))))
              -- Reject ownership, default grants, grants in other databases and object kinds outside this profile.
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_shdepend d WHERE d.refclassid='pg_authid'::regclass AND d.refobjid=@oid
                AND NOT ((d.deptype='a' AND d.objsubid=0
                  AND d.dbid=CASE WHEN d.classid='pg_database'::regclass THEN 0::oid ELSE (SELECT oid FROM db) END
                  AND EXISTS(SELECT FROM allowed a WHERE (a.classid,a.objid)=(d.classid,d.objid)))
                  OR (@installed AND d.deptype='r' AND d.dbid=(SELECT oid FROM db) AND d.classid='pg_policy'::regclass
                    AND d.objsubid=0 AND EXISTS(SELECT FROM pg_catalog.pg_policy p
                      JOIN pg_catalog.pg_class c ON c.oid=p.polrelid JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                      WHERE p.oid=d.objid AND n.nspname='crm_identity' AND c.relname='ServiceToken'
                        AND p.polname IN ('issue_insert_v1','issue_select_v1')))))
              AND (NOT @installed OR (NOT pg_catalog.has_database_privilege(@oid,current_database(),'CREATE')
                AND NOT pg_catalog.has_database_privilege(@oid,current_database(),'TEMP')
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n WHERE n.nspname !~ '^pg_'
                  AND pg_catalog.has_schema_privilege(@oid,n.oid,'CREATE'))))
            """, connection, transaction);
        rights.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, oid);
        rights.Parameters.AddWithValue("installed", installed);
        if (await rights.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
        return oid;
    }
}
