#!/usr/bin/env python3
"""Verify the v3 source cut against a real commissioned Core and native RMA rows."""

import argparse
from concurrent.futures import ThreadPoolExecutor
import csv
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "rma-tenant-migration"))
from verify import A, B, BASE_ENV, FLAGS, OWNER, Fixture, q, require, schema, token, wait_for
from verify_commission import COMMISSION, database_for, expected_histories
from verify_original_journal import COMMAND as JOURNAL, cli_from, write_original

import source_v3

DATABASE = "w374_live"


def outbound(tenant, outbound_id, number, customer):
    return """
        INSERT INTO public."T_OutboundOrder"
            ("Id","TenantId","OutboundNo","OutboundType","CustomerCd","WarehouseCd","PlannedDate","Status",
             "Priority","CreateDate","IsDeleted","RowVersion")
        VALUES ({},{},{},2,{},'W1','2026-11-09',4,0,'2026-11-09',false,decode('0000000000000000','hex'));
    """.format(q(outbound_id), q(tenant), q(number), q(customer))


def detail(tenant, line_id, number, line_no, deleted=False):
    return """
        INSERT INTO public."T_OutboundOrderDetail"
            ("Id","TenantId","OutboundNo","LineNo","ProductCd","RequiredQty","AllocatedQty","ShippedQty",
             "UnitCd","WarehouseCd","LocationCd","LotNo","ShipTxnNo","CreateDate","IsDeleted","RowVersion")
        VALUES ({},{},{},{},'P-SRC',1234567890123.12345678,1234567890123.12345678,1234567890123.12345678,
                'EA',NULL,'LOC-1','LOT-2','SHIP-EXACT','2026-11-09',{},decode('0000000000000000','hex'));
    """.format(q(line_id), q(tenant), q(number), line_no, str(deleted).lower())


def lineage(tenant, line_id, number, marker):
    payload = {
        "SnapshotId": token(), "OrderId": token(), "OrderNumber": marker,
        "OrderLineId": token(), "OrderLineNumber": 4, "ProductCode": "P-SRC",
        "Quantity": "1234567890123.12345678", "QuantityUnit": "EA", "CapturedAt": "2026-11-09T09:00:00+00:00",
    }
    sql = """
        INSERT INTO cp6_outbound_lineage_v1.order_lines (tenant_id,outbound_line_id,outbound_no,payload)
        VALUES ({},{},{},{}::jsonb);
    """.format(q(tenant), q(line_id), q(number), q(json.dumps(payload)))
    return sql, payload


def seed(f):
    a, b = token(), token()
    commissioning, _ = database_for(f, DATABASE, (a, b))
    f.invoke("commission-original", COMMISSION, commissioning)
    with cli_from(f, f.args.journal_cli):
        f.invoke("journal-installed", JOURNAL, {**commissioning, "OperationId": token()})
    application, operation = token(), token()
    number = "W374-SAME-DOC "
    for tenant, role, marker in ((a, A, "A"), (b, B, "B")):
        outbound_id, line_id, actor = token(), token(), token()
        head = """
            INSERT INTO {schema}.heads (tenant_id,id,actor_id,outbound_id,version)
                VALUES ({tenant},{application},{actor},{outbound_id},1);
            INSERT INTO {schema}.versions (tenant_id,id,version,state,payload)
                VALUES ({tenant},{application},1,'DRAFT',{payload}::jsonb);
        """.format(schema=schema(tenant), tenant=q(tenant), application=q(application), actor=q(actor),
                   outbound_id=q(outbound_id), payload=q(json.dumps({"Id": application, "Version": 1,
                                                                   "State": "DRAFT", "OutboundId": outbound_id})))
        f.sql(DATABASE, "BEGIN;\n" + outbound(tenant, outbound_id, number, "CUSTOMER-" + marker)
              + detail(tenant, line_id, number, 1) + head + "COMMIT;")
        request = json.dumps({"OperationId": operation, "ApplicationId": application,
                              "OutboundId": outbound_id}).encode("utf-8")
        result = json.dumps({"OperationId": operation, "State": "DRAFT", "Marker": marker}).encode("utf-8")
        f.sql(DATABASE, write_original(tenant, operation, actor, request, result, [application]), role=role)
    return commissioning


