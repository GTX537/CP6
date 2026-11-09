#!/usr/bin/env python3
"""Stage and recover the pinned historical IAM reference archive on PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True

from verify_stage import (BASE_ENV, OWNER, Fixture, assert_empty, check_acl, invoke, new_target, pinned, put_json,
                          require, token)
import archive
import iam_v6
import stage


def stage_with_participant_fault(f, target, tenant, digest, bundle):
    request = f.root / "v6-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle, output=f.root / "v6-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        driver = output / "driver.sql"
        script = driver.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_iam_references$"
        require(script.count(marker) == 1, "missing native participant check")
        # Forge a head flag for the original journal-only actor after all COPYs, preserving the pin.
        fault = """
            UPDATE rma_archive_stage.iam_reference_snapshot SET facts=jsonb_set(facts,'{referenced_head}','true'::jsonb)
            WHERE kind='participant' AND facts->'user_present'='false'::jsonb AND facts->'referenced_head'='false'::jsonb;
        """
        driver.write_text(script.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-iam-participant-mismatch", "late native participant check did not reject")
        else:
            raise AssertionError("false participant head reference was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"P0001" in stderr and b"rma-stage-iam-participant-mismatch" in stderr, "missing native participant error")


def check_roundtrip(f, database, table, order, original):
    # The native archive can have any fractional-second width supported by PostgreSQL.
    with f.psql(database, app="W391-roundtrip") as proc:
        sql = f"""
            SET bytea_output='hex'; SET timezone='UTC'; SET datestyle='ISO, YMD';
            COPY (SELECT * FROM rma_archive_stage.{table} ORDER BY {order}) TO STDOUT WITH (FORMAT csv, HEADER true);
        """
        output, error = f.collect(proc, sql.encode("utf-8"))
        f.save(table + ".csv", output)
        f.save(table + ".stderr", error)
        require(proc.returncode == 0 and output == original.read_bytes(), "staging changed original " + table)


def exercise(f, source):
    bundle, tenant, digest = source
    target = new_target(f, "w391_iam")
    database = target["Database"]
    stage_with_participant_fault(f, target, tenant, digest, bundle)
    assert_empty(f, database)
    invoke(f, "v6-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")

    invoke(f, "v6-stage-discarded", "stage", target, tenant, digest, bundle, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")
    recovered = invoke(f, "v6-recover", "recover", target, tenant, digest)
    require(recovered == original, "v6 recovery changed persisted receipt bytes")
    receipt = json.loads(recovered)
    manifest = json.loads((bundle / "manifest.json").read_text(encoding="utf-8"))
    counts = {name: value["count"] for name, value in manifest["identityPriority"]["tables"].items()}
    require(receipt["format"] == iam_v6.FORMAT and receipt["target"] == target and receipt["tenantId"] == tenant
            and receipt["manifestSha256"] == digest and receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["counts"] == manifest["counts"] and receipt["journalCounts"] == manifest["journal"]["counts"]
            and receipt["sourceCounts"] == manifest["rmaSource"]["counts"]
            and receipt["visibilityCounts"] == manifest["rmaVisibility"]["counts"]
            and receipt["identityPriorityCounts"] == counts
            and receipt["iamReferenceCounts"] == manifest["rmaIamReferences"]["counts"], "wrong original v6 stage receipt")

    check_roundtrip(f, database, "iam_reference_snapshot", iam_v6.ORDER, bundle / iam_v6.MEMBER)
    check_roundtrip(f, database, "original_journal_snapshot", "operation_id", bundle / archive.JOURNAL_MEMBER)
    check_acl(f, database, tenant)
    facts = json.loads(f.sql(database, """
        SELECT json_build_object(
            'missing', count(*) FILTER (WHERE kind='participant' AND facts @> '{"user_present":false,"user_name":null,
                         "enabled":null,"referenced_head":false,"referenced_journal":true}'::jsonb),
            'both', count(*) FILTER (WHERE kind='participant' AND facts @> '{"user_present":true,
                         "referenced_head":true,"referenced_journal":true}'::jsonb),
            'disabledRole', count(*) FILTER (WHERE kind='role' AND facts @> '{"role_present":true,"enabled":false}'::jsonb),
            'isolated', to_regclass('public."Sys_Users"') IS NULL AND to_regclass('public."Sys_Roles"') IS NULL
                AND to_regnamespace('cp6_authentication_v1') IS NULL AND to_regnamespace('crm_identity_priority') IS NULL)
        FROM rma_archive_stage.iam_reference_snapshot;
    """, label="v6-native-presence"))
    require(facts == {"missing": 1, "both": 1, "disabledRole": 1, "isolated": True},
            "v6 changed historical absence, disabled role, or isolation")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "bundle"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    source = pinned(args.bundle, args.manifest_sha256, iam_v6.FORMAT)
    fixture = Fixture(args, "rma-stage-v6-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("source.json", {"Bundle": str(source[0]), "TenantId": source[1], "ManifestSha256": source[2]})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture, source)
    print("native v6 IAM staging, late participant rollback and original recovery passed", flush=True)


if __name__ == "__main__":
    main()
