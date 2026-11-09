#!/usr/bin/env python3
"""Verify the FileContent history CLI against an existing synthetic Owner fixture."""

import argparse
import csv
from datetime import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from uuid import uuid4

import archive


CLI = Path(__file__).with_name("file_content_history.py")


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def offline():
    for action in ("export", "verify"):
        result = subprocess.run([sys.executable, "-B", str(CLI), action, "--help"], capture_output=True, timeout=15)
        sys.stderr.buffer.write(result.stderr)
        require(result.returncode == 0 and b"--file-content-root" in result.stdout,
                f"FileContent history {action} entry unavailable")


def invoke(output, env, label, action, arguments, rejection=None):
    result = subprocess.run([sys.executable, "-B", str(CLI), action, *map(str, arguments)], env=env,
                            capture_output=True, timeout=100, creationflags=archive.FLAGS)
    (output / (label + ".stdout")).write_bytes(result.stdout)
    (output / (label + ".stderr")).write_bytes(result.stderr)
    (output / (label + ".exit")).write_text(str(result.returncode), encoding="ascii")
    if rejection:
        require(result.returncode == 1 and not result.stdout and rejection.encode("ascii") in result.stderr,
                f"{label} wrong rejection")
        return None
    require(result.returncode == 0 and not result.stderr, f"{label} failed; see native output")
    return json.loads(result.stdout)


def export_args(pg_bin, target_file, username, tenant, assets, output):
    return ["--pg-bin", pg_bin, "--target-file", target_file, "--username", username, "--tenant", tenant,
            "--file-content-root", assets, "--output", output]


def path_boundaries(pg_bin, target_file, username, tenant, output, env):
    asset_root = output / "asset-input"
    asset_root.mkdir()
    invoke(output, env, "overlap", "export", export_args(pg_bin, target_file, username, tenant, asset_root,
           asset_root / "unwanted"), "file-content-history-output-overlaps-assets")
    require(not (asset_root / "unwanted").exists(), "output appeared inside assets")
    if os.name == "nt":
        junction = output / "asset-junction"
        subprocess.run(["cmd.exe", "/d", "/c", "mklink", "/J", str(junction), str(asset_root)], check=True,
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, creationflags=archive.FLAGS, timeout=10)
        try:
            invoke(output, env, "junction-export", "export", export_args(pg_bin, target_file, username, tenant,
                   asset_root, junction / "unwanted"), "file-content-root-rejected")
            invoke(output, env, "junction-verify", "verify", ["--bundle", junction, "--tenant", tenant,
                   "--manifest-sha256", "0" * 64, "--file-content-root", asset_root], "file-content-root-rejected")
        finally:
            junction.rmdir()
        for label, leaf in (("reserved", "NUL.txt"), ("trailing", "trimmed."), ("ads", "base:stream")):
            invoke(output, env, label, "export", export_args(pg_bin, target_file, username, tenant, asset_root,
                   output / leaf), "file-content-history-output-rejected")
        require(not (asset_root / "unwanted").exists() and not list(asset_root.iterdir()), "junction alias wrote assets")


def read_rows(path):
    with path.open(encoding="utf-8", newline="") as stream:
        return list(csv.DictReader(stream, strict=True))


def timestamp(value):
    value = re.sub(r"\.([0-9]+)", lambda match: "." + match[1].ljust(6, "0"), value)
    return datetime.fromisoformat(value + ":00" if re.search(r"[+-][0-9]{2}$", value) else value)


def native_value(value, typ):
    if typ == "bigint":
        return int(value)
    if typ == "timestamp with time zone":
        return timestamp(value)
    if typ == "jsonb":
        return json.loads(value)
    if typ == "uuid[]":
        return value[1:-1].split(",") if value != "{}" else []
    return value


def compare_rows(part, path, source):
    rows = read_rows(path)
    require(len(rows) == len(source), f"{path.name} row count differs from W383 raw original")
    existing = {tuple(str(row[key]) for key in part.key): row for row in source}
    for row in rows:
        expected = existing.pop(tuple(row[key] for key in part.key))
        for name, typ in zip(part.columns, part.types):
            old = timestamp(expected[name]) if typ == "timestamp with time zone" else expected[name]
            require(native_value(row[name], typ) == old, f"{path.name} original {name} differs")
    require(not existing, f"{path.name} lost original keys")


