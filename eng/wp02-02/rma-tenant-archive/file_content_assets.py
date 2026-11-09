"""Same-cut external LocalFileStore dependencies for the original FileContent Owner."""

import csv
import hashlib
import os
import re
import stat
from uuid import UUID


FORMAT = "cp6-rma-source-archive-v7"
MEMBER = "file-content-assets.csv"
MIGRATION = "20261029030000_FileContentV1"
OWNER = "Main.File.Content.v1"
PARENT_OWNER = "Main.Plm.EngineeringRevision.v1"
MAX_BYTES = 20 * 1024 * 1024
COPY_SETTINGS = "SET LOCAL row_security=off;"
COLUMNS = ("tenant_id", "file_id", "parent_id", "parent_version", "parent_digest", "file_name", "digest",
           "byte_length", "media_type", "store_path", "state_version", "state_etag", "state_digest")
RELATIONS = ("cp6_file_content_v1.contents", "cp6_file_content_v1.heads", "cp6_file_content_v1.events",
             "cp6_engineering_revision_v1.working_versions")


def capture_count(query, tenant, owner_oid):
    original = query("""
        LOCK TABLE {relations} IN ACCESS SHARE MODE;
        SET LOCAL row_security=off;
        SELECT json_build_object(
            'adopted',EXISTS (SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"='{migration}'),
            'owned',(SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE c.oid IN ({catalog}) AND c.relkind='r' AND c.relowner={owner}::oid AND n.nspowner={owner}::oid)=4
                AND EXISTS (SELECT FROM pg_proc WHERE oid='cp6_file_content_v1.ready()'::regprocedure AND proowner={owner}::oid));
    """.format(relations=", ".join(RELATIONS), catalog=", ".join("'{}'::regclass".format(rel) for rel in RELATIONS),
               migration=MIGRATION, owner=owner_oid),
        failure="file-content-snapshot-read-failed (see native logs)")
    if not (original["adopted"] and original["owned"]):
        raise ValueError("file-content-original-authority-rejected")
    captured = query("""
        SELECT json_build_object('ready',cp6_file_content_v1.ready(),
            'count',(SELECT count(*) FROM cp6_file_content_v1.contents WHERE tenant_id='{}'::uuid));
    """.format(tenant), failure="file-content-snapshot-read-failed (see native logs)")
    if not captured["ready"]:
        raise ValueError("file-content-original-authority-rejected")
    return captured["count"]


def rows_sql(tenant):
    # The content binds the exact historical parent version. The head selects the current state event at this cut.
    return """
        SELECT c.tenant_id,c.file_id,c.parent_id,c.parent_version,c.parent_digest,c.file_name,c.digest,c.byte_length,
            c.media_type,c.store_path,h.revision AS state_version,
            convert_from(e.body_bytes,'UTF8')::jsonb->>'etag' AS state_etag,e.body_digest AS state_digest
        FROM cp6_file_content_v1.contents c
        JOIN cp6_file_content_v1.heads h USING (tenant_id,file_id)
        JOIN cp6_file_content_v1.events e ON e.tenant_id=c.tenant_id AND e.file_id=c.file_id AND e.revision=h.revision
        JOIN cp6_engineering_revision_v1.working_versions p ON p.tenant_id=c.tenant_id AND p.revision_id=c.parent_id
            AND p.revision=c.parent_version::bigint AND p.body_digest=c.parent_digest
        WHERE c.tenant_id='{}'::uuid ORDER BY c.file_id
    """.format(tenant)


def manifest_section(count):
    return {"sourceSchema": "cp6_file_content_v1", "migrationId": MIGRATION, "member": MEMBER, "columns": list(COLUMNS),
            "contentReference": {"owner": OWNER, "version": "1", "hashAlgorithm": "SHA256"},
            "parentOwner": PARENT_OWNER, "storage": "LocalFileStore", "count": count}


def matches_manifest(manifest):
    section = manifest.get("fileContentAssets")
    count = section.get("count") if isinstance(section, dict) else None
    return type(count) is int and count >= 0 and section == manifest_section(count)


