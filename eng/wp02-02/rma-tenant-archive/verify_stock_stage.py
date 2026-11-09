#!/usr/bin/env python3
"""Exercise a pinned v10 Stock slice and a separate Published component in disposable PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
from verify_file_content_stage import invoke_stage
from verify_privacy_evidence_stage import AUDIT, HOLDS, check_original
from verify_stage import (A, B, BASE_ENV, OWNER, Fixture, assert_empty, check_acl, ident, invoke,
                          new_target, pinned, put_json, q, require, token)
import archive
import privacy_evidence
import stage
import stock_stage_v10 as neutral


def fail_after_stock_copy(f, target, source, stock):
    bundle, tenant, digest = source
    request = f.root / "v10-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle,
                              file_content_root=f.args.file_content_root, output=f.root / "v10-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        script = output / "driver.sql"
        driver = script.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_stock_outbox$"
        require(driver.count(marker) == 1 and driver.index(neutral.member(stock.JOURNAL)) < driver.index(marker),
                "missing native Stock graph boundary")
        fault = f'UPDATE {neutral.relation(stock.BINDING)} SET "RawEnvelopeSha256"=repeat(\'0\',64);\n'
        script.write_text(driver.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-stock-originals-rejected", "wrong late stage rejection")
        else:
            raise AssertionError("native Stock graph accepted a changed original binding")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"P0001" in stderr and b"rma-stage-stock-originals-rejected" in stderr,
            "native Stock graph failure not observed")


def check_copy(f, database, relation, order, original, label):
    with f.psql(database, app="W407-native-roundtrip") as process:
        sql = f"""SET TIME ZONE 'UTC'; SET DateStyle='ISO, YMD'; SET bytea_output='hex'; SET client_encoding='UTF8';
            COPY (SELECT * FROM {relation} ORDER BY {order}) TO STDOUT WITH (FORMAT csv, HEADER true);
        """
        out, err = f.collect(process, sql.encode("utf-8"))
    f.save(label, out)
    if err:
        f.save(label + ".stderr", err)
    require(process.returncode == 0 and not err and out == original.read_bytes(),
            "staging changed original CSV: " + original.name)


def check_stock_originals(f, database, stock, bundle, prefix, label):
    relation_list = ",".join(q(neutral.relation(table)) + "::regclass" for table in stock.TABLES)
    catalog = json.loads(f.sql(database, f"""
        SELECT json_object_agg(c.relname, json_build_object(
            'types',(SELECT json_agg(json_build_array(a.attname,t.typname) ORDER BY a.attnum)
                FROM pg_attribute a JOIN pg_type t ON t.oid=a.atttypid
                WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped),
            'keys',(SELECT json_agg(json_build_array(k.contype,ARRAY(SELECT a.attname::text
                FROM unnest(k.conkey) WITH ORDINALITY x(num,ord) JOIN pg_attribute a
                ON a.attrelid=k.conrelid AND a.attnum=x.num ORDER BY x.ord)))
                FROM pg_constraint k WHERE k.conrelid=c.oid AND k.contype IN ('p','u'))))
        FROM pg_class c WHERE c.oid IN ({relation_list});
    """, label=label + "-catalog"))

    for table in stock.TABLES:
        expected_types = [list(field) for field in table.fields] + ([["source_xmin", "xid"]] if table.xmin else [])
        expected_keys = {("p" if index == 0 else "u", key) for index, key in enumerate(table.keys)}
        entry = catalog["stock_" + table.name]
        require(entry["types"] == expected_types and
                {(kind, tuple(key)) for kind, key in entry["keys"]} == expected_keys,
                "staging changed Stock native types or keys: " + table.name)
        order = ",".join(map(ident, table.keys[0]))
        check_copy(f, database, neutral.relation(table), order, bundle / (prefix + table.member),
                   label + "-" + table.member)


def check_stock_scope(f, database, tenant, stock):
    check_acl(f, database, tenant)
    native = neutral.relation(stock.NATIVE)
    f.sql(database, f'UPDATE {native} SET "TenantId"={q(token())};', error="23514", label="stock-tenant-denied")
    route = neutral.relation(stock.ROUTE_TABLE)
    f.sql(database, f'UPDATE {route} SET "Id"=repeat(\'0\',64);', error="23514", label="stock-root-denied")
    seals = neutral.relation(stock.SEAL)
    f.sql(database, f"INSERT INTO {seals} VALUES ({q(token())},'unrelated');",
          error="23503", label="stock-seal-native-denied")
    require(f.sql(database, f"""
        SELECT to_regnamespace('cp6_stock_outbox_v1') IS NULL
            AND NOT EXISTS (SELECT FROM pg_roles r JOIN pg_class c
                ON c.relnamespace='rma_archive_stage'::regnamespace AND left(c.relname,6)='stock_'
                WHERE r.rolname IN ({q(A)},{q(B)}) AND (
                    has_schema_privilege(r.oid,'rma_archive_stage','USAGE') OR
                    has_table_privilege(r.oid,c.oid,'SELECT,INSERT,UPDATE,DELETE')));
    """, label="stock-native-inactive-acl") == "t", "Stock stage became an active or exposed queue")
    f.sql(database, "SELECT 1;", role=A, error="permission denied for database", label="ordinary-a-connect-denied")
    f.sql(database, "SELECT 1;", role=B, error="permission denied for database", label="ordinary-b-connect-denied")
    f.sql(database, f"SET ROLE {ident(A)}; SELECT * FROM {native};", error="42501", label="ordinary-a-stock-denied")
    f.sql(database, f"SET ROLE {ident(B)}; SELECT * FROM {native};", error="42501", label="ordinary-b-stock-denied")


def exercise_v10(f, source, stock):
    bundle, tenant, digest = source
    target = new_target(f, "w407_v10")
    database = target["Database"]
    invoke_stage(f, "v10-no-asset-root", target, source, None, "file-content-root-required")
    fail_after_stock_copy(f, target, source, stock)
    assert_empty(f, database)
    invoke(f, "v10-rollback-no-receipt", "recover", target, tenant, digest, expected=1, error="rma-stage-receipt-not-found")

    invoke_stage(f, "v10-stage-discarded", target, source, f.args.file_content_root, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;", label="v10-persisted-receipt").encode()
    recovered = invoke(f, "v10-recover", "recover", target, tenant, digest)
    require(recovered == original, "same-key recovery changed the persisted receipt")
    invoke(f, "v10-other-pin", "recover", target, tenant, "0" * 64, expected=1, error="rma-stage-receipt-not-found")

    manifest = json.loads((bundle / "manifest.json").read_bytes())
    receipt = json.loads(recovered)
    require(receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["format"] == archive.FORMAT_WITH_STOCK_OUTBOX and receipt["target"] == target
            and receipt["tenantId"] == tenant and receipt["manifestSha256"] == digest
            and receipt["privacyEvidence"] == manifest["privacyEvidence"]
            and receipt["stockOutbox"] == manifest["stockOutbox"], "v10 receipt changed the pinned original facts")
    check_stock_scope(f, database, tenant, stock)
    check_stock_originals(f, database, stock, bundle, archive.STOCK_MEMBER_PREFIX, "pending")
    check_copy(f, database, "rma_archive_stage.original_journal_snapshot", "operation_id",
               bundle / archive.JOURNAL_MEMBER, "rma-original-roundtrip.csv")
    check_original(f, database, bundle, AUDIT, privacy_evidence.SECURITY_LOGS)
    check_original(f, database, bundle, HOLDS, privacy_evidence.HOLDS)
    require(f.sql(database, f'''
        SELECT count(*)=1 AND bool_and(n."Status"=0 AND b."ClaimEpoch" IS NULL)
        FROM {neutral.relation(stock.NATIVE)} n JOIN {neutral.relation(stock.BINDING)} b ON b."NativeId"=n."Id";
    ''', label="v10-native-pending") == "t", "expected the actual unclaimed Pending source")


def exercise_published(f, stock, bundle, pin):
    manifest = json.loads((bundle / "manifest.json").read_bytes())
    tenant = archive.guid(manifest["tenantId"])
    manifest = stock.verify(bundle, tenant, pin)
    target = new_target(f, "w407_published_component")
    database = target["Database"]
    output = f.root / "published-component"
    archive.private_directory(output)
    (output / "bundle").mkdir()
    tables = {}
    for table in stock.TABLES:
        name = neutral.member(table)
        tables[name] = manifest["tables"][table.member]
        shutil.copyfile(bundle / table.member, output / "bundle" / name)
    section = archive.stock_section(stock, manifest["root"], manifest["databaseCollation"], tables)
    plan = {"target": target, "stockOutbox": section}
    driver = stage.initial_sql(plan) + stage.EMPTY_STAGE + "CREATE SCHEMA rma_archive_stage;\n"
    driver += neutral.stage_sql(stock, tenant, section, stage.TARGET_PLAN)
    native = neutral.relation(stock.NATIVE)
    driver += f'SELECT count(*) FROM {native} WHERE "Status"=2;\n'
    (output / "driver.sql").write_text(driver, encoding="utf-8")
    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True):
        observed = stage.run_driver(f.args.pg_bin, archive.PhysicalTarget.from_mapping(target), OWNER, output, "stage")
    require(observed == b"1", "published component did not preserve the original Published message")
    check_stock_originals(f, database, stock, bundle, "", "published")
    f.save("published-component.json", {"Source": manifest["source"], "Tenant": str(tenant), "Pin": pin,
                                       "Target": target, "RecoveryReceipt": False})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "bundle", "file-content-root", "published-bundle"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("manifest-sha256", "published-manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    source = pinned(args.bundle, args.manifest_sha256, archive.FORMAT_WITH_STOCK_OUTBOX)
    stock = archive.stock_outbox_module()
    label = "rma-stage-v10-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8]
    fixture = Fixture(args, label)
    fixture.save("disposable.json", {"PgData": "data", "Port": args.port, "Owner": "W407-01", "Disposable": True})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        fixture.sql("postgres", "SELECT version();", label="pg-version")
        exercise_v10(fixture, source, stock)
        exercise_published(fixture, stock, args.published_bundle, args.published_manifest_sha256)
    print("native v10 stage, late graph rollback, exact receipt recovery, and separate Published component passed", flush=True)


if __name__ == "__main__":
    main()
