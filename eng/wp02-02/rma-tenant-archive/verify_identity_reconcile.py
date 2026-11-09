#!/usr/bin/env python3
"""Exercise read-only Identity Priority reconciliation on one real Core PostgreSQL."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import sys

sys.dont_write_bytecode = True
from verify_identity_priority import DATABASE, cli, inbox, observed_bytes, outbox, seed
from verify import Fixture, OWNER, ident, q, require, token
from verify_commission import expected_histories

import archive
import reconcile_identity_priority as reconcile


OUTBOX = 'crm_identity_priority."Cp6_OutboxMessage"'
INBOX = 'crm_identity_priority."Cp6_InboxMessage"'
FOREIGN_MARKER = "W384-foreign-row-must-stay-private"
CURRENT_LEASE = "b" * 32
LEASE_OWNER = "W384-current-lease-owner-private"


def exercise(f):
    commission = seed(f)
    tenant, other = (row["TenantId"] for row in commission["Tenants"])
    target, installation = commission["Target"], commission["InstallationId"]
    keys = {name: "w384-" + name for name in (
        "publish", "lease", "foreign-out", "missing-out", "payload", "binding", "processed",
        "foreign-in", "missing-in", "digest",
    )}
    keys["publish"] = "w384-Publish-'quoted\\雪"
    old_lease = token().replace("-", "")
    native_payload = b"\x00\xff\r\nW384-native-envelope"
    initial_outbox = ("publish", "lease", "foreign-out", "missing-out", "payload", "binding")
    initial_inbox = ("processed", "foreign-in", "missing-in", "digest")
    f.sql(DATABASE, "BEGIN;\n" + "".join(outbox(tenant, keys[name], native_payload, old_lease if name == "lease" else None)
                                         for name in initial_outbox) +
          "".join(inbox(tenant, keys[name]) for name in initial_inbox) + """
        UPDATE {outbox} SET "Status"=0,"PublishedAtUtc"=NULL WHERE "MessageId"<>{lease};
        UPDATE {inbox} SET "Status"=0,"ProcessedAtUtc"=NULL,"OutcomeCode"=NULL;
        INSERT INTO {outbox}
            ("Id","MessageId","TenantId","TopicName","PartitionKey","Payload","PayloadSha256","CorrelationId","CausationId",
             "AggregateId","AggregateVersion","Status","AttemptCount","CreatedAtUtc","AvailableAtUtc","RowVersion")
        SELECT gen_random_uuid(),'w384-scale-'||s,"TenantId","TopicName","PartitionKey","Payload","PayloadSha256",
               "CorrelationId","CausationId","AggregateId","AggregateVersion",0,0,"CreatedAtUtc","AvailableAtUtc","RowVersion"
        FROM {outbox} CROSS JOIN generate_series(1,129) s WHERE "MessageId"={publish};
        COMMIT;
    """.format(outbox=OUTBOX, inbox=INBOX, lease=q(keys["lease"]), publish=q(keys["publish"])))

    f.save("target.json", target)
    f.save("original-keys.json", keys)
    export = f.root / "archive-v5"
    exported = cli(f, archive, "archive-v5", OWNER, "export", "--pg-bin", f.args.pg_bin,
                   "--target-file", f.root / "target.json", "--output", export, "--username", OWNER,
                   "--installation", installation, "--tenant", tenant, "--include-original-journal",
                   "--include-rma-source", "--include-rma-visibility", "--include-identity-priority")
    require(exported.returncode == 0 and not exported.stderr, "real v5 export did not complete")
    bundle = export / "bundle"
    pin = archive.file_digest(bundle / "manifest.json")["sha256"]

    def run(label, username=OWNER, source_bundle=bundle, digest=pin, target_file=None):
        return cli(f, reconcile, label, username, "--pg-bin", f.args.pg_bin,
                   "--target-file", target_file or f.root / "target.json", "--output", f.root / (label + "-output"),
                   "--username", username, "--tenant", tenant, "--bundle", source_bundle, "--manifest-sha256", digest)

    unchanged = run("all-current")
    require(unchanged.returncode == 0 and not unchanged.stderr and json.loads(unchanged.stdout)["result"] == "OBSERVED",
            "unchanged current keys did not produce a complete observation")

    f.sql(DATABASE, """
        BEGIN;
        UPDATE {outbox} SET "Status"=2,"PublishedAtUtc"='2026-11-09T11:00:00Z' WHERE "MessageId"={publish};
        UPDATE {outbox} SET "LeaseOwner"={lease_owner},"LeaseToken"={lease},"LeaseExpiresAtUtc"=now()+interval '30 min'
            WHERE "MessageId"={lease_key};
        UPDATE {outbox} SET "Payload"=decode('00ff01','hex') WHERE "MessageId"={payload};
        UPDATE {outbox} SET "PartitionKey"='new-binding' WHERE "MessageId"={binding};
        DELETE FROM {outbox} WHERE "MessageId" IN ({foreign_out},{missing_out});
        UPDATE {inbox} SET "Status"=1,"ProcessedAtUtc"='2026-11-09T11:01:00Z',"OutcomeCode"='APPLIED'
            WHERE "MessageId"={processed};
        UPDATE {inbox} SET "PayloadSha256"=repeat('a',64) WHERE "MessageId"={digest};
        DELETE FROM {inbox} WHERE "MessageId" IN ({foreign_in},{missing_in});
    """.format(outbox=OUTBOX, inbox=INBOX, publish=q(keys["publish"]), lease=q(CURRENT_LEASE), lease_owner=q(LEASE_OWNER),
               lease_key=q(keys["lease"]), payload=q(keys["payload"]), binding=q(keys["binding"]),
               foreign_out=q(keys["foreign-out"]), missing_out=q(keys["missing-out"]), processed=q(keys["processed"]),
               digest=q(keys["digest"]), foreign_in=q(keys["foreign-in"]), missing_in=q(keys["missing-in"])) +
          outbox(other, keys["foreign-out"].upper() + " ", FOREIGN_MARKER.encode(), token().replace("-", "")) +
          inbox(other, keys["foreign-in"]) + "COMMIT;")
    require(f.sql(DATABASE, "SELECT count(*) FROM {} WHERE \"MessageId\"={};".format(OUTBOX, q(keys["foreign-out"])))
            == "1", "the accepted native global collation/space equality was not exercised")
    before = {tenant_id: observed_bytes(f, tenant_id) for tenant_id in (tenant, other)}
    original_lease = f.sql(DATABASE, 'SELECT "LeaseToken" FROM {} WHERE "MessageId"={};'.format(OUTBOX, q(keys["lease"])))
    require(original_lease == CURRENT_LEASE, "current lease did not rotate")
    f.sql("postgres", "ALTER DATABASE {} SET standard_conforming_strings=off;".format(ident(DATABASE)))
    require(f.sql(DATABASE, "SHOW standard_conforming_strings;") == "off", "fixture did not override string handling")

    observed = run("reconcile")
    require(observed.returncode == 2 and not observed.stderr, "expected completed fail-closed reconciliation")
    report = Path(json.loads(observed.stdout)["report"])
    raw = report.read_bytes()
    records = [json.loads(line) for line in raw.splitlines()]
    header, *rows, summary = records
    require(header["manifestSha256"] == pin and header["tenantId"] == tenant and header["current"]["readOnly"] is True
            and header["current"]["isolation"] == "repeatable read", "missing authoritative snapshot metadata")
    require(summary["result"] == "BLOCKED" and summary["recoveryReady"] is False
            and summary["counts"] == {"PRESERVE_CURRENT": 132, "BLOCKED": 5, "UNKNOWN": 2}, "unexpected decisions")
    by_key = {row["key"]["MessageId"]: row for row in rows}
    for label in ("foreign-out", "foreign-in"):
        row = by_key[keys[label]]
        require(row["decision"] == "BLOCKED" and row["reason"] == "GLOBAL_KEY_CONFLICT" and "current" not in row,
                "foreign key leaked data or did not block")
    require(all(value.encode() not in raw for value in (other, FOREIGN_MARKER, CURRENT_LEASE, LEASE_OWNER, old_lease,
                                                       keys["foreign-out"].upper())), "sensitive current data escaped")
    for label in ("missing-out", "missing-in"):
        require(by_key[keys[label]]["decision"] == "UNKNOWN", "missing evidence acquired a usable result")
    for label in ("payload", "binding", "digest"):
        row = by_key[keys[label]]
        require(row["decision"] == "BLOCKED" and row["reason"] == "BINDING_CONFLICT" and "current" not in row,
                "changed immutable binding was not blocked")
    published = by_key[keys["publish"]]
    processed = by_key[keys["processed"]]
    lease = by_key[keys["lease"]]
    require(published["archivedState"] == "Pending" and published["current"]["state"] == "Published"
            and published["current"]["publishedAtUtc"], "actual published outcome was not preserved")
    require(processed["archivedState"] == "Processing" and processed["current"]["state"] == "Processed"
            and processed["current"]["processedAtUtc"], "actual processed outcome was not preserved")
    require(lease["current"]["state"] == "Dispatching" and lease["current"]["leaseActive"] is True
            and lease["current"]["leaseChanged"] is True, "current lease was not preserved")
    after = {tenant_id: observed_bytes(f, tenant_id) for tenant_id in (tenant, other)}
    require(before == after, "native A or B rows changed during reconciliation")
    f.save("native-state-hashes.json", {side: {
        label: {member: hashlib.sha256(data).hexdigest() for member, data in snapshot[tenant_id].items()}
        for label, tenant_id in (("A", tenant), ("B", other))
    } for side, snapshot in (("before", before), ("after", after))})

    bad_pin = run("wrong-digest", digest="0" * 64)
    require(bad_pin.returncode == 1 and b"manifest-digest-mismatch" in bad_pin.stderr, "wrong pin was accepted")
    wrong_target = {**target, "DatabaseOid": target["DatabaseOid"] + 1}
    f.save("wrong-target.json", wrong_target)
    bad_target = run("wrong-target", target_file=f.root / "wrong-target.json")
    require(bad_target.returncode == 1 and b"source-target-mismatch" in bad_target.stderr, "foreign target was accepted")

    forged = f.root / "wrong-physical-bundle"
    shutil.copytree(bundle, forged)
    manifest = json.loads((forged / "manifest.json").read_bytes())
    manifest["source"] = wrong_target
    (forged / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    native_target = run("wrong-physical", source_bundle=forged,
                        digest=archive.file_digest(forged / "manifest.json")["sha256"], target_file=f.root / "wrong-target.json")
    require(native_target.returncode == 1 and b"target-identity-rejected" in native_target.stderr
            and not (f.root / "wrong-physical-output" / "reconciliation.jsonl").exists(), "live wrong target was accepted")

    reader = "w384_read_all"
    f.passwords[reader] = secrets.token_hex(24)
    f.sql("postgres", "SET log_statement='none'; CREATE ROLE {} LOGIN IN ROLE pg_read_all_data PASSWORD {};".format(
        ident(reader), q(f.passwords[reader])))
    f.sql(DATABASE, "GRANT CONNECT ON DATABASE {} TO {};\n"
          "GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO {};\n"
          "ALTER TABLE {} ENABLE ROW LEVEL SECURITY; ALTER TABLE {} FORCE ROW LEVEL SECURITY;".format(
              ident(DATABASE), ident(reader), ident(reader), OUTBOX, OUTBOX))
    masked = f.sql(DATABASE, "SELECT count(*) FROM {};".format(OUTBOX), role=reader, label="rls-masked-count")
    require(masked == "0", "native RLS did not hide current global evidence")
    denied = run("rls-denied", username=reader)
    denied_output = f.root / "rls-denied-output"
    require(denied.returncode == 1 and b"priority-current-read-failed" in denied.stderr
            and b"42501" in (denied_output / "holder.stderr").read_bytes()
            and {path.name for path in denied_output.iterdir()} == {"holder.stdout", "holder.stderr"},
            "RLS denial lost its native error or left a pending report or private input")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "journal-cli", "webapi"):
        parser.add_argument("--" + name, type=lambda path: Path(path).resolve(strict=True), required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "identity-reconcile-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    artifacts = {name: hashlib.sha256(getattr(args, name).read_bytes()).hexdigest() for name in ("webapi", "cli", "journal_cli")}
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source, "Sha256": artifacts})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("native read-only Identity Priority reconciliation passed", flush=True)


if __name__ == "__main__":
    main()
