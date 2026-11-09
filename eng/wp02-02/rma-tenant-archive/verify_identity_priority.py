#!/usr/bin/env python3
"""Exercise the Identity Priority slice against one real commissioned Core PostgreSQL."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
from verify_source_archive import DATABASE, seed
from verify import BASE_ENV, FLAGS, OWNER, Fixture, ident, q, require, token
from verify_commission import expected_histories

import identity_priority

READER = "w380_backup"
AGGREGATE = "identity-fixture-aggregate"
CONSUMER = "identity-fixture-consumer"


def outbox(tenant, message, payload, lease):
    owner = q("worker-a ") if lease else "NULL"
    lease_token = q(lease) if lease else "NULL"
    expiry = q("2026-11-10T11:00:00+00:00") if lease else "NULL"
    published = "NULL" if lease else q("2026-11-09T10:00:00+00:00")
    return """
        INSERT INTO crm_identity_priority."Cp6_OutboxMessage"
        ("Id","MessageId","TenantId","TopicName","PartitionKey","Payload","PayloadSha256","CorrelationId","CausationId",
         "AggregateId","AggregateVersion","Status","AttemptCount","CreatedAtUtc","AvailableAtUtc","LeaseOwner","LeaseToken",
         "LeaseExpiresAtUtc","PublishedAtUtc","SupportReference","RowVersion")
        VALUES ({id},{message},{tenant},'identity-priority-fixture','priority-partition',decode({payload},'hex'),
            {digest},'correlation-fixture','causation-fixture',{aggregate},4,{status},7,'2026-11-09T09:00:00+00:00',
            '2026-11-09T09:15:00+00:00',{owner},{lease_token},{expiry},{published},'fixture-support',
            decode('0000000000000000','hex'));
    """.format(id=q(token()), message=q(message), tenant=q(tenant), payload=q(payload.hex()),
               digest=q(hashlib.sha256(payload).hexdigest()), aggregate=q(AGGREGATE), status=1 if lease else 2,
               owner=owner, lease_token=lease_token, expiry=expiry, published=published)


def inbox(tenant, message):
    return """
        INSERT INTO crm_identity_priority."Cp6_InboxMessage"
        ("Id","ConsumerName","MessageId","TenantId","TopicName","PartitionKey","PayloadSha256",
         "AggregateId","AggregateVersion","Status","AttemptCount","OutcomeCode","SupportReference",
         "ReceivedAtUtc","ProcessedAtUtc","RowVersion")
        VALUES ({id},{consumer},{message},{tenant},'identity-priority-fixture','priority-partition',{digest},
                {aggregate},4,2,3,'APPLIED','fixture-inbox','2026-11-09T09:16:00+00:00',
                '2026-11-09T09:17:00+00:00',decode('0000000000000000','hex'));
    """.format(id=q(token()), consumer=q(CONSUMER), message=q(message), tenant=q(tenant),
               digest=q(hashlib.sha256(message.encode("utf-8")).hexdigest()), aggregate=q(AGGREGATE))


def checkpoint(tenant):
    return """
        INSERT INTO crm_identity_priority."Cp6_InboxAggregateCheckpoint"
        ("Id","ConsumerName","TenantId","AggregateId","AggregateVersion","UpdatedAtUtc","RowVersion")
        VALUES ({id},{consumer},{tenant},{aggregate},4,'2026-11-09T09:17:00+00:00',decode('0000000000000000','hex'));
    """.format(id=q(token()), consumer=q(CONSUMER), tenant=q(tenant), aggregate=q(AGGREGATE))


def dead_letter(tenant, message, payload):
    return """
        INSERT INTO crm_identity_priority."Cp6_DeadLetterRecord"
        ("Id","Direction","TenantId","MessageId","ConsumerName","PayloadSha256","ErrorCode",
         "SupportReference","CreatedAtUtc","RowVersion")
        VALUES ({id},1,{tenant},{message},{consumer},{digest},'FIXTURE-ERROR','fixture-dead',
                '2026-11-09T09:18:00+00:00',decode('0000000000000000','hex'));
    """.format(id=q(token()), tenant=q(tenant), message=q(message), consumer=q(CONSUMER),
               digest=q(hashlib.sha256(payload).hexdigest()))


def seed_priority(f, a, b):
    a_message, b_message, inbox_a, inbox_b, published = (token() for _ in range(5))
    a_payload = b"\x00\xff\r\nidentity-A-before"
    b_payload = b"\x00\x81\r\nidentity-B-before"
    a_lease, b_lease = token().replace("-", ""), token().replace("-", "")
    f.sql(DATABASE, "BEGIN;\n" + outbox(a, a_message, a_payload, a_lease) +
          outbox(a, published, b"identity-A-published", None) + outbox(b, b_message, b_payload, b_lease) +
          inbox(a, inbox_a) + inbox(b, inbox_b) + checkpoint(a) + checkpoint(b) +
          dead_letter(a, a_message, a_payload) + "COMMIT;")
    f.sql(DATABASE, outbox(b, a_message, b_payload, b_lease), error="23505", label="outbox-global-key")
    f.sql(DATABASE, inbox(b, inbox_a), error="23505", label="inbox-global-key")
    require(f.sql(DATABASE, """
        SELECT count(*) FROM crm_identity_priority."Cp6_InboxAggregateCheckpoint"
        WHERE "ConsumerName"={} AND "AggregateId"={};
    """.format(q(CONSUMER), q(AGGREGATE))) == "2", "checkpoint original scoped key was not exercised")
    return {"A": a_message, "B": b_message}


def observed_bytes(f, tenant):
    outputs = {}
    for table in identity_priority.TABLES:
        with f.psql(DATABASE, app="W380-independent-priority-copy") as proc:
            sql = """
                SET bytea_output='hex';
                SET TIME ZONE 'UTC';
                SET DateStyle='ISO, YMD';
                COPY (SELECT * FROM crm_identity_priority.{name} WHERE "TenantId"={tenant} ORDER BY "Id")
                TO STDOUT WITH (FORMAT csv, HEADER true);
            """.format(name=ident(table.name), tenant=q(tenant))
            out, err = f.collect(proc, sql.encode("utf-8"))
            require(proc.returncode == 0 and not err, "independent queue read failed")
            outputs[table.member] = out
    return outputs


def cli(f, archive, label, username, *arguments):
    env = BASE_ENV if username is None else {**BASE_ENV, "PGPASSWORD": f.passwords[username]}
    result = subprocess.run([sys.executable, "-B", str(Path(archive.__file__).resolve()), *map(str, arguments)],
                            cwd=f.root, env=env, capture_output=True, timeout=45, creationflags=FLAGS)
    f.save(label + ".stdout", result.stdout)
    f.save(label + ".stderr", result.stderr)
    f.save(label + ".process.json", {"ExitCode": result.returncode})
    return result


def prepare(f):
    commission = seed(f)
    a, b = (row["TenantId"] for row in commission["Tenants"])
    facts = seed_priority(f, a, b)
    f.save("priority-fixture.json", facts)
    require(f.sql(DATABASE, """
        SELECT NOT relrowsecurity AND NOT relforcerowsecurity FROM pg_class
        WHERE oid='crm_identity_priority."Cp6_OutboxMessage"'::regclass;
    """) == "t", "expected actual W351 priority baseline before the fixture policy")
    # The optional production overlay has its own first-install gates. Only this test
    # enables native RLS on the actual Outbox to exercise the archive denial boundary.
    f.sql(DATABASE, """
        ALTER TABLE crm_identity_priority."Cp6_OutboxMessage" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE crm_identity_priority."Cp6_OutboxMessage" FORCE ROW LEVEL SECURITY;
    """)
    f.passwords[READER] = secrets.token_hex(24)
    f.sql("postgres", "SET log_statement='none'; CREATE ROLE {} LOGIN IN ROLE pg_read_all_data PASSWORD {};".format(
        ident(READER), q(f.passwords[READER])))
    f.sql(DATABASE, """
        GRANT CONNECT ON DATABASE {} TO {};
        GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO {};
    """.format(ident(DATABASE), ident(READER), ident(READER)))
    masked = f.sql(DATABASE, """
        SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={};
    """.format(q(a)), role=READER, label="priority-rls-masked-count")
    require(masked == "0", "restricted role unexpectedly saw actual priority rows")
    return commission, a, b, facts


def exercise(f, prepared=None):
    import archive

    commission, a, b, facts = prepare(f) if prepared is None else prepared
    target = archive.PhysicalTarget.from_mapping(commission["Target"])
    installation = commission["InstallationId"]
    before = observed_bytes(f, a)
    environment = {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}

    def after_snapshot(snapshot):
        holder = next(row for row in f.activity(DATABASE) if row["application_name"] == "cp6-rma-archive-holder")
        require(holder["state"] == "idle in transaction", "identity snapshot holder is not live")
        a_sql = """
            BEGIN;
            UPDATE crm_identity_priority."Cp6_OutboxMessage" SET "Status"=2,"LeaseOwner"=NULL,"LeaseToken"=NULL,
                "LeaseExpiresAtUtc"=NULL,"PublishedAtUtc"='2026-11-09T11:00:00+00:00' WHERE "MessageId"={};
            UPDATE crm_identity_priority."Cp6_InboxMessage" SET "OutcomeCode"='LATER' WHERE "TenantId"={};
            UPDATE crm_identity_priority."Cp6_InboxAggregateCheckpoint" SET "AggregateVersion"=5 WHERE "TenantId"={};
            UPDATE crm_identity_priority."Cp6_DeadLetterRecord" SET "ReplayReasonCode"='REVIEWED',
                "ReplayedAtUtc"='2026-11-09T11:01:00+00:00' WHERE "TenantId"={};
            SELECT pg_backend_pid(); COMMIT;
        """.format(q(facts["A"]), q(a), q(a), q(a))
        b_sql = """
            BEGIN;
            UPDATE crm_identity_priority."Cp6_OutboxMessage" SET "AttemptCount"=8 WHERE "MessageId"={};
            SELECT pg_backend_pid(); COMMIT;
        """.format(q(facts["B"]))
        with ThreadPoolExecutor(max_workers=2) as workers:
            work = [workers.submit(f.sql, DATABASE, sql, label="post-cut-" + label)
                    for label, sql in (("a", a_sql), ("b", b_sql))]
            pids = [int(job.result()) for job in work]
        require(len(set(pids)) == 2, "A/B did not use independent native writers")
        f.save("priority-snapshot.json", {"Snapshot": snapshot, "Holder": holder["pid"], "WriterPids": pids})

    bundle = archive.export_bundle(f.args.pg_bin, target, OWNER, installation, a, f.root / "v5-cut",
                                   include_original_journal=True, include_rma_source=True, include_rma_visibility=True,
                                   include_identity_priority=True, environ=environment, on_snapshot=after_snapshot)
    manifest = archive.verify_bundle(bundle, a)
    require(manifest["format"] == identity_priority.FORMAT and manifest["recoveryReady"] is False
            and {name: row["count"] for name, row in manifest["identityPriority"]["tables"].items()} ==
            {"Cp6_DeadLetterRecord": 1, "Cp6_InboxAggregateCheckpoint": 1, "Cp6_InboxMessage": 1, "Cp6_OutboxMessage": 2},
            "v5 cut omitted priority rows or changed scope")
    after = observed_bytes(f, a)
    require(all((bundle / member).read_bytes() == before[member] and after[member] != before[member] for member in before),
            "v5 archive did not preserve the exact earlier source bytes in all four tables")
    f.save("priority-byte-comparison.json", {
        key: {"before": hashlib.sha256(before[key]).hexdigest(), "after": hashlib.sha256(after[key]).hexdigest()}
        for key in before})

    f.save("v5-target.json", target.mapping())
    command = ("export", "--pg-bin", f.args.pg_bin, "--target-file", f.root / "v5-target.json",
               "--installation", installation, "--include-identity-priority")
    flags = ("--include-original-journal", "--include-rma-source", "--include-rma-visibility")
    denied = cli(f, archive, "v5-cli-rls", READER, *command, *flags, "--username", READER, "--tenant", a,
                 "--output", f.root / "v5-rls")
    native_error = (f.root / "v5-rls/native/holder.stderr").read_bytes()
    require(denied.returncode == 1 and b"identity-priority-read-failed" in denied.stderr
            and b"42501" in native_error and b"Cp6_OutboxMessage" in native_error
            and not (f.root / "v5-rls/bundle/manifest.json").exists(), "v5 did not fail at native Priority RLS")
    missing_flags = cli(f, archive, "v5-without-v4", OWNER, *command, "--username", OWNER, "--tenant", a,
                        "--output", f.root / "v5-no-v4")
    require(missing_flags.returncode == 1 and b"identity-priority-visibility-required" in missing_flags.stderr,
            "v5 accepted missing v4 opt-in")

    exported = cli(f, archive, "v5-cli-export-b", OWNER, *command, *flags, "--username", OWNER, "--tenant", b,
                   "--output", f.root / "v5-cli-b")
    require(exported.returncode == 0, "normal v5 export CLI failed")
    b_bundle = f.root / "v5-cli-b/bundle"
    verified = cli(f, archive, "v5-cli-verify-b", None, "verify", "--bundle", b_bundle, "--tenant", b)
    require(verified.returncode == 0 and json.loads(verified.stdout)["format"] == identity_priority.FORMAT,
            "normal v5 verify CLI failed")
    b_manifest = archive.verify_bundle(b_bundle, b)
    b_actual = observed_bytes(f, b)
    require(all((b_bundle / key).read_bytes() == value for key, value in b_actual.items()),
            "B v5 package changed or crossed native tenant data")
    empty = b_manifest["identityPriority"]["tables"]["Cp6_DeadLetterRecord"]
    require(empty["count"] == 0 and len((b_bundle / empty["member"]).read_bytes().splitlines()) == 1,
            "empty priority membership was not explicit")
    broken = f.root / "v5-missing-empty"
    shutil.copytree(b_bundle, broken)
    (broken / empty["member"]).unlink()
    invalid = cli(f, archive, "v5-missing-empty", None, "verify", "--bundle", broken, "--tenant", b)
    require(invalid.returncode == 1 and b"bundle-member-mismatch" in invalid.stderr,
            "v5 accepted a missing empty member")
    for version in ("v1", "v2", "v3", "v4"):
        path = getattr(f.args, version + "_bundle")
        old = json.loads((path / "manifest.json").read_text(encoding="utf-8"))
        checked = cli(f, archive, version + "-reused-verify", None, "verify", "--bundle", path, "--tenant", old["tenantId"])
        require(checked.returncode == 0 and json.loads(checked.stdout)["format"] == old["format"],
                "prior archive was rejected")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    paths = ("root", "pg-bin", "dotnet", "cli", "journal-cli", "webapi", "v1-bundle", "v2-bundle", "v3-bundle", "v4-bundle")
    for name in paths:
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in paths:
        name = name.replace("-", "_")
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "identity-priority-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    artifacts = {name: hashlib.sha256(getattr(args, name).read_bytes()).hexdigest() for name in ("webapi", "cli", "journal_cli")}
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source, "Sha256": artifacts})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("real Identity Priority v5 tenant snapshot, original bytes, RLS and v1-v4 compatibility passed", flush=True)


if __name__ == "__main__":
    main()
