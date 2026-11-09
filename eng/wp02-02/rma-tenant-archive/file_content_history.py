#!/usr/bin/env python3
"""Export and verify native FileContent history from one approved PostgreSQL snapshot."""

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
from typing import NamedTuple

sys.dont_write_bytecode = True
import archive
import file_content_assets as assets


FORMAT = "cp6-file-content-history-v1"
CONTRACT = "Main.File.Content.commands.v1"
PARENT_MIGRATION = "20261026200000_EngineeringRevisionDraftV1"
JOURNAL_MIGRATION = "20261022061500_MainOwnerOperationsV1"
PARENT_HEADS = "cp6_engineering_revision_v1.heads"
MARKER = "CP6-PRIVACY-RESTORE-CANDIDATE/v1/"
MANIFEST = "manifest.json"
q = archive.literal


class Member(NamedTuple):
    relation: str
    columns: tuple
    types: tuple
    key: tuple


MEMBERS = {
    "contents": Member("cp6_file_content_v1.contents",
                       ("tenant_id", "file_id", "creator_id", "parent_id", "parent_version", "parent_digest",
                        "file_name", "digest", "byte_length", "media_type", "store_path"),
                       ("uuid", "uuid", "uuid", "uuid", "text", "text", "text", "text", "bigint", "text", "text"),
                       ("tenant_id", "file_id")),
    "heads": Member("cp6_file_content_v1.heads", ("tenant_id", "file_id", "revision"),
                    ("uuid", "uuid", "bigint"), ("tenant_id", "file_id")),
    "events": Member("cp6_file_content_v1.events",
                     ("tenant_id", "file_id", "revision", "body_bytes", "body_digest", "actor_id", "recorded_at"),
                     ("uuid", "uuid", "bigint", "bytea", "text", "uuid", "timestamp with time zone"),
                     ("tenant_id", "file_id", "revision")),
    "operations": Member(archive.JOURNAL_TABLE, archive.JOURNAL_COLUMNS,
                         ("uuid", "text", "uuid", "uuid", "text", "text", "bytea", "text", "bytea", "uuid[]",
                          "jsonb", "timestamp with time zone"), ("tenant_id", "owner", "operation_id")),
    "parent-versions": Member("cp6_engineering_revision_v1.working_versions",
                              ("tenant_id", "revision_id", "revision", "body_bytes", "body_digest", "actor_id", "recorded_at"),
                              ("uuid", "uuid", "bigint", "bytea", "text", "uuid", "timestamp with time zone"),
                              ("tenant_id", "revision_id", "revision")),
}
DEPENDENCIES = {"migrations": [assets.MIGRATION, PARENT_MIGRATION, JOURNAL_MIGRATION],
                "owner": assets.OWNER, "contract": CONTRACT, "parentOwner": assets.PARENT_OWNER,
                "parentScope": "referenced-working-versions", "storage": "LocalFileStore"}


def filename(key):
    return f"file-content-{key}.csv"


def rows_sql(tenant):
    rows = {key: f"SELECT {', '.join(part.columns)} FROM {part.relation} src WHERE src.tenant_id={q(tenant)}::uuid"
            for key, part in MEMBERS.items()}
    rows["operations"] += f" AND owner={q(assets.OWNER)}"
    rows["parent-versions"] += f""" AND EXISTS (
        SELECT FROM {MEMBERS['contents'].relation} c JOIN {PARENT_HEADS} h
          ON (h.tenant_id,h.revision_id)=(c.tenant_id,c.parent_id)
        WHERE (c.tenant_id,c.parent_id,c.parent_version::bigint,c.parent_digest)=
              (src.tenant_id,src.revision_id,src.revision,src.body_digest) AND src.revision<=h.revision)"""
    return rows


