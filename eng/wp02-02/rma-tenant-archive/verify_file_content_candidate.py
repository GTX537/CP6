#!/usr/bin/env python3
"""Exercise FileContent candidate import against a prepared isolated PostgreSQL fixture."""

import argparse
from dataclasses import dataclass, replace
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from unittest.mock import patch
from uuid import uuid4

import archive
import file_content_history as history


CLI = Path(__file__).with_name("file_content_candidate.py")
q = archive.literal


@dataclass(frozen=True)
class Inputs:
    action: str
    pg_bin: Path
    target_file: Path
    output: Path
    username: str
    tenant: str
    candidate_id: str
    manifest_sha256: str
    bundle: Path
    file_content_root: Path


def require(value, message):
    if not value:
        raise RuntimeError(message)


def offline():
    for action in ("import", "recover"):
        result = subprocess.run([sys.executable, "-B", str(CLI), action, "--help"], capture_output=True, timeout=15)
        sys.stderr.buffer.write(result.stderr)
        require(result.returncode == 0 and b"--candidate-id" in result.stdout,
                f"FileContent candidate {action} entry unavailable")


def invoke(f, label, args, rejection=None):
    command = [sys.executable, "-B", str(CLI), args.action]
    for name in ("pg_bin", "target_file", "output", "username", "tenant", "candidate_id", "manifest_sha256", "file_content_root"):
        command.extend(("--" + name.replace("_", "-"), str(getattr(args, name))))
    if args.action == "import":
        command.extend(("--bundle", str(args.bundle)))
    result = subprocess.run(command, env=f.env, capture_output=True, timeout=95, creationflags=archive.FLAGS)
    (f.output / (label + ".stdout")).write_bytes(result.stdout)
    (f.output / (label + ".stderr")).write_bytes(result.stderr)
    (f.output / (label + ".exit")).write_text(str(result.returncode), encoding="ascii")
    if rejection:
        require(result.returncode == 1 and not result.stdout and rejection.encode("ascii") in result.stderr,
                f"{label} wrong rejection")
    else:
        require(result.returncode == 0 and not result.stderr, f"{label} failed; see native output")
    return result.stdout


def fault(f, args, edit_driver, rejection):
    import file_content_candidate as candidate
    import stage

    run_driver = stage.run_driver

    def inject(pg_bin, target, username, output, action):
        driver = output / "driver.sql"
        sql = driver.read_text(encoding="utf-8")
        driver.write_text(edit_driver(sql, output), encoding="utf-8")
        return run_driver(pg_bin, target, username, output, action)

    with patch.dict(os.environ, f.env, clear=True), patch.object(stage, "run_driver", side_effect=inject):
        try:
            candidate.execute(args)
        except stage.StageError as error:
            require(str(error) == rejection, f"fixture returned {error}, expected {rejection}")
        else:
            raise RuntimeError("fixture imported unexpectedly")
    return (args.output / "stage.stderr").read_bytes()


def insert_before(sql, marker, insertion):
    require(sql.count(marker) == 1, "native fault point changed")
    return sql.replace(marker, insertion + marker)


def protected(f, label, tenant):
    tables = ("cp6_file_content_v1.contents", "cp6_file_content_v1.heads", "cp6_file_content_v1.events")
    counts = ",".join(f"'{table}',(SELECT jsonb_agg(to_jsonb(t) ORDER BY file_id) FROM {table} t"
                      f" WHERE tenant_id<>{q(tenant)})" for table in tables)
    return f.sql(label, f"""
        SELECT jsonb_build_object('foreignFileContent',jsonb_build_object({counts}),
            'parentHeads',(SELECT jsonb_agg(to_jsonb(h) ORDER BY tenant_id,revision_id) FROM cp6_engineering_revision_v1.heads h),
            'parentVersions',(SELECT jsonb_agg(to_jsonb(v) ORDER BY tenant_id,revision_id,revision)
                FROM cp6_engineering_revision_v1.working_versions v),
            'otherJournal',(SELECT jsonb_agg(to_jsonb(o) ORDER BY tenant_id,owner,operation_id)
                FROM {archive.JOURNAL_TABLE} o WHERE tenant_id<>{q(tenant)} OR owner<>{q(history.assets.OWNER)}),
            'admission',(SELECT count(*) FROM cp6_privacy_disposition_v1.restore_admission),
            'triggers',(SELECT jsonb_agg(jsonb_build_array(t.oid,t.tgfoid,t.tgenabled,pg_get_triggerdef(t.oid)) ORDER BY t.oid)
                FROM pg_trigger t WHERE tgrelid IN ({','.join(q(table)+'::regclass' for table in (*tables, archive.JOURNAL_TABLE))})),
            'journalAcl',(SELECT to_jsonb(c.relacl) FROM pg_class c WHERE c.oid='{archive.JOURNAL_TABLE}'::regclass));
        """)