def fixture_facts(f, a, b):
    header = json.loads(f.sql(DATABASE, """
        SELECT json_build_object(
            'id',o."Id",'number',convert_from(bpcharsend(o."OutboundNo"),'UTF8'),'customer',o."CustomerCd",
            'warehouse',o."WarehouseCd",'order',o."WebOrderNo",'type',o."OutboundType",'status',o."Status",
            'deleted',o."IsDeleted")
        FROM public."T_OutboundOrder" o
        JOIN {schema}.heads h ON h.tenant_id=o."TenantId" AND h.outbound_id=o."Id"
        WHERE o."TenantId"={tenant};
    """.format(schema=schema(a), tenant=q(a))))
    require(header["customer"] == "CUSTOMER-A" and header["order"] is None and header["type"] == 2
            and header["status"] == 4 and header["number"].endswith(" "), "unexpected initial source header")
    original = f.sql(DATABASE, """
        SELECT "Id" FROM public."T_OutboundOrderDetail"
        WHERE "TenantId"={} AND "OutboundNo"={} AND NOT "IsDeleted";
    """.format(q(a), q(header["number"])))
    require(len(original.splitlines()) == 1, "expected one initial source line")
    native_quantity = f.sql(DATABASE, """
        SELECT format_type(atttypid,atttypmod) FROM pg_attribute
        WHERE attrelid='public."T_OutboundOrderDetail"'::regclass AND attname='ShippedQty';
    """)
    require(native_quantity == "numeric(21,8)", "native shipped quantity shape changed")

    padded, mismatch, deleted, unused_line, unused_out = (token() for _ in range(5))
    native_number = header["number"]
    b_out, b_line = f.sql(DATABASE, """
        SELECT o."Id",d."Id" FROM public."T_OutboundOrder" o JOIN public."T_OutboundOrderDetail" d
          ON d."TenantId"=o."TenantId" AND d."OutboundNo"=o."OutboundNo"
        WHERE o."TenantId"={} AND o."OutboundNo"={};
    """.format(q(b), q(native_number))).split("|")
    good_lineage, payload = lineage(a, padded, native_number, "A-ORIGINAL")
    wrong_lineage, _ = lineage(a, mismatch, native_number.rstrip(" "), "A-TEXT-MISMATCH")
    deleted_lineage, _ = lineage(a, deleted, native_number, "A-DELETED")
    b_lineage, _ = lineage(b, b_line, native_number, "B-SAME-DOCUMENT")
    unused_lineage, _ = lineage(a, unused_line, "A-UNREFERENCED", "A-UNREFERENCED")
    f.sql(DATABASE, "BEGIN;\n" + outbound(a, unused_out, "A-UNREFERENCED", "A-UNUSED") +
          detail(a, padded, native_number + " ", 2) + detail(a, mismatch, native_number, 3) +
          detail(a, deleted, native_number, 4, deleted=True) +
          detail(a, unused_line, "A-UNREFERENCED", 1) + good_lineage + wrong_lineage +
          deleted_lineage + b_lineage + unused_lineage + "COMMIT;")
    require(f.sql(DATABASE, """
        SELECT (o."OutboundNo"=d."OutboundNo")::text || '|' ||
            (convert_from(bpcharsend(o."OutboundNo"),'UTF8')=convert_from(bpcharsend(d."OutboundNo"),'UTF8'))::text
        FROM public."T_OutboundOrder" o,public."T_OutboundOrderDetail" d WHERE o."Id"={} AND d."Id"={};
    """.format(q(header["id"]), q(padded))) == "true|false",
            "fixture did not distinguish native BPCHAR equality from exact lineage text")
    facts = {"Header": header, "Original": original, "Padded": padded, "Mismatch": mismatch,
             "Deleted": deleted, "BLine": b_line, "BOutbound": b_out, "UnusedLine": unused_line, "Payload": payload}
    f.save("fixture-facts.json", facts)
    return facts


def csv_rows(path):
    with path.open(encoding="utf-8", newline="") as source:
        reader = csv.DictReader(source)
        return reader.fieldnames, list(reader)