def source_owner(query, tenant):
    source = query(f"""
        SELECT json_build_object('owner',d.datdba::bigint,
            'source',NOT d.datistemplate AND d.datallowconn
                AND left(coalesce(shobj_description(d.oid,'pg_database'),''),{len(MARKER)})<>{q(MARKER)},
            'tenant',EXISTS(SELECT FROM public."Sys_Tenants" t WHERE t."Id"={q(tenant)}::uuid))
        FROM pg_database d WHERE d.datname=current_database();
        """)
    if not source["source"] or not source["tenant"]:
        raise archive.ArchiveError("file-content-history-source-rejected")
    return source["owner"]


def check_layout(query, owner_oid):
    def array(values):
        return "ARRAY[" + ",".join(map(q, values)) + "]"

    expected = ",\n".join(f"({q(member.relation)}," + array(
        name + ":" + typ for name, typ in zip(member.columns, member.types)) + "," + array(member.key) + ")"
        for member in MEMBERS.values())
    journal = MEMBERS["operations"].relation
    layout = query(f"""
        LOCK TABLE {PARENT_HEADS},{journal} IN ACCESS SHARE MODE;
        SELECT to_json(
            (SELECT count(*) FROM public."__EFMigrationsHistory"
             WHERE "MigrationId" IN ({q(PARENT_MIGRATION)},{q(JOURNAL_MIGRATION)}))=2
            AND NOT EXISTS (SELECT FROM (VALUES {expected}) e(relation,columns,key_columns)
                LEFT JOIN pg_class c ON c.oid=to_regclass(e.relation)
                LEFT JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE c.relkind IS DISTINCT FROM 'r' OR c.relowner IS DISTINCT FROM {owner_oid}::oid
                   OR n.nspowner IS DISTINCT FROM {owner_oid}::oid OR e.columns IS DISTINCT FROM (
                      SELECT array_agg(attname::text||':'||atttypid::regtype::text ORDER BY attnum)
                      FROM pg_attribute WHERE attrelid=c.oid AND attnum>0 AND NOT attisdropped)
                   OR e.key_columns IS DISTINCT FROM (
                      SELECT array_agg(a.attname::text ORDER BY k.seq)
                      FROM pg_constraint p CROSS JOIN LATERAL unnest(p.conkey) WITH ORDINALITY k(attnum,seq)
                      JOIN pg_attribute a ON a.attrelid=p.conrelid AND a.attnum=k.attnum
                      WHERE p.conrelid=c.oid AND p.contype='p' AND p.convalidated))
            AND EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid=to_regclass('{PARENT_HEADS}') AND c.relkind='r'
                  AND c.relowner={owner_oid}::oid AND n.nspowner={owner_oid}::oid)
            AND EXISTS (SELECT FROM pg_constraint WHERE conrelid=to_regclass('{MEMBERS['contents'].relation}')
                AND contype='u' AND convalidated AND conkey=ARRAY[11]::smallint[])
            AND (SELECT count(*) FROM (VALUES ('immutable','cp6_main_owner_v1.immutable()',58),
                      ('authority_gate','cp6_authentication_v1.before_change()',62)) e(name,fn,typ)
                 JOIN pg_trigger t ON t.tgrelid=to_regclass('{journal}') AND t.tgname=e.name
                   AND t.tgenabled IN ('O','A') AND NOT t.tgisinternal AND t.tgtype=e.typ
                   AND t.tgqual IS NULL AND t.tgfoid=to_regprocedure(e.fn))=2);
        """)
    if not layout:
        raise archive.ArchiveError("file-content-history-layout-rejected")


