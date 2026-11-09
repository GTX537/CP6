#!/usr/bin/env python3
"""Stage the pinned A and B v5 Identity Priority archives on isolated PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True

from verify_stage import (BASE_ENV, OWNER, Fixture, assert_empty, check_acl, check_journal, invoke,
                          new_target, pinned, put_json, require, token)
import identity_priority
import stage


def stage_with_global_key_fault(f, target, tenant, digest, bundle):
    request = f.root / "v5-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle, output=f.root / "v5-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        driver = output / "driver.sql"
        script = driver.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_identity_priority$"
        require(script.count(marker) == 1, "missing native Identity Priority check")
        # After all COPYs, collide under the original ICU collation with a different raw key spelling.
        fault = """
            UPDATE rma_archive_stage."Cp6_OutboxMessage" target
            SET "MessageId"=upper(original."MessageId"::text)||'  '
            FROM (SELECT "Id","MessageId" FROM rma_archive_stage."Cp6_OutboxMessage" ORDER BY "Id" LIMIT 1) original
            WHERE target."Id"<>original."Id";
        """
        driver.write_text(script.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-native-failed (see native logs)", "wrong late native stage rejection")
        else:
            raise AssertionError("late Identity Priority duplicate was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"23505" in stderr and b"Cp6_OutboxMessage_MessageId_key" in stderr,
            "missing original native global Outbox uniqueness error")


def native_roundtrip(f, label, database, table, bundle):
    with f.psql(database, app="W385-roundtrip") as proc:
        sql = f"""
            SET bytea_output='hex'; SET timezone='UTC'; SET datestyle='ISO, YMD';
            COPY (SELECT * FROM rma_archive_stage."{table.name}" ORDER BY "Id") TO STDOUT WITH (FORMAT csv, HEADER true);
        """
        output, error = f.collect(proc, sql.encode("utf-8"))
        f.save(label + "-" + table.member, output)
        require(proc.returncode == 0 and output == (bundle / table.member).read_bytes(),
                "native Identity Priority bytes changed for " + table.name + ": " + error.decode("utf-8"))


def check_native_keys(f, database, manifest):
    keys = json.loads(f.sql(database, """
        SELECT json_object_agg(c.relname,(SELECT json_agg(json_build_object('primary',i.indisprimary,'columns',ARRAY(
            SELECT a.attname::text FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(num,ord)
            JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum=k.num
            WHERE k.ord<=i.indnkeyatts ORDER BY k.ord)))
            FROM pg_index i WHERE i.indrelid=c.oid AND i.indisunique AND i.indisvalid
              AND i.indpred IS NULL AND i.indexprs IS NULL))
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='rma_archive_stage' AND c.relkind='r' AND left(c.relname,4)='Cp6_';
    """, label="v5-native-keys"))
    expected = manifest["identityPriority"]["tables"]
    require(keys.keys() == expected.keys(), "wrong staged Identity Priority relation set")
    for name, rows in keys.items():
        actual = sorted((tuple(row["columns"]), row["primary"]) for row in rows)
        pinned_keys = sorted((tuple(key), key == ["Id"]) for key in expected[name]["uniqueKeys"])
        require(actual == pinned_keys, "changed original native key scopes for " + name)


def check_receipt(raw, source, target, counts):
    bundle, tenant, digest = source
    original = json.loads((bundle / "manifest.json").read_text(encoding="utf-8"))
    receipt = json.loads(raw)
    require(receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["format"] == identity_priority.FORMAT and receipt["target"] == target
            and receipt["tenantId"] == tenant and receipt["manifestSha256"] == digest
            and receipt["counts"] == original["counts"] and receipt["journalCounts"] == original["journal"]["counts"]
            and receipt["sourceCounts"] == original["rmaSource"]["counts"]
            and receipt["visibilityCounts"] == original["rmaVisibility"]["counts"]
            and [receipt["identityPriorityCounts"][table.name] for table in identity_priority.TABLES] == counts,
            "wrong v5 original stage receipt")
    return original


def exercise(f, a, b):
    bundle, tenant, digest = a
    target = new_target(f, "w385_priority_a")
    database = target["Database"]
    stage_with_global_key_fault(f, target, tenant, digest, bundle)
    assert_empty(f, database)
    invoke(f, "v5-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")

    invoke(f, "v5-stage-discarded", "stage", target, tenant, digest, bundle, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")
    recovered = invoke(f, "v5-recover", "recover", target, tenant, digest)
    require(recovered == original, "v5 recovery changed persisted receipt bytes")
    manifest = check_receipt(recovered, a, target, [1, 1, 1, 2])
    for table in identity_priority.TABLES:
        native_roundtrip(f, "a", database, table, bundle)
    check_native_keys(f, database, manifest)
    check_journal(f, database, bundle)
    check_acl(f, database, tenant)
    require(f.sql(database, "SELECT to_regnamespace('crm_identity_priority') IS NULL;") == "t",
            "Identity Priority staging created the active schema")

    b_bundle, b_tenant, b_digest = b
    b_target = new_target(f, "w385_priority_b")
    receipt = invoke(f, "v5-stage-b", "stage", b_target, b_tenant, b_digest, b_bundle)
    check_receipt(receipt, b, b_target, [0, 1, 1, 1])
    dead_letter = identity_priority.TABLES[0]
    native_roundtrip(f, "b", b_target["Database"], dead_letter, b_bundle)
    require(f.sql(b_target["Database"], 'SELECT count(*) FROM rma_archive_stage."Cp6_DeadLetterRecord";') == "0",
            "header-only v5 member staged a dead-letter row")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "a-bundle", "b-bundle"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("a-sha256", "b-sha256"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--port", type=int, required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    a = pinned(args.a_bundle, args.a_sha256, identity_priority.FORMAT)
    b = pinned(args.b_bundle, args.b_sha256, identity_priority.FORMAT)
    fixture = Fixture(args, "rma-stage-v5-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("sources.json", {name: [str(bundle[0]), *bundle[1:]] for name, bundle in (("A", a), ("B", b))})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture, a, b)
    print("native v5 staging, late 23505 rollback, exact receipt and header-only member passed", flush=True)


if __name__ == "__main__":
    main()
