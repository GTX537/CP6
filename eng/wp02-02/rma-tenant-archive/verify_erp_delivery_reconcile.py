#!/usr/bin/env python3
"""Verify ERP reconciliation on the disposable W395 source clone with its level3 probe installed."""

import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path
import sys

sys.dont_write_bytecode = True
import archive
import reconcile_erp_deliveries as reconcile
from verify_erp_archive import BACKUP, Fixture, relation, require


TABLES = (reconcile.COMMAND, reconcile.OUTBOX, reconcile.BRIDGE)


def read_cli(fixture, label, bundle, tenant, digest, *, expected=0, username=None, asset_root=None):
    args = fixture.args
    output = fixture.root / label
    username = username or args.username
    stdout, stderr = fixture.run(label, [sys.executable, "-B", reconcile.__file__, "--pg-bin", str(args.pg_bin),
        "--target-file", str(args.target_file), "--username", username, "--tenant", str(tenant), "--bundle", str(bundle),
        "--manifest-sha256", digest, "--output", str(output), "--file-content-root", str(asset_root or args.file_content_root)],
        expected=expected, username=username)
    if expected == 1:
        require(not (output / reconcile.REPORT).exists() and not (output / (reconcile.REPORT + ".pending")).exists(),
                "rejected read left a report")
        return stderr
    report = output / reconcile.REPORT
    require(not stderr and Path(json.loads(stdout)["report"]) == report, "missing native reconciliation report")
    header, *rows, summary = (json.loads(line) for line in report.read_bytes().splitlines())
    require(header["tenantId"] == str(tenant) and header["manifestSha256"] == digest and header["current"]["readOnly"]
            and header["current"]["isolation"] == "repeatable read" and summary["recoveryReady"] is False,
            "incorrect report binding or transaction")
    require(len(rows) == len(TABLES) and summary["result"] == ("OBSERVED" if expected == 0 else "BLOCKED"),
            "incorrect report scope or decision")
    return header, {row["table"]: row for row in rows}


def snapshot(fixture, label, tenants):
    queries = ["BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;", "SET LOCAL row_security=off;",
               "SET LOCAL bytea_output='hex'; SET LOCAL TIME ZONE 'UTC'; SET LOCAL DateStyle='ISO, YMD';"]
    for tenant in tenants:
        for table in TABLES:
            order = ",".join('"' + column + '"' for column in table.unique_keys[0])
            queries.append(f'COPY (SELECT * FROM {relation(table.name)} WHERE "TenantId"={archive.literal(tenant)} '
                           f'ORDER BY {order}) TO STDOUT WITH (FORMAT csv, HEADER);')
    data, err = fixture.sql(label, "\n".join([*queries, "COMMIT;"]))
    require(not err, "native row snapshot failed")
    return hashlib.sha256(data).hexdigest()


def native_collation(fixture, label):
    data, err = fixture.sql(label, """
        SELECT json_build_object('oid',c.oid::bigint,'name',c.collname,'provider',c.collprovider,
            'locale',COALESCE(to_jsonb(c)->>'colllocale',to_jsonb(c)->>'colliculocale'),
            'indexMatches',i.indcollation[0]=a.attcollation)
        FROM pg_attribute a JOIN pg_collation c ON c.oid=a.attcollation
        JOIN pg_index i ON i.indrelid=a.attrelid AND i.indisunique AND i.indisvalid AND i.indisready
            AND i.indnkeyatts=1 AND i.indkey[0]=a.attnum
        WHERE a.attrelid='erp_integration."Cp6_OutboxMessage"'::regclass AND a.attname='MessageId';
    """)
    require(not err, "native Outbox key query failed")
    return json.loads(data)


def guarded_change(fixture, label, statement):
    data = archive.literal(fixture.args.disposable_pgdata.resolve(strict=True).as_posix())
    physical = archive.literal(json.dumps(fixture.target.mapping()))
    fixture.sql(label, f"""
        BEGIN READ WRITE;
        SET LOCAL search_path=pg_catalog;
        DO $target$
        BEGIN
            IF current_setting('data_directory') COLLATE "C" <> {data}
              OR current_setting('default_transaction_read_only') <> 'on'
              OR current_user <> {archive.literal(fixture.args.username)}
              OR NOT EXISTS (
                SELECT FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database()
                  AND jsonb_build_object('Server',host(inet_server_addr()),'Port',inet_server_port(),
                    'Database',d.datname,'DatabaseOid',d.oid::bigint,'SystemIdentifier',p.system_identifier::text)
                    = {physical}::jsonb
              ) THEN RAISE EXCEPTION 'unexpected disposable source'; END IF;
        END; $target$;
        {statement}
        COMMIT;
    """)


