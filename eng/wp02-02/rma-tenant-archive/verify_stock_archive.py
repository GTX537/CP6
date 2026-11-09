#!/usr/bin/env python3
"""Focused native-cut assertions for a guarded v10 source fixture."""

import csv
import json
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
import archive


def require(value, message):
    if not value:
        raise AssertionError(message)


def source_options(assets):
    return dict(include_original_journal=True, include_rma_source=True, include_rma_visibility=True,
                include_identity_priority=True, include_rma_iam_references=True, file_content_root=assets,
                include_erp_integration=True, include_privacy_evidence=True, include_main_stock_outbox=True)


def call(evidence, label, command, *, expected=0):
    run = subprocess.run(list(map(str, command)), stdin=subprocess.DEVNULL, capture_output=True,
                         timeout=140, creationflags=archive.FLAGS)
    (evidence / (label + ".stdout")).write_bytes(run.stdout)
    (evidence / (label + ".stderr")).write_bytes(run.stderr)
    require(run.returncode == expected, label + " returned " + str(run.returncode))
    return run.stdout, run.stderr


def export_cut(pg_bin, target, username, installation, tenant, assets, baseline, evidence, late_writer, *, status):
    tenant = str(tenant)
    stock = archive.stock_outbox_module()
    before = archive.verify_bundle(baseline, tenant, file_content_root=assets)
    bundle = archive.export_bundle(pg_bin, target, username, installation, tenant, evidence / "v10-cut",
                                   **source_options(assets), on_snapshot=late_writer)
    manifest = archive.verify_bundle(bundle, tenant, file_content_root=assets)
    require(manifest["format"] == archive.FORMAT_WITH_STOCK_OUTBOX and manifest["source"] == target.mapping()
            and manifest["tenantId"] == str(tenant) and manifest["recoveryReady"] is False
            and manifest["counts"] == before["counts"] and manifest["stockOutbox"] == before["stockOutbox"]
            and manifest["privacyEvidence"] == before["privacyEvidence"], "manifest left the original source or pre-cut scope")
    require(manifest["members"].keys() == before["members"].keys()
            and all(manifest["members"][member] == before["members"][member]
                    for member in manifest["members"] if member != "rma.pgcustom"), "v9 or Stock CSV changed across the cut")
    schema_dump = []
    for label, source in (("before", baseline), ("cut", bundle)):
        native = [archive.binary(pg_bin, "pg_restore"), "--data-only", "--file=-", source / "rma.pgcustom"]
        raw, err = call(evidence, "rma-data-" + label, native)
        require(not err, "native RMA logical inspection failed")
        # PostgreSQL 18 emits a different protective nonce for each SQL stream.
        schema_dump.append([line for line in raw.splitlines() if not line.startswith((b"\\restrict ", b"\\unrestrict "))])
    require(schema_dump[0] == schema_dump[1], "archived RMA rows changed across the cut")

    prefix = archive.STOCK_MEMBER_PREFIX
    tables = manifest["stockOutbox"]["tables"]
    require(set(tables) == {prefix + name for name in stock.MEMBERS}, "incomplete Stock slice")
    for item in (stock.AUTHORITY, stock.ROUTE_TABLE, stock.NATIVE, stock.SOURCE, stock.BINDING, stock.JOURNAL):
        require(tables[prefix + item.member]["count"] == 1, "missing pre-cut Main Stock original: " + item.name)
    for item in (stock.RUN, stock.CLAIM, stock.TICKET, stock.MUTATION, stock.SEND):
        require(tables[prefix + item.member]["count"] == int(status == 2), "unexpected pre-cut companion count: " + item.name)
    with (bundle / (prefix + stock.NATIVE.member)).open(encoding="utf-8", newline="") as file:
        original, = csv.DictReader(file, strict=True)
    require(int(original["Status"]) == status and original["TenantId"] == tenant, "pre-cut message is outside A or wrong state")
    return bundle, manifest


def verify_boundaries(bundle, tenant, assets, evidence):
    cli = [sys.executable, "-B", archive.__file__, "verify", "--tenant", tenant, "--file-content-root", assets]
    result, err = call(evidence, "verify-cut", [*cli, "--bundle", bundle])
    require(not err and json.loads(result)["format"] == archive.FORMAT_WITH_STOCK_OUTBOX, "normal CLI rejected v10")
    ticket = archive.STOCK_MEMBER_PREFIX + archive.stock_outbox_module().TICKET.member
    for label, expected in (("missing-stock", b"bundle-member-mismatch"), ("changed-stock", b"bundle-digest-mismatch")):
        private = evidence / label
        shutil.copytree(bundle, private)
        if label == "missing-stock":
            (private / ticket).unlink()
        else:
            with (private / ticket).open("ab") as file:
                file.write(b"private changed original receipt\n")
        _, err = call(evidence, "verify-" + label, [*cli, "--bundle", private], expected=1)
        require(expected in err, "wrong failure for " + label)
    foreign = evidence / "foreign-stock"
    shutil.copytree(bundle, foreign)
    member = archive.STOCK_MEMBER_PREFIX + archive.stock_outbox_module().AUTHORITY.member
    (foreign / member).write_bytes((foreign / member).read_bytes().replace(str(tenant).encode(), b"00000000-0000-0000-0000-000000000001"))
    changed = json.loads((foreign / "manifest.json").read_bytes())
    changed["members"][member] = archive.file_digest(foreign / member)
    (foreign / "manifest.json").write_text(json.dumps(changed), encoding="utf-8")
    _, err = call(evidence, "verify-foreign-stock", [*cli, "--bundle", foreign], expected=1)
    require(b"stock-member-scope-rejected: Authority" in err, "repinned foreign Stock member escaped the original tenant check")
    return archive.file_digest(bundle / "manifest.json")["sha256"]