def seed_foreign(f, args):
    from verify_file_content_history import read_rows

    operation = read_rows(args.bundle / history.filename("operations"))[0]["operation_id"]
    foreign = json.loads(f.sql("foreign-tenant", f"""
        SELECT to_json((SELECT "Id" FROM public."Sys_Tenants" WHERE "Id"<>{q(args.tenant)}
            AND "Id"<>'00000000-0000-0000-0000-000000000000' ORDER BY "Id" LIMIT 1));
        """))
    require(foreign and foreign != args.tenant, "missing independent fixture B")
    actor = str(uuid4())
    f.sql("synthetic-foreign-same-key", f"""
        BEGIN; DO $$ BEGIN PERFORM cp6_authentication_v1.acquire(); END $$;
        INSERT INTO {archive.JOURNAL_TABLE} ({', '.join(archive.JOURNAL_COLUMNS[:-1])})
        VALUES ({q(foreign)},{q(history.assets.OWNER)},{q(operation)},{q(actor)},{q(history.CONTRACT)},'CLOSED',NULL,NULL,
          convert_to(json_build_object('OperationId',{q(operation)}::uuid,'State','CLOSED','Result',NULL)::text,'UTF8'),
          ARRAY[]::uuid[],'{{"fixture":"foreign-same-key"}}');
        COMMIT;
        """)
    return foreign, operation


