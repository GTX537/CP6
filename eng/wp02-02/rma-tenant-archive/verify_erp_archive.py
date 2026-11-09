#!/usr/bin/env python3
"""Exercise v8 against an explicitly named disposable PostgreSQL clone."""

import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import erp_integration


SCHEMA = "erp_integration"
BACKUP = "w395_backup"
NOW = "2026-11-09T12:00:00.123456+00:00"
FUTURE = "2026-11-10T12:00:00.765432+00:00"
TOKEN = b"\x00" * 8
FLAGS = ("--include-original-journal", "--include-rma-source", "--include-rma-visibility", "--include-identity-priority",
         "--include-rma-iam-references", "--include-erp-integration")


def require(value, message):
    if not value:
        raise AssertionError(message)


def relation(table):
    return SCHEMA + '."' + table + '"'


def literal(value):
    if isinstance(value, bytes):
        return "decode('{}','hex')".format(value.hex())
    if isinstance(value, bool):
        return str(value).lower()
    if isinstance(value, int):
        return str(value)
    return "NULL" if value is None else archive.literal(value)


def insert(table, **fields):
    columns = ",".join('"{}"'.format(column) for column in fields)
    return "INSERT INTO {} ({}) VALUES ({});\n".format(relation(table), columns, ",".join(map(literal, fields.values())))