def restore_collation(fixture):
    original = "public.w395_original_provider_collation"
    before = native_collation(fixture, "outbox-key-before")
    require(before["name"] == "cp6_ci_as_provider_v1" and before["locale"] == "und-u-ks-level3"
            and before["indexMatches"], "expected W395 level3 fault on the exact current index")
    guarded_change(fixture, "restore-exact-collation", f"""
        DO $collation$
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_collation WHERE oid='{original}'::regcollation
                AND collprovider='i' AND NOT collisdeterministic AND collencoding=-1
                AND COALESCE(to_jsonb(pg_collation)->>'colllocale',to_jsonb(pg_collation)->>'colliculocale') COLLATE "C"
                    = 'und-u-ks-level2' AND COALESCE(to_jsonb(pg_collation)->>'collicurules','') = '')
              THEN RAISE EXCEPTION 'unexpected preserved original collation'; END IF;
        END; $collation$;
        SELECT ('{original}'::regcollation)::oid;
        ALTER COLLATION public.cp6_ci_as_provider_v1 RENAME TO w399_level3_probe_collation;
        ALTER COLLATION {original} RENAME TO cp6_ci_as_provider_v1;
        ALTER TABLE {relation(reconcile.OUTBOX.name)} ALTER COLUMN "MessageId" TYPE bpchar COLLATE public.cp6_ci_as_provider_v1;
    """)
    after = native_collation(fixture, "outbox-key-after")
    original_oid = int((fixture.root / "restore-exact-collation.stdout").read_bytes())
    require(after == dict(oid=original_oid, name="cp6_ci_as_provider_v1", provider="i", locale="und-u-ks-level2",
                          indexMatches=True) and after["oid"] != before["oid"], "original collation OID was not rebound")


def set_one(table, assignment, where):
    return f"""
        UPDATE {relation(table.name)} SET {assignment} WHERE {where};
        GET DIAGNOSTICS changed = ROW_COUNT;
        IF changed <> 1 THEN RAISE EXCEPTION 'expected exactly one synthetic {table.name} row'; END IF;
    """