def capture(query, tenant, owner_oid):
    """Check the original graph in the caller's already pinned read-only snapshot."""
    file_count = assets.capture_count(query, tenant, owner_oid)
    check_layout(query, owner_oid)
    rows = rows_sql(tenant)
    data = query(f"""
        WITH contents AS MATERIALIZED ({rows['contents']}), heads AS MATERIALIZED ({rows['heads']}),
        events AS MATERIALIZED (SELECT e.*,convert_from(e.body_bytes,'UTF8')::jsonb body FROM ({rows['events']}) e),
        parents AS MATERIALIZED (SELECT p.*,convert_from(p.body_bytes,'UTF8')::jsonb body FROM ({rows['parent-versions']}) p),
        originals AS MATERIALIZED (
            SELECT o.*,convert_from(o.result_bytes,'UTF8')::jsonb result FROM ({rows['operations']}) o),
        matched AS MATERIALIZED (
            SELECT o.operation_id,e.tenant_id,e.file_id,e.revision FROM originals o JOIN events e
              ON (e.tenant_id,e.file_id,e.actor_id)=(o.tenant_id,o.object_ids[1],o.actor_id)
             AND o.object_ids=ARRAY[e.file_id] AND e.revision::text=o.result->>'Version'
             AND e.file_id::text=o.result->>'ObjectId' AND e.body_digest=o.result->>'Digest'
            WHERE o.state='COMMITTED' AND o.result->>'Kind'='FILE_CONTENT' AND jsonb_typeof(o.result->'Version')='string'),
        bad AS (
            SELECT 1 FROM contents c LEFT JOIN heads h USING(tenant_id,file_id)
            LEFT JOIN parents p ON (p.tenant_id,p.revision_id,p.revision,p.body_digest)=
                (c.tenant_id,c.parent_id,c.parent_version::bigint,c.parent_digest)
            WHERE h.file_id IS NULL OR p.revision_id IS NULL
            UNION ALL
            SELECT 1 FROM parents p WHERE p.body_digest<>encode(sha256(p.body_bytes),'hex')
                OR p.body->>'tenantId' IS DISTINCT FROM p.tenant_id::text
                OR p.body->>'id' IS DISTINCT FROM p.revision_id::text
                OR p.body->>'version' IS DISTINCT FROM p.revision::text
            UNION ALL
            SELECT 1 FROM heads h LEFT JOIN events e USING(tenant_id,file_id)
            GROUP BY h.tenant_id,h.file_id,h.revision
            HAVING min(e.revision) IS DISTINCT FROM 1::bigint OR max(e.revision) IS DISTINCT FROM h.revision
                OR count(e.revision)<>h.revision
            UNION ALL
            SELECT 1 FROM events e WHERE e.body->>'tenantId' IS DISTINCT FROM e.tenant_id::text
                OR e.body->>'id' IS DISTINCT FROM e.file_id::text OR e.body->>'version' IS DISTINCT FROM e.revision::text
                OR e.body->>'actorId' IS DISTINCT FROM e.actor_id::text OR e.body_digest<>encode(sha256(e.body_bytes),'hex')
            UNION ALL
            SELECT 1 FROM originals o WHERE o.contract IS DISTINCT FROM {q(CONTRACT)}
                OR jsonb_typeof(o.result) IS DISTINCT FROM 'object'
                OR o.result->>'OperationId' IS DISTINCT FROM o.operation_id::text
                OR CASE o.state WHEN 'CLOSED' THEN o.input_bytes IS NOT NULL OR o.input_sha256 IS NOT NULL
                      OR cardinality(o.object_ids)<>0 OR o.result->>'State' IS DISTINCT FROM 'CLOSED'
                      OR o.result->>'Result' IS NOT NULL
                   WHEN 'COMMITTED' THEN o.input_bytes IS NULL OR o.input_sha256 IS DISTINCT FROM encode(sha256(o.input_bytes),'hex')
                      OR NOT EXISTS(SELECT FROM matched m WHERE m.operation_id=o.operation_id)
                   ELSE true END
            UNION ALL
            SELECT 1 FROM events e LEFT JOIN matched m USING(tenant_id,file_id,revision)
            GROUP BY e.tenant_id,e.file_id,e.revision HAVING count(m.operation_id)<>1)
        SELECT json_build_object('valid',NOT EXISTS(SELECT FROM bad),
            'contents',(SELECT count(*) FROM contents),'heads',(SELECT count(*) FROM heads),
            'events',(SELECT count(*) FROM events),'operations',(SELECT count(*) FROM originals),
            'parent-versions',(SELECT count(*) FROM parents));
        """, failure="file-content-history-graph-read-failed (see native logs)")
    if not data.pop("valid") or data["contents"] != file_count or data["heads"] != file_count:
        raise archive.ArchiveError("file-content-history-graph-rejected")
    return rows, data


