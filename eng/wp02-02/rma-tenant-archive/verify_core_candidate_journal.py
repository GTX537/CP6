#!/usr/bin/env python3
"""Exercise original RMA adoption against a closed Core candidate prepared from its pinned archive."""

import argparse
import csv
import hashlib
import json
import os
import subprocess
import sys
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Optional
from unittest.mock import patch
from uuid import uuid4

import archive
import candidate_core as candidate
import stage
from verify_stage import BASE_ENV, FLAGS, require


TABLE = archive.JOURNAL_TABLE
OWNER = archive.JOURNAL_OWNER
CONTRACT = archive.JOURNAL_CONTRACT
MEMBER = archive.JOURNAL_MEMBER
COLUMNS = ", ".join(archive.JOURNAL_COLUMNS)
q = archive.literal


@dataclass(frozen=True)
class Inputs:
    action: str
    pg_bin: Path
    target_file: Path
    commission_receipt: Path
    output: Path
    username: str
    tenant: str
    candidate_id: str
    manifest_sha256: str
    bundle: Path
    file_content_root: Optional[Path] = None


def cli_command(args):
    command = [sys.executable, "-B", str(Path(candidate.__file__)), args.action]
    for name in ("pg_bin", "target_file", "commission_receipt", "output", "username", "tenant",
                 "candidate_id", "manifest_sha256"):
        command.extend(("--" + name.replace("_", "-"), str(getattr(args, name))))
    if args.action.startswith("import"):
        command.extend(("--bundle", str(args.bundle)))
        if args.file_content_root is not None:
            command.extend(("--file-content-root", str(args.file_content_root)))
    return command


def offline(args):
    tenant = str(archive.guid(args.tenant))
    require(archive.file_digest(args.bundle / "manifest.json")["sha256"] == args.manifest_sha256,
            "fixture archive pin differs")
    archive.verify_bundle(args.bundle, tenant, file_content_root=args.file_content_root)
    result = subprocess.run([*cli_command(args), "--help"], env=BASE_ENV, capture_output=True, timeout=15,
                            creationflags=FLAGS)
    sys.stderr.buffer.write(result.stderr)
    require(result.returncode == 0 and b"--commission-receipt" in result.stdout,
            "candidate original journal entry is unavailable")


def native_cli(f, label, args, error=None):
    environ = {**BASE_ENV, "PGPASSWORD": f.passwords.get(args.username, "")}
    result = subprocess.run(cli_command(args), env=environ, capture_output=True, timeout=90, creationflags=FLAGS)
    (f.output / (label + ".stdout")).write_bytes(result.stdout)
    (f.output / (label + ".stderr")).write_bytes(result.stderr)
    (f.output / (label + ".exit")).write_text(str(result.returncode), encoding="ascii")
    if error:
        require(result.returncode == 1 and not result.stdout and error.encode("ascii") in result.stderr,
                f"{label} wrong rejection")
    else:
        require(result.returncode == 0 and not result.stderr, f"{label} failed; see native evidence")
    return result.stdout


def sample_state(f, label, a, b):
    a_schema, b_schema = ("t_" + archive.guid(tenant).hex for tenant in (a, b))
    return f.read(label, f"""
        SELECT jsonb_build_object(
            'coreReceipt',(SELECT to_jsonb(r) FROM {candidate.RECEIPTS}.receipt r),
            'commission',(SELECT to_jsonb(r) FROM cp6_rma_core_commission_v1.installation r),
            'journalInstallation',(SELECT to_jsonb(r) FROM cp6_rma_original_journal_v1.installation r),
            'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
            'directory',(SELECT jsonb_agg(to_jsonb(r) ORDER BY tenant_id) FROM cp6_tenancy_v1.login_tenant r),
            'migrations',(SELECT jsonb_agg(to_jsonb(r) ORDER BY tenant_id) FROM cp6_rma_tenant_v1.tenant_migrations r),
            'aHeads',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM {a_schema}.heads r),
            'aVersions',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id,version) FROM {a_schema}.versions r),
            'bHeads',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM {b_schema}.heads r),
            'bVersions',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id,version) FROM {b_schema}.versions r),
            'otherOriginals',(SELECT jsonb_agg(to_jsonb(r) ORDER BY tenant_id,owner,operation_id) FROM {TABLE} r
                              WHERE tenant_id<>{q(a)} OR owner<>{q(OWNER)}),
            'journalAcl',(SELECT to_jsonb(c.relacl) FROM pg_catalog.pg_class c WHERE c.oid='{TABLE}'::regclass),
            'journalColumns',(SELECT jsonb_agg(jsonb_build_array(attname,attacl) ORDER BY attnum)
                              FROM pg_catalog.pg_attribute WHERE attrelid='{TABLE}'::regclass AND attnum>0),
            'triggers',(SELECT jsonb_agg(jsonb_build_array(t.oid,t.tgenabled,t.tgfoid,pg_get_triggerdef(t.oid)) ORDER BY t.oid)
                        FROM pg_catalog.pg_trigger t WHERE t.tgrelid='{TABLE}'::regclass));
        """)


