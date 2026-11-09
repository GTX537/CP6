#!/usr/bin/env python3
"""Exercise Stock archive export against the isolated retained W352 synthetic source."""

import argparse
import csv
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
import stock_outbox as stock

archive = stock.archive


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def call(evidence, label, command, *, expected=0):
    result = subprocess.run(list(map(str, command)), env=archive.client_environment(os.environ, "W403-" + label),
                            capture_output=True, stdin=subprocess.DEVNULL, timeout=125, creationflags=archive.FLAGS)
    (evidence / (label + ".stdout")).write_bytes(result.stdout)
    (evidence / (label + ".stderr")).write_bytes(result.stderr)
    require(result.returncode == expected, label + " returned " + str(result.returncode))
    return result.stdout, result.stderr


def read_rows(bundle, item):
    with (bundle / item.member).open(encoding="utf-8", newline="") as stream:
        return list(csv.DictReader(stream, strict=True))


def exercise(args):
    archive.private_directory(args.evidence)
    evidence = args.evidence
    actual = json.loads(args.observations.read_bytes())
    a, b, operation = (actual[key] for key in ("aId", "bId", "operation"))
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_bytes()))
    cli = [sys.executable, "-B", stock.__file__]
    export = [*cli, "export", "--pg-bin", args.pg_bin, "--target-file", args.target_file]

    def observed(label, tenant, expected_row):
        out, err = call(evidence, label, [*export, "--username", args.username, "--tenant", tenant, "--output", evidence / label])
        result = json.loads(out)
        bundle = Path(result["bundle"])
        pin = result["manifestSha256"]
        verified, rejected = call(evidence, "verify-" + label, [*cli, "verify", "--tenant", tenant, "--bundle", bundle,
                                  "--manifest-sha256", pin])
        require(not err and not rejected and json.loads(verified) == result, "normal verify or manifest pin differs")
        manifest = json.loads((bundle / "manifest.json").read_bytes())
        require(manifest["source"] == target.mapping() and manifest["snapshot"]["readOnly"] is True
                and manifest["snapshot"]["isolation"] == "repeatable read" and manifest["recoveryReady"] is False,
                "native archive identity or transaction differs")
        native, = read_rows(bundle, stock.NATIVE)
        source, = read_rows(bundle, stock.SOURCE)
        binding, = read_rows(bundle, stock.BINDING)
        route, = read_rows(bundle, stock.ROUTE_TABLE)
        journal, = read_rows(bundle, stock.JOURNAL)
        require(native["TenantId"] == source["TenantId"] == route["TenantId"] == journal["tenant_id"] == tenant
                and native["Id"] == source["NativeId"] == binding["NativeId"] == expected_row["native"]
                and native["MessageId"] == binding["MessageId"] == expected_row["message"]
                and route["Id"] == binding["RootId"] == expected_row["route"]
                and int(route["CurrentEpoch"]) == int(binding["AssignedEpoch"]) == expected_row["epoch"]
                and int(native["Status"]) == expected_row["status"] and source["OperationId"] == journal["operation_id"] == operation,
                "original stock scope, key, epoch or state differs")

        expected = (stock.RUN, stock.CLAIM, stock.TICKET, stock.SEND, stock.MUTATION)
        numbers = [len(expected_row[k]) for k in ("runs", "claims", "claims", "sends", "completions")]
        for item, count in zip(expected, numbers):
            require(len(read_rows(bundle, item)) == count, "companion count differs: " + item.name)

        # Observe original bytea and exact receipt text independently of the archive query/COPY.
        original, native_err = call(evidence, "native-original-" + label,
            [archive.binary(args.pg_bin, "psql"), "-XqAtw", "-v", "ON_ERROR_STOP=1", *archive.connection_args(target, args.username),
             "-c", f"""
                BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
                SELECT json_build_object('input',encode(o.input_bytes,'hex'),'result',encode(o.result_bytes,'hex'),
                    'sourceHash',s."ResultSha256",'payload',encode(n."Payload",'hex'),'nativeXmin',n.xmin::text,
                    'ticket',t."Ticket",'send',a."OriginalTicket",'mutationInput',m."Input",'mutationOutput',m."Output")
                FROM cp6_main_owner_v1.operations o
                JOIN cp6_stock_outbox_v1."Source" s ON (s."TenantId",s."SourceOwner",s."OperationId")=(o.tenant_id,o.owner,o.operation_id)
                JOIN cp6_stock_outbox_v1."Cp6_OutboxMessage" n ON n."Id"=s."NativeId"
                LEFT JOIN cp6_stock_outbox_v1."Cp6_OutboxClaimTicketV2" t ON t."NativeId"=s."NativeId"
                LEFT JOIN cp6_stock_outbox_v1."Cp6_OutboxSendAttemptV2" a ON (a."RootId",a."MutationId")=(t."RootId",t."ReceiptReference")
                LEFT JOIN cp6_stock_outbox_v1."Cp6_OutboxMutationReceiptV2" m ON (m."RootId",m."MutationId")=(t."RootId",t."ReceiptReference")
                WHERE o.tenant_id={archive.literal(tenant)} AND o.owner='Main.Wms.StockMovement'
                  AND o.operation_id={archive.literal(operation)};
                COMMIT;
             """])
        raw = json.loads(original)
        require(not native_err and journal["input_bytes"] == "\\x" + raw["input"]
                and journal["result_bytes"] == "\\x" + raw["result"] and source["ResultSha256"] == raw["sourceHash"]
                and native["Payload"] == "\\x" + raw["payload"] and native["source_xmin"] == raw["nativeXmin"],
                "original bytea, digest or observed native xmin differs")
        if numbers[2]:
            ticket, = read_rows(bundle, stock.TICKET)
            send, = read_rows(bundle, stock.SEND)
            mutation, = read_rows(bundle, stock.MUTATION)
            require(ticket["Ticket"] == raw["ticket"] and send["OriginalTicket"] == raw["send"]
                    and mutation["Input"] == raw["mutationInput"] and mutation["Output"] == raw["mutationOutput"]
                    and mutation["MutationId"] in expected_row["completions"]
                    and json.loads(mutation["Output"])["Transition"] == "Published", "original receipt bytes differ")
        return bundle, pin

    a_bundle, a_pin = observed("a", a, actual["sourceAfter"])
    observed("b", b, actual["bLive"])
    rejected = evidence / "restricted"
    _, error = call(evidence, "restricted", [*export, "--username", args.limited_username, "--tenant", a, "--output", rejected], expected=1)
    require(b"42501" in (rejected / "native/holder.stderr").read_bytes()
            and b"Cp6_OutboxClaimTicketV2" in (rejected / "native/holder.stderr").read_bytes()
            and not (rejected / "bundle/manifest.json").exists() and b"failed" in error, "missing original companion privilege was not rejected")
    wrong_target = evidence / "wrong-target.json"
    wrong_target.write_text(json.dumps(dict(target.mapping(), DatabaseOid=target.database_oid + 1)) + "\n", encoding="utf-8")
    rejected = evidence / "wrong-physical"
    _, error = call(evidence, "wrong-physical", [*cli, "export", "--pg-bin", args.pg_bin, "--target-file", wrong_target,
                    "--username", args.username, "--tenant", a, "--output", rejected], expected=1)
    require(b"stock-source-target-rejected" in error and not (rejected / "bundle/manifest.json").exists(),
            "mismatched physical database OID reached export")
    altered = evidence / "altered-bundle"
    shutil.copytree(a_bundle, altered)
    with (altered / stock.TICKET.member).open("ab") as stream:
        stream.write(b"changed original ticket bytes\n")
    _, error = call(evidence, "verify-altered", [*cli, "verify", "--tenant", a, "--bundle", altered,
                    "--manifest-sha256", a_pin], expected=1)
    require(b"stock-member-digest-mismatch: Cp6_OutboxClaimTicketV2" in error, "pinned verify accepted altered original bytes")
    _, error = call(evidence, "verify-other-tenant", [*cli, "verify", "--tenant", b, "--bundle", a_bundle,
                    "--manifest-sha256", a_pin], expected=1)
    require(b"stock-manifest-scope-rejected" in error, "pinned verify accepted a different tenant")
    print("W403 native A/B, exact original bytes and fail-closed export/verify passed")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "observations", "evidence"):
        parser.add_argument("--" + name, type=lambda value: Path(value).absolute(), required=True)
    for name in ("username", "limited-username"):
        parser.add_argument("--" + name, required=True)
    exercise(parser.parse_args())


if __name__ == "__main__":
    main()
