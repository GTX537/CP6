#!/usr/bin/env python3
"""Verify a Stock adjustment candidate with exact retained B and owned PostgreSQL targets."""
import argparse
from dataclasses import dataclass, replace
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
import archive
import candidate_core as core
import stock_adjust_candidate as importer


STOCK = importer.STOCK
Q = archive.literal


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


@dataclass(frozen=True)
class Request:
    pg_bin: Path
    target_file: Path
    username: str
    tenant: str
    candidate_id: str
    manifest_sha256: str
    effects_sha256: str
    bundle: Path
    effects: Path
    file_content_root: Path
    output: Path
    action: str = "import"

    @property
    def key(self):
        return {"target": self.target.mapping(), "candidateId": self.candidate_id, "tenantId": self.tenant,
                "manifestSha256": self.manifest_sha256, "effectsSha256": self.effects_sha256}

    @property
    def target(self):
        return archive.PhysicalTarget.from_mapping(json.loads(self.target_file.read_text(encoding="utf-8-sig")))


def native(request, label, sql, *, write=False, error=None):
    out = request.output
    command = [archive.binary(request.pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=verbose",
               *archive.connection_args(request.target, request.username), "--single-transaction", "--file=-"]
    text = core.target_sql({"key": request.key}, not write) + archive.JOURNAL_COPY_SETTINGS + sql
    if write:
        text = core.target_sql({"key": request.key}, False) + importer.closed_sql() + archive.JOURNAL_COPY_SETTINGS + sql
    (out / (label + ".sql")).write_text(text, encoding="utf-8")
    result = subprocess.run(command, input=text.encode("utf-8"), capture_output=True, cwd=out,
                            env=archive.client_environment(os.environ, "cp6-stock-adjust-verify"), timeout=40,
                            creationflags=archive.FLAGS)
    (out / (label + ".stdout")).write_bytes(result.stdout)
    (out / (label + ".stderr")).write_bytes(result.stderr)
    if error is None:
        require(result.returncode == 0 and not result.stderr, label + " native execution failed")
    else:
        require(result.returncode != 0 and error in result.stderr and not result.stdout, label + " wrong native failure")
    return result.stdout


def run_cli(request, label, *, error=None, discard=False):
    output = request.output / label
    args = [sys.executable, "-B", str(Path(importer.__file__).resolve()), request.action]
    fields = ("pg_bin", "target_file", "username", "tenant", "candidate_id", "manifest_sha256", "effects_sha256")
    if request.action == "import":
        fields += ("bundle", "effects", "file_content_root")
    for field in fields:
        args.extend(("--" + field.replace("_", "-"), str(getattr(request, field))))
    args.extend(("--output", str(output)))
    result = subprocess.run(args, stdout=subprocess.DEVNULL if discard else subprocess.PIPE, stderr=subprocess.PIPE,
                            cwd=request.output, timeout=95, creationflags=archive.FLAGS)
    (request.output / (label + ".process.json")).write_text(json.dumps(
        {"exit": result.returncode, "discarded": discard, "error": result.stderr.decode("utf-8")}) + "\n", encoding="utf-8")
    if error is None:
        require(result.returncode == 0 and not result.stderr, label + " command failed")
    else:
        require(result.returncode == 1 and not result.stdout and error in result.stderr, label + " wrong command failure")
    return result.stdout


def read_effects(request, label, tenant):
    rows = {}
    for part in importer.EFFECTS:
        tenant_column = "tenant_id" if part is importer.EFFECTS[3] else '"TenantId"'
        rows[part.member] = native(request, label + "-" + part.member, f"""
          COPY (SELECT {importer.columns(part)} FROM {part.relation} WHERE {tenant_column}={Q(tenant)})
          TO STDOUT WITH (FORMAT csv, HEADER true);
        """)
    return rows


def read_stock(request, label, tenant, *, preserve_xmin):
    rows = {}
    for part in STOCK.TABLES:
        select = STOCK.rows_sql(part, tenant, STOCK.root(tenant)["id"])
        if not preserve_xmin and part.xmin:
            select = select.replace(',r.xmin::text AS "source_xmin"', "")
        rows[part.member] = native(request, label + "-" + part.member,
                                  f"COPY ({select}) TO STDOUT WITH (FORMAT csv, HEADER true);")
    return rows


def without_xmin(raw, part):
    if not part.xmin:
        return raw
    # Snapshot transaction IDs are PostgreSQL metadata, not native stored columns.
    return re.sub(rb",(?:[0-9]+|source_xmin)(?=\r?$)", b"", raw, flags=re.MULTILINE)


def boundary(request, label):
    physical = (importer.relation(part) for part in (*importer.EFFECTS, *STOCK.TABLES))
    relations = ",".join(Q(part) + "::regclass" for part in physical)
    raw = native(request, label, f"""
    SELECT jsonb_build_object(
      'core',(SELECT jsonb_build_array(last_value,is_called) FROM {importer.CORE_SEQUENCE}),
      'movement',(SELECT jsonb_build_array(last_value,is_called) FROM {importer.MOVEMENT_SEQUENCE}),
      'authorityRevision',(SELECT revision FROM cp6_platform_admin_guard_v1.root WHERE singleton),
      'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
      'environment',(SELECT count(*) FROM cp6_scope_authorization_v1.environment),
      'receipt',to_regnamespace('{importer.RECEIPTS}') IS NOT NULL,
      'database',(SELECT jsonb_build_array(datacl,shobj_description(oid,'pg_database'))
                  FROM pg_database WHERE datname=current_database()),
      'tableAcls',(SELECT jsonb_agg(jsonb_build_array(c.oid,c.relacl,c.relrowsecurity,c.relforcerowsecurity) ORDER BY c.oid)
                   FROM pg_class c WHERE c.oid IN ({relations})),
      'triggers',(SELECT jsonb_agg(jsonb_build_array(t.oid,t.tgenabled,pg_get_triggerdef(t.oid)) ORDER BY t.oid)
                  FROM pg_trigger t WHERE t.tgrelid IN ({relations})));
    """)
    return json.loads(raw)


def inject_late_failure(request, core_next, movement_next):
    real_run = importer.stage.run_driver
    source_write = importer.insert(STOCK.SOURCE)
    proof_check = f"SET CONSTRAINTS {STOCK.SCHEMA}.stock_source_proof IMMEDIATE;"
    alter_source = f"""UPDATE {importer.temporary(STOCK.SOURCE)} SET "ResultSha256"=repeat('0',64);"""
    observe = f"""
    DO $late_stock_adjust$
    BEGIN
        IF (SELECT jsonb_build_array(last_value,is_called) FROM {importer.CORE_SEQUENCE})<>jsonb_build_array({core_next},false)
           OR (SELECT jsonb_build_array(last_value,is_called) FROM {importer.MOVEMENT_SEQUENCE})<>jsonb_build_array({movement_next},false)
           OR NOT EXISTS (SELECT FROM {importer.RECEIPTS}.receipt)
           OR NOT EXISTS (SELECT FROM public."T_Stock" WHERE "TenantId"={Q(request.tenant)})
           OR NOT EXISTS (SELECT FROM {STOCK.relation(STOCK.NATIVE)} WHERE "TenantId"={Q(request.tenant)}) THEN
            RAISE EXCEPTION 'fault test did not reach restored rows, restarted allocators and saved receipt';
        END IF;
        RAISE NOTICE 'late Stock source proof after native rows, allocator restarts and receipt';
    END
    $late_stock_adjust$;
    """

    def fault(pg, target, username, output, action):
        driver = output / "driver.sql"
        sql = driver.read_text(encoding="utf-8")
        require(sql.count(source_write) == sql.count(proof_check) == 1, "Stock fault injection point changed")
        driver.write_text(sql.replace(source_write, alter_source + "\n" + source_write)
                             .replace(proof_check, observe + proof_check), encoding="utf-8")
        return real_run(pg, target, username, output, action)

    attempt = replace(request, output=request.output / "late-source")
    with patch.object(importer.stage, "run_driver", side_effect=fault):
        try:
            importer.execute(attempt)
        except importer.stage.StageError:
            error = (attempt.output / "stage.stderr").read_bytes()
            require(b"late Stock source proof after native rows" in error and
                    b"23514: STOCK_OUTBOX_SOURCE_PROOF_REQUIRED" in error and not (attempt.output / "stage.stdout").read_bytes(),
                    "native late rejection differed")
        else:
            raise RuntimeError("late Stock source proof accepted a wrong original digest")


def verify(request, tenant_b, b_effects, b_queue, core_next, movement_next):
    before = boundary(request, "boundary-before")
    require(before["core"][1] is False and before["movement"][1] is False and
            before["core"][0] < core_next and
            before["movement"][0] + before["movement"][1] < movement_next and not before["receipt"] and
            before["admission"] == before["environment"] == 0, "expected an empty closed candidate with stale sequences")
    a_effects = read_effects(request, "before-a-effect", request.tenant)
    a_queue = read_stock(request, "before-a-queue", request.tenant, preserve_xmin=True)
    require(all(len(value.splitlines()) == 1 for value in (*a_effects.values(), *a_queue.values())),
            "candidate already has A originals")
    expected_b_effects = {part.member: (b_effects / part.member).read_bytes() for part in importer.EFFECTS}
    expected_b_queue = {part.member: without_xmin((b_queue / part.member).read_bytes(), part) for part in STOCK.TABLES}
    before_b_queue = read_stock(request, "before-b-queue", tenant_b, preserve_xmin=True)
    require(read_effects(request, "before-b-effect", tenant_b) == expected_b_effects, "retained B effects differ")
    require({part.member: without_xmin(before_b_queue[part.member], part) for part in STOCK.TABLES} == expected_b_queue,
            "retained B queue differs")
    run_cli(replace(request, effects_sha256="0" * 64), "wrong-effects-pin",
            error=b"rma-stage-stock-effect-digest-mismatch")
    inject_late_failure(request, core_next, movement_next)
    recovery = replace(request, action="recover")
    run_cli(recovery, "after-fault-recover", error=b"rma-stage-candidate-receipt-not-found")
    require(boundary(request, "boundary-after-fault") == before and
            read_effects(request, "after-fault-a-effect", request.tenant) == a_effects and
            read_stock(request, "after-fault-a-queue", request.tenant, preserve_xmin=True) == a_queue and
            read_effects(request, "after-fault-b-effect", tenant_b) == expected_b_effects and
            read_stock(request, "after-fault-b-queue", tenant_b, preserve_xmin=True) == before_b_queue,
            "native rollback changed a row, sequence, trigger or receipt")
    run_cli(request, "import-discarded", discard=True)
    original = run_cli(recovery, "recovered")
    retried = run_cli(request, "same-key")
    receipt = json.loads(original)
    persisted = native(request, "persisted-receipt", f"""
      SELECT encode(convert_to(result_json,'UTF8'),'hex') FROM {importer.RECEIPTS}.receipt;
    """)
    require(original == retried == bytes.fromhex(persisted.decode("ascii")) and
            receipt["binding"] == request.key and receipt["recoveryReady"] is False and
            receipt["profile"] == importer.PROFILE and receipt["outcome"] == "IMPORTED" and
            receipt["coreRowVersionNext"] == core_next and receipt["movementSequenceNext"] == movement_next,
            "original receipt or allocation floor changed")
    run_cli(replace(recovery, effects_sha256="0" * 64), "wrong-recover-effects-pin",
            error=b"rma-stage-candidate-receipt-key-rejected")
    expected_a_effects = {part.member: (request.effects / part.member).read_bytes() for part in importer.EFFECTS}
    expected_a_queue = {part.member: without_xmin(
        (request.bundle / (archive.STOCK_MEMBER_PREFIX + part.member)).read_bytes(), part) for part in STOCK.TABLES}
    require(read_effects(request, "restored-a-effect", request.tenant) == expected_a_effects and
            read_stock(request, "restored-a-queue", request.tenant, preserve_xmin=False) == expected_a_queue and
            read_effects(request, "retained-b-effect", tenant_b) == expected_b_effects and
            read_stock(request, "retained-b-queue", tenant_b, preserve_xmin=True) == before_b_queue,
            "an original native row or retained B changed")
    tenant = Q(request.tenant)
    for label, sql, error in (
        ("immutable-journal", f"UPDATE {importer.JOURNAL} SET result_bytes=result_bytes WHERE tenant_id={tenant};",
         b"23514: MAIN_ORIGINAL_OPERATION_IMMUTABLE"),
        ("immutable-movement", f"UPDATE {importer.EFFECTS[3].relation} SET pool_id=pool_id WHERE tenant_id={tenant};",
         b"23514: RMA_FORMAL_FACT_IMMUTABLE"),
        ("immutable-source", f'''UPDATE {STOCK.relation(STOCK.SOURCE)} SET "ResultSha256"="ResultSha256"
                                  WHERE "TenantId"={tenant};''', b"55000: CP6_OUTBOX_IMMUTABLE_EVIDENCE"),
    ):
        native(request, label, sql, write=True, error=error)
    complete = boundary(request, "boundary-after")
    require(complete["authorityRevision"] > before["authorityRevision"] and
            complete == dict(before, core=[core_next, False], movement=[movement_next, False], receipt=True,
                             authorityRevision=complete["authorityRevision"]),
            "unexpected closure, allocation or native guard change")
    result = {"result": "PASS", "target": request.target.mapping(), "receipt": receipt,
              "receiptBytes": len(original), "lateNativeRollback": True, "exactNativeA": True, "retainedNativeB": True}
    (request.output / "actual.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    for field in ("pg-bin", "target-file", "bundle", "effects", "file-content-root", "output", "b-effects", "b-queue"):
        cli.add_argument("--" + field, type=Path, required=True)
    for field in ("username", "tenant", "tenant-b", "candidate-id", "manifest-sha256", "effects-sha256"):
        cli.add_argument("--" + field, required=True)
    for field in ("core-next", "movement-next"):
        cli.add_argument("--" + field, type=int, required=True)
    args = cli.parse_args()
    request = Request(**{name: getattr(args, name) for name in Request.__dataclass_fields__ if name != "action"})
    require(archive.guid(args.tenant) != archive.guid(args.tenant_b), "fixture tenants must be distinct")
    output = importer.paths.checked_output(request.output.absolute(), request.target_file.resolve(strict=True))
    for item in (request.bundle, request.effects, request.file_content_root, args.b_effects, args.b_queue):
        importer.paths.checked_output(output, item.resolve(strict=True))
    request = replace(request, output=output)
    archive.private_directory(output)
    verify(request, args.tenant_b, args.b_effects, args.b_queue, args.core_next, args.movement_next)


if __name__ == "__main__":
    main()
