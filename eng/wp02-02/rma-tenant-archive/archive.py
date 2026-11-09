#!/usr/bin/env python3
"""Export and check the bounded local Core RMA source archive."""

import argparse
import csv
from contextlib import contextmanager
from dataclasses import dataclass
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

WINDOWS = os.name == "nt"
FLAGS = subprocess.CREATE_NO_WINDOW if WINDOWS else 0
FORMAT = "cp6-rma-source-archive-v1"
PROFILE = "rma-application-core-tenant-v1"
LINEAGE = "rma-application-v1"
MIGRATION = "rma-tenant-storage-0001"
HISTORY = "rma_migration_history_v1"
TABLES = ("heads", HISTORY, "versions")
MEMBERS = ("rma.pgcustom", "outbounds.csv")
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
            subprocess.Popen([binary(pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1", *connection],
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

        def query(statement):
            process.stdin.write((statement + "\n").encode("utf-8"))
            process.stdin.flush()
            try:
                response = responses.get(timeout=35)
            except Empty:
                raise ArchiveError("snapshot-holder-timeout") from None
            if response is None:
                raise ArchiveError("snapshot-holder-failed (see native logs)")
            return json.loads(response)

        completed = False
        try:
            yield query
            completed = True
        finally:
            try:
                process.stdin.write(b"COMMIT;\n" if completed else b"ROLLBACK;\n")
                process.stdin.close()
            except BrokenPipeError:
                pass
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


def capture(query, tenant, schema, ready):
    result = query("""
        LOCK TABLE {schema}.heads, {schema}.versions, {schema}.{history} IN ACCESS SHARE MODE;
        WITH target_schema AS (
            SELECT oid,nspowner,nspacl FROM pg_namespace WHERE nspname={schema_name}
        ), grants AS (
            SELECT n.nspowner AS object_owner,g.grantee,g.grantor FROM target_schema n,
                LATERAL aclexplode(coalesce(n.nspacl,acldefault('n',n.nspowner))) g
            UNION ALL
            SELECT c.relowner,g.grantee,g.grantor FROM pg_class c JOIN target_schema n ON n.oid=c.relnamespace,
                LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) g
            WHERE c.relkind='r'
            UNION ALL
            SELECT c.relowner,g.grantee,g.grantor FROM pg_attribute a
                JOIN pg_class c ON c.oid=a.attrelid JOIN target_schema n ON n.oid=c.relnamespace,
                LATERAL aclexplode(a.attacl) g
            UNION ALL
            SELECT p.proowner,g.grantee,g.grantor FROM pg_proc p JOIN target_schema n ON n.oid=p.pronamespace,
                LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) g
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
          'aclInScope',NOT EXISTS (SELECT FROM grants WHERE grantee NOT IN (object_owner,{login}::oid)
                      OR grantor NOT IN (object_owner,{login}::oid))
                      AND NOT EXISTS (SELECT FROM pg_default_acl a JOIN target_schema n ON n.oid=a.defaclnamespace));
    """.format(schema=schema, history=HISTORY, tenant=literal(tenant), lineage=literal(LINEAGE),
               outbounds=outbounds_sql(tenant, schema), schema_name=literal(schema),
               login=literal(ready["roleOid"]), owner=literal(ready["ownerOid"])))
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


def copy_slice(pg_bin, connection, environ, logs, bundle, tenant, schema, snapshot):
    sql = """
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SET TRANSACTION SNAPSHOT {snapshot};
        SET LOCAL search_path=pg_catalog;
        SET LOCAL standard_conforming_strings=on;
        SET LOCAL statement_timeout='100s';
        COPY ({outbounds}) TO STDOUT WITH (FORMAT csv, HEADER true);
        COMMIT;
    """.format(snapshot=literal(snapshot), outbounds=outbounds_sql(tenant, schema))
    run_native(pg_bin, "psql", ["-XqAtw", "-v", "ON_ERROR_STOP=1", *connection], environ, logs, "shared-copy",
               sql=sql, output=bundle / "outbounds.csv")


def file_digest(path):
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
            size += len(chunk)
    return {"bytes": size, "sha256": digest.hexdigest()}


def export_bundle(pg_bin, target, username, installation, tenant, output, *, environ=None, on_snapshot=None):
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
        captured = capture(query, tenant, schema, ready)
        snapshot = captured["snapshotId"]
        if on_snapshot is not None:
            on_snapshot(snapshot)
        run_native(pg_bin, "pg_dump", [*connection, "--no-password", "--format=custom", "--strict-names",
                   "--lock-wait-timeout=5s", "--schema=" + schema, "--snapshot=" + snapshot,
                   "--file=" + str(bundle / "rma.pgcustom")], environ, logs, "pg-dump")
        copy_slice(pg_bin, connection, environ, logs, bundle, tenant, schema, snapshot)

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
    pending = logs / "manifest.pending"
    with pending.open("x", encoding="utf-8", newline="\n") as file:
        json.dump(manifest, file, indent=2)
        file.write("\n")
    pending.replace(bundle / "manifest.json")
    return bundle


def verify_bundle(bundle, tenant):
    tenant = guid(tenant)
    members = list(bundle.iterdir())
    if {path.name for path in members} != {*MEMBERS, "manifest.json"} \
            or any(path.is_symlink() or not path.is_file() for path in members):
        raise ArchiveError("bundle-member-mismatch")
    manifest = json.loads((bundle / "manifest.json").read_text(encoding="utf-8"))
    if not isinstance(manifest, dict) or manifest.get("format") != FORMAT or manifest.get("recoveryReady") is not False:
        raise ArchiveError("bundle-format-rejected")
    if manifest.get("tenantId") != str(tenant) or manifest.get("schema") != "t_" + tenant.hex:
        raise ArchiveError("bundle-tenant-mismatch")
    expected = manifest.get("members")
    if not isinstance(expected, dict) or expected.keys() != set(MEMBERS):
        raise ArchiveError("bundle-member-mismatch")
    if any(file_digest(bundle / name) != expected[name] for name in MEMBERS):
        raise ArchiveError("bundle-digest-mismatch")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    export = actions.add_parser("export")
    for key in ("pg-bin", "target-file", "output"):
        export.add_argument("--" + key, type=Path, required=True)
    for key in ("installation", "tenant", "username"):
        export.add_argument("--" + key, required=True)
    verify = actions.add_parser("verify")
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--tenant", required=True)
    args = parser.parse_args()
    try:
        if args.action == "export":
            target = PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
            bundle = export_bundle(args.pg_bin.resolve(strict=True), target, args.username, args.installation,
                                   args.tenant, args.output)
            print(bundle)
        else:
            manifest = verify_bundle(args.bundle, args.tenant)
            print(json.dumps({"tenantId": manifest["tenantId"], "format": FORMAT, "recoveryReady": False}))
    except (ArchiveError, OSError, ValueError, subprocess.SubprocessError) as error:
        print("archive rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
