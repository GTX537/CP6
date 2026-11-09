#!/usr/bin/env python3
"""Verify pinned v10 current Stock and the separate W352-to-W403 native progression."""

import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import reconcile_stock_outbox as reconcile
from verify_erp_archive import Fixture, require
from verify_erp_delivery_reconcile import guarded_change


LIMITED = "w408_stock_limited"


def invoke(f, label, bundle, tenant, digest, *, expected=0, username=None, target_file=None, assets=None):
    args = f.args
    output = f.root / label
    stdout, stderr = f.run(label, [
        sys.executable, "-B", reconcile.__file__, "--pg-bin", str(args.pg_bin),
        "--target-file", str(target_file or args.target_file), "--username", username or args.username,
        "--tenant", str(tenant), "--bundle", str(bundle), "--manifest-sha256", digest, "--output", str(output),
        "--file-content-root", str(assets or args.file_content_root),
    ], expected=expected, username=username)
    if expected == 1:
        require(not stdout and not (output / reconcile.REPORT).exists()
                and not (output / (reconcile.REPORT + ".pending")).exists(), "rejection published a report")
        return stderr
    require(not stderr and Path(json.loads(stdout)["report"]) == output / reconcile.REPORT, "report missing")
    return [json.loads(row) for row in (output / reconcile.REPORT).read_bytes().splitlines()]


def snapshot(f, label, stock, tenants):
    sql = ["BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;", stock.COPY_SETTINGS]
    for tenant in tenants:
        for table in stock.TABLES:
            sql.append("COPY (" + stock.rows_sql(table, tenant, stock.root(tenant)["id"])
                       + ") TO STDOUT WITH (FORMAT csv, HEADER true);")
    out, err = f.sql(label, "\n".join([*sql, "COMMIT;"]))
    require(not err, "native snapshot failed")
    return hashlib.sha256(out).hexdigest()


def scope_keys(f, stock, a, b):
    out, err = f.sql("native-a-b-keys", f"""
        SELECT json_agg(json_build_object('tenant',s."TenantId",'operation',s."OperationId",
            'native',s."NativeId",'message',n."MessageId",'status',n."Status") ORDER BY s."TenantId",s."OperationId")
        FROM {stock.relation(stock.SOURCE)} s JOIN {stock.relation(stock.NATIVE)} n ON n."Id"=s."NativeId"
        WHERE s."TenantId" IN ({archive.literal(a)},{archive.literal(b)});
    """)
    require(not err, "native A/B lookup failed")
    return json.loads(out)


def restricted_reader(f, stock):
    readable = ",".join(stock.relation(table) for table in stock.TABLES if table is not stock.TICKET)
    guarded_change(f, "setup-limited", f"""
        CREATE ROLE {LIMITED} LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
        GRANT CONNECT ON DATABASE {stock.ident(f.target.database)} TO {LIMITED};
        GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO {LIMITED};
        GRANT USAGE ON SCHEMA cp6_rma_tenant_v1,cp6_rma_core_commission_v1,cp6_tenancy_v1,
            cp6_stock_outbox_v1,cp6_main_owner_v1 TO {LIMITED};
        GRANT SELECT ON cp6_rma_tenant_v1.installation,cp6_rma_tenant_v1.tenant_migrations,
            cp6_rma_core_commission_v1.installation,cp6_tenancy_v1.login_tenant,
            public."__EFMigrationsHistory",public."Sys_Tenants",{readable} TO {LIMITED};
    """)


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def target_rejections(f, source, tenant):
    bundle, digest = source
    target = f.root / "wrong-target.json"
    wrong = dict(f.target.mapping(), DatabaseOid=f.target.database_oid + 1)
    write_json(target, wrong)
    mismatch = invoke(f, "wrong-target", bundle, tenant, digest, expected=1, target_file=target)
    require(b"source-target-mismatch" in mismatch and not (f.root / "wrong-target/holder.stdout").exists(),
            "an unbound target reached PostgreSQL")

    # A private repin removes the metadata mismatch so the actual PostgreSQL identity must reject.
    changed = f.root / "wrong-physical-bundle"
    shutil.copytree(bundle, changed)
    path = changed / "manifest.json"
    manifest = json.loads(path.read_bytes())
    manifest["source"] = wrong
    write_json(path, manifest)
    mismatch = invoke(f, "wrong-native-physical", changed, tenant, archive.file_digest(path)["sha256"],
                      expected=1, target_file=target)
    native = f.root / "wrong-native-physical/holder.stdout"
    require(b"target-identity-rejected" in mismatch and json.loads(native.read_bytes())["DatabaseOid"] == f.target.database_oid,
            "native physical target mismatch was not rejected")