def fault(f, args, label, marker, insertion, message):
    run_driver = stage.run_driver

    def inject(pg_bin, target, username, output, action):
        driver = output / "driver.sql"
        sql = driver.read_text(encoding="utf-8")
        require(sql.count(marker) == 1, "native fault point changed")
        driver.write_text(sql.replace(marker, insertion + marker), encoding="utf-8")
        return run_driver(pg_bin, target, username, output, action)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords.get(args.username, "")}, clear=True), \
            patch.object(stage, "run_driver", side_effect=inject):
        try:
            candidate.execute(args)
        except stage.StageError as error:
            require(str(error) == message, f"{label} returned {error}")
        else:
            raise RuntimeError(f"{label} committed unexpectedly")
    return (args.output / "stage.stderr").read_bytes()


def native(f, args, ordinary, original_core, *, seeded_b=False, seeded_other=False):
    """Uses the actual commissioned database owner and both actual tenant LOGIN credentials."""
    a = str(archive.guid(args.tenant))
    (tenant_a, login_a), (b, login_b) = ordinary
    require(a == tenant_a and a != b, "fixture tenant bindings differ")
    with (args.bundle / MEMBER).open(encoding="utf-8", newline="") as stream:
        records = list(csv.DictReader(stream))
    expected = {row["operation_id"]: dict(Actor=row["actor_id"], State=row["state"], Result=row["result_bytes"][2:])
                for row in records}
    require(len(expected) == len(records) == 2 and all(row["state"] == "COMMITTED" for row in records),
            "fixture expects original draft and submit")
    same_id = records[0]["operation_id"]
    if seeded_b:
        actor = str(archive.guid(f.sql("b-prior-seed", f"""
            SELECT actor_id FROM {TABLE} WHERE tenant_id={q(b)} AND owner={q(OWNER)} AND contract={q(CONTRACT)}
              AND operation_id={q(same_id)} AND state='CLOSED' AND audit_json='{{"fixture":"B"}}';
            """)))
    else:
        actor = str(uuid4())
        f.sql("b-native-same-key", f"""
            SELECT cp6_rma_original_journal_v1.store({q(b)}::uuid,{q(same_id)}::uuid,{q(actor)}::uuid,'CLOSED',
                NULL::bytea,NULL::text,convert_to(json_build_object('OperationId',{q(same_id)}::uuid,'State','CLOSED',
                'Result',NULL)::text,'UTF8'),ARRAY[]::uuid[],'{{"fixture":"B"}}'::jsonb);
            """, role=login_b)
    insert = f"""INSERT INTO {TABLE}(tenant_id,owner,operation_id,actor_id,contract,state,result_bytes,object_ids,audit_json)
        VALUES ({q(a)},'fixture.w413.other-owner',{q(same_id)},{q(actor)},'fixture','CLOSED',
            convert_to('{{}}','UTF8'),ARRAY[]::uuid[],'{{}}');"""
    if seeded_other:
        require(f.sql("other-owner-prior-seed", f"""
            SELECT count(*) FROM {TABLE} WHERE tenant_id={q(a)} AND owner='fixture.w413.other-owner'
              AND operation_id={q(same_id)} AND actor_id={q(actor)} AND contract='fixture' AND state='CLOSED'
              AND result_bytes=convert_to('{{}}','UTF8') AND object_ids=ARRAY[]::uuid[] AND audit_json='{{}}';
            """) == "1", "exact other Owner seed changed")
    else:
        f.sql("other-owner-before", "BEGIN; DO $$ BEGIN PERFORM cp6_authentication_v1.acquire(); END $$; " + insert + " COMMIT;")
    where = f"tenant_id={q(a)} AND owner={q(OWNER)}"
    baseline = sample_state(f, "protected-before", a, b)
    require(baseline["admission"] == 0, "fixture is open")
    recover = replace(args, action="recover-journal", output=f.output / "journal-recover-missing")
    native_cli(f, "journal-recover-missing", recover, "rma-stage-candidate-receipt-not-found")
    native_cli(f, "wrong-marker", replace(args, candidate_id=str(uuid4()), output=f.output / "wrong-marker"),
               "rma-stage-candidate-marker-rejected")

    graph = fault(f, replace(args, output=f.output / "bad-graph"), "bad graph", "-- Keep the original Owner-specific",
                  "UPDATE pg_temp.rma_original_import SET result_bytes=convert_to(\n"
                  "json_build_object('OperationId',operation_id,'State','DRAFT','Marker','fixture')::text,'UTF8');\n",
                  "rma-stage-candidate-journal-graph-rejected")
    require(b"rma-stage-candidate-journal-graph-rejected" in graph, "native graph rejection was not reached")

    late = fault(f, replace(args, output=f.output / "late-rollback"), "late journal", "ALTER TABLE ONLY " + TABLE +
                 " ENABLE ALWAYS TRIGGER rma_core_legacy_disabled;", f"""
        IF (SELECT count(*) FROM {TABLE} WHERE {where})<>2 OR NOT EXISTS (SELECT FROM pg_trigger
             WHERE tgrelid='{TABLE}'::regclass AND tgname='rma_core_legacy_disabled' AND tgenabled='D') THEN
            RAISE EXCEPTION 'fixture did not reach native insert';
        END IF;
        RAISE NOTICE 'fixture observed originals and the uncommitted legacy gate mode';
        UPDATE {TABLE} SET audit_json='{{}}' WHERE {where};
        """, "rma-stage-native-failed (see native logs)")
    require(b"fixture observed originals" in late and b"23514" in late and b"MAIN_ORIGINAL_OPERATION_IMMUTABLE" in late,
            "native late rollback or immutable fence evidence missing")
    native_cli(f, "recover-after-rollback", replace(recover, output=f.output / "recover-after-rollback"),
               "rma-stage-candidate-receipt-not-found")
    require(sample_state(f, "protected-after-rollback", a, b) == baseline
            and f.sql("a-empty-after-rollback", f"SELECT count(*) FROM {TABLE} WHERE {where};") == "0",
            "late native failure changed candidate state")

    imported = native_cli(f, "import-journal", replace(args, output=f.output / "import-journal"))
    recovered = native_cli(f, "recover-journal", replace(recover, output=f.output / "recover-journal"))
    replay = native_cli(f, "same-import-journal", replace(args, output=f.output / "same-import-journal"))
    core = native_cli(f, "core-recover-original", replace(recover, action="recover", output=f.output / "core-recover-original"))
    require(imported == recovered == replay and core == original_core, "same-key or original Core receipt bytes changed")
    receipt = json.loads(imported)
    require(receipt["outcome"] == "IMPORTED" and receipt["recoveryReady"] is False
            and receipt["binding"]["manifestSha256"] == args.manifest_sha256
            and receipt["journalCounts"] == {"rows": 2, "committed": 2, "closed": 0}, "journal receipt differs")
    native_cli(f, "recover-wrong-pin", replace(recover, manifest_sha256="0" * 64, output=f.output / "recover-wrong-pin"),
               "rma-stage-candidate-receipt-key-rejected")

    f.sql("actual-original-csv", f"""
        SET timezone='UTC'; SET datestyle='ISO, YMD'; SET bytea_output='hex';
        COPY (SELECT {COLUMNS} FROM {TABLE} WHERE {where} ORDER BY operation_id) TO STDOUT WITH (FORMAT csv,HEADER);
        """)
    raw = (f.output / "actual-original-csv.stdout").read_bytes()
    require(raw == (args.bundle / MEMBER).read_bytes(), "all-column PostgreSQL original CSV differs")
    projection = f"""
        SELECT coalesce(jsonb_object_agg(operation_id,jsonb_build_object('Actor',actor_id,'State',state,
            'Result',encode(result_bytes,'hex'))),'{{}}'::jsonb) FROM {TABLE};
        """
    a_rows = json.loads(f.sql("a-ordinary-read", projection, role=login_a))
    b_rows = json.loads(f.sql("b-ordinary-read", projection, role=login_b))
    require(a_rows == expected and list(b_rows) == [same_id] and b_rows[same_id]["Actor"] == actor
            and b_rows[same_id]["State"] == "CLOSED", "ordinary original projection leaked or changed")
    f.sql("ordinary-receipt-denied", f"SELECT * FROM {candidate.RECEIPTS}.journal_receipt;", role=login_a, error="42501")
    f.sql("ordinary-insert-denied", insert.replace("fixture.w413.other-owner", OWNER), role=login_a, error="42501")
    f.sql("legacy-insert-still-denied", "BEGIN; DO $$ BEGIN PERFORM cp6_authentication_v1.acquire(); END $$; " +
          insert.replace("fixture.w413.other-owner", OWNER).replace(q(same_id), q(str(uuid4()))) + " COMMIT;",
          error="RMA_SHARED_AUTHORITY_RETIRED")
    require(sample_state(f, "protected-after", a, b) == baseline, "other Owner, tenant B or protected layout changed")
    return dict(OriginalCsvSha256=hashlib.sha256(raw).hexdigest(), JournalReceiptSha256=hashlib.sha256(imported).hexdigest(),
                CoreReceiptSha256=hashlib.sha256(core).hexdigest(), OrdinaryA=list(a_rows), OrdinaryB=list(b_rows),
                CandidateReceipt=receipt)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.set_defaults(action="import-journal")
    for field in ("pg-bin", "target-file", "commission-receipt", "output", "bundle", "file-content-root"):
        parser.add_argument("--" + field, type=Path, required=field != "file-content-root")
    for field in ("username", "tenant", "candidate-id", "manifest-sha256"):
        parser.add_argument("--" + field, required=True)
    offline(parser.parse_args())