class Fixture:
    def __init__(self, args):
        self.args = args
        self.target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_bytes()))
        self.root = args.evidence
        archive.private_directory(self.root)

    def run(self, label, command, *, expected=0, username=None, statement=None):
        environment = archive.client_environment(os.environ, "W395-" + label)
        if username == BACKUP:
            environment.pop("PGPASSWORD", None)
            environment.pop("PGPASSFILE", None)
        result = subprocess.run(command, input=statement.encode("utf-8") if statement else None, capture_output=True,
                                env=environment, timeout=50, creationflags=archive.FLAGS)
        (self.root / (label + ".stdout")).write_bytes(result.stdout)
        (self.root / (label + ".stderr")).write_bytes(result.stderr)
        require(result.returncode == expected, label + " returned " + str(result.returncode))
        return result.stdout, result.stderr

    def sql(self, label, statement, *, username=None, error=None):
        username = username or self.args.username
        return self.run(label, [archive.binary(self.args.pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1", "-v",
                        "VERBOSITY=verbose", *archive.connection_args(self.target, username)], username=username,
                        expected=3 if error else 0, statement=statement + "\n")

    def cli(self, label, *arguments, expected=0, username=None):
        return self.run(label, [sys.executable, "-B", archive.__file__, *map(str, arguments)], expected=expected,
                        username=username)


def seed(tenant, aggregate, request, operation, order, command_key, outbox_key, payload, *, complete):
    digest = hashlib.sha256(payload).hexdigest()
    outbox_id = uuid4()
    sql = insert("Aggregate", TenantId=tenant, Kind=1, AggregateId=aggregate, LastRequestVersion=3,
                 LastAggregateVersion=7, LastResultVersion=11, RowVersion=TOKEN)
    sql += insert("CommandInbox", MessageId=command_key, TenantId=tenant, EventType="cp6.erp.fixture  ",
                  AggregateId=aggregate, AggregateVersion=7, Payload=payload, PayloadSha256=digest, Status=1,
                  AttemptCount=4, ConflictCount=2, LastConflictSha256="9" * 64, ReceivedAtUtc=NOW, ProcessedAtUtc=NOW,
                  RetryAtUtc=FUTURE, ReplayedAtUtc=NOW, ReplayReasonCode="fixture", RowVersion=TOKEN)
    sql += insert("Cp6_OutboxMessage", Id=outbox_id, MessageId=outbox_key, TenantId=tenant, TopicName="erp.fixture",
                  PartitionKey="original-partition ", Payload=payload, PayloadSha256=digest, CorrelationId="original",
                  CausationId=command_key, AggregateId=str(aggregate), AggregateVersion=7, Status=1, AttemptCount=5,
                  CreatedAtUtc=NOW, AvailableAtUtc=NOW, LeaseOwner="worker-a  " if complete else "worker-b",
                  LeaseToken="d" * 32 if complete else "e" * 32, LeaseExpiresAtUtc=FUTURE, RowVersion=TOKEN)
    sql += insert("OrderBridgeDispatch", TenantId=tenant, OrderKey=order, AttemptCount=3, AvailableAtUtc=NOW,
                  LastErrorCode="WMS_RETRY", LeaseOwner="bridge-owner ", LeaseExpiresAtUtc=FUTURE, RowVersion=TOKEN)
    sql += insert("Request", TenantId=tenant, Kind=1, AggregateId=aggregate, RequestVersion=3, RequestId=request,
                  AccountId=uuid4(), InputSha256=digest, Terminal=True, Succeeded=True, ResultVersion=11,
                  ResultType="erp.fixture.result", ResultDataJson='{ "z": 1, "a": "原件" }  ', UpdatedAtUtc=NOW, RowVersion=TOKEN)
    sql += """
        INSERT INTO {inbox_audit} SELECT {tenant},{operation},"MessageId","PayloadSha256","RowVersion",
            'fixture-auditor','original-replay',"AttemptCount",{now} FROM {inbox}
        WHERE "TenantId"={tenant} AND "MessageId"={command};
    """.format(inbox_audit=relation("InboxReplayAudit"), tenant=literal(tenant), operation=literal(operation),
               now=literal(NOW), inbox=relation("CommandInbox"), command=literal(command_key))
    if complete:
        sql += insert("Cp6_DeadLetterRecord", Id=uuid4(), Direction=1, TenantId=tenant, MessageId=outbox_key,
                      PayloadSha256=digest, ErrorCode="original-error", SupportReference="original-support",
                      CreatedAtUtc=NOW, ReplayedAtUtc=NOW, ReplayReasonCode="original-replay", RowVersion=TOKEN)
        sql += """
            INSERT INTO {delivery} SELECT {tenant},{operation},'result',"Id"::text,"MessageId","PayloadSha256","RowVersion",
                'fixture-auditor','original-replay',"AttemptCount",{now} FROM {outbox} WHERE "Id"={id};
        """.format(delivery=relation("DeliveryReplayAudit"), tenant=literal(tenant), operation=literal(operation),
                   now=literal(NOW), outbox=relation("Cp6_OutboxMessage"), id=literal(outbox_id))
    return "BEGIN;\n" + sql + "COMMIT;", outbox_id


def independent(f, label, tenant):
    observed = {}
    for table in erp_integration.TABLES:
        order = ",".join('"{}"'.format(column) for column in table.unique_keys[0])
        data, err = f.sql(label + "-" + Path(table.member).stem, """
            SET bytea_output='hex'; SET TIME ZONE 'UTC'; SET DateStyle='ISO, YMD';
            COPY (SELECT * FROM {table} WHERE "TenantId"={tenant} ORDER BY {order}) TO STDOUT WITH CSV HEADER;
        """.format(table=relation(table.name), tenant=literal(tenant), order=order))
        require(not err, "independent native COPY failed")
        observed[table.member] = data
    return observed


def exercise(args):
    f = Fixture(args)
    live, _ = f.sql("physical-clone", """
        SELECT json_build_object('Server',host(inet_server_addr()),'Port',inet_server_port(),'Database',current_database(),
            'DatabaseOid',d.oid::bigint,'SystemIdentifier',p.system_identifier::text,'DataDirectory',current_setting('data_directory'))
        FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
    """)
    physical = json.loads(live)
    require(Path(physical.pop("DataDirectory")).resolve(strict=True) == args.disposable_pgdata.resolve(strict=True)
            and physical == f.target.mapping(), "fixture writes require the exact disposable physical clone")

    source = [json.loads((bundle / "manifest.json").read_bytes()) for bundle in (args.a_bundle, args.b_bundle)]
    a, b = (archive.guid(row["tenantId"]) for row in source)
    installation = source[0]["installationId"]
    require(source[1]["installationId"] == installation, "fixture bundles disagree on installation")
    aggregate, request, operation = uuid4(), uuid4(), uuid4()
    order, message, outgoing = "w395-order  ", "W395-Command", "w395-delivery"
    payload = b"\x00\xff\r\nW395 exact original A"
    a_sql, a_out = seed(a, aggregate, request, operation, order, message, outgoing, payload, complete=True)
    b_sql, b_out = seed(b, aggregate, request, operation, order, message.lower(), "w395-distinct", b"B original", complete=False)
    f.sql("seed-a", a_sql)
    f.sql("seed-b", b_sql)
    for label, statement, key in (
        ("command-global-c", 'INSERT INTO {t} SELECT {key},{tenant},"EventType","AggregateId","AggregateVersion","Payload",'
         '"PayloadSha256","Status","AttemptCount","ConflictCount","LastConflictSha256","ErrorCode","ReceivedAtUtc",'
         '"ProcessedAtUtc","RetryAtUtc","ReplayedAtUtc","ReplayReasonCode","RowVersion" FROM {t} WHERE "TenantId"={a};', message + " "),
        ("outbox-global-icu", 'UPDATE {t} SET "MessageId"={key} WHERE "Id"={id};', outgoing.upper() + " "),
    ):
        table = "CommandInbox" if label == "command-global-c" else "Cp6_OutboxMessage"
        _, err = f.sql(label, statement.format(t=relation(table), key=literal(key), tenant=literal(b),
                                               a=literal(a), id=literal(b_out)), error="23505")
        require(b"23505" in err and (b"PK_CommandInbox" if table == "CommandInbox" else b"IX_Cp6_OutboxMessage_MessageId") in err,
                "native global key did not reject its expected alias")

    before = independent(f, "before-a", a)
    root = f.root / "empty-file-content-root"
    archive.private_directory(root)

    def after_cut(snapshot):
        writer, err = f.sql("after-cut-writer", """
            BEGIN;
            UPDATE {s}."Aggregate" SET "LastResultVersion"=12 WHERE "TenantId"={a};
            UPDATE {s}."CommandInbox" SET "AttemptCount"=5 WHERE "TenantId"={a};
            UPDATE {s}."Cp6_DeadLetterRecord" SET "ReplayReasonCode"='new-replay' WHERE "TenantId"={a};
            UPDATE {s}."Cp6_OutboxMessage" SET "LeaseToken"=repeat('f',32) WHERE "Id"={out};
            UPDATE {s}."OrderBridgeDispatch" SET "CompletedAtUtc"={now} WHERE "TenantId"={a};
            UPDATE {s}."Request" SET "ResultVersion"=12 WHERE "TenantId"={a};
            INSERT INTO {s}."InboxReplayAudit" SELECT "TenantId",{new_op},"MessageId","PayloadSha256","InputRowVersion",
                "ActorId",'new-replay',"PreviousAttemptCount",{now} FROM {s}."InboxReplayAudit" WHERE "TenantId"={a};
            INSERT INTO {s}."DeliveryReplayAudit" SELECT "TenantId",{new_op},"Kind","TargetId","MessageId","PayloadSha256",
                "InputRowVersion","ActorId",'new-replay',"PreviousAttemptCount",{now} FROM {s}."DeliveryReplayAudit" WHERE "TenantId"={a};
            COMMIT; SELECT pg_backend_pid();
        """.format(s=SCHEMA, a=literal(a), out=literal(a_out), new_op=literal(uuid4()), now=literal(FUTURE)))
        require(not err, "A update after the cut failed")
        b_writer, err = f.sql("after-cut-b", 'UPDATE {} SET "AttemptCount"=6 WHERE "TenantId"={}; SELECT pg_backend_pid();'
                             .format(relation("CommandInbox"), literal(b)))
        require(not err, "B update after the cut failed")
        (f.root / "cut.json").write_text(json.dumps({"Snapshot": snapshot, "WriterA": int(writer), "WriterB": int(b_writer)}) + "\n",
                                          encoding="utf-8")

    a_bundle = archive.export_bundle(args.pg_bin, f.target, args.username, installation, str(a), f.root / "v8-cut",
        include_original_journal=True, include_rma_source=True, include_rma_visibility=True, include_identity_priority=True,
        include_rma_iam_references=True, file_content_root=root, include_erp_integration=True, on_snapshot=after_cut)
    current = independent(f, "after-a", a)
    manifest = json.loads((a_bundle / "manifest.json").read_bytes())
    require(manifest["format"] == erp_integration.FORMAT and manifest["recoveryReady"] is False
            and manifest["erpIntegration"] == erp_integration.manifest_section({table.name: 1 for table in erp_integration.TABLES}),
            "v8 ERP manifest did not bind all original members")
    for member, raw in before.items():
        require((a_bundle / member).read_bytes() == raw and current[member] != raw, "snapshot drift in " + member)
        require(all(row["TenantId"] == str(a) for row in csv.DictReader(io.StringIO(raw.decode("utf-8")))), "B entered A archive")
    require(payload.hex().encode() in before["erp-command-inbox.csv"] and b"original-partition " in before["erp-outbox.csv"],
            "the chosen native binary and text values did not reach the cut")

    def export(label, tenant, username=args.username, expected=0):
        output = f.root / label
        result = f.cli(label, "export", "--pg-bin", args.pg_bin, "--target-file", args.target_file, "--output", output,
                       "--username", username, "--installation", installation, "--tenant", tenant, *FLAGS,
                       "--file-content-root", root, expected=expected, username=username)
        return output, result

    b_out_dir, _ = export("v8-b", b)
    b_bundle = b_out_dir / "bundle"
    b_manifest = json.loads((b_bundle / "manifest.json").read_bytes())
    for table in erp_integration.TABLES:
        raw = (b_bundle / table.member).read_bytes()
        rows = list(csv.DictReader(io.StringIO(raw.decode("utf-8"))))
        require(len(rows) == (0 if table.name in ("Cp6_DeadLetterRecord", "DeliveryReplayAudit") else 1)
                and all(row["TenantId"] == str(b) for row in rows)
                and b_manifest["erpIntegration"]["tables"][table.name]["count"] == len(rows), "B member omitted or crossed")
    f.cli("v8-b-verify", "verify", "--bundle", b_bundle, "--tenant", b, "--file-content-root", root)
    v7 = json.loads((args.v7_bundle / "manifest.json").read_bytes())
    verified, _ = f.cli("v7-reused-verify", "verify", "--bundle", args.v7_bundle, "--tenant", v7["tenantId"],
                        "--file-content-root", args.v7_file_root)
    require(json.loads(verified)["format"] == "cp6-rma-source-archive-v7", "actual W389 v7 did not verify")

    f.sql("localized-rls", """
        CREATE ROLE w395_backup LOGIN IN ROLE pg_read_all_data;
        GRANT CONNECT ON DATABASE {db} TO w395_backup;
        GRANT EXECUTE ON FUNCTION pg_catalog.pg_control_system() TO w395_backup;
        ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
    """.format(db='"' + f.target.database + '"', table=relation("CommandInbox")))
    masked, _ = f.sql("rls-masked", "SELECT count(*) FROM {};".format(relation("CommandInbox")), username=BACKUP)
    require(masked.strip() == b"0", "native RLS did not mask the original ERP table")
    refused, (_, err) = export("v8-rls", a, username=BACKUP, expected=1)
    require(b"erp-integration-read-failed" in err and b"42501" in (refused / "native/holder.stderr").read_bytes()
            and not (refused / "bundle/manifest.json").exists(), "masked ERP archive did not fail closed")
    (f.root / "evidence-summary.json").write_text(json.dumps({"CutManifest": archive.file_digest(a_bundle / "manifest.json"),
        "OriginalMembers": {name: archive.file_digest(a_bundle / name) for name in before},
        "BManifest": archive.file_digest(b_bundle / "manifest.json")}, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "a-bundle", "b-bundle", "v7-bundle", "v7-file-root", "disposable-pgdata", "evidence"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--username", required=True)
    exercise(parser.parse_args())
    print("same-cut native ERP eight-table bytes, global keys, isolated B and RLS denial passed", flush=True)


if __name__ == "__main__":
    main()
