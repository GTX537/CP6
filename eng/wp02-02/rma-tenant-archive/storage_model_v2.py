"""Storage-model-v2 boundary for the complete v12 archive and same-source recovery.

The data shape is unchanged. Source migration originals remain source originals;
the closed candidate is not activated or rebound to that source identity.
"""
import json
import re

FORMAT = "cp6-rma-source-archive-v12"
MODEL = "rma-application-core-tenant-v2"
MIGRATION = "rma-tenant-storage-0002"
SHARED = "20261113090000_RmaTenantModelV2Shared"
VIEWS = ("application_heads_v2", "application_versions_v2")
WRITERS = "cp6_rma_model_v2.writer_transactions"


def require(condition, reason):
    import archive
    if not condition:
        raise archive.ArchiveError("rma-storage-model-v2-" + reason)


def capture(query, target, installation, tenant, ready):
    import archive
    q, schema = archive.literal, "t_" + tenant.hex
    original = query(f"""
        SELECT coalesce((SELECT json_build_object('operationId',v.operation_id,
            'scriptSha256',v.script_sha256,'resultJson',v.result_json)
          FROM cp6_rma_model_v2.tenant_migrations v
          JOIN {schema}.{archive.HISTORY} h ON h.tenant_id=v.tenant_id
          WHERE v.tenant_id={q(tenant)}::uuid AND v.installation_id={q(installation)}::uuid
            AND h.lineage={q(archive.LINEAGE)} AND h.migration_id={q(MIGRATION)}
            AND (h.operation_id,h.script_sha256,h.result_json)=(v.operation_id,v.script_sha256,v.result_json)
            AND EXISTS(SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"={q(SHARED)})), 'null'::json);
    """)
    require(original is not None, "migration-original-required")
    value=dict(model=MODEL, migrationId=MIGRATION, sharedMigration=SHARED, original=original)
    verify(value, tenant, installation, target)
    require(query(f"""
        SELECT to_json(
          (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.oid IN ('{schema}.application_heads_v2'::regclass,'{schema}.application_versions_v2'::regclass)
              AND c.relkind='v' AND c.relowner={ready['ownerOid']}::oid AND n.nspowner=c.relowner
              AND c.reloptions @> ARRAY['security_invoker=true']
              AND NOT EXISTS(SELECT FROM aclexplode(c.relacl) a WHERE
                a.grantee<>c.relowner AND (a.grantee<>{ready['roleOid']}::oid OR a.privilege_type<>'SELECT'
                  OR a.is_grantable OR a.grantor<>c.relowner)))=2
          AND (SELECT count(*) FROM pg_trigger WHERE tgrelid IN ('{schema}.heads'::regclass,'{schema}.versions'::regclass)
            AND tgname='rma_model_v2_writer' AND tgenabled='A'
            AND tgfoid='cp6_rma_model_v2.require_writer()'::regprocedure)=2
          AND EXISTS(SELECT FROM pg_trigger WHERE tgrelid='{archive.JOURNAL_TABLE}'::regclass
            AND tgname='rma_model_v2_journal_writer' AND tgenabled='A'
            AND tgfoid='cp6_rma_model_v2.require_journal_writer()'::regprocedure)
          AND (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE c.oid IN ('cp6_rma_model_v2.tenant_migrations'::regclass,'{WRITERS}'::regclass)
              AND c.relowner={ready['ownerOid']}::oid AND n.nspowner=c.relowner)=2
          AND NOT has_table_privilege({ready['roleOid']}::oid,'{WRITERS}','INSERT,UPDATE,DELETE'));
    """), "current-layout-rejected")
    return value


def verify(value, tenant, installation, target):
    """Validate the external model descriptor once, at archive ingestion/capture."""
    import archive
    require(isinstance(value,dict) and set(value)=={'model','migrationId','sharedMigration','original'}
        and value['model']==MODEL and value['migrationId']==MIGRATION and value['sharedMigration']==SHARED,
        'descriptor-rejected')
    original=value['original']
    require(isinstance(original,dict) and set(original)=={'operationId','scriptSha256','resultJson'}
        and isinstance(original['scriptSha256'],str) and re.fullmatch('[0-9a-f]{64}',original['scriptSha256'])
        and isinstance(original['resultJson'],str),'original-rejected')
    operation=archive.guid(original['operationId'])
    result=json.loads(original['resultJson'])
    expected=dict(Outcome='READY',InstallationId=str(installation),OperationId=str(operation),TenantId=str(tenant),
        Schema='t_'+tenant.hex,Profile=MODEL,Lineage=archive.LINEAGE,MigrationId=MIGRATION,SharedMigration=SHARED,
        ScriptSha256=original['scriptSha256'],DatabaseOid=target.database_oid,SystemIdentifier=target.system_identifier)
    require(isinstance(result,dict) and all(result.get(k)==v for k,v in expected.items()),'original-binding-rejected')


def begin_recovery(query, tenant):
    # live_recovery already owns the original database, authentication root and tenant lock.
    # Keep the ordinary writer gate enabled; this permit belongs only to this recovery xid.
    import archive
    query(f"INSERT INTO {WRITERS}(tenant_id,writer_xid) VALUES({archive.literal(tenant)}::uuid,pg_current_xact_id()); SELECT to_json(true);")


def end_recovery(query, tenant):
    import archive
    query(f"DELETE FROM {WRITERS} WHERE tenant_id={archive.literal(tenant)}::uuid AND writer_xid=pg_current_xact_id(); SELECT to_json(true);")
