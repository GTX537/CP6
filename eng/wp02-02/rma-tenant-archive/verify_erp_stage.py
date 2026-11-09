#!/usr/bin/env python3
"""Stage and recover the exact pinned v8 A/B ERP archives in disposable PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
from verify_stage import A, B, BASE_ENV, OWNER, Fixture, assert_empty, check_acl, invoke, new_target, pinned, put_json, q, require, token
from verify_file_content_stage import invoke_stage
import erp_integration
import stage


def neutral(table):
    return f'rma_archive_stage."erp_{table.name}"'


def copy_row(table, new_key, key_column):
    columns = ", ".join(new_key if name == key_column else f'"{name}"' for name in table.columns)
    return f"INSERT INTO {neutral(table)} SELECT {columns} FROM {neutral(table)} LIMIT 1;"


def stage_with_late_global_fault(f, target, source):
    bundle, tenant, digest = source
    request = f.root / "v8-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle,
                              file_content_root=f.args.file_content_root, output=f.root / "v8-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        script = output / "driver.sql"
        driver = script.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_erp_integration$"
        require(driver.count(marker) == 1 and driver.index("erp-request.csv") < driver.index(marker),
                "missing late native ERP stage boundary")
        outbox = next(table for table in erp_integration.TABLES if table.name == "Cp6_OutboxMessage")
        columns = ["gen_random_uuid()", "upper(\"MessageId\"::text)||'  '", *[f'"{c}"' for c in outbox.columns[2:]]]
        fault = f"INSERT INTO {neutral(outbox)} SELECT {', '.join(columns)} FROM {neutral(outbox)} LIMIT 1;\n"
        script.write_text(driver.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-native-failed (see native logs)", "wrong late stage rejection")
        else:
            raise AssertionError("native late ERP duplicate was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"23505" in stderr and b"erp_Cp6_OutboxMessage_MessageId_key" in stderr,
            "native ERP global uniqueness did not reject the altered case and padding")


def roundtrip(f, database, label, table, original):
    order = ", ".join(f'"{name}"' for name in table.unique_keys[0])
    with f.psql(database, app="W398-erp-native-roundtrip") as process:
        sql = f"""
            SET bytea_output='hex'; SET timezone='UTC'; SET datestyle='ISO, YMD';
            COPY (SELECT * FROM {neutral(table)} ORDER BY {order}) TO STDOUT WITH (FORMAT csv, HEADER true);
        """
        out, err = f.collect(process, sql.encode("utf-8"))
        f.save(label + "-" + table.member, out)
        require(process.returncode == 0 and not err and out == (original / table.member).read_bytes(),
                "native stage changed original " + table.name)


def check_receipt(raw, source, target):
    bundle, tenant, digest = source
    manifest = json.loads((bundle / "manifest.json").read_bytes())
    expected = {name: table["count"] for name, table in manifest["erpIntegration"]["tables"].items()}
    receipt = json.loads(raw)
    require(receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["format"] == erp_integration.FORMAT and receipt["target"] == target
            and receipt["tenantId"] == tenant and receipt["manifestSha256"] == digest
            and receipt["fileContentAssetCount"] == manifest["fileContentAssets"]["count"] == 0
            and receipt["erpIntegrationCounts"] == expected, "v8 receipt did not bind the original stage")
    return expected


def check_keys(f, database):
    native = json.loads(f.sql(database, """
        SELECT json_object_agg(c.relname,(SELECT json_agg(json_build_object('primary',i.indisprimary,'columns',ARRAY(
            SELECT a.attname::text FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(num,ord)
            JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=k.num
            WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)))
            FROM pg_index i WHERE i.indrelid=c.oid AND i.indisunique AND i.indisvalid
              AND i.indpred IS NULL AND i.indexprs IS NULL))
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='rma_archive_stage' AND c.relkind='r' AND left(c.relname,4)='erp_';
    """, label="v8-native-keys"))
    require(native.keys() == {"erp_" + table.name for table in erp_integration.TABLES}, "wrong staged ERP relation set")
    for table in erp_integration.TABLES:
        observed = sorted((tuple(row["columns"]), row["primary"]) for row in native["erp_" + table.name])
        expected = sorted((key, index == 0) for index, key in enumerate(table.unique_keys))
        require(observed == expected, "original key columns changed for " + table.name)

    command = next(table for table in erp_integration.TABLES if table.name == "CommandInbox")
    variant = copy_row(command, 'lower("MessageId"::text)', "MessageId")
    require(f.sql(database, f"BEGIN; {variant} SELECT count(*) FROM {neutral(command)}; ROLLBACK;",
                  label="v8-native-c-case") == "2", "native CommandInbox key lost case sensitivity")
    f.sql(database, copy_row(command, '\"MessageId\"::text||\'  \'', "MessageId"), error="23505", label="v8-native-c-padding")


def check_inactive(f, database):
    require(f.sql(database, """
        SELECT to_regnamespace('erp_integration') IS NULL
            AND to_regnamespace('cp6_storage_erp') IS NULL
            AND to_regnamespace('cp6_rma_tenant_v1') IS NULL
            AND to_regnamespace('cp6_main_owner_v1') IS NULL
            AND NOT has_schema_privilege({a},'rma_archive_stage','USAGE')
            AND NOT has_schema_privilege({b},'rma_archive_stage','USAGE');
    """.format(a=q(A), b=q(B))) == "t",
        "staged ERP became active or exposed the neutral schema")


def exercise(f, a, b):
    bundle, tenant, digest = a
    target = new_target(f, "w398_erp_a")
    database = target["Database"]
    stage_with_late_global_fault(f, target, a)
    assert_empty(f, database)
    invoke(f, "v8-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")
    invoke_stage(f, "v8-stage-discarded", target, a, f.args.file_content_root, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")
    recovered = invoke(f, "v8-recover", "recover", target, tenant, digest)
    require(recovered == original and all(count == 1 for count in check_receipt(recovered, a, target).values()),
            "same target/pin retry did not persist the exact A receipt")
    for table in erp_integration.TABLES:
        roundtrip(f, database, "a", table, bundle)
    check_keys(f, database)
    check_acl(f, database, tenant)

    b_target = new_target(f, "w398_erp_b")
    b_database = b_target["Database"]
    invoke_stage(f, "v8-stage-b", b_target, b, f.args.file_content_root)
    receipt = (f.root / "v8-stage-b.stdout").read_bytes()
    counts = check_receipt(receipt, b, b_target)
    empty = {name for name, count in counts.items() if count == 0}
    require(empty == {"Cp6_DeadLetterRecord", "DeliveryReplayAudit"}, "unexpected B empty membership")
    for table in erp_integration.TABLES:
        if table.name in empty:
            roundtrip(f, b_database, "b", table, b[0])
    for database in (database, b_database):
        check_inactive(f, database)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "a-bundle", "b-bundle", "file-content-root"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("a-sha256", "b-sha256"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    a = pinned(args.a_bundle, args.a_sha256, erp_integration.FORMAT)
    b = pinned(args.b_bundle, args.b_sha256, erp_integration.FORMAT)
    fixture = Fixture(args, "rma-stage-v8-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("disposable.json", {"PgData": "data", "Port": args.port, "Owner": "W398-01", "Disposable": True})
    fixture.save("sources.json", {name: [str(bundle[0]), *bundle[1:]] for name, bundle in (("A", a), ("B", b))})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture, a, b)
    print("native v8 ERP stage, late global-key rollback, exact receipt and A/B original bytes passed", flush=True)


if __name__ == "__main__":
    main()