def exercise(args):
    fixture = Fixture(args)
    a, b = [archive.guid(json.loads((bundle / "manifest.json").read_bytes())["tenantId"])
            for bundle in (args.a_bundle, args.b_bundle)]
    require(a != b, "expected distinct W395 synthetic tenants")
    def run_a(label, **kwargs):
        return read_cli(fixture, label, args.a_bundle, a, args.a_manifest_sha256, **kwargs)

    wrong = "0" * 64
    err = read_cli(fixture, "wrong-pin", args.a_bundle, a, wrong, expected=1)
    require(b"manifest-digest-mismatch" in err and not (fixture.root / "wrong-pin/holder.stdout").exists(),
            "wrong pin reached PostgreSQL")
    err = run_a("missing-asset-root", expected=1, asset_root=fixture.root / "missing-assets")
    require(b"file-content-root-rejected" in err and not (fixture.root / "missing-asset-root/holder.stdout").exists(),
            "unverified asset root reached PostgreSQL")
    start = snapshot(fixture, "before-level3", (a, b))
    require(b"erp-integration-key-collation-rejected" in run_a("level3-denied", expected=1),
            "same-name wrong ICU definition was accepted")
    require(start == snapshot(fixture, "after-level3", (a, b)), "level3 rejection changed live source bytes")
    restore_collation(fixture)
    pre = snapshot(fixture, "before-observation", (a, b))
    require(pre == start, "collation correction changed synthetic row bytes")
    header, a_rows = run_a("observe-a")
    _, b_rows = read_cli(fixture, "observe-b", args.b_bundle, b, args.b_manifest_sha256)
    command, outbox, bridge = (a_rows[table.name] for table in TABLES)
    require(all(row["decision"] == "PRESERVE_CURRENT" for row in (*a_rows.values(), *b_rows.values())), "original binding changed")
    require(command["archivedBinding"]["MessageId"] == "W395-Command" and command["archived"]["attemptCount"] == 4
            and command["current"]["attemptCount"] == 5 and command["current"]["state"] == "Processed"
            and b_rows[reconcile.COMMAND.name]["archivedBinding"]["MessageId"] == "w395-command"
            and b_rows[reconcile.COMMAND.name]["current"]["attemptCount"] == 6,
            "native global C CommandInbox keys or states were conflated")
    expiry = datetime.fromisoformat("2026-11-10T12:00:00.765432+00:00")
    captured_at = header["current"]["capturedAt"]
    as_of = datetime.strptime(captured_at, "%Y-%m-%dT%H:%M:%S" + (".%f" if "." in captured_at else "") + "%z")
    require(outbox["archivedBinding"]["PartitionKey"] == "original-partition " and outbox["current"]["state"] == "Dispatching"
            and outbox["current"]["leaseTokenChanged"] and outbox["current"]["leaseExpiresAtUtc"] == "2026-11-10 12:00:00.765432+00"
            and outbox["current"]["hasUnexpiredDispatchLease"] == (expiry > as_of)
            and outbox["current"]["publishedAtUtc"] is None and not b_rows[reconcile.OUTBOX.name]["current"]["leaseTokenChanged"],
            "expected current raw binding and distinct recorded lease")
    require(bridge["archivedBinding"]["OrderKey"] == "w395-order  " and bridge["archived"]["state"] == "Pending"
            and bridge["current"]["state"] == "Completed" and bridge["current"]["completedAtUtc"].endswith(".765432+00")
            and b_rows[reconcile.BRIDGE.name]["current"]["state"] == "Pending"
            and all(row["archived"]["rowVersion"] != row["current"]["rowVersion"] for row in a_rows.values()),
            "Bridge tenant scope, native clocks, or actual row-version progression was lost")
    require(pre == snapshot(fixture, "after-observation", (a, b)), "reconciliation changed A or B bytes")

    pre = snapshot(fixture, "before-low-role", (a, b))
    err = run_a("native-rls-denied", expected=1, username=BACKUP)
    native = (fixture.root / "native-rls-denied/holder.stderr").read_bytes()
    require(b"erp-current-read-failed" in err and b"42501" in native and b"CommandInbox" in native,
            "native FORCE RLS did not reject an incomplete global read")
    require(pre == snapshot(fixture, "after-low-role", (a, b)), "low role rejection changed source bytes")

    a_id = archive.literal(a)
    b_id = archive.literal(b)
    command_id = '"MessageId"'
    order_key = '"OrderKey"'
    outbox_id = archive.literal(outbox["archivedBinding"]["Id"])
    for label, changes, reasons in (
        ("foreign-icu-missing-c", (
            set_one(reconcile.COMMAND, f"{command_id}='W395-Command-away'", f'"TenantId"={a_id} AND {command_id}=\'W395-Command\''),
            set_one(reconcile.OUTBOX, f'"TenantId"={b_id},{command_id}=\'W395-DELIVERY  \'', f'"TenantId"={a_id} AND "Id"={outbox_id}'),
            set_one(reconcile.BRIDGE, f"{order_key}='W395-ORDER  '", f'"TenantId"={a_id} AND {order_key}=\'w395-order\''),
        ), ("ORIGINAL_KEY_NOT_FOUND", "GLOBAL_KEY_CONFLICT", "ORIGINAL_KEY_NOT_FOUND")),
        ("foreign-c-raw-bindings", (
            set_one(reconcile.COMMAND, f'"TenantId"={b_id},{command_id}=\'W395-Command  \'', f'"TenantId"={a_id} AND {command_id}=\'W395-Command-away\''),
            set_one(reconcile.OUTBOX, f'"TenantId"={a_id},{command_id}=\'w395-delivery\',"Payload"="Payload" || decode(\'01\',\'hex\')',
                    f'"TenantId"={b_id} AND "Id"={outbox_id}'),
            set_one(reconcile.BRIDGE, f"{order_key}='w395-order'", f'"TenantId"={a_id} AND {order_key}=\'W395-ORDER\''),
        ), ("GLOBAL_KEY_CONFLICT", "BINDING_CONFLICT", "BINDING_CONFLICT")),
    ):
        guarded_change(fixture, label + "-synthetic-writes", "DO $rows$ DECLARE changed integer; BEGIN\n"
                       + "\n".join(changes) + "\nEND; $rows$;")
        pre = snapshot(fixture, "before-" + label, (a, b))
        _, rows = run_a(label, expected=2)
        for table, reason in zip(TABLES, reasons):
            row = rows[table.name]
            require(row["reason"] == reason and "current" not in row
                    and row["decision"] == ("UNKNOWN" if reason == "ORIGINAL_KEY_NOT_FOUND" else "BLOCKED"),
                    "wrong native miss or binding conflict, or unbound current data disclosed")
        require(pre == snapshot(fixture, "after-" + label, (a, b)), "blocked reconciliation changed live source bytes")
    print("W399 v8 exact A/B, ICU and RLS rejection, native key and raw binding conflicts passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("pg-bin", "target-file", "a-bundle", "b-bundle", "file-content-root", "evidence", "disposable-pgdata"):
        parser.add_argument("--" + arg, type=lambda value: Path(value).absolute(), required=True)
    for arg in ("username", "a-manifest-sha256", "b-manifest-sha256"):
        parser.add_argument("--" + arg, required=True)
    exercise(parser.parse_args())


if __name__ == "__main__":
    main()
