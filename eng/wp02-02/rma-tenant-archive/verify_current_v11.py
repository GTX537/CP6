#!/usr/bin/env python3
"""Verify current revocation, epoch and global-key decisions on an explicitly disposable v11 source."""

import argparse
import json
import os
from pathlib import Path
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import erp_integration
import reconcile_erp_deliveries as erp
from verify_erp_archive import Fixture, require
from verify_erp_delivery_reconcile import guarded_change


ENTRIES = {"iam": ("reconcile_iam.py", "iam-reconciliation.jsonl"),
           "erp": ("reconcile_erp_deliveries.py", "reconciliation.jsonl"),
           "stock": ("reconcile_stock_outbox.py", "reconciliation.jsonl")}


def read_current(f, kind, label, expected=0):
    script, name = ENTRIES[kind]
    out = f.root / label
    stdout, stderr = f.run(label, [sys.executable, "-B", str(Path(__file__).with_name(script)),
        "--pg-bin", str(f.args.pg_bin), "--target-file", str(f.args.target_file), "--username", f.args.username,
        "--tenant", f.args.tenant, "--bundle", str(f.args.bundle), "--manifest-sha256", f.args.manifest_sha256,
        "--file-content-root", str(f.args.file_content_root), "--output", str(out)], expected=expected)
    require(not stderr and json.loads(stdout)["recoveryReady"] is False, "current command changed recovery admission")
    return [json.loads(row) for row in (out / name).read_bytes().splitlines()]


def protected(f, stock, label):
    a, b = map(archive.literal, (f.args.tenant, f.args.foreign_tenant))
    parts = []
    tables = [(stock.relation(t), '"TenantId"' if any(name == "TenantId" for name, _ in t.fields) else "tenant_id")
              for t in stock.TABLES if any(name in ("TenantId", "tenant_id") for name, _ in t.fields)]
    tables += [(erp_integration.relation(t, erp_integration.SCHEMA), '"TenantId"') for t in erp_integration.TABLES]
    for relation, tenant_column in tables:
        parts.append(f"{archive.literal(relation)},(SELECT jsonb_build_array(count(*),"
                     f"encode(sha256(convert_to(coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)),'[]'::jsonb)::text,"
                     f"'UTF8')),'hex')) FROM {relation} t WHERE {tenant_column}={b}::uuid)")
    body = "SELECT jsonb_build_object(" + ",".join(parts) + f""",
        'tenantB',(SELECT to_jsonb(t) FROM public."Sys_Tenants" t WHERE "Id"={b}::uuid),
        'rmaA',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM t_{archive.guid(f.args.tenant).hex}.heads t),
        'rmaB',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM t_{archive.guid(f.args.foreign_tenant).hex}.heads t),
        'dispatches',(SELECT count(*) FROM {stock.relation(stock.RUN)}),
        'sourceEnabled',(SELECT "Enable" FROM public."Sys_Tenants" WHERE "Id"={a}::uuid));"""
    raw, err = f.sql(label, "BEGIN READ ONLY; " + body + " COMMIT;")
    require(not err, "native protection snapshot failed")
    return json.loads(raw)


def global_keys(f, manifest):
    tenant = archive.guid(f.args.tenant)
    binding, _, _ = next(erp.archived_rows(f.args.bundle / erp.OUTBOX.member, erp.OUTBOX, tenant, 1))
    raw, err = f.sql("foreign-global-message", f"SELECT to_json(\"MessageId\"::text) FROM "
        f"{erp_integration.relation(erp.OUTBOX, erp_integration.SCHEMA)} "
        f"WHERE \"TenantId\"={archive.literal(f.args.foreign_tenant)}::uuid;")
    require(not err and raw.strip(), "real foreign global key required")
    foreign = json.loads(raw)
    probes = [dict(binding, MessageId=foreign), dict(binding, MessageId=str(uuid4()).replace("-", ""))]
    log = f.root / "native-global-key-probes"
    log.mkdir()
    with archive.snapshot_session(f.args.pg_bin, archive.connection_args(f.target, f.args.username), os.environ, log) as query:
        archive.check_source(query, f.target, archive.guid(manifest["installationId"]), tenant)
        observed = erp.lookup(query, erp.OUTBOX, tenant, probes)
    require(observed == [dict(decision="BLOCKED", reason="GLOBAL_KEY_CONFLICT"),
                         dict(decision="UNKNOWN", reason="ORIGINAL_KEY_NOT_FOUND")],
            "global conflict or unknown disclosed another tenant or invented an original")
    (f.root / "global-keys.json").write_text(json.dumps(observed, indent=2) + "\n", encoding="utf-8")
    return observed


