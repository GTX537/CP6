#!/usr/bin/env python3
"""Keep an ordinary B RMA write live during an A v11 cut and neutral restore."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import sys
from unittest.mock import patch

sys.dont_write_bytecode = True
import archive
import erp_integration as erp
import stage
from verify_stage import A, B, BASE_ENV, OWNER, Fixture, check_acl, ident, new_target, put_json, q, require, token
from verify_stock_stage import check_copy, check_stock_originals


NEUTRAL = "v11_concurrent_neutral"
HOLDER = "cp6-rma-archive-holder"
EXECUTOR = "cp6_rma_original_journal_executor_v1"
CONTROLS = ("PG_VERSION", "pg_hba.conf", "global/pg_control")


def controls(data):
    return {name: archive.file_digest(data / name) for name in CONTROLS}


def dump_data(pg_bin, root, label, bundle):
    archive.run_native(pg_bin, "pg_restore", ["--data-only", "--file=-", bundle / "rma.pgcustom"], BASE_ENV, root, label)
    lines = (root / (label + ".stdout")).read_bytes().splitlines(keepends=True)
    return b"".join(line for line in lines if not line.startswith((b"\\restrict ", b"\\unrestrict ")))


def role_state(fixture, database, side, tenant, foreign, role):
    schema = "t_" + archive.guid(tenant).hex
    return json.loads(fixture.sql(database, f"""
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SELECT json_build_object('login',session_user,'role',current_user,
          'tenant',cp6_tenancy_v1.current_tenant_id(),
          'heads',(SELECT coalesce(jsonb_agg(to_jsonb(h) ORDER BY id),'[]'::jsonb) FROM {schema}.heads h),
          'versions',(SELECT coalesce(jsonb_agg(to_jsonb(v) ORDER BY id,version),'[]'::jsonb) FROM {schema}.versions v),
          'originals',(SELECT coalesce(jsonb_agg(jsonb_build_object('operation',operation_id,
                'actor',actor_id,'objects',object_ids,'state',state,'input',encode(input_bytes,'hex'),
                'digest',input_sha256,'result',encode(result_bytes,'hex')) ORDER BY operation_id),'[]'::jsonb)
              FROM {archive.JOURNAL_TABLE} WHERE tenant_id={q(tenant)} AND owner={q(archive.JOURNAL_OWNER)}),
          'foreignOriginals',(SELECT count(*) FROM {archive.JOURNAL_TABLE}
              WHERE tenant_id={q(foreign)} AND owner={q(archive.JOURNAL_OWNER)}));
        COMMIT;
    """, role=role, label=side))


def write_b(fixture, database, tenant, before):
    # Exercise the native draft entry under B's ordinary LOGIN using B's previously accepted source,
    # actor and content with new operation and application keys. HTTP authorization is outside this fixture.
    [original] = [row for row in before["originals"] if json.loads(bytes.fromhex(row["result"]))["State"] == "DRAFT"]
    [old_application] = original["objects"]
    [old_view] = [row["payload"] for row in before["versions"] if row["id"] == old_application and row["version"] == 1]
    old_operation = original["operation"]
    operation, application = token(), token()
    encoded_input = bytes.fromhex(original["input"])
    require(hashlib.sha256(encoded_input).hexdigest() == original["digest"], "stored B draft input digest differs")
    encoded_input = encoded_input.replace(old_operation.encode(), operation.encode()).replace(old_application.encode(), application.encode())
    digest = hashlib.sha256(encoded_input).hexdigest()
    encoded_result = (bytes.fromhex(original["result"]).replace(old_operation.encode(), operation.encode())
                      .replace(old_application.encode(), application.encode())
                      .replace(old_application.replace("-", "").encode(), application.replace("-", "").encode()))
    result = json.loads(encoded_result)
    view = dict(old_view, Id=application, RequestedAt=datetime.now(timezone.utc).isoformat(), Etag=result["Etag"])
    audit = json.dumps(dict(action="create-application", result=result))
    receipt = json.loads(fixture.sql(database, f"""
        BEGIN ISOLATION LEVEL SERIALIZABLE READ WRITE;
        SET LOCAL lock_timeout='5s';
        SELECT cp6_rma_draft_writer_v1.create_draft({q(tenant)},{q(operation)},{q(application)},
          {q(original['actor'])},{q(view['OutboundId'])},{q(json.dumps(view))}::jsonb,
          decode({q(encoded_input.hex())},'hex'),{q(digest)},decode({q(encoded_result.hex())},'hex'),{q(audit)}::jsonb);
        SELECT json_build_object('login',session_user,'role',current_user,'tenant',cp6_tenancy_v1.current_tenant_id(),
          'pid',pg_backend_pid(),'xid',pg_current_xact_id()::text,'operation',{q(operation)},'application',{q(application)});
        COMMIT;
    """, role=B, label="ordinary-b-draft-commit", seconds=20))
    require(receipt["login"] == receipt["role"] == B and receipt["tenant"] == tenant, "B writer role or tenant changed")
    return receipt, dict(operation=operation, actor=original["actor"], objects=[application], state="COMMITTED",
                         input=encoded_input.hex(), digest=digest, result=encoded_result.hex()), view


def verify_delta(before, after, expected, view):
    delta = {}
    for name in ("heads", "versions", "originals"):
        delta[name] = [row for row in after[name] if row not in before[name]]
        require(len(delta[name]) == 1 and all(row in after[name] for row in before[name]), f"B {name} did not append one row")
    require(delta["heads"] == [dict(tenant_id=after["tenant"], id=view["Id"], actor_id=expected["actor"],
                                   outbound_id=view["OutboundId"], version=1)]
            and delta["versions"] == [dict(tenant_id=after["tenant"], id=view["Id"], version=1, state="DRAFT", payload=view)]
            and delta["originals"] == [expected] and after["foreignOriginals"] == 0,
            "B committed graph, original bytes or foreign visibility differ")


def stage_archive(args, fixture, bundle, assets, digest, tenant, foreign, source_target, b_write, a_before):
    with fixture.running():
        target = new_target(fixture, NEUTRAL)
        require(target["SystemIdentifier"] != source_target.system_identifier, "neutral cluster is not physically distinct")
        # pg_restore resolves the original ACL role by name. Keep the neutral role unable to log in or inherit.
        fixture.sql("postgres", f"CREATE ROLE {ident(EXECUTOR)} NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS "
                    "NOCREATEDB NOCREATEROLE NOREPLICATION;")
        target_file = fixture.root / "target.json"
        put_json(target_file, target)
        common = dict(pg_bin=args.pg_bin, target_file=target_file, username=OWNER, tenant=tenant, manifest_sha256=digest)
        environment = {**BASE_ENV, "PGPASSWORD": fixture.passwords[OWNER]}
        with patch.dict(os.environ, environment, clear=True):
            imported = stage.execute(argparse.Namespace(**common, action="stage", output=fixture.root / "stage", bundle=bundle,
                                     file_content_root=assets, include_file_content_history=True))
            recovered = stage.execute(argparse.Namespace(**common, action="recover", output=fixture.root / "recover"))
        receipt = json.loads(imported)
        require(imported == recovered and receipt["outcome"] == "STAGED" and receipt["recoveryReady"] is False,
                "neutral stage receipt or recovery changed")
        fixture.save("receipt.json", imported)
        check_acl(fixture, NEUTRAL, tenant)
        fixture.sql(NEUTRAL, "SELECT 1;", role=B, error="permission denied for database", label="ordinary-b-connect-denied")
        foreign_schema = "t_" + archive.guid(foreign).hex
        require(fixture.sql(NEUTRAL, f"""
            SELECT to_regnamespace({q(foreign_schema)}) IS NULL AND to_regnamespace('cp6_stock_outbox_v1') IS NULL
                AND to_regnamespace('erp_integration') IS NULL AND to_regnamespace('cp6_scope_authorization_v1') IS NULL
                AND NOT EXISTS (SELECT FROM rma_archive_stage.original_journal_snapshot WHERE operation_id={q(b_write['operation'])})
                AND EXISTS (SELECT FROM pg_roles r WHERE r.rolname={q(EXECUTOR)}
                    AND NOT (r.rolcanlogin OR r.rolinherit OR r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                    AND NOT has_database_privilege(r.oid,current_database(),'CONNECT')
                    AND NOT EXISTS (SELECT FROM pg_auth_members m WHERE r.oid IN (m.member,m.roleid)));
        """, label="neutral-closed") == "t", "neutral target contains B or active runtime schemas")
        stock = archive.stock_outbox_module()
        check_stock_originals(fixture, NEUTRAL, stock, bundle, archive.STOCK_MEMBER_PREFIX, "neutral")
        for table in erp.TABLES:
            order = ",".join(f'"{column}"' for column in table.unique_keys[0])
            check_copy(fixture, NEUTRAL, f'rma_archive_stage."erp_{table.name}"', order, bundle / table.member,
                       "neutral-" + table.member)
        check_copy(fixture, NEUTRAL, "rma_archive_stage.original_journal_snapshot", "operation_id",
                   bundle / archive.JOURNAL_MEMBER, "neutral-" + archive.JOURNAL_MEMBER)
        a_schema = "t_" + archive.guid(tenant).hex
        restored = json.loads(fixture.sql(NEUTRAL, f"""
            SELECT json_build_object('heads',jsonb_agg(to_jsonb(h) ORDER BY id),'versions',
               (SELECT jsonb_agg(to_jsonb(v) ORDER BY id,version) FROM {a_schema}.versions v))
            FROM {a_schema}.heads h;
        """, label="neutral-rma-data"))
        require(restored == {name: a_before[name] for name in restored}, "neutral RMA heads or versions changed")
        return dict(PhysicalTarget=target, Receipt=receipt, OriginalCopyBackMembers=len(stock.TABLES) + len(erp.TABLES) + 1)


def run(args):
    source_data = args.source_pgdata.resolve(strict=True)
    original_bundle = args.bundle.resolve(strict=True)
    original_assets = args.file_content_root.resolve(strict=True)
    original = archive.verify_bundle(original_bundle, args.tenant, file_content_root=original_assets)
    require(original["format"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY
            and archive.file_digest(original_bundle / "manifest.json")["sha256"] == stage.digest_pin(args.manifest_sha256),
            "the pinned same-source v11 archive is required")
    target = archive.PhysicalTarget.from_mapping(original["source"])
    request = json.loads(args.commission_request.read_bytes())
    require(request["Target"] == target.mapping() and request["InstallationId"] == original["installationId"],
            "archive and original commissioning differ")
    roles = {row["Login"]: row["TenantId"] for row in request["Tenants"]}
    tenant, foreign = roles[A], roles[B]
    require(tenant == args.tenant and tenant != foreign and args.port != target.port, "wrong A/B or neutral listener")
    require(not any(path.is_symlink() or path.is_junction() for path in source_data.rglob("*")),
            "stopped source contains linked files or directories")
    require(not (source_data / "postmaster.pid").exists() and not any((source_data / "pg_tblspc").iterdir()),
            "source must be stopped without external tablespaces")
    args.root = args.root.absolute()
    require(args.root.resolve() == args.root, "output must be a new absolute path without links")
    require(all(not args.root.is_relative_to(path) for path in (source_data, original_bundle, original_assets)),
            "output overlaps a protected original")
    protected = controls(source_data)
    archive.private_directory(args.root)
    sealed = args.root / "sealed"
    sealed.mkdir()
    bundle_input = sealed / "bundle"
    shutil.copytree(original_bundle, bundle_input)
    assets = sealed / "assets"
    shutil.copytree(original_assets, assets)
    archive.verify_bundle(bundle_input, tenant, file_content_root=assets)
    source = Fixture(argparse.Namespace(root=args.root, pg_bin=args.pg_bin, port=target.port), "source")
    source.run_pg("original-status", "pg_ctl", ["-D", source_data, "status"], expected=3)
    shutil.copytree(source_data, source.data)
    require(controls(source.data) == protected and controls(source_data) == protected, "stopped source control files changed")
    hba = source.root / "loopback-hba.conf"
    hba.write_text(f"host postgres,{target.database} {OWNER} 127.0.0.1/32 trust\n"
                   f"host {target.database} all 127.0.0.1/32 scram-sha-256\n", encoding="ascii")
    for port in (target.port, args.port):
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", port))
    opts = (f"-h 127.0.0.1 -p {target.port} -c hba_file={hba.as_posix()} -c shared_buffers=32MB "
            "-c max_connections=16 -c work_mem=4MB -c autovacuum=off -c default_transaction_read_only=on")
    pidfile = source.data / "postmaster.pid"
    try:
        source.run_pg("pg-start", "pg_ctl", ["-D", source.data, "-l", source.log, "-w", "-t", "25", "-o", opts, "start"])
        database = target.database
        require(Path(source.sql(database, "SHOW data_directory;")).resolve() == source.data.resolve(), "source clone path differs")
        # Refresh only the two synthetic LOGIN passwords in this clone, with credential SQL logging off.
        source.sql(database, f"""
            SET log_statement='none';
            SET log_min_error_statement='panic';
            BEGIN READ WRITE;
            ALTER ROLE {ident(A)} PASSWORD {q(source.passwords[A])};
            ALTER ROLE {ident(B)} PASSWORD {q(source.passwords[B])};
            COMMIT;
        """)
        a_before = role_state(source, database, "ordinary-a-before", tenant, foreign, A)
        b_before = role_state(source, database, "ordinary-b-before", foreign, tenant, B)
        require((a_before["login"], a_before["role"], a_before["tenant"], a_before["foreignOriginals"]) == (A, A, tenant, 0)
                and (b_before["login"], b_before["role"], b_before["tenant"], b_before["foreignOriginals"]) == (B, B, foreign, 0),
                "ordinary login mapping or journal ACL differs")
        a_schema = "t_" + archive.guid(tenant).hex
        source.sql(database, f"SELECT id FROM {a_schema}.heads;", role=B, error="42501", label="ordinary-b-a-schema-denied")
        overlap = {}
        b_after = None

        def during_snapshot(snapshot):
            nonlocal b_after

            def holder():
                [row] = [row for row in source.activity(database) if row["application_name"] == HOLDER]
                require(row["state"] == "idle in transaction", "archive snapshot holder is not live")
                return row["pid"]

            pid = holder()
            write, expected, view = write_b(source, database, foreign, b_before)
            b_after = role_state(source, database, "ordinary-b-after-commit", foreign, tenant, B)
            verify_delta(b_before, b_after, expected, view)
            require(holder() == pid and write["pid"] != pid, "B did not commit while the original A holder was live")
            overlap.update(Snapshot=snapshot, HolderPid=pid, Writer=write, Added={"heads": 1, "versions": 1, "originals": 1})

        bundle = archive.export_bundle(args.pg_bin, target, OWNER, request["InstallationId"], tenant, args.root / "concurrent-cut",
            include_original_journal=True, include_rma_source=True, include_rma_visibility=True, include_identity_priority=True,
            include_rma_iam_references=True, file_content_root=assets, include_erp_integration=True, include_privacy_evidence=True,
            include_main_stock_outbox=True, include_file_content_history=True, environ=BASE_ENV, on_snapshot=during_snapshot)
        fresh = archive.verify_bundle(bundle, tenant, file_content_root=assets)
        require(fresh["snapshot"]["snapshotId"] == overlap["Snapshot"] and fresh["counts"] == original["counts"]
                and fresh["members"].keys() == original["members"].keys(), "archive snapshot, member set or A counts changed")
        csv_members = [name for name in fresh["members"] if name.endswith(".csv")]
        require(all(fresh["members"][name] == original["members"][name] for name in csv_members), "A original CSV bytes changed")
        require(dump_data(args.pg_bin, source.root, "original-rma-data", bundle_input) ==
                dump_data(args.pg_bin, source.root, "concurrent-rma-data", bundle), "A PostgreSQL dump data changed")
        watermark, xid = fresh["snapshot"]["watermark"], overlap["Writer"]["xid"]
        require(source.sql(database, f"SELECT pg_visible_in_snapshot({q(xid)}::xid8,{q(watermark)}::pg_snapshot);",
                           label="b-xid-outside-a-cut") == "f", "B committed transaction appeared in the A cut")
        digest = archive.file_digest(bundle / "manifest.json")["sha256"]
        neutral = Fixture(args, "neutral")
        staged = stage_archive(args, neutral, bundle, assets, digest, tenant, foreign, target, overlap["Writer"], a_before)
        require(role_state(source, database, "ordinary-a-after-stage", tenant, foreign, A) == a_before
                and role_state(source, database, "ordinary-b-after-stage", foreign, tenant, B) == b_after,
                "staging changed ordinary source visibility")
    finally:
        if pidfile.exists():
            pid = pidfile.read_text(encoding="utf-8").splitlines()
            require(Path(pid[1]).resolve() == source.data.resolve() and int(pid[3]) == target.port, "source stop identity mismatch")
            source.run_pg("pg-stop", "pg_ctl", ["-D", source.data, "-m", "fast", "-w", "-t", "25", "stop"])
            source.run_pg("pg-status", "pg_ctl", ["-D", source.data, "status"], expected=3)
            require(not pidfile.exists(), "source postmaster remained")
            source.save("stopped.json", dict(PostmasterPid=int(pid[0]), Port=target.port))
    require(controls(source_data) == protected and not (source_data / "postmaster.pid").exists()
            and archive.verify_bundle(original_bundle, tenant, file_content_root=original_assets) == original,
            "pinned original source, manifest or files changed")
    report = dict(Result="PASS", Scope="concurrent v11 export and closed neutral stage", RecoveryReady=False,
        Source=target.mapping(), OriginalManifest=args.manifest_sha256, ConcurrentManifest=digest, Watermark=watermark,
        ByteIdenticalSourceCsvMembers=len(csv_members), IdenticalRmaDumpData=True, SourceControls=protected,
        ConcurrentWrite=overlap, Neutral=staged)
    put_json(args.root / "actual.json", report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "source-pgdata", "bundle", "file-content-root", "commission-request"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--tenant", required=True)
    parser.add_argument("--manifest-sha256", required=True)
    parser.add_argument("--port", type=int, required=True, help="isolated neutral PostgreSQL listener")
    print(json.dumps(run(parser.parse_args()), indent=2))


if __name__ == "__main__":
    main()