def key_probes(f, stock, tenant, route, header, row, foreign, late):
    log = f.root / "key-probes-native"
    log.mkdir()
    key = row["archivedBinding"]
    unknown_op, unknown_id = uuid4(), uuid4()
    absent = dict(key, OperationId=str(unknown_op), NativeId=str(unknown_id),
                  MessageId="stock-hint:" + tenant.hex + ":" + unknown_op.hex)
    # Native source stays unchanged. These are deliberately different expected keys for the same SQL lookup.
    probes = [
        dict(key, MessageId=foreign["message"]),
        dict(key, NativeId=late["native"]),
        dict(key, ResultSha256="0" * 64),
        absent,
        dict(absent, MessageId=foreign["message"] + " "),
        dict(absent, MessageId=foreign["message"].upper()),
    ]
    with archive.snapshot_session(f.args.pg_bin, archive.connection_args(f.target, f.args.username),
                                  os.environ, log) as query:
        ready = archive.check_source(query, f.target, archive.guid(header["installationId"]), tenant)
        query(stock.COPY_SETTINGS + "SELECT to_json(true);")
        stock.check_layout(query, ready["ownerOid"])
        results = reconcile.lookup(query, stock, tenant, route, probes)
    reasons = ("GLOBAL_KEY_CONFLICT", "BINDING_CONFLICT", "ORIGINAL_OPERATION_CONFLICT",
               "ORIGINAL_KEY_NOT_FOUND", "ORIGINAL_KEY_NOT_FOUND", "ORIGINAL_KEY_NOT_FOUND")
    require(results == [dict(decision="UNKNOWN" if reason == "ORIGINAL_KEY_NOT_FOUND" else "BLOCKED", reason=reason)
                        for reason in reasons], "native key conflict or missing-key handling disclosed current details")
    write_json(f.root / "key-probes.json", results)

    # The tiny fixture can prefer a table scan. Show the actual native index is eligible for this predicate.
    plan, err = f.sql("global-native-key-plan", f"""
        BEGIN READ ONLY;
        SET LOCAL enable_seqscan=off;
        EXPLAIN (COSTS OFF) SELECT "Id" FROM {stock.relation(stock.NATIVE)}
            WHERE "MessageId"={archive.literal(key["MessageId"])}::varchar;
        COMMIT;
    """)
    require(not err and b"IX_Cp6_OutboxMessage_MessageId" in plan, "native global varchar index is unavailable")


def v10(f, stock):
    args = f.args
    manifest = json.loads((args.bundle / "manifest.json").read_bytes())
    tenant = archive.guid(manifest["tenantId"])
    b = archive.guid(args.b_tenant)
    route = stock.root(tenant)
    source = (args.bundle, args.manifest_sha256)
    wrong_pin = invoke(f, "wrong-pin", args.bundle, tenant, "0" * 64, expected=1)
    require(b"manifest-digest-mismatch" in wrong_pin and not (f.root / "wrong-pin/holder.stdout").exists(),
            "wrong pin reached PostgreSQL")
    missing_root = invoke(f, "missing-root", args.bundle, tenant, args.manifest_sha256,
                          expected=1, assets=f.root / "missing-assets")
    require(b"file-content-root-rejected" in missing_root and not (f.root / "missing-root/holder.stdout").exists(),
            "missing v10 asset root reached PostgreSQL")
    target_rejections(f, source, tenant)
    restricted_reader(f, stock)

    before = snapshot(f, "source-before", stock, (tenant, b))
    header, row, summary = invoke(f, "observe-v10", args.bundle, tenant, args.manifest_sha256)
    require(header["format"] == reconcile.FORMAT and header["source"] == f.target.mapping()
            and header["tenantId"] == str(tenant) and header["manifestSha256"] == args.manifest_sha256
            and header["current"]["readOnly"] and header["current"]["isolation"] == "repeatable read"
            and summary == dict(type="summary", result="OBSERVED",
                                counts=dict(PRESERVE_CURRENT=1, BLOCKED=0, UNKNOWN=0), recoveryReady=False),
            "native v10 report identity or snapshot differs")
    rows = scope_keys(f, stock, tenant, b)
    operation = row["archivedBinding"]["OperationId"]
    a_early, = [entry for entry in rows if entry["tenant"] == str(tenant) and entry["operation"] == operation]
    foreign, = [entry for entry in rows if entry["tenant"] == str(b) and entry["operation"] == operation]
    late, = [entry for entry in rows if entry["tenant"] == str(tenant) and entry["operation"] != operation]
    require(len(rows) == 3 and row["decision"] == "PRESERVE_CURRENT"
            and row["archivedBinding"]["NativeId"] == a_early["native"]
            and row["archived"]["state"] == row["current"]["state"] == "Pending"
            and row["current"]["routeEpoch"] == 1 and row["current"]["claimEpoch"] is None
            and not row["current"]["hasLeaseToken"] and row["current"]["publishedOriginal"] is None
            and all(fact["status"] == 0 for fact in (a_early, late, foreign)), "expected original A and live A/B Pending")
    report = (f.root / "observe-v10" / reconcile.REPORT).read_bytes()
    require(all(value.encode() not in report for fact in (late, foreign)
                for value in (fact["native"], fact["message"])), "late A or B entered the pinned report")

    denied = invoke(f, "restricted", args.bundle, tenant, args.manifest_sha256, expected=1, username=LIMITED)
    native = (f.root / "restricted/holder.stderr").read_bytes()
    require(b"failed" in denied and b"42501" in native and b"Cp6_OutboxClaimTicketV2" in native,
            "restricted Stock evidence reader did not fail closed")
    key_probes(f, stock, tenant, route, header, row, foreign, late)
    require(before == snapshot(f, "source-after", stock, (tenant, b)), "reconciliation changed current A/B bytes")
    print("W408 v10 current original, protected live A/B, rejected target/pin/ACL and native key probes passed")