def member_shape(part):
    return {"relation": part.relation, "columns": list(part.columns), "types": list(part.types), "key": list(part.key)}


def verify_assets(bundle, tenant, count, root):
    seen, previous = 0, 0
    with (bundle / filename("contents")).open(encoding="utf-8", newline="") as stream:
        rows = csv.DictReader(stream, strict=True)
        if rows.fieldnames != list(MEMBERS["contents"].columns):
            raise archive.ArchiveError("file-content-history-member-header-rejected")
        for row in rows:
            seen += 1
            if seen > count or None in row or None in row.values() or row["tenant_id"] != str(tenant):
                raise archive.ArchiveError("file-content-history-content-rejected")
            file_id, length, digest = archive.guid(row["file_id"]).int, int(row["byte_length"]), row["digest"]
            if file_id <= previous or not 1 <= length <= assets.MAX_BYTES or not re.fullmatch(r"[0-9a-f]{64}", digest):
                raise archive.ArchiveError("file-content-history-content-rejected")
            assets.check_key(tenant, row["store_path"])
            assets.verify_file(root, row["store_path"], length, digest)
            previous = file_id
    if seen != count:
        raise archive.ArchiveError("file-content-history-content-count-mismatch")


def checked_output(output, root):
    if not output.is_absolute() or not output.name or output.name == ".." or (os.name == "nt" and (
            output.is_reserved() or output.name.endswith((".", " ")) or ":" in output.name)):
        raise archive.ArchiveError("file-content-history-output-rejected")
    parent = assets.checked_root(output.parent).resolve(strict=True)
    output = parent / output.name
    if root == output or root in output.parents or output in root.parents:
        raise archive.ArchiveError("file-content-history-output-overlaps-assets")
    return output


def export_bundle(pg_bin, target, username, tenant, output, file_content_root, *, on_snapshot=None):
    root = assets.checked_root(file_content_root).resolve(strict=True)
    output = checked_output(output, root)
    if not username or "\0" in username:
        raise archive.ArchiveError("invalid-username")
    archive.private_directory(output)
    bundle, logs = output / "bundle", output / "native"
    bundle.mkdir(mode=0o700)
    logs.mkdir(mode=0o700)
    connection = archive.connection_args(target, username)
    with archive.snapshot_session(pg_bin, connection, os.environ, logs) as query:
        archive.check_physical_source(query, target)
        owner_oid = source_owner(query, tenant)
        rows, counts = capture(query, tenant, owner_oid)
        snapshot = query("""
            SELECT json_build_object('id',pg_export_snapshot(),'visibility',pg_current_snapshot()::text,
                'walLsn',pg_current_wal_lsn()::text,'capturedAt',statement_timestamp(),
                'serverVersion',current_setting('server_version'));
            """)
        if on_snapshot is not None:
            on_snapshot(snapshot["id"])
        for key, part in MEMBERS.items():
            archive.copy_snapshot(pg_bin, connection, os.environ, logs, bundle / filename(key), key + "-copy",
                                  snapshot["id"], rows[key] + " ORDER BY " + ", ".join(part.key), archive.JOURNAL_COPY_SETTINGS)
    verify_assets(bundle, tenant, counts["contents"], root)
    manifest = {"format": FORMAT, "tenantId": str(tenant), "source": target.mapping(), "snapshot": snapshot,
                "dependencies": DEPENDENCIES, "members": {}, "recoveryReady": False}
    for key, part in MEMBERS.items():
        name = filename(key)
        manifest["members"][name] = {**member_shape(part), "rows": counts[key], **archive.file_digest(bundle / name)}
    with (bundle / MANIFEST).open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, indent=2, ensure_ascii=False)
        stream.write("\n")
    return bundle