def entry(path, expected_kind, error):
    status = path.lstat()
    reparse = getattr(status, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    if reparse or not expected_kind(status.st_mode):
        raise ValueError(error)
    return status


def checked_root(root):
    if root is None:
        raise ValueError("file-content-root-required")
    if not root.is_absolute() or ".." in root.parts:
        raise ValueError("file-content-root-rejected")
    if os.name == "nt":
        from ctypes import windll
        if root.anchor.startswith("\\\\") or windll.kernel32.GetDriveTypeW(str(root.anchor)) not in (2, 3, 5, 6):
            raise ValueError("file-content-root-rejected")
    try:
        for part in (*reversed(root.parents), root):
            entry(part, stat.S_ISDIR, "file-content-root-rejected")
    except FileNotFoundError as error:
        raise ValueError("file-content-root-rejected") from error
    return root


def verify_file(root, key, length, expected, *, sink=None):
    relative = key.split("/")
    namespace = root / relative[0]
    directory = namespace / relative[1]
    path = directory / relative[2]
    try:
        for part in (namespace, directory):
            entry(part, stat.S_ISDIR, "file-content-asset-path-rejected")
        original = entry(path, stat.S_ISREG, "file-content-asset-path-rejected")
        if path.resolve(strict=True) != path:
            raise ValueError("file-content-asset-path-rejected")
        with path.open("rb") as stream:
            opened = os.fstat(stream.fileno())
            if not os.path.samestat(original, opened):
                raise ValueError("file-content-asset-path-rejected")
            if opened.st_size != length:
                raise ValueError("file-content-asset-length-mismatch")
            digest = hashlib.sha256()
            remaining = length
            while remaining:
                chunk = stream.read(min(remaining, 1024 * 1024))
                if not chunk:
                    break
                digest.update(chunk)
                if sink is not None and sink.write(chunk) != len(chunk):
                    raise OSError("file-content-short-copy")
                remaining -= len(chunk)
            if remaining or stream.read(1):
                raise ValueError("file-content-asset-length-mismatch")
            after = os.fstat(stream.fileno())
            current = entry(path, stat.S_ISREG, "file-content-asset-path-rejected")
            if not os.path.samestat(opened, current) or opened.st_mtime_ns != after.st_mtime_ns or after.st_size != length:
                raise ValueError("file-content-asset-changed")
            if digest.hexdigest() != expected:
                raise ValueError("file-content-asset-digest-mismatch")
    except FileNotFoundError as error:
        raise ValueError("file-content-asset-not-found") from error


def check_key(tenant, key):
    prefix = "native-content-v1/" + tenant.hex + "/"
    if not key.startswith(prefix) or not re.fullmatch(r"[0-9a-f]{32}", key[len(prefix):]) \
            or key[len(prefix):] == "0" * 32:
        raise ValueError("file-content-asset-key-rejected")


def member_files(path, tenant, count):
    seen = 0
    previous = 0
    with path.open(encoding="utf-8", newline="") as stream:
        rows = csv.DictReader(stream, strict=True)
        if rows.fieldnames != list(COLUMNS):
            raise ValueError("file-content-member-header-rejected")
        for row in rows:
            seen += 1
            if seen > count or None in row or None in row.values() or row["tenant_id"] != str(tenant):
                raise ValueError("file-content-member-row-rejected")
            file_id = UUID(row["file_id"]).int
            length = int(row["byte_length"])
            if file_id <= previous or not UUID(row["parent_id"]).int or not UUID(row["state_etag"]).int \
                    or not 1 <= length <= MAX_BYTES or not 0 < int(row["parent_version"]) < 2**63 \
                    or not 0 < int(row["state_version"]) < 2**63 \
                    or any(not re.fullmatch(r"[0-9a-f]{64}", row[name]) for name in ("digest", "parent_digest", "state_digest")):
                raise ValueError("file-content-reference-rejected")
            key = row["store_path"]
            check_key(tenant, key)
            yield key, length, row["digest"]
            previous = file_id
    if seen != count:
        raise ValueError("file-content-reference-count-mismatch")


def verify_member(path, tenant, count, root):
    for key, length, digest in member_files(path, tenant, count):
        verify_file(root, key, length, digest)