def assert_capture(f, bundle, tenant, facts):
    header_columns, headers = csv_rows(bundle / "outbounds.csv")
    detail_columns, details = csv_rows(bundle / "rma-source-details.csv")
    lineage_columns, lineage_rows = csv_rows(bundle / "rma-source-lineage.csv")
    require(header_columns == list(source_v3.HEADER_COLUMNS) and detail_columns == list(source_v3.DETAIL_COLUMNS)
            and lineage_columns == list(source_v3.LINEAGE_COLUMNS), "source CSV layout mismatch")
    h = facts["Header"]
    require(headers == [{"tenant_id": tenant, "outbound_id": h["id"], "outbound_no": h["number"],
                         "warehouse_cd": h["warehouse"], "status": str(h["status"]), "is_deleted": "f",
                         "customer_cd": h["customer"], "web_order_no": "", "outbound_type": str(h["type"])}],
            "wrong referenced header or post-cut value included")
    require([row["outbound_line_id"] for row in details] ==
            [facts[name] for name in ("Original", "Padded", "Mismatch")], "wrong active detail closure")
    require(all(row["tenant_id"] == tenant and row["outbound_id"] == h["id"] for row in details),
            "detail crossed the referenced tenant or header")
    exact = details[1]
    require(exact["outbound_no"] == h["number"] + " " and exact["shipped_qty"] == "1234567890123.12345678"
            and exact["product_cd"] == "P-SRC" and exact["unit_cd"] == "EA" and exact["warehouse_cd"] == ""
            and exact["location_cd"] == "LOC-1" and exact["lot_no"] == "LOT-2"
            and exact["ship_txn_no"] == "SHIP-EXACT", "native detail values changed")
    require(len(lineage_rows) == 1 and lineage_rows[0]["tenant_id"] == tenant
            and lineage_rows[0]["outbound_line_id"] == facts["Padded"] and lineage_rows[0]["outbound_no"] == h["number"]
            and json.loads(lineage_rows[0]["payload"]) == facts["Payload"], "wrong original optional lineage set")
    f.save("source-archive-counts.json", {"headers": len(headers), "details": len(details), "lineage": len(lineage_rows)})


def archive_cli(f, archive, label, *arguments):
    result = subprocess.run([sys.executable, "-B", str(Path(archive.__file__).resolve()), *map(str, arguments)], cwd=f.root,
                            env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, capture_output=True,
                            timeout=45, creationflags=FLAGS)
    f.save(label + ".stdout", result.stdout)
    f.save(label + ".stderr", result.stderr)
    f.save(label + ".process.json", {"ExitCode": result.returncode})
    return result