def native(f, source, candidate, tenant, asset_root, originals):
    import file_content_history as history

    out = f.output
    tenant_id = archive.guid(tenant)
    source_file, candidate_file, wrong_file = (out / (label + "-target.json") for label in ("source", "candidate", "wrong"))
    for path, target in ((source_file, source.mapping()), (candidate_file, candidate.mapping()),
                         (wrong_file, {**source.mapping(), "DatabaseOid": source.database_oid + 1})):
        path.write_text(json.dumps(target), encoding="utf-8")

    def export(label, target=source_file, subject=tenant, rejection=None):
        return invoke(out, f.env, label, "export", export_args(f.pg_bin, target, f.username, subject, asset_root,
                      out / label), rejection)

    def verify(label, bundle, pin, root=asset_root, rejection=None):
        return invoke(out, f.env, label, "verify", ["--bundle", bundle, "--tenant", tenant,
                      "--manifest-sha256", pin, "--file-content-root", root], rejection)

    original = json.loads((originals / "source-before.json").read_bytes())
    result = json.loads((originals / "result.json").read_bytes())
    first = export("true-owner-export")
    bundle, pin = Path(first["bundle"]), first["manifestSha256"]
    verified = verify("verify-owner-export", bundle, pin)
    require(verified == {key: value for key, value in first.items() if key != "bundle"}, "pinned verify result differs")
    for key, raw in (("contents", "contents"), ("heads", "heads"), ("events", "events"), ("operations", "journal")):
        compare_rows(history.MEMBERS[key], bundle / history.filename(key), original[raw])
    parents = read_rows(bundle / history.filename("parent-versions"))
    expect_parents = {(asset["Parent"]["Id"], asset["Parent"]["Version"], asset["Parent"]["Digest"]) for asset in result["assets"]}
    require({(p["revision_id"], p["revision"], p["body_digest"]) for p in parents} == expect_parents
            and all(p["tenant_id"] == tenant and hashlib.sha256(bytes.fromhex(p["body_bytes"][2:])).hexdigest() == p["body_digest"]
                    for p in parents), "exact historical Engineering parent differs")
    export("wrong-target", wrong_file, rejection="target-identity-rejected")
    export("closed-candidate-rejected", candidate_file, rejection="file-content-history-source-rejected")
    verify("wrong-pin", bundle, "0" * 64, rejection="file-content-history-manifest-pin-rejected")
    modified = out / "modified-bundle"
    shutil.copytree(bundle, modified)
    with (modified / history.filename("events")).open("ab") as stream:
        stream.write(b"\n")
    verify("changed-event-file", modified, pin, rejection="file-content-history-member-digest-rejected")
    changed_assets = out / "changed-assets"
    shutil.copytree(asset_root, changed_assets)
    wrong_body = changed_assets / result["assets"][0]["StoreKey"]
    body = wrong_body.read_bytes()
    wrong_body.write_bytes(bytes([body[0] ^ 1]) + body[1:])
    verify("same-length-wrong-asset", bundle, pin, root=changed_assets, rejection="file-content-asset-digest-mismatch")

    # The closed W383 database supplies actual v2 history evidence, not an eligible export source.
    candidate_graph = out / "closed-candidate-component"
    candidate_graph.mkdir()
    conn = archive.connection_args(candidate, f.username)
    with archive.snapshot_session(f.pg_bin, conn, f.env, candidate_graph) as query:
        archive.check_physical_source(query, candidate)
        owner = query("SELECT to_json(datdba::bigint) FROM pg_database WHERE datname=current_database();")
        sql, candidate_counts = history.capture(query, tenant_id, owner)
        snapshot = query("SELECT to_json(pg_export_snapshot());")
        for key in ("events", "operations"):
            archive.copy_snapshot(f.pg_bin, conn, f.env, candidate_graph, candidate_graph / history.filename(key), key,
                                  snapshot, sql[key], archive.JOURNAL_COPY_SETTINGS)
    require(candidate_counts == {"contents": 3, "heads": 3, "events": 5, "operations": 5, "parent-versions": 1},
            "closed-candidate actual history differs")
    after = json.loads((originals / "candidate-after.json").read_bytes())
    for key, raw in (("events", "events"), ("operations", "journal")):
        compare_rows(history.MEMBERS[key], candidate_graph / history.filename(key), after[raw])

    q = archive.literal
    shared_op = original["journal"][0]["operation_id"]
    late_op = str(uuid4())
    actor = result["fixtureIdentity"]["Actor"]
    foreign = json.loads(f.sql("actual-foreign-tenant", source, f"""
        SELECT to_json((SELECT "Id" FROM public."Sys_Tenants" WHERE "Id"<>{q(tenant)}
            AND "Id"<>'00000000-0000-0000-0000-000000000000' ORDER BY "Id" LIMIT 1));
        """))
    require(foreign and foreign != tenant, "missing existing independent tenant fixture")

    def closed(subject, operation):
        return f"""INSERT INTO {archive.JOURNAL_TABLE} ({', '.join(archive.JOURNAL_COLUMNS[:-1])})
          VALUES ({q(subject)},{q(history.assets.OWNER)},{q(operation)},{q(actor)},{q(history.CONTRACT)},'CLOSED',NULL,NULL,
            convert_to(json_build_object('OperationId',{q(operation)}::uuid,'State','CLOSED','Result',NULL)::text,'UTF8'),
            ARRAY[]::uuid[],'{{"fixture":"late-close"}}');"""

    cut = history.export_bundle(f.pg_bin, source, f.username, tenant_id, out / "same-cut", asset_root,
        on_snapshot=lambda snapshot: f.sql("late-after-holder", source, "BEGIN; "
            "DO $$ BEGIN PERFORM cp6_authentication_v1.acquire(); END $$; " + closed(tenant, late_op) + closed(foreign, shared_op)
            + " COMMIT; SELECT json_build_object('pid',pg_backend_pid(),'cut'," + q(snapshot) + ");"))
    require(all((cut / history.filename(key)).read_bytes() == (bundle / history.filename(key)).read_bytes()
                for key in history.MEMBERS), "late writes leaked into original holder cut")
    current = Path(export("current-after-cut")["bundle"])
    operations = read_rows(current / history.filename("operations"))
    closed_rows = [row for row in operations if row["state"] == "CLOSED"]
    require(len(operations) == 4 and len(closed_rows) == 1 and closed_rows[0]["operation_id"] == late_op
            and all(row["tenant_id"] == tenant for row in operations), "current tenant CLOSED or foreign key differs")
    foreign_bundle = Path(export("foreign-after-cut", subject=foreign)["bundle"])
    b_originals = read_rows(foreign_bundle / history.filename("operations"))
    require(len(b_originals) == 1 and b_originals[0]["operation_id"] == shared_op and b_originals[0]["state"] == "CLOSED"
            and b_originals[0]["tenant_id"] == foreign, "foreign independent original key was overwritten")

    # A native but unpaired receipt in the disposable fixture must never produce a completed manifest.
    bad = str(uuid4())
    file_id = original["contents"][0]["file_id"]
    f.sql("unpaired-native-original", source, f"""
        BEGIN; DO $$ BEGIN PERFORM cp6_authentication_v1.acquire(); END $$;
        INSERT INTO {archive.JOURNAL_TABLE} ({', '.join(archive.JOURNAL_COLUMNS[:-1])})
        VALUES ({q(tenant)},{q(history.assets.OWNER)},{q(bad)},{q(actor)},{q(history.CONTRACT)},'COMMITTED',
            convert_to('{{}}','UTF8'),encode(sha256(convert_to('{{}}','UTF8')),'hex'),
            convert_to(json_build_object('OperationId',{q(bad)}::uuid,'Kind','FILE_CONTENT','ObjectId',{q(file_id)}::uuid,
              'Version','1','Digest',repeat('0',64))::text,'UTF8'),ARRAY[{q(file_id)}::uuid],'{{}}');
        COMMIT;
        """)
    export("bad-native-graph", rejection="file-content-history-graph-rejected")
    require(not (out / "bad-native-graph/bundle/manifest.json").exists(), "invalid native graph published a manifest")
    return dict(ManifestSha256=pin, GenuineOwnerCounts={key: len(read_rows(bundle / history.filename(key)))
                for key in history.MEMBERS}, ClosedCandidateCounts=candidate_counts,
                LateSyntheticClose=late_op, ForeignSameKey=shared_op, ForeignTenant=foreign)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", required=True)
    parser.parse_args()
    offline()
