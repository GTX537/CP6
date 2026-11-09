#!/usr/bin/env python3
"""Export and check the bounded local Core RMA source archive."""

import argparse
import csv
from contextlib import contextmanager
from dataclasses import dataclass
import errno
import hashlib
import io
import ipaddress
import json
import os
from pathlib import Path
from queue import Empty, Queue
import re
import subprocess
import sys
from threading import Thread
from uuid import UUID

import file_content_assets
import iam_v6
import identity_priority
import source_v3
import visibility_v4

WINDOWS = os.name == "nt"
FLAGS = subprocess.CREATE_NO_WINDOW if WINDOWS else 0
FORMAT = "cp6-rma-source-archive-v1"
FORMAT_WITH_JOURNAL = "cp6-rma-source-archive-v2"
PROFILE = "rma-application-core-tenant-v1"
LINEAGE = "rma-application-v1"
MIGRATION = "rma-tenant-storage-0001"
HISTORY = "rma_migration_history_v1"
TABLES = ("heads", HISTORY, "versions")
MEMBERS = ("rma.pgcustom", "outbounds.csv")
JOURNAL_MEMBER = "rma-original.csv"
JOURNAL_TABLE = "cp6_main_owner_v1.operations"
JOURNAL_INSTALLATION = "cp6_rma_original_journal_v1.installation"
JOURNAL_PROFILE = "rma-core-original-journal-v1"
JOURNAL_OWNER = "Main.Wms.RmaApplication.v1"
JOURNAL_CONTRACT = "RMA-NATIVE-APPLICATION/1"
JOURNAL_COLUMNS = ("tenant_id", "owner", "operation_id", "actor_id", "contract", "state", "input_bytes",
                   "input_sha256", "result_bytes", "object_ids", "audit_json", "committed_at")
JOURNAL_COPY_SETTINGS = """
    SET LOCAL row_security=off;
    SET LOCAL bytea_output='hex';
    SET LOCAL timezone='UTC';
    SET LOCAL datestyle='ISO, YMD';
"""
COLUMNS = ("tenant_id", "outbound_id", "outbound_no", "warehouse_cd", "status", "is_deleted")
EXCLUSIONS = (
    "IAM, credentials, signing keys and current authorization",
    "original business operation journal, receipts and close outcomes",
    "Inbox, Outbox, companion state, route epochs and dispatcher watermarks",
    "other shared outbound fields, details, orders, warehouses and global definitions",
    "audit, legal hold, immutable external assets and cross-database dependencies",
    "role creation, current-state reconciliation and any active or production restore",
)


class ArchiveError(RuntimeError):
    pass


@dataclass(frozen=True)
class PhysicalTarget:
    server: str
    port: int
    database: str
    database_oid: int
    system_identifier: str

    @classmethod
    def from_mapping(cls, value):
        keys = {"Server", "Port", "Database", "DatabaseOid", "SystemIdentifier"}
        if not isinstance(value, dict) or value.keys() != keys:
            raise ArchiveError("invalid-physical-target")
        server, port, database, oid, system = (value[k] for k in
                                               ("Server", "Port", "Database", "DatabaseOid", "SystemIdentifier"))
        try:
            local = isinstance(server, str) and ipaddress.ip_address(server).is_loopback
        except ValueError:
            local = False
        if not local or type(port) is not int or not 1 <= port <= 65535 \
                or type(oid) is not int or not 1 <= oid <= 0xFFFFFFFF \
                or not isinstance(database, str) or not database or "\0" in database \
                or len(database.encode("utf-8")) > 63 \
                or not isinstance(system, str) or not re.fullmatch(r"[0-9]{1,20}", system):
            raise ArchiveError("invalid-physical-target")
        return cls(server, port, database, oid, system)

    def mapping(self):
        return {"Server": self.server, "Port": self.port, "Database": self.database,
                "DatabaseOid": self.database_oid, "SystemIdentifier": self.system_identifier}


def guid(value):
    try:
        parsed = UUID(value)
    except (ValueError, TypeError, AttributeError):
        raise ArchiveError("invalid-guid") from None
    if not parsed.int:
        raise ArchiveError("invalid-guid")
    return parsed


def literal(value):
    # These literals run with standard_conforming_strings forced on.
    return "'" + str(value).replace("'", "''") + "'"


def connection_args(target, username):
    # -d interprets an unescaped name containing '=' as libpq connection options.
    name = target.database.replace("\\", "\\\\").replace("'", "\\'")
    return ["--host=" + target.server, "--port=" + str(target.port),
            "--username=" + username, "--dbname=dbname='" + name + "'"]


def client_environment(environ, app):
    # Do not let ambient libpq options override the explicitly pinned target.
    environment = {key: value for key, value in environ.items() if not key.upper().startswith("PG")}
    for key in ("PGPASSFILE", "PGPASSWORD"):
        if key in environ:
            environment[key] = environ[key]
    return dict(environment, PGAPPNAME=app, PGCONNECT_TIMEOUT="5", PGCLIENTENCODING="UTF8", PGSSLMODE="disable")


def private_directory(path):
    path.mkdir(mode=0o700)
    if WINDOWS:
        sid_csv = subprocess.check_output(["whoami", "/user", "/fo", "csv", "/nh"], creationflags=FLAGS)
        sid = next(csv.reader(io.StringIO(sid_csv.decode(errors="replace").strip())))[1]
        subprocess.run(["icacls", str(path), "/inheritance:r", "/grant:r", "*{}:(OI)(CI)F".format(sid)],
                       capture_output=True, check=True, creationflags=FLAGS, timeout=10)