def copy_text(row):
    # The anchored W352 pg_restore rows represent bytea with an escaped backslash and NULL as \N.
    return {key: "" if value == "\\N" else value[1:] if value.startswith("\\\\x") else value
            for key, value in row.items()}


def one_row(bundle, table):
    with (bundle / table.member).open(encoding="utf-8", newline="") as file:
        row, = csv.DictReader(file, strict=True)
        return row


def published(f, stock):
    args = f.args
    original = json.loads(args.observation.read_bytes())
    require(archive.file_digest(args.observation)["sha256"] == args.observation_sha256
            and archive.file_digest(args.observation.with_name("before-data.sql"))["sha256"] == original["sqlSha256"]
            and archive.file_digest(Path(original["backupPath"]))["sha256"] == original["backupSha256"],
            "W352 original extraction or sealed dump pin differs")
    rows = {name: copy_text(row) for name, row in original["rawPgRestoreCopyTextRows"].items()}
    native, source, binding, journal = (rows[table.name] for table in (stock.NATIVE, stock.SOURCE, stock.BINDING, stock.JOURNAL))
    tenant, b = archive.guid(native["TenantId"]), archive.guid(args.b_tenant)
    route = stock.root(tenant)
    proof = reconcile.journal_original(stock, tenant, journal)
    archived = reconcile.original_key(stock, tenant, route, native, source, binding, proof)
    current_bundle = Path(original["currentBundlePath"])
    published_manifest = stock.verify(current_bundle, tenant, original["currentBundleSha256"])
    require(published_manifest["source"] == original["currentSource"] == f.target.mapping(),
            "current Published source identity differs")

    before = snapshot(f, "source-before", stock, (tenant, b))
    log = f.root / "published-native"
    log.mkdir()
    with archive.snapshot_session(args.pg_bin, archive.connection_args(f.target, args.username), os.environ, log) as query:
        owner, collation = stock.check_target(query, f.target)
        require(collation == published_manifest["databaseCollation"], "Published native collation differs")
        stock.check_layout(query, owner)
        state = query(stock.COPY_SETTINGS + """
            SELECT json_build_object('readOnly',current_setting('transaction_read_only')::bool,
                'isolation',current_setting('transaction_isolation'));
        """)
        actual, = reconcile.observe(query, stock, tenant, route, [archived])
    ticket, mutation, send = (one_row(current_bundle, table) for table in (stock.TICKET, stock.MUTATION, stock.SEND))
    expected = dict(claimRequestId=ticket["RequestId"], claimReceiptRef=ticket["ReceiptReference"],
                    mutationId=mutation["MutationId"], completionReceiptRef=json.loads(mutation["Output"])["ReceiptReference"],
                    ticketSha256=hashlib.sha256(ticket["Ticket"].encode()).hexdigest(),
                    inputSha256=hashlib.sha256(mutation["Input"].encode()).hexdigest(),
                    outputSha256=hashlib.sha256(mutation["Output"].encode()).hexdigest(),
                    sendAttempt=dict(claimId=send["ClaimId"],
                                     ticketSha256=hashlib.sha256(send["OriginalTicket"].encode()).hexdigest()))
    require(state == dict(readOnly=True, isolation="repeatable read") and actual["decision"] == "PRESERVE_CURRENT"
            and actual["archived"]["state"] == original["archivedStatus"] == "Pending"
            and actual["archived"]["nativeXmin"] is None
            and actual["current"]["state"] == original["observedCurrentStatus"] == "Published"
            and actual["current"]["publishedOriginal"] == expected
            and actual["current"]["publishedAtUtc"] == one_row(current_bundle, stock.NATIVE)["PublishedAtUtc"],
            "actual Pending to Published native progression or original completion differs")
    require(before == snapshot(f, "source-after", stock, (tenant, b)), "Published observation changed current A/B bytes")
    write_json(f.root / "published-observation.json", dict(backupSha256=original["backupSha256"],
                currentSource=f.target.mapping(), currentBundleSha256=original["currentBundleSha256"], **state, **actual))
    print("W408 genuine W352 Pending to W403 Published via the same native lookup passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("v10", "published"))
    for name in ("pg-bin", "target-file", "evidence", "disposable-pgdata", "bundle", "file-content-root", "observation"):
        parser.add_argument("--" + name, type=Path, required=name in ("pg-bin", "target-file", "evidence", "disposable-pgdata"))
    for name in ("username", "b-tenant", "manifest-sha256", "observation-sha256"):
        parser.add_argument("--" + name, required=name in ("username", "b-tenant"))
    args = parser.parse_args()
    stock = archive.stock_outbox_module()
    (v10 if args.mode == "v10" else published)(Fixture(args), stock)


if __name__ == "__main__":
    main()