def native(f, args, target, v2_raw, v2_heads):
    import file_content_candidate as candidate

    out = f.output
    tenant = args.tenant
    foreign, operation = seed_foreign(f, args)
    before = protected(f, "protected-before", tenant)
    before_state = json.loads(before)
    require(before_state["admission"] == 0 and len(before_state["otherJournal"]) == 3, "closed original target differs")
    recover = replace(args, action="recover")
    invoke(f, "recover-missing", replace(recover, output=out / "recover-missing"), "rma-stage-candidate-receipt-not-found")
    invoke(f, "wrong-marker", replace(args, output=out / "wrong-marker", candidate_id=str(uuid4())),
           "rma-stage-candidate-marker-rejected")
    wrong = out / "wrong-physical.json"
    wrong.write_text(json.dumps({**target.mapping(), "DatabaseOid": target.database_oid + 1}), encoding="utf-8")
    invoke(f, "wrong-physical", replace(args, output=out / "wrong-physical", target_file=wrong),
           "rma-stage-candidate-target-rejected")

    wrong_assets = out / "wrong-assets"
    shutil.copytree(args.file_content_root, wrong_assets)
    file = next(path for path in wrong_assets.rglob("*") if path.is_file())
    original = file.read_bytes()
    file.write_bytes(bytes([original[0] ^ 1]) + original[1:])
    invoke(f, "wrong-assets", replace(args, output=out / "wrong-assets-output", file_content_root=wrong_assets),
           "file-content-asset-digest-mismatch")
    require(not (out / "wrong-assets-output/driver.sql").exists(), "bad assets reached database driver")

    ready = "DO $file_candidate_ready$"
    database = '"' + target.database.replace('"', '""') + '"'
    fault(f, replace(args, output=out / "unexpected-database-grant"), lambda sql, _: insert_before(sql, ready,
        f"GRANT CREATE ON DATABASE {database} TO pg_monitor;\n"), "rma-stage-file-candidate-acl-rejected")
    fault(f, replace(args, output=out / "unexpected-column-grant"), lambda sql, _: insert_before(sql, ready,
        f"GRANT SELECT (creator_id) ON {candidate.CONTENTS} TO pg_monitor;\n"), "rma-stage-file-candidate-acl-rejected")

    marker = "DO $file_candidate_adopt$"
    fault(f, replace(args, output=out / "wrong-parent"), lambda sql, _: insert_before(sql, marker,
        "UPDATE pg_temp.file_import_parent_versions SET recorded_at=recorded_at+interval '1 second';\n"),
        "rma-stage-file-candidate-parent-rejected")

    def v2_fault(sql, output):
        commands = []
        for key in ("events", "operations"):
            name = history.filename(key)
            shutil.copyfile(v2_raw / name, output / name)
            table = "pg_temp." + candidate.temporary(key)
            columns = ", ".join(history.MEMBERS[key].columns)
            commands += [f"TRUNCATE {table};", f"\\copy {table} ({columns}) FROM '{name}' WITH (FORMAT csv, HEADER MATCH)"]
        updated = [row for row in v2_heads if row["revision"] == 2]
        require(len(updated) == 2 and all(row["tenant_id"] == tenant for row in updated), "unexpected v2 fixture heads")
        keys = ", ".join(q(row["file_id"]) for row in updated)
        commands.append(f"UPDATE pg_temp.file_import_heads SET revision=2 WHERE file_id IN ({keys});")
        commands.append(f"""SELECT set_config('{candidate.PLAN}',(current_setting('{candidate.PLAN}')::jsonb
            || jsonb_build_object('counts',(current_setting('{candidate.PLAN}')::jsonb->'counts')
            || '{{"events":5,"operations":5}}'::jsonb))::text,true) AS fixture_counts \\gset""")
        sql = insert_before(sql, marker, "\n".join(commands) + "\n")
        return insert_before(sql, f"CREATE SCHEMA {candidate.RECEIPTS};", f"""
            DO $fixture$
            BEGIN
              IF (SELECT count(*) FROM {candidate.EVENTS} WHERE tenant_id={q(tenant)})<>5
                 OR (SELECT count(*) FROM {candidate.HEADS} WHERE tenant_id={q(tenant)} AND revision=2)<>2
                 OR (SELECT count(*) FROM {candidate.JOURNAL} WHERE tenant_id={q(tenant)} AND owner={q(candidate.OWNER)})<>5 THEN
                RAISE EXCEPTION 'fixture did not reach complete native v2 history';
              END IF;
              RAISE NOTICE 'fixture saw original five events and receipts after native constraints';
              UPDATE {candidate.EVENTS} SET recorded_at=recorded_at WHERE tenant_id={q(tenant)};
            END
            $fixture$;
            """)

    late = fault(f, replace(args, output=out / "late-v2-rollback"), v2_fault, "rma-stage-native-failed (see native logs)")
    require(b"fixture saw original five events and receipts" in late and b"23514" in late,
            "native v2 validation or original immutable error did not execute")
    require(protected(f, "protected-after-rollback", tenant) == before, "late failure changed protected target")
    for table in (candidate.CONTENTS, candidate.HEADS, candidate.EVENTS):
        require(f.sql("zero-" + table.split(".")[-1], f"SELECT count(*) FROM {table} WHERE tenant_id={q(tenant)};").strip() == b"0",
                "late failure left tenant rows")
    require(f.sql("zero-owner-journal", f"SELECT count(*) FROM {candidate.JOURNAL} WHERE tenant_id={q(tenant)}"
                  f" AND owner={q(candidate.OWNER)};").strip() == b"0", "late failure left original journal")
    invoke(f, "recover-after-rollback", replace(recover, output=out / "recover-after-rollback"),
           "rma-stage-candidate-receipt-not-found")

    imported = invoke(f, "import", replace(args, output=out / "import"))
    replay = invoke(f, "same-import", replace(args, output=out / "same-import"))
    require(imported == replay, "normal same-key replay changed original receipt")
    root = args.file_content_root
    displaced = root.with_name(root.name + "-unavailable")
    require(root.resolve(strict=True) == root and root.parent == out.parent and displaced.parent == root.parent
            and not displaced.exists(), "fixture refuses to move a foreign root")
    root.rename(displaced)
    try:
        recovered = invoke(f, "recover", replace(recover, output=out / "recover"))
        require(imported == recovered, "durable same-key recovery changed original receipt")
    finally:
        displaced.rename(root)
    invoke(f, "recover-wrong-root", replace(recover, output=out / "recover-wrong-root", file_content_root=displaced),
           "rma-stage-candidate-receipt-key-rejected")

    expected_counts = {"contents": 3, "heads": 3, "events": 3, "operations": 3, "parent-versions": 1}
    receipt = json.loads(imported)
    require(receipt["outcome"] == "IMPORTED" and receipt["recoveryReady"] is False
            and receipt["counts"] == expected_counts and receipt["binding"]["fileContentRoot"] == root.as_posix(),
            "durable candidate receipt binding differs")
    for key, member in history.MEMBERS.items():
        predicate = f"tenant_id={q(tenant)}" + (f" AND owner={q(candidate.OWNER)}" if key == "operations" else "")
        selected = history.rows_sql(tenant)[key] if key == "parent-versions" else \
            f"SELECT {', '.join(member.columns)} FROM {member.relation} WHERE {predicate}"
        raw = f.sql("actual-" + key, f"BEGIN; {archive.JOURNAL_COPY_SETTINGS} "
            f"COPY ({selected} ORDER BY {', '.join(member.key)}) "
            "TO STDOUT WITH (FORMAT csv,HEADER); COMMIT;")
        require(raw == (args.bundle / history.filename(key)).read_bytes(), f"native target original {key} differs")
    require(protected(f, "protected-after", tenant) == before, "parent, other Owner, B, or native gates changed")
    return dict(Receipt=receipt, ReceiptBytes=len(imported), ForeignTenant=foreign, ForeignSameKey=operation,
                JournalSha256=archive.file_digest(out / "actual-operations.stdout")["sha256"],
                ReceiptSha256=archive.file_digest(out / "import.stdout")["sha256"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", required=True)
    parser.parse_args()
    offline()
