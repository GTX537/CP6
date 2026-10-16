namespace CP6.Core.Persistence.Tenancy;

// Shared verbatim preflight; changing it changes the accepted input of existing provisioning.
internal static class TenantEmptyLayoutV1
{
    internal const string Sql = """
        SELECT NOT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname !~ '^pg_' AND nspname NOT IN ('public','information_schema'))
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname ~ '^pg_'
            AND nspname NOT IN ('pg_catalog','pg_toast') AND nspname !~ '^pg_(toast_)?temp_[0-9]+$')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_event_trigger)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_depend WHERE objid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_extension WHERE extname<>'plpgsql')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_cast WHERE oid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_operator o JOIN pg_catalog.pg_namespace n ON n.oid=o.oprnamespace WHERE n.nspname='public')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n,LATERAL pg_catalog.aclexplode(n.nspacl) a
            WHERE n.nspname='public' AND a.grantee<>n.nspowner AND (a.grantee<>0 OR a.is_grantable OR a.privilege_type NOT IN ('USAGE','CREATE')))
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_database d,LATERAL pg_catalog.aclexplode(d.datacl) a
            WHERE d.datname=current_database() AND a.grantee<>d.datdba
              AND (a.is_grantable OR (a.privilege_type<>'CONNECT' AND NOT(a.grantee=0 AND a.privilege_type='TEMPORARY'))))
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_class WHERE relnamespace='pg_catalog'::regnamespace AND oid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc WHERE pronamespace='pg_catalog'::regnamespace AND oid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_type WHERE typnamespace='pg_catalog'::regnamespace AND oid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_operator WHERE oprnamespace='pg_catalog'::regnamespace AND oid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_collation WHERE collnamespace='pg_catalog'::regnamespace AND oid>=16384)
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname='public')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_collation c JOIN pg_catalog.pg_namespace n ON n.oid=c.collnamespace WHERE n.nspname='public')
          AND NOT EXISTS(SELECT FROM pg_catalog.pg_roles WHERE rolname='cp6_tenant_lookup_v1')
        """;
}
