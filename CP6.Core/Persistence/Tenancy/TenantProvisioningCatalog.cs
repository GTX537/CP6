using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Database-local structural seal. Business rows and mutable sequence counters are excluded.</summary>
internal static class TenantProvisioningCatalog
{
    internal static async Task<string> HashAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(Sql, connection, tx);
        var value = (string)(await command.ExecuteScalarAsync(ct))!;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    // Seal this profile's structural categories and user-object dependencies; OIDs pin identities.
    // Explicit columns omit VACUUM/statistics/storage-counter fields which ordinary traffic changes.
    private const string Sql = """
        WITH ns AS (SELECT oid FROM pg_catalog.pg_namespace WHERE nspname !~ '^pg_' AND nspname<>'information_schema'),
        cls AS (SELECT oid FROM pg_catalog.pg_class WHERE relnamespace IN (SELECT oid FROM ns)),
        roles AS (SELECT role_oid AS oid FROM cp6_tenancy_v1.login_tenant
          UNION SELECT oid FROM pg_catalog.pg_roles WHERE rolname IN (current_user,'cp6_tenant_lookup_v1')),
        objects AS (
          SELECT 'extension' kind,oid::text id,to_jsonb(e)::text definition FROM pg_catalog.pg_extension e
          UNION ALL SELECT 'event-trigger',oid::text,to_jsonb(e)::text FROM pg_catalog.pg_event_trigger e
          UNION ALL SELECT 'dependency',classid||':'||objid||':'||objsubid||':'||refclassid||':'||refobjid||':'||refobjsubid||':'||deptype::text,
            to_jsonb(d)::text FROM pg_catalog.pg_depend d WHERE objid>=16384 OR refobjid>=16384
          UNION ALL SELECT 'cast',oid::text,to_jsonb(c)::text FROM pg_catalog.pg_cast c WHERE oid>=16384
          UNION ALL SELECT 'operator',oid::text,to_jsonb(o)::text FROM pg_catalog.pg_operator o WHERE oprnamespace IN (SELECT oid FROM ns)
          UNION ALL SELECT 'system-user-class',oid::text,to_jsonb(c)::text FROM pg_catalog.pg_class c WHERE relnamespace='pg_catalog'::regnamespace AND oid>=16384
          UNION ALL SELECT 'system-user-proc',oid::text,to_jsonb(p)::text FROM pg_catalog.pg_proc p WHERE pronamespace='pg_catalog'::regnamespace AND oid>=16384
          UNION ALL SELECT 'system-user-type',oid::text,to_jsonb(t)::text FROM pg_catalog.pg_type t WHERE typnamespace='pg_catalog'::regnamespace AND oid>=16384
          UNION ALL SELECT 'system-user-operator',oid::text,to_jsonb(o)::text FROM pg_catalog.pg_operator o WHERE oprnamespace='pg_catalog'::regnamespace AND oid>=16384
          UNION ALL SELECT 'system-user-collation',oid::text,to_jsonb(c)::text FROM pg_catalog.pg_collation c WHERE collnamespace='pg_catalog'::regnamespace AND oid>=16384
          UNION ALL
          SELECT 'schema' kind, n.oid::text id, to_jsonb(n)::text definition FROM pg_catalog.pg_namespace n WHERE oid IN (SELECT oid FROM ns)
          UNION ALL SELECT 'class',c.oid::text,jsonb_build_array(c.oid,c.relname,c.relnamespace,c.relowner,c.relam,c.relkind,
            c.relpersistence,c.relrowsecurity,c.relforcerowsecurity,c.relreplident,c.relacl,c.reloptions,c.relispartition)::text
            FROM pg_catalog.pg_class c WHERE oid IN (SELECT oid FROM cls)
          UNION ALL SELECT 'column',a.attrelid||':'||a.attnum,to_jsonb(a)::text FROM pg_catalog.pg_attribute a
            WHERE attrelid IN (SELECT oid FROM cls) AND attnum>0
          UNION ALL SELECT 'default',oid::text,pg_catalog.pg_get_expr(adbin,adrelid) FROM pg_catalog.pg_attrdef WHERE adrelid IN (SELECT oid FROM cls)
          UNION ALL SELECT 'constraint',oid::text,to_jsonb(c)::text FROM pg_catalog.pg_constraint c
            WHERE connamespace IN (SELECT oid FROM ns)
          UNION ALL SELECT 'index',indexrelid::text,to_jsonb(i)::text FROM pg_catalog.pg_index i WHERE indrelid IN (SELECT oid FROM cls)
          UNION ALL SELECT 'trigger',oid::text,to_jsonb(t)::text FROM pg_catalog.pg_trigger t WHERE tgrelid IN (SELECT oid FROM cls)
          UNION ALL SELECT 'policy',oid::text,to_jsonb(p)::text FROM pg_catalog.pg_policy p WHERE polrelid IN (SELECT oid FROM cls)
          UNION ALL SELECT 'rewrite',oid::text,to_jsonb(r)::text FROM pg_catalog.pg_rewrite r WHERE ev_class IN (SELECT oid FROM cls)
          UNION ALL SELECT 'function',oid::text,to_jsonb(p)::text FROM pg_catalog.pg_proc p WHERE pronamespace IN (SELECT oid FROM ns)
          UNION ALL SELECT 'collation',oid::text,to_jsonb(c)::text FROM pg_catalog.pg_collation c WHERE collnamespace IN (SELECT oid FROM ns)
          UNION ALL SELECT 'type',oid::text,to_jsonb(t)::text FROM pg_catalog.pg_type t WHERE typnamespace IN (SELECT oid FROM ns)
          UNION ALL SELECT 'sequence',seqrelid::text,to_jsonb(s)::text FROM pg_catalog.pg_sequence s WHERE seqrelid IN (SELECT oid FROM cls)
          UNION ALL SELECT 'role',oid::text,(to_jsonb(r)-'rolpassword')::text FROM pg_catalog.pg_roles r WHERE oid IN (SELECT oid FROM roles)
          UNION ALL SELECT 'membership',roleid||':'||member,to_jsonb(m)::text FROM pg_catalog.pg_auth_members m
            WHERE roleid IN (SELECT oid FROM roles) OR member IN (SELECT oid FROM roles)
          UNION ALL SELECT 'role-setting',setdatabase||':'||setrole,to_jsonb(s)::text FROM pg_catalog.pg_db_role_setting s
            WHERE setrole=0 OR setrole IN (SELECT oid FROM roles)
          UNION ALL SELECT 'default-acl',oid::text,to_jsonb(d)::text FROM pg_catalog.pg_default_acl d
            WHERE defaclrole IN (SELECT oid FROM roles) OR defaclnamespace IN (SELECT oid FROM ns)
          UNION ALL SELECT 'database',oid::text,jsonb_build_array(oid,datname,datdba,datacl,datallowconn,datconnlimit,datistemplate)::text
            FROM pg_catalog.pg_database WHERE datname=current_database()
        ) SELECT coalesce(jsonb_agg(jsonb_build_array(kind,id,definition) ORDER BY kind,id)::text,'[]') FROM objects
        """;
}