def verify_bundle(bundle, tenant, pin, file_content_root):
    bundle = assets.checked_root(bundle).resolve(strict=True)
    names = {filename(key) for key in MEMBERS}
    if {entry.name for entry in bundle.iterdir()} != names | {MANIFEST}:
        raise archive.ArchiveError("file-content-history-member-set-rejected")
    for name in names | {MANIFEST}:
        assets.entry(bundle / name, stat.S_ISREG, "file-content-history-member-rejected")
    manifest_bytes = (bundle / MANIFEST).read_bytes()
    if not re.fullmatch(r"[0-9a-f]{64}", pin) or hashlib.sha256(manifest_bytes).hexdigest() != pin:
        raise archive.ArchiveError("file-content-history-manifest-pin-rejected")
    manifest = json.loads(manifest_bytes)
    if not isinstance(manifest, dict) or manifest.get("format") != FORMAT or manifest.get("tenantId") != str(tenant) \
            or manifest.get("recoveryReady") is not False or manifest.get("dependencies") != DEPENDENCIES:
        raise archive.ArchiveError("file-content-history-manifest-rejected")
    archive.PhysicalTarget.from_mapping(manifest.get("source"))
    snapshot = manifest.get("snapshot")
    if not isinstance(snapshot, dict) or snapshot.keys() != {"id", "visibility", "walLsn", "capturedAt", "serverVersion"} \
            or any(not isinstance(value, str) or not value for value in snapshot.values()):
        raise archive.ArchiveError("file-content-history-snapshot-rejected")
    members = manifest.get("members")
    if not isinstance(members, dict) or members.keys() != names:
        raise archive.ArchiveError("file-content-history-manifest-members-rejected")
    for key, part in MEMBERS.items():
        path = bundle / filename(key)
        member = members[path.name]
        count = member.get("rows") if isinstance(member, dict) else None
        if type(count) is not int or count < 0 or member != {**member_shape(part), "rows": count, **archive.file_digest(path)}:
            raise archive.ArchiveError("file-content-history-member-digest-rejected")
        with path.open("rb") as stream:
            if stream.readline(4096).rstrip(b"\r\n") != ",".join(part.columns).encode("ascii"):
                raise archive.ArchiveError("file-content-history-member-header-rejected")
    root = assets.checked_root(file_content_root).resolve(strict=True)
    verify_assets(bundle, tenant, members[filename("contents")]["rows"], root)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    export = commands.add_parser("export")
    verify = commands.add_parser("verify")
    for command in (export, verify):
        command.add_argument("--tenant", required=True)
        command.add_argument("--file-content-root", type=Path, required=True)
    for option in ("pg-bin", "target-file", "output"):
        export.add_argument("--" + option, type=Path, required=True)
    export.add_argument("--username", required=True)
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--manifest-sha256", required=True)
    args = parser.parse_args()
    try:
        tenant = archive.guid(args.tenant)
        if args.action == "export":
            target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
            bundle = export_bundle(args.pg_bin.resolve(strict=True), target, args.username, tenant,
                                   args.output.absolute(), args.file_content_root)
            pin = archive.file_digest(bundle / MANIFEST)["sha256"]
            result = {"bundle": str(bundle), "manifestSha256": pin}
        else:
            verify_bundle(args.bundle.absolute(), tenant, args.manifest_sha256, args.file_content_root)
            result = {"manifestSha256": args.manifest_sha256}
        print(json.dumps({**result, "format": FORMAT, "tenantId": str(tenant), "recoveryReady": False}))
    except (archive.ArchiveError, OSError, ValueError, csv.Error, subprocess.SubprocessError) as error:
        print("FileContent history rejected:", error, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