def verify(args):
    f = Fixture(args)
    manifest = json.loads((args.bundle / "manifest.json").read_bytes())
    require(manifest["tenantId"] == args.tenant and manifest["source"] == f.target.mapping(), "same v11 source required")
    stock = archive.stock_outbox_module()
    before = protected(f, stock, "source-before")
    require(before["sourceEnabled"] is True, "enabled original source required")
    iam_before = read_current(f, "iam", "iam-original-current")
    erp_before = read_current(f, "erp", "erp-original-current")
    stock_before = read_current(f, "stock", "stock-original-current")
    require(stock_before[-1]["counts"] == dict(PRESERVE_CURRENT=1, BLOCKED=0, UNKNOWN=0), "expected original pending Stock")
    keys = global_keys(f, manifest)
    require(protected(f, stock, "after-original-reads") == before, "read-only reconciliation changed the source")
    root = stock.root(archive.guid(args.tenant))["id"]
    try:
        guarded_change(f, "advance-current-route-epoch", f'UPDATE {stock.relation(stock.ROUTE_TABLE)} '
                       f'SET "CurrentEpoch"="CurrentEpoch"+1 WHERE "Id"={archive.literal(root)};')
    except AssertionError:
        error = (f.root / "advance-current-route-epoch.stderr").read_bytes()
        require(b"55000" in error and b"CP6_OUTBOX_IMMUTABLE_EVIDENCE" in error,
                "route change failed for a different reason")
    else:
        raise RuntimeError("this pinned immutable profile unexpectedly allowed an epoch update")
    unchanged = read_current(f, "stock", "stock-after-rejected-epoch")
    require(unchanged[-1]["counts"] == dict(PRESERVE_CURRENT=1, BLOCKED=0, UNKNOWN=0),
            "rejected epoch update changed the current route")
    require(protected(f, stock, "after-epoch-read") == before, "epoch probe altered another Owner or sent a message")
    guarded_change(f, "simulate-current-tenant-revocation", 'UPDATE public."Sys_Tenants" SET "Enable"=false '
                   f'WHERE "Id"={archive.literal(args.tenant)}::uuid;')
    revoked = read_current(f, "iam", "iam-current-revoked")
    tenant = next(row for row in revoked[1:-1] if row["kind"] == "tenant" and row["id"] == args.tenant)
    require(tenant["decision"] == "CHANGED" and tenant["originalFacts"]["enabled"] is True
            and tenant["currentFacts"]["enabled"] is False, "current revocation was lost or interpreted as historical authority")
    revoked_stock = read_current(f, "stock", "stock-current-revoked", expected=2)
    require(revoked_stock[-1]["result"] == "BLOCKED" and revoked_stock[-1]["counts"]["BLOCKED"] == 1
            and revoked_stock[1]["reason"] == "ROUTE_AUTHORITY_CONFLICT", "current revocation did not fence Stock reconciliation")
    after = protected(f, stock, "after-current-revocation")
    require(after == {**before, "sourceEnabled": False}, "current probes changed protected original data")
    actual = dict(Result="PASS", Manifest=args.manifest_sha256, SimulatedChanges=["current tenant Enable=false"],
        NativeCli=True, NativeKeyProbes=keys, ForeignPreserved=True, NoDispatch=True, RecoveryReady=False,
        EpochMutation="REJECTED_55000_CP6_OUTBOX_IMMUTABLE_EVIDENCE", ActualEpochChange="NOT_RUN",
        EpochRemaining="real epoch rotation requires the applicable formally delivered provider/profile",
        CurrentRevocationObserved=True, CurrentStockRevocation="BLOCKED_ROUTE_AUTHORITY_CONFLICT",
        OriginalIamRows=len(iam_before)-2, OriginalErpRows=len(erp_before)-2)
    (f.root / "actual.json").write_text(json.dumps(actual, indent=2) + "\n", encoding="utf-8")
    return actual


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "bundle", "file-content-root", "evidence", "disposable-pgdata"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("username", "tenant", "foreign-tenant", "manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    print(json.dumps(verify(parser.parse_args()), indent=2))


if __name__ == "__main__":
    main()