def exercise(f):
    import archive

    commission = seed(f)
    target = archive.PhysicalTarget.from_mapping(commission["Target"])
    installation = commission["InstallationId"]
    a, b = (row["TenantId"] for row in commission["Tenants"])
    facts = fixture_facts(f, a, b)
    environment = {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}
    h = facts["Header"]

    def after_snapshot(snapshot):
        holder = next(row for row in f.activity(DATABASE) if row["application_name"] == "cp6-rma-archive-holder")
        require(holder["state"] == "idle in transaction", "source snapshot holder is not live")
        later_lineage, _ = lineage(a, facts["Original"], h["number"], "A-LATER-LINEAGE")
        update_a = """
            BEGIN;
            UPDATE public."T_OutboundOrder" SET "CustomerCd"='A-LATER',"WebOrderNo"='LATE-ORDER-A' WHERE "Id"={};
            UPDATE public."T_OutboundOrderDetail" SET "ShippedQty"=0.11111111 WHERE "Id"={};
        """.format(q(h["id"]), q(facts["Padded"])) + later_lineage + "SELECT pg_backend_pid(); COMMIT;"
        update_b = """
            BEGIN;
            UPDATE public."T_OutboundOrder" SET "CustomerCd"='B-LATER' WHERE "Id"={};
            SELECT pg_backend_pid(); COMMIT;
        """.format(q(facts["BOutbound"]))
        with ThreadPoolExecutor(max_workers=2) as jobs:
            writers = [jobs.submit(f.sql, DATABASE, sql, label="after-cut-" + name)
                       for name, sql in (("a", update_a), ("b", update_b))]
            pids = [int(writer.result()) for writer in writers]
        require(len(set(pids)) == 2, "source changes did not use independent PostgreSQL sessions")
        f.save("source-snapshot.json", {"Snapshot": snapshot, "Holder": holder["pid"], "WriterPids": pids})

    bundle = archive.export_bundle(f.args.pg_bin, target, OWNER, installation, a, f.root / "v3-cut",
                                   include_original_journal=True, include_rma_source=True, environ=environment,
                                   on_snapshot=after_snapshot)
    manifest = archive.verify_bundle(bundle, a)
    require(manifest["format"] == source_v3.FORMAT and manifest["recoveryReady"] is False
            and manifest["rmaSource"]["counts"] == {"headers": 1, "details": 3, "lineage": 1}
            and manifest["journal"]["counts"] == {"rows": 1, "committed": 1, "closed": 0},
            "v3 manifest missed the earlier source/journal cut")
    assert_capture(f, bundle, a, facts)
    require(f.sql(DATABASE, """
        SELECT count(*) FROM public."T_OutboundOrder"
        WHERE ("Id"={} AND "CustomerCd"='A-LATER' AND "WebOrderNo"='LATE-ORDER-A')
           OR ("Id"={} AND "CustomerCd"='B-LATER');
    """.format(q(h["id"]), q(facts["BOutbound"]))) == "2", "live source deltas not committed")
    require(f.sql(DATABASE, """
        SELECT "ShippedQty"::text FROM public."T_OutboundOrderDetail" WHERE "Id"={};
    """.format(q(facts["Padded"]))) == "0.11111111", "live quantity delta not committed")

    f.save("v3-target.json", target.mapping())
    command = ("export", "--pg-bin", f.args.pg_bin, "--target-file", f.root / "v3-target.json",
               "--username", OWNER, "--installation", installation, "--tenant", a, "--include-rma-source")
    refused = archive_cli(f, archive, "v3-without-journal", *command, "--output", f.root / "v3-no-journal")
    require(refused.returncode == 1 and b"rma-source-journal-required" in refused.stderr
            and not (f.root / "v3-no-journal/bundle/manifest.json").exists(), "v3 accepted missing journal opt-in")
    exported = archive_cli(f, archive, "v3-cli-export", *command, "--include-original-journal",
                           "--output", f.root / "v3-cli")
    require(exported.returncode == 0, "normal v3 export CLI failed")
    new_bundle = f.root / "v3-cli/bundle"
    verified = archive_cli(f, archive, "v3-cli-verify", "verify", "--bundle", new_bundle, "--tenant", a)
    require(verified.returncode == 0 and json.loads(verified.stdout)["format"] == source_v3.FORMAT
            and archive.verify_bundle(new_bundle, a)["rmaSource"]["counts"]["lineage"] == 2,
            "normal v3 verify did not accept the subsequent source cut")
    broken = f.root / "v3-missing-source"
    shutil.copytree(bundle, broken)
    (broken / "rma-source-lineage.csv").unlink()
    invalid = archive_cli(f, archive, "v3-missing-source", "verify", "--bundle", broken, "--tenant", a)
    require(invalid.returncode == 1 and b"bundle-member-mismatch" in invalid.stderr, "v3 accepted missing lineage member")
    for key, path, version in (("v1", f.args.v1_bundle, archive.FORMAT),
                               ("v2", f.args.v2_bundle, archive.FORMAT_WITH_JOURNAL)):
        old = json.loads((path / "manifest.json").read_text(encoding="utf-8"))
        checked = archive_cli(f, archive, key + "-reused-verify", "verify", "--bundle", path, "--tenant", old["tenantId"])
        require(checked.returncode == 0 and json.loads(checked.stdout)["format"] == version, "prior bundle was rejected")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    paths = ("root", "pg-bin", "dotnet", "cli", "journal-cli", "webapi", "v1-bundle", "v2-bundle")
    for name in paths:
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in paths:
        name = name.replace("-", "_")
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "rma-source-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    artifacts = {name: hashlib.sha256(getattr(args, name).read_bytes()).hexdigest()
                 for name in ("webapi", "cli", "journal_cli")}
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source, "Sha256": artifacts})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        fixture.sql("postgres", "ALTER SYSTEM SET log_line_prefix TO " + q("%m [%p] %a %u@%d xid=%x ") + ";")
        fixture.sql("postgres", "SELECT pg_reload_conf();")
        wait_for(lambda: "%u@%d" in fixture.sql("postgres", "SHOW log_line_prefix;"), "PG role logging did not reload")
        exercise(fixture)
    print("real Core native source cut, v3 archive and v1/v2 compatibility passed", flush=True)


if __name__ == "__main__":
    main()
