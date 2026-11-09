#!/usr/bin/env python3
"""Stage and recover the real v4 visibility archive on isolated PostgreSQL."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True

from verify_stage import (BASE_ENV, OWNER, Fixture, assert_empty, check_acl, check_journal, copy_staged, invoke,
                          new_target, pinned, put_json, require, token)
import stage
import visibility_v4


def stage_with_graph_fault(f, target, tenant, digest, bundle):
    request = f.root / "v4-late-target.json"
    put_json(request, target)
    args = argparse.Namespace(action="stage", pg_bin=f.args.pg_bin, target_file=request, username=OWNER,
                              tenant=tenant, manifest_sha256=digest, bundle=bundle, output=f.root / "v4-late-native")
    run_driver = stage.run_driver

    def inject(pg_bin, physical, username, output, action):
        # Change one staged active detail after all COPYs without changing the pinned input.
        driver = output / "driver.sql"
        script = driver.read_text(encoding="utf-8")
        marker = "DO $cp6_stage_visibility$"
        require(script.count(marker) == 1, "missing native visibility check")
        fault = """
            UPDATE rma_archive_stage.visibility_snapshot SET facts=jsonb_set(facts,'{warehouse_cd}','\"\"'::jsonb)
            WHERE kind='outbound_detail' AND facts->'is_deleted'='false'::jsonb AND facts->'warehouse_cd'='null'::jsonb;
        """
        driver.write_text(script.replace(marker, fault + marker, 1), encoding="utf-8")
        return run_driver(pg_bin, physical, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", inject):
        try:
            stage.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-visibility-graph-mismatch", "late native graph check did not reject")
        else:
            raise AssertionError("late visibility corruption was accepted")
    stderr = (args.output / "stage.stderr").read_bytes()
    require(b"P0001" in stderr and b"rma-stage-visibility-graph-mismatch" in stderr, "missing native graph error")


def exercise(f, source):
    bundle, tenant, digest = source
    target = new_target(f, "w382_visibility")
    database = target["Database"]
    stage_with_graph_fault(f, target, tenant, digest, bundle)
    assert_empty(f, database)
    invoke(f, "v4-rollback-no-receipt", "recover", target, tenant, digest, None, 1, "rma-stage-receipt-not-found")

    invoke(f, "v4-stage-discarded", "stage", target, tenant, digest, bundle, discard=True)
    original = f.sql(database, "SELECT result_json FROM rma_archive_stage.receipt;").encode("utf-8")
    recovered = invoke(f, "v4-recover", "recover", target, tenant, digest)
    require(recovered == original, "v4 recovery changed persisted receipt bytes")
    receipt = json.loads(recovered)
    require(receipt["format"] == visibility_v4.FORMAT and receipt["target"] == target and receipt["tenantId"] == tenant
            and receipt["manifestSha256"] == digest and receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False
            and receipt["counts"] == {"heads": 3, "versions": 3, "history": 1, "outbounds": 3}
            and receipt["journalCounts"] == {"rows": 1, "committed": 1, "closed": 0}
            and receipt["sourceCounts"] == {"headers": 3, "details": 3, "lineage": 0}
            and receipt["visibilityCounts"] == {"outbound": 3, "outbound_detail": 4, "order": 3, "work_order": 2,
                                               "location": 4}, "wrong original v4 stage receipt")

    copy_staged(f, database, "visibility_snapshot", visibility_v4.ORDER, bundle / visibility_v4.MEMBER)
    check_journal(f, database, bundle)
    check_acl(f, database, tenant)
    facts = json.loads(f.sql(database, """
        SELECT json_build_object(
            'nullWarehouse', count(*) FILTER (WHERE kind='outbound_detail' AND facts->'warehouse_cd'='null'::jsonb),
            'emptyWarehouse', count(*) FILTER (WHERE kind='outbound_detail' AND facts->>'warehouse_cd'=''),
            'deletedDetail', count(*) FILTER (WHERE kind='outbound_detail' AND facts->'is_deleted'='true'::jsonb
                                                AND facts->'creator'='null'::jsonb),
            'quarantined', count(*) FILTER (WHERE kind='location' AND facts->'is_initialization_quarantined'='true'::jsonb
                                               AND facts->'area_cd'='null'::jsonb),
            'emptyArea', count(*) FILTER (WHERE kind='location' AND facts->'is_initialization_quarantined'='false'::jsonb
                                             AND facts->>'area_cd'=''))
        FROM rma_archive_stage.visibility_snapshot;
    """, label="v4-native-null-quarantine"))
    require(facts == {"nullWarehouse": 1, "emptyWarehouse": 2, "deletedDetail": 1, "quarantined": 1, "emptyArea": 1},
            "v4 changed JSON NULL, empty strings, deletion, or quarantine")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "bundle"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--manifest-sha256", required=True)
    args = parser.parse_args()
    args.root = args.root.resolve(strict=True)
    args.pg_bin = args.pg_bin.resolve(strict=True)
    source = pinned(args.bundle, args.manifest_sha256, visibility_v4.FORMAT)
    fixture = Fixture(args, "rma-stage-v4-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("source.json", {"Bundle": str(source[0]), "TenantId": source[1], "ManifestSha256": source[2]})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture, source)
    print("native v4 visibility staging, late rollback, same-pin retry and original recovery passed", flush=True)


if __name__ == "__main__":
    main()