def binary(pg_bin, name):
    return str(pg_bin / (name + (".exe" if WINDOWS else "")))


def run_native(pg_bin, tool, args, environ, logs, label, sql=None, output=None):
    with (output or (logs / (label + ".stdout"))).open("xb") as out, \
            (logs / (label + ".stderr")).open("xb") as err:
        inputs = {"stdin": subprocess.DEVNULL} if sql is None else {"input": sql.encode("utf-8")}
        result = subprocess.run([binary(pg_bin, tool), *args], **inputs, stdout=out, stderr=err,
                                env=client_environment(environ, "cp6-rma-archive-" + label),
                                timeout=120, creationflags=FLAGS)
    if result.returncode:
        raise ArchiveError("{}-failed (exit {}, see native logs)".format(label, result.returncode))


@contextmanager
def snapshot_session(pg_bin, connection, environ, logs):
    # psql stays open while pg_dump and COPY import its exported snapshot.
    with (logs / "holder.stdout").open("xb") as out, (logs / "holder.stderr").open("xb") as err, \
            subprocess.Popen([binary(pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1",
                              "-v", "VERBOSITY=verbose", *connection],
                             stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err, creationflags=FLAGS,
                             env=client_environment(environ, "cp6-rma-archive-holder")) as process:
        responses = Queue()

        def read_responses():
            for line in process.stdout:
                out.write(line)
                out.flush()
                responses.put(line)
            responses.put(None)

        reader = Thread(target=read_responses, daemon=True)
        reader.start()

        def query(statement, failure="snapshot-holder-failed (see native logs)"):
            process.stdin.write((statement + "\n").encode("utf-8"))
            process.stdin.flush()
            try:
                response = responses.get(timeout=35)
            except Empty:
                raise ArchiveError("snapshot-holder-timeout") from None
            if response is None:
                raise ArchiveError(failure)
            return json.loads(response)

        completed = False
        try:
            yield query
            completed = True
        finally:
            try:
                if completed:
                    process.stdin.write(b"COMMIT;\n")
                # EOF rolls back a failed read-only session without writing to a rejected query's closed pipe.
                process.stdin.close()
            except OSError as error:
                disconnected = isinstance(error, BrokenPipeError) or (WINDOWS and error.errno == errno.EINVAL)
                if completed or not disconnected:
                    raise ArchiveError("snapshot-holder-close-failed (see native logs)") from error
            finally:
                try:
                    result = process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
                    result = -1
                reader.join()
            if completed and result:
                raise ArchiveError("snapshot-holder-close-failed (see native logs)")


def check_source(query, target, installation, tenant):
    live = query("""
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SET LOCAL search_path=pg_catalog;
        SET LOCAL standard_conforming_strings=on;
        SET LOCAL lock_timeout='5s';
        SET LOCAL statement_timeout='30s';
        SET LOCAL idle_in_transaction_session_timeout='150s';
        SELECT json_build_object(
            'Server', host(inet_server_addr()), 'Port', inet_server_port(), 'Database', d.datname,
            'DatabaseOid', d.oid::bigint, 'SystemIdentifier', p.system_identifier::text, 'Primary', NOT pg_is_in_recovery())
        FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
    """)
    if live != dict(target.mapping(), Primary=True):
        raise ArchiveError("target-identity-rejected")
    ready = query("""
        SELECT coalesce((SELECT json_build_object(
            'roleOid',r.oid::bigint,'ownerOid',d.datdba::bigint,
            'migration',json_build_object('operationId',m.operation_id,'migrationId',m.migration_id,
                'scriptSha256',m.script_sha256,'receiptSha256',encode(sha256(convert_to(m.result_json,'UTF8')),'hex')))
          FROM cp6_rma_tenant_v1.installation i
          CROSS JOIN cp6_rma_core_commission_v1.installation p
          JOIN cp6_rma_tenant_v1.tenant_migrations m ON m.tenant_id={tenant}::uuid
          JOIN cp6_tenancy_v1.login_tenant l ON l.tenant_id=m.tenant_id AND l.role_oid=m.role_oid AND l.role_name=m.role_name
          JOIN pg_roles r ON r.oid=l.role_oid AND r.rolname=l.role_name
          CROSS JOIN pg_database d
          WHERE i.singleton AND p.singleton AND i.installation_id={installation}::uuid
            AND d.datname=current_database()
            AND NOT EXISTS (SELECT FROM pg_class c WHERE c.oid IN (
                'cp6_rma_tenant_v1.installation'::regclass, 'cp6_rma_core_commission_v1.installation'::regclass,
                'cp6_rma_tenant_v1.tenant_migrations'::regclass, 'cp6_tenancy_v1.login_tenant'::regclass)
                AND c.relowner<>d.datdba)
            AND i.target={target}::jsonb AND i.profile={profile}
            AND p.commission_receipt::jsonb->>'InstallationId'=i.installation_id::text
            AND p.commission_receipt::jsonb->>'Profile'=i.profile
            AND m.lineage={lineage} AND m.state='Ready' AND m.migration_id={migration}
            AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
            AND NOT EXISTS (SELECT FROM pg_auth_members WHERE member=r.oid OR roleid=r.oid)
            AND m.script_sha256 ~ '^[0-9a-f]{{64}}$'), 'null'::json);
    """.format(tenant=literal(tenant), installation=literal(installation), target=literal(json.dumps(target.mapping())),
               profile=literal(PROFILE), lineage=literal(LINEAGE), migration=literal(MIGRATION)))
    if ready is None:
        raise ArchiveError("rma-ready-rejected")
    return ready


def outbounds_sql(tenant, schema):
    return """
        SELECT o."TenantId" AS tenant_id, o."Id" AS outbound_id, o."OutboundNo"::text AS outbound_no,
               o."WarehouseCd"::text AS warehouse_cd, o."Status" AS status, o."IsDeleted" AS is_deleted
        FROM public."T_OutboundOrder" o
        JOIN (SELECT DISTINCT outbound_id FROM {schema}.heads WHERE tenant_id={tenant}::uuid) h ON h.outbound_id=o."Id"
        WHERE o."TenantId"={tenant}::uuid
        ORDER BY o."Id"
    """.format(tenant=literal(tenant), schema=schema)


def adopted_draft_executor(query, target, installation, ready):
    executor = query("""
        SELECT coalesce((SELECT to_json(r.oid::bigint)
            FROM pg_roles r JOIN pg_proc p ON p.proowner=r.oid
            JOIN pg_namespace n ON n.oid=p.pronamespace
            JOIN pg_class i ON i.relnamespace=n.oid AND i.relname='installation'
            WHERE r.rolname='cp6_rma_original_journal_executor_v1'
              AND NOT (r.rolcanlogin OR r.rolsuper OR r.rolbypassrls OR r.rolinherit
                       OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
              AND NOT EXISTS (SELECT FROM pg_auth_members m WHERE m.member=r.oid OR m.roleid=r.oid)
              AND p.oid=to_regprocedure('cp6_rma_draft_writer_v1.create_draft(uuid,uuid,uuid,uuid,uuid,jsonb,bytea,text,bytea,jsonb)')
              AND p.prosecdef AND n.nspowner={owner}::oid AND i.relowner=n.nspowner
              AND i.relkind='r' AND NOT i.relrowsecurity
              AND NOT EXISTS (SELECT FROM aclexplode(i.relacl) a WHERE a.grantee<>i.relowner)
              AND NOT EXISTS (SELECT FROM pg_attribute a, LATERAL aclexplode(a.attacl) g
                              WHERE a.attrelid=i.oid AND g.grantee<>i.relowner)
              AND EXISTS (SELECT FROM pg_proc j
                          WHERE j.oid=to_regprocedure('cp6_rma_original_journal_v1.store(uuid,uuid,uuid,text,bytea,text,bytea,uuid[],jsonb)')
                            AND j.proowner=r.oid AND j.prosecdef)), 'null'::json);
    """.format(owner=literal(ready["ownerOid"])))
    if executor is None:
        return None
    adopted = query("""
        LOCK TABLE cp6_rma_draft_writer_v1.installation IN ACCESS SHARE MODE;
        SELECT to_json(EXISTS (SELECT FROM cp6_rma_draft_writer_v1.installation i
            WHERE i.singleton AND i.installation_id={installation}::uuid
              AND i.result_json::jsonb->>'Outcome'='INSTALLED'
              AND i.result_json::jsonb->>'Profile'='rma-core-draft-writer-v1'
              AND i.result_json::jsonb->>'InstallationId'={installation}
              AND i.result_json::jsonb->>'DatabaseOid'={db_oid}
              AND i.result_json::jsonb->>'SystemIdentifier'={system_id}
              AND i.result_json::jsonb->>'CatalogSha256'=i.catalog_sha256));
    """.format(installation=literal(installation), db_oid=literal(target.database_oid),
               system_id=literal(target.system_identifier)))
    return executor if adopted else None


def adopted_submit_writer(query, target, installation, ready, executor):
    if executor is None:
        return False
    protected = query("""
        SELECT to_json(EXISTS (SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            JOIN pg_class i ON i.relnamespace=n.oid AND i.relname='installation'
            WHERE p.oid=to_regprocedure('cp6_rma_submit_writer_v1.submit_draft(uuid,uuid,uuid,uuid,integer,jsonb,bytea,text,bytea,jsonb)')
              AND p.proowner={executor}::oid AND p.prosecdef
              AND n.nspowner={owner}::oid AND i.relowner=n.nspowner AND i.relkind='r' AND NOT i.relrowsecurity
              AND NOT EXISTS (SELECT FROM aclexplode(i.relacl) a WHERE a.grantee<>i.relowner)
              AND NOT EXISTS (SELECT FROM pg_attribute a, LATERAL aclexplode(a.attacl) g
                              WHERE a.attrelid=i.oid AND g.grantee<>i.relowner)));
    """.format(executor=literal(executor), owner=literal(ready["ownerOid"])))
    if not protected:
        return False
    return query("""
        LOCK TABLE cp6_rma_submit_writer_v1.installation IN ACCESS SHARE MODE;
        SELECT to_json(EXISTS (SELECT FROM cp6_rma_submit_writer_v1.installation s
            CROSS JOIN cp6_rma_draft_writer_v1.installation d
            WHERE s.singleton AND d.singleton AND s.installation_id={installation}::uuid
              AND d.installation_id=s.installation_id AND s.draft_request_json=d.request_json
              AND s.draft_catalog_before=d.catalog_sha256
              AND s.draft_catalog_after<>s.draft_catalog_before
              AND s.result_json::jsonb->>'Outcome'='INSTALLED'
              AND s.result_json::jsonb->>'Profile'='rma-core-submit-writer-v1'
              AND s.result_json::jsonb->>'InstallationId'={installation}
              AND s.result_json::jsonb->>'DatabaseOid'={db_oid}
              AND s.result_json::jsonb->>'SystemIdentifier'={system_id}
              AND s.result_json::jsonb->>'DraftCatalogBefore'=s.draft_catalog_before
              AND s.result_json::jsonb->>'DraftCatalogAfter'=s.draft_catalog_after
              AND s.result_json::jsonb->>'CatalogSha256'=s.catalog_sha256));
    """.format(installation=literal(installation), db_oid=literal(target.database_oid),
               system_id=literal(target.system_identifier)))


def capture(query, tenant, schema, ready, draft_executor, submit_writer):
    result = query("""
        LOCK TABLE {schema}.heads, {schema}.versions, {schema}.{history} IN ACCESS SHARE MODE;
        WITH target_schema AS (
            SELECT oid,nspowner,nspacl FROM pg_namespace WHERE nspname={schema_name}
        ), grants AS (
            SELECT n.nspowner AS object_owner,g.grantee,g.grantor,g.privilege_type,g.is_grantable,
                'schema' AS scope,NULL::text AS object_name,NULL::text AS column_name FROM target_schema n,
                LATERAL aclexplode(coalesce(n.nspacl,acldefault('n',n.nspowner))) g
            UNION ALL
            SELECT c.relowner,g.grantee,g.grantor,g.privilege_type,g.is_grantable,'table',c.relname::text,NULL
                FROM pg_class c JOIN target_schema n ON n.oid=c.relnamespace,
                LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) g
            WHERE c.relkind='r'
            UNION ALL
            SELECT c.relowner,g.grantee,g.grantor,g.privilege_type,g.is_grantable,'column',c.relname::text,a.attname::text
                FROM pg_attribute a
                JOIN pg_class c ON c.oid=a.attrelid JOIN target_schema n ON n.oid=c.relnamespace,
                LATERAL aclexplode(a.attacl) g
            UNION ALL
            SELECT p.proowner,g.grantee,g.grantor,g.privilege_type,g.is_grantable,'function',p.proname::text,NULL
                FROM pg_proc p JOIN target_schema n ON n.oid=p.pronamespace,
                LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) g
        ), scoped_grants AS (
            SELECT *,
                grantee IS NOT DISTINCT FROM {draft_executor}::oid AND grantor=object_owner AND NOT is_grantable
                AND CASE scope
                    WHEN 'schema' THEN privilege_type='USAGE'
                    WHEN 'column' THEN CASE privilege_type
                        WHEN 'INSERT' THEN
                            (object_name='heads' AND column_name IN ('tenant_id','id','actor_id','outbound_id','version'))
                            OR (object_name='versions' AND column_name IN ('tenant_id','id','version','state','payload'))
                        WHEN 'UPDATE' THEN {submit_writer} AND object_name='heads' AND column_name='version'
                        WHEN 'SELECT' THEN {submit_writer} AND (
                            (object_name='heads' AND column_name IN ('tenant_id','id','version'))
                            OR (object_name='versions' AND column_name IN ('tenant_id','id','version','state')))
                        ELSE false END
                    ELSE false END AS writer_grant
            FROM grants
        )
        SELECT json_build_object(
          'snapshotId',pg_export_snapshot(), 'watermark',pg_current_snapshot()::text,
          'capturedAt',transaction_timestamp(), 'serverVersionNum',current_setting('server_version_num')::integer,
          'migration',(SELECT json_build_object('operationId',h.operation_id,'migrationId',h.migration_id,
                'scriptSha256',h.script_sha256,'receiptSha256',encode(sha256(convert_to(h.result_json,'UTF8')),'hex'))
              FROM {schema}.{history} h WHERE h.tenant_id={tenant}::uuid AND h.lineage={lineage}),
          'counts',json_build_object(
            'heads',(SELECT count(*) FROM {schema}.heads), 'versions',(SELECT count(*) FROM {schema}.versions),
            'history',(SELECT count(*) FROM {schema}.{history}), 'outbounds',(SELECT count(*) FROM ({outbounds}) o)),
          'referencedOutbounds',(SELECT count(DISTINCT outbound_id) FROM {schema}.heads),
          'tenantOnly',NOT EXISTS (SELECT FROM {schema}.heads WHERE tenant_id<>{tenant}::uuid)
                   AND NOT EXISTS (SELECT FROM {schema}.versions WHERE tenant_id<>{tenant}::uuid),
          'relations',(SELECT array_agg(c.relname::text ORDER BY c.relname::text COLLATE "C")
              FROM pg_class c WHERE c.relnamespace=to_regnamespace({schema_name}) AND c.relkind NOT IN ('i','I')),
          'functions',(SELECT array_agg(p.proname::text) FROM pg_proc p WHERE p.pronamespace=to_regnamespace({schema_name})),
          'ownersInScope',EXISTS (SELECT FROM target_schema WHERE nspowner={owner}::oid)
                      AND NOT EXISTS (SELECT FROM pg_class c JOIN target_schema n ON n.oid=c.relnamespace
                                      WHERE c.relowner<>{owner}::oid)
                      AND NOT EXISTS (SELECT FROM pg_proc p JOIN target_schema n ON n.oid=p.pronamespace
                                      WHERE p.proowner<>{owner}::oid),
          'aclInScope',NOT EXISTS (SELECT FROM scoped_grants
                      WHERE grantor NOT IN (object_owner,{login}::oid)
                         OR (grantee NOT IN (object_owner,{login}::oid) AND NOT writer_grant))
                      AND (SELECT count(*) FROM grants WHERE grantee={draft_executor}::oid) IN (0,{writer_grants})
                      AND NOT EXISTS (SELECT FROM pg_default_acl a JOIN target_schema n ON n.oid=a.defaclnamespace));
    """.format(schema=schema, history=HISTORY, tenant=literal(tenant), lineage=literal(LINEAGE),
               outbounds=outbounds_sql(tenant, schema), schema_name=literal(schema),
               login=literal(ready["roleOid"]), owner=literal(ready["ownerOid"]),
               draft_executor="NULL" if draft_executor is None else literal(draft_executor),
               submit_writer="TRUE" if submit_writer else "FALSE", writer_grants=19 if submit_writer else 11))
    if result["migration"] != ready["migration"] or result["counts"]["history"] != 1:
        raise ArchiveError("rma-history-mismatch")
    if not result["tenantOnly"] or result["relations"] != list(TABLES) \
            or result["functions"] != ["rma_application_immutable_v1"]:
        raise ArchiveError("rma-schema-scope-rejected")
    if not result["ownersInScope"]:
        raise ArchiveError("rma-schema-owner-rejected")
    if not result["aclInScope"]:
        raise ArchiveError("rma-schema-acl-rejected")
    if result["referencedOutbounds"] != result["counts"]["outbounds"]:
        raise ArchiveError("rma-outbound-reference-missing")
    return result


def original_journal_rows(tenant):
    return "SELECT {columns} FROM {table} WHERE tenant_id={tenant}::uuid AND owner={owner}".format(
        columns=", ".join(JOURNAL_COLUMNS), table=JOURNAL_TABLE, tenant=literal(tenant), owner=literal(JOURNAL_OWNER))


def check_original_journal(query, target, installation, ready, rows_sql):
    if not query("SELECT to_json(to_regclass({}) IS NOT NULL);".format(literal(JOURNAL_INSTALLATION))):
        raise ArchiveError("rma-original-journal-overlay-rejected")
    overlay = query("""
        LOCK TABLE {table} IN ACCESS SHARE MODE;
        SELECT coalesce((SELECT json_build_object(
                    'operationId',j.result_json::jsonb->>'OperationId',
                    'catalogSha256',j.catalog_sha256)
            FROM {installation_table} j
            CROSS JOIN cp6_rma_core_commission_v1.installation p
            JOIN pg_class c ON c.oid={table_name}::regclass
            JOIN pg_class i ON i.oid={installation_name}::regclass
            JOIN pg_namespace n ON n.oid=i.relnamespace
            WHERE j.singleton AND p.singleton AND j.commission_json=p.request_json
                AND c.relkind='r' AND c.relowner={owner_oid}::oid AND c.relrowsecurity AND c.relforcerowsecurity
                AND i.relkind='r' AND i.relowner=c.relowner AND n.nspowner=c.relowner AND NOT i.relrowsecurity
                AND NOT EXISTS (SELECT FROM aclexplode(i.relacl) a WHERE a.grantee<>i.relowner)
                AND NOT EXISTS (SELECT FROM pg_attribute a, LATERAL aclexplode(a.attacl) acl
                                WHERE a.attrelid=i.oid AND acl.grantee<>i.relowner)
                AND j.result_json::jsonb->>'Outcome'='INSTALLED'
                AND j.result_json::jsonb->>'Profile'={profile}
                AND j.result_json::jsonb->>'InstallationId'={installation}
                AND j.result_json::jsonb->>'DatabaseOid'={db_oid}
                AND j.result_json::jsonb->>'SystemIdentifier'={system_id}
                AND j.result_json::jsonb->>'CommissionOperationId'=p.commission_receipt::jsonb->>'OperationId'
                AND j.result_json::jsonb->>'CatalogSha256'=j.catalog_sha256
                AND j.catalog_sha256 ~ '^[0-9a-f]{{64}}$'), 'null'::json);
    """.format(table=JOURNAL_TABLE, installation_table=JOURNAL_INSTALLATION, table_name=literal(JOURNAL_TABLE),
               installation_name=literal(JOURNAL_INSTALLATION), owner_oid=literal(ready["ownerOid"]),
               profile=literal(JOURNAL_PROFILE), installation=literal(installation),
               db_oid=literal(target.database_oid), system_id=literal(target.system_identifier)))
    if overlay is None:
        raise ArchiveError("rma-original-journal-overlay-rejected")

    # PostgreSQL must raise instead of silently filtering this authoritative backup read.
    counts = query("""
        SET LOCAL row_security=off;
        SELECT json_build_object(
            'rows',count(*),
            'committed',count(*) FILTER (WHERE state='COMMITTED'),
            'closed',count(*) FILTER (WHERE state='CLOSED'),
            'unexpectedContract',count(*) FILTER (WHERE contract IS DISTINCT FROM {contract}))
        FROM ({rows}) journal;
    """.format(contract=literal(JOURNAL_CONTRACT), rows=rows_sql),
                   failure="rma-original-journal-read-failed (see native logs)")
    if counts.pop("unexpectedContract") or counts["rows"] != counts["committed"] + counts["closed"]:
        raise ArchiveError("rma-original-journal-row-rejected")
    return dict(overlay, profile=JOURNAL_PROFILE, owner=JOURNAL_OWNER, contract=JOURNAL_CONTRACT, counts=counts,
                columns=list(JOURNAL_COLUMNS))


def copy_snapshot(pg_bin, connection, environ, logs, output, label, snapshot, rows_sql, settings=""):
    sql = """
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SET TRANSACTION SNAPSHOT {snapshot};
        SET LOCAL search_path=pg_catalog;
        SET LOCAL standard_conforming_strings=on;
        SET LOCAL statement_timeout='100s';
        {settings}
        COPY ({rows}) TO STDOUT WITH (FORMAT csv, HEADER true);
        COMMIT;
    """.format(snapshot=literal(snapshot), rows=rows_sql, settings=settings)
    run_native(pg_bin, "psql", ["-XqAtw", "-v", "ON_ERROR_STOP=1", *connection], environ, logs, label,
               sql=sql, output=output)


def file_digest(path):
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
            size += len(chunk)
    return {"bytes": size, "sha256": digest.hexdigest()}


def export_bundle(pg_bin, target, username, installation, tenant, output, *,
                  include_original_journal=False, include_rma_source=False, include_rma_visibility=False,
                  include_identity_priority=False, include_rma_iam_references=False, file_content_root=None,
                  environ=None, on_snapshot=None):
    if file_content_root is not None:
        if not include_rma_iam_references:
            raise ArchiveError("file-content-iam-references-required")
        file_content_root = file_content_assets.checked_root(file_content_root)
    if include_rma_iam_references and not include_identity_priority:
        raise ArchiveError("rma-iam-reference-identity-priority-required")
    if include_identity_priority and not include_rma_visibility:
        raise ArchiveError("identity-priority-visibility-required")
    if include_rma_visibility and not include_rma_source:
        raise ArchiveError("rma-visibility-source-required")
    if include_rma_source and not include_original_journal:
        raise ArchiveError("rma-source-journal-required")
    installation, tenant = guid(installation), guid(tenant)
    schema = "t_" + tenant.hex
    if not username or "\0" in username:
        raise ArchiveError("invalid-backup-username")
    environ = os.environ if environ is None else environ
    output = output.absolute()
    private_directory(output)
    bundle, logs = output / "bundle", output / "native"
    bundle.mkdir(mode=0o700)
    logs.mkdir(mode=0o700)
    connection = connection_args(target, username)
    with snapshot_session(pg_bin, connection, environ, logs) as query:
        ready = check_source(query, target, installation, tenant)
        draft_executor = adopted_draft_executor(query, target, installation, ready)
        submit_writer = adopted_submit_writer(query, target, installation, ready, draft_executor)
        captured = capture(query, tenant, schema, ready, draft_executor, submit_writer)
        snapshot = captured["snapshotId"]
        if include_identity_priority:
            identity_counts = identity_priority.capture_counts(query, tenant, ready["ownerOid"])
        if include_original_journal:
            journal_rows = original_journal_rows(tenant)
            journal = check_original_journal(query, target, installation, ready, journal_rows)
        if include_rma_source:
            sources = source_v3.slices(tenant)
            source_counts = source_v3.capture_counts(query, sources)
            if source_counts["headers"] != captured["counts"]["outbounds"]:
                raise ArchiveError("rma-source-header-count-mismatch")
        if include_rma_visibility:
            visibility_rows = visibility_v4.rows_sql(tenant)
            visibility_counts = visibility_v4.capture_counts(query, visibility_rows)
            if not visibility_v4.covers_source(visibility_counts, source_counts):
                raise ArchiveError("rma-visibility-count-mismatch")
        if include_rma_iam_references:
            iam_rows = iam_v6.rows_sql(tenant, journal_rows, visibility_rows)
            iam_counts = iam_v6.capture_counts(query, iam_rows)
        if file_content_root is not None:
            file_count = file_content_assets.capture_count(query, tenant, ready["ownerOid"])
        if on_snapshot is not None:
            on_snapshot(snapshot)
        run_native(pg_bin, "pg_dump", [*connection, "--no-password", "--format=custom", "--strict-names",
                   "--lock-wait-timeout=5s", "--schema=" + schema, "--snapshot=" + snapshot,
                   "--file=" + str(bundle / "rma.pgcustom")], environ, logs, "pg-dump")
        if include_rma_source:
            for part in sources:
                copy_snapshot(pg_bin, connection, environ, logs, bundle / part.member,
                              "source-" + part.key + "-copy", snapshot,
                              part.query + " ORDER BY " + part.order, source_v3.READ_ALL_ROWS)
        else:
            copy_snapshot(pg_bin, connection, environ, logs, bundle / "outbounds.csv", "shared-copy", snapshot,
                          outbounds_sql(tenant, schema))
        if include_original_journal:
            copy_snapshot(pg_bin, connection, environ, logs, bundle / JOURNAL_MEMBER, "journal-copy", snapshot,
                          journal_rows + " ORDER BY operation_id", JOURNAL_COPY_SETTINGS)
        if include_rma_visibility:
            copy_snapshot(pg_bin, connection, environ, logs, bundle / visibility_v4.MEMBER, "visibility-copy", snapshot,
                          visibility_rows + " ORDER BY " + visibility_v4.ORDER, source_v3.READ_ALL_ROWS)
        if include_identity_priority:
            for table in identity_priority.TABLES:
                copy_snapshot(pg_bin, connection, environ, logs, bundle / table.member,
                              Path(table.member).stem + "-copy", snapshot,
                              identity_priority.rows_sql(table, tenant), identity_priority.COPY_SETTINGS)
        if include_rma_iam_references:
            copy_snapshot(pg_bin, connection, environ, logs, bundle / iam_v6.MEMBER, "iam-references-copy", snapshot,
                          iam_rows + " ORDER BY " + iam_v6.ORDER, source_v3.READ_ALL_ROWS)
        if file_content_root is not None:
            copy_snapshot(pg_bin, connection, environ, logs, bundle / file_content_assets.MEMBER, "file-content-assets-copy",
                          snapshot, file_content_assets.rows_sql(tenant), file_content_assets.COPY_SETTINGS)

    if file_content_root is not None:
        file_content_assets.verify_member(bundle / file_content_assets.MEMBER, tenant, file_count, file_content_root)

    toc = logs / "pg-restore-list.stdout"
    run_native(pg_bin, "pg_restore", ["--list", str(bundle / "rma.pgcustom")], environ, logs, "pg-restore-list")
    entries = re.findall(r"(?m)^\d+; \d+ \d+ TABLE DATA (\S+) (\S+)", toc.read_text(encoding="utf-8"))
    if sorted(entries) != [(schema, name) for name in TABLES]:
        raise ArchiveError("archive-table-of-contents-mismatch")

    manifest = {
        "format": FORMAT, "recoveryReady": False, "tenantId": str(tenant), "schema": schema,
        "source": target.mapping(), "installationId": str(installation), "profile": PROFILE,
        "snapshot": {name: captured[name] for name in ("snapshotId", "watermark", "capturedAt", "serverVersionNum")},
        "migration": ready["migration"], "counts": captured["counts"], "tenantTables": list(TABLES),
        "sharedProjection": {"table": "public.T_OutboundOrder", "columns": list(COLUMNS),
                             "scope": "same tenant, referenced by a head at the exported snapshot"},
        "members": {name: file_digest(bundle / name) for name in MEMBERS},
        "excluded": list(EXCLUSIONS),
    }
    if include_original_journal:
        manifest["format"] = FORMAT_WITH_JOURNAL
        manifest["journal"] = journal
        manifest["members"][JOURNAL_MEMBER] = file_digest(bundle / JOURNAL_MEMBER)
        manifest["excluded"][1] = "original operations and close outcomes outside this tenant and RMA Owner"
    if include_rma_source:
        manifest["format"] = source_v3.FORMAT
        manifest["sharedProjection"] = source_v3.header_projection()
        manifest["rmaSource"] = source_v3.manifest_section(source_counts)
        manifest["members"].update({name: file_digest(bundle / name) for name in source_v3.MEMBERS})
        manifest["excluded"][3] = "other outbound fields, inactive details, Orders, WorkOrders and the current warehouse graph and global definitions"
    if include_rma_visibility:
        manifest["format"] = visibility_v4.FORMAT
        manifest["rmaVisibility"] = visibility_v4.manifest_section(visibility_counts)
        manifest["members"][visibility_v4.MEMBER] = file_digest(bundle / visibility_v4.MEMBER)
        manifest["excluded"][3] = "other outbound, Order, WorkOrder and location fields, warehouse masters and global definitions"
    if include_identity_priority:
        manifest["format"] = identity_priority.FORMAT
        manifest["identityPriority"] = identity_priority.manifest_section(identity_counts)
        manifest["members"].update({name: file_digest(bundle / name) for name in identity_priority.MEMBERS})
        manifest["excluded"][2] = "other queue domains, companion state, route epochs, dispatcher watermarks and identity rowversion sequence"
    if include_rma_iam_references:
        manifest["format"] = iam_v6.FORMAT
        manifest["rmaIamReferences"] = iam_v6.manifest_section(iam_counts)
        manifest["members"][iam_v6.MEMBER] = file_digest(bundle / iam_v6.MEMBER)
        manifest["excluded"][0] = "credentials, MFA, tokens, signing keys, global IAM definitions and current authorization"
    if file_content_root is not None:
        manifest["format"] = file_content_assets.FORMAT
        manifest["fileContentAssets"] = file_content_assets.manifest_section(file_count)
        manifest["members"][file_content_assets.MEMBER] = file_digest(bundle / file_content_assets.MEMBER)
        manifest["excluded"][4] = "audit, legal hold, external file bodies, other assets, FileContent history and cross-database dependencies"
    pending = logs / "manifest.pending"
    with pending.open("x", encoding="utf-8", newline="\n") as file:
        json.dump(manifest, file, indent=2)
        file.write("\n")
    pending.replace(bundle / "manifest.json")
    return bundle


def verify_bundle(bundle, tenant, *, file_content_root=None):
    tenant = guid(tenant)
    files = list(bundle.iterdir())
    names = {path.name for path in files}
    if "manifest.json" not in names or any(path.is_symlink() or not path.is_file() for path in files):
        raise ArchiveError("bundle-member-mismatch")
    manifest = json.loads((bundle / "manifest.json").read_text(encoding="utf-8"))
    iam_formats = (iam_v6.FORMAT, file_content_assets.FORMAT)
    identity_formats = (identity_priority.FORMAT, *iam_formats)
    visibility_formats = (visibility_v4.FORMAT, *identity_formats)
    source_formats = (source_v3.FORMAT, *visibility_formats)
    if not isinstance(manifest, dict) or manifest.get("format") not in (FORMAT, FORMAT_WITH_JOURNAL, *source_formats) \
            or manifest.get("recoveryReady") is not False:
        raise ArchiveError("bundle-format-rejected")
    members = MEMBERS
    if manifest["format"] in (FORMAT_WITH_JOURNAL, *source_formats):
        members += (JOURNAL_MEMBER,)
        journal = manifest.get("journal")
        if not isinstance(journal, dict) or journal.get("profile") != JOURNAL_PROFILE \
                or journal.get("owner") != JOURNAL_OWNER or journal.get("contract") != JOURNAL_CONTRACT \
                or journal.get("columns") != list(JOURNAL_COLUMNS):
            raise ArchiveError("bundle-format-rejected")
    if manifest["format"] in source_formats:
        members += source_v3.MEMBERS
        if not source_v3.matches_manifest(manifest):
            raise ArchiveError("bundle-format-rejected")
    if manifest["format"] in visibility_formats:
        members += (visibility_v4.MEMBER,)
        if not visibility_v4.matches_manifest(manifest):
            raise ArchiveError("bundle-format-rejected")
    if manifest["format"] in identity_formats:
        members += identity_priority.MEMBERS
        if not identity_priority.matches_manifest(manifest):
            raise ArchiveError("bundle-format-rejected")
    if manifest["format"] in iam_formats:
        members += (iam_v6.MEMBER,)
        if not iam_v6.matches_manifest(manifest):
            raise ArchiveError("bundle-format-rejected")
    if manifest["format"] == file_content_assets.FORMAT:
        members += (file_content_assets.MEMBER,)
        if not file_content_assets.matches_manifest(manifest):
            raise ArchiveError("bundle-format-rejected")
    elif file_content_root is not None:
        raise ArchiveError("file-content-format-required")
    if names != {*members, "manifest.json"}:
        raise ArchiveError("bundle-member-mismatch")
    if manifest.get("tenantId") != str(tenant) or manifest.get("schema") != "t_" + tenant.hex:
        raise ArchiveError("bundle-tenant-mismatch")
    expected = manifest.get("members")
    if not isinstance(expected, dict) or expected.keys() != set(members):
        raise ArchiveError("bundle-member-mismatch")
    if any(file_digest(bundle / name) != expected[name] for name in members):
        raise ArchiveError("bundle-digest-mismatch")
    if manifest["format"] == file_content_assets.FORMAT:
        root = file_content_assets.checked_root(file_content_root)
        file_content_assets.verify_member(bundle / file_content_assets.MEMBER, tenant, manifest["fileContentAssets"]["count"], root)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    export = actions.add_parser("export")
    for key in ("pg-bin", "target-file", "output"):
        export.add_argument("--" + key, type=Path, required=True)
    for key in ("installation", "tenant", "username"):
        export.add_argument("--" + key, required=True)
    export.add_argument("--include-original-journal", action="store_true",
                        help="include the commissioned RMA original journal in a v2 archive")
    export.add_argument("--include-rma-source", action="store_true",
                        help="include referenced original source facts in v3 (requires --include-original-journal)")
    export.add_argument("--include-rma-visibility", action="store_true",
                        help="include original outbound visibility facts in v4 (requires --include-rma-source)")
    export.add_argument("--include-identity-priority", action="store_true",
                        help="include the four Identity Priority tables in v5 (requires --include-rma-visibility)")
    export.add_argument("--include-rma-iam-references", action="store_true",
                        help="include historical RMA IAM references in v6 (requires --include-identity-priority)")
    export.add_argument("--file-content-root", type=Path,
                        help="verify original FileContent assets under this private local root in v7 (requires v6 opt-in)")
    verify = actions.add_parser("verify")
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--tenant", required=True)
    verify.add_argument("--file-content-root", type=Path, help="private local file root for rechecking v7 asset references")
    args = parser.parse_args()
    try:
        if args.action == "export":
            target = PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
            bundle = export_bundle(args.pg_bin.resolve(strict=True), target, args.username, args.installation,
                                   args.tenant, args.output, include_original_journal=args.include_original_journal,
                                   include_rma_source=args.include_rma_source,
                                   include_rma_visibility=args.include_rma_visibility,
                                   include_identity_priority=args.include_identity_priority,
                                   include_rma_iam_references=args.include_rma_iam_references,
                                   file_content_root=args.file_content_root)
            print(bundle)
        else:
            manifest = verify_bundle(args.bundle, args.tenant, file_content_root=args.file_content_root)
            print(json.dumps({"tenantId": manifest["tenantId"], "format": manifest["format"], "recoveryReady": False}))
    except (ArchiveError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print("archive rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
