#!/usr/bin/env python3
"""Verify the optional visibility cut in a real commissioned PostgreSQL Core."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import sys

sys.dont_write_bytecode = True

import archive
import source_v3
import visibility_v4
from verify_source_archive import (BASE_ENV, DATABASE, OWNER, Fixture, archive_cli, csv_rows, detail,
                                   expected_histories, outbound, q, require, schema, seed, token)


def insert(table, tenant, row_id, **columns):
    data = {"Id": row_id, "TenantId": tenant, "CreateDate": "2026-11-09", "IsDeleted": False, **columns}
    names = ",".join('"' + name + '"' for name in data)
    values = ",".join("NULL" if value is None else q(value) for value in data.values())
    return f"""INSERT INTO public."{table}" ({names},"RowVersion")
        VALUES ({values},decode('0000000000000000','hex'));\n"""


def selected_outbound(tenant, row_id, number):
    application, actor = token(), token()
    payload = json.dumps({"Id": application, "Version": 1, "State": "DRAFT", "OutboundId": row_id})
    return outbound(tenant, row_id, number, "CUSTOMER-A") + f"""
        INSERT INTO {schema(tenant)}.heads (tenant_id,id,actor_id,outbound_id,version)
            VALUES ({q(tenant)},{q(application)},{q(actor)},{q(row_id)},1);
        INSERT INTO {schema(tenant)}.versions (tenant_id,id,version,state,payload)
            VALUES ({q(tenant)},{q(application)},1,'DRAFT',{q(payload)}::jsonb);
    """


def order(tenant, row_id, number, department, deleted=False):
    return insert("T_Order", tenant, row_id, WebOrderNo=number, OrderDepartment=department, CustomerCd="C",
                  OrderType="WEB", OrderStatus="OPEN", Status=1, McTransferFlg=False, ShipStatus=0, IsDeleted=deleted)


def work_order(tenant, row_id, number, order_number):
    return insert("T_WorkOrder", tenant, row_id, WorkOrderNo=number, WebOrderNo=order_number, ProductCd="P",
                  Status=1, ProductionQty=1, CompletedQty=0, DefectQty=0, Priority=0)


def location(tenant, row_id, warehouse, code, area, deleted=False, quarantined=False):
    return insert("T_Location", tenant, row_id, WarehouseCd=warehouse, LocationCd=code, AreaCd=area,
                  LocationLevel=1, CapacityQty=0, ReservedCapacityQty=0, IsPickable=not quarantined,
                  IsBlocked=quarantined, IsDeleted=deleted, IsInitializationQuarantined=quarantined)


def existing_source(f, tenant):
    return f.sql(DATABASE, f"""
        SELECT o."Id",d."Id" FROM public."T_OutboundOrder" o JOIN public."T_OutboundOrderDetail" d
          ON d."TenantId"=o."TenantId" AND d."OutboundNo"=o."OutboundNo"
        WHERE o."TenantId"={q(tenant)};
    """).split("|")


def fixture_facts(f, a, b):
    a_header, a_line = existing_source(f, a)
    b_header, b_line = existing_source(f, b)
    empty, foreign, deleted, empty_line, quarantine_line = (token() for _ in range(5))
    direct_order, empty_order, deleted_order, b_order, unrelated_order = (token() for _ in range(5))
    direct_work, empty_work, b_work = (token() for _ in range(3))
    a_location, deleted_location, empty_location, wrong_warehouse, quarantined_location, b_location = (
        token() for _ in range(6))
    # Seed a synthetic dangling reference to a quarantined placeholder. Restore the exact native
    # guard in the same fixture transaction before any archive opens a connection.
    quarantine = 'ALTER TABLE public."T_Location" {} TRIGGER location_draft_native;\n'
    native_guard = """
        SELECT json_build_object('oid',oid,'mode',tgenabled,'function',tgfoid,'definition',pg_get_triggerdef(oid))
        FROM pg_catalog.pg_trigger
        WHERE tgrelid='public."T_Location"'::regclass AND tgname='location_draft_native' AND tgenabled='O'
          AND tgfoid='cp6_location_draft_v1.native_guard()'::regprocedure;
    """
    original_guard = f.sql(DATABASE, native_guard, label="location-guard-before")
    require(original_guard, "native location guard missing before fixture setup")
    sql = (selected_outbound(a, empty, "A-EMPTY") + selected_outbound(a, foreign, "A-FOREIGN-WORK")
           + detail(a, deleted, "W374-SAME-DOC   ", 2, deleted=True) + detail(a, empty_line, "A-EMPTY", 1)
           + detail(a, quarantine_line, "A-EMPTY", 2)
           + order(a, direct_order, "dIRECT-order", "DePt-A  ") + order(a, empty_order, "", "Fallback ")
           + order(a, deleted_order, "deleted-order", None, deleted=True) + order(b, b_order, "b-order", "B-Dept")
           + order(a, unrelated_order, "A-UNRELATED", "Excluded")
           + work_order(a, direct_work, "work-direct", "direct-order") + work_order(a, empty_work, "", "")
           + work_order(b, b_work, "B-ONLY-WORK ", "B-ORDER ")
           + location(a, a_location, "w1", "lOC-1", "ArEa-A  ")
           + location(a, deleted_location, "w2", "loc-gone", None, deleted=True)
           + location(a, empty_location, "", "loc-empty", "")
           + location(a, wrong_warehouse, "W1", "loc-quarantine", "Excluded")
           + location(b, b_location, "W1", "LOC-1", "Area-B")
           + quarantine.format("DISABLE") + location(a, quarantined_location, "", "loc-quarantine", None, quarantined=True)
           + quarantine.format("ENABLE"))
    f.sql(DATABASE, "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + sql + f"""
        UPDATE public."T_OutboundOrder" SET "Creator"='MiXeD-Actor  ', "WarehouseCd"='W1 ',
            "WebOrderNo"='DIRECT-Order ', "WorkOrderNo"='Work-Direct ' WHERE "Id"={q(a_header)};
        UPDATE public."T_OutboundOrder" SET "Creator"='MIXED-ACTOR', "WebOrderNo"='', "WorkOrderNo"=''
            WHERE "Id"={q(empty)};
        UPDATE public."T_OutboundOrder" SET "WebOrderNo"='Deleted-Order ', "WorkOrderNo"='b-ONLY-WORK  '
            WHERE "Id"={q(foreign)};
        UPDATE public."T_OutboundOrder" SET "Creator"='Tenant-B', "WorkOrderNo"='b-only-work' WHERE "Id"={q(b_header)};
        UPDATE public."T_OutboundOrderDetail" SET "OutboundNo"='w374-same-doc  ', "Creator"='mixed-actor',
            "LocationCd"='Loc-1 ' WHERE "Id"={q(a_line)};
        UPDATE public."T_OutboundOrderDetail" SET "WarehouseCd"='W2 ', "LocationCd"='Loc-Gone  '
            WHERE "Id"={q(deleted)};
        UPDATE public."T_OutboundOrderDetail" SET "Creator"='mixed-actor ', "WarehouseCd"='', "LocationCd"='LOC-EMPTY'
            WHERE "Id"={q(empty_line)};
        UPDATE public."T_OutboundOrderDetail" SET "Creator"='mixed-actor ', "WarehouseCd"='', "LocationCd"='LOC-QUARANTINE'
            WHERE "Id"={q(quarantine_line)};
        UPDATE public."T_OutboundOrderDetail" SET "Creator"='Tenant-B' WHERE "Id"={q(b_line)};
        COMMIT;
    """, label="fixture-visibility")
    require(f.sql(DATABASE, native_guard, label="location-guard-after") == original_guard,
            "native location guard changed during fixture setup")

    a_expected = {
        ("outbound", a_header): dict(outbound_no="W374-SAME-DOC ", creator="MiXeD-Actor  ", warehouse_cd="W1 ",
                                     web_order_no="DIRECT-Order ", work_order_no="Work-Direct ", is_deleted=False),
        ("outbound", empty): dict(outbound_no="A-EMPTY", creator="MIXED-ACTOR", warehouse_cd="W1",
                                  web_order_no="", work_order_no="", is_deleted=False),
        ("outbound", foreign): dict(outbound_no="A-FOREIGN-WORK", creator=None, warehouse_cd="W1",
                                    web_order_no="Deleted-Order ", work_order_no="b-ONLY-WORK  ", is_deleted=False),
        ("outbound_detail", a_line): dict(outbound_id=a_header, outbound_no="w374-same-doc  ", creator="mixed-actor",
                                          warehouse_cd=None, location_cd="Loc-1 ", is_deleted=False),
        ("outbound_detail", deleted): dict(outbound_id=a_header, outbound_no="W374-SAME-DOC   ", creator=None,
                                           warehouse_cd="W2 ", location_cd="Loc-Gone  ", is_deleted=True),
        ("outbound_detail", empty_line): dict(outbound_id=empty, outbound_no="A-EMPTY", creator="mixed-actor ",
                                              warehouse_cd="", location_cd="LOC-EMPTY", is_deleted=False),
        ("outbound_detail", quarantine_line): dict(outbound_id=empty, outbound_no="A-EMPTY", creator="mixed-actor ",
                                                   warehouse_cd="", location_cd="LOC-QUARANTINE", is_deleted=False),
        ("order", direct_order): dict(web_order_no="dIRECT-order", order_department="DePt-A  ", is_deleted=False),
        ("order", empty_order): dict(web_order_no="", order_department="Fallback ", is_deleted=False),
        ("order", deleted_order): dict(web_order_no="deleted-order", order_department=None, is_deleted=True),
        ("work_order", direct_work): dict(work_order_no="work-direct", web_order_no="direct-order", is_deleted=False),
        ("work_order", empty_work): dict(work_order_no="", web_order_no="", is_deleted=False),
        ("location", a_location): dict(warehouse_cd="w1", location_cd="lOC-1", area_cd="ArEa-A  ",
                                       is_deleted=False, is_initialization_quarantined=False),
        ("location", deleted_location): dict(warehouse_cd="w2", location_cd="loc-gone", area_cd=None,
                                             is_deleted=True, is_initialization_quarantined=False),
        ("location", empty_location): dict(warehouse_cd="", location_cd="loc-empty", area_cd="",
                                           is_deleted=False, is_initialization_quarantined=False),
        ("location", quarantined_location): dict(warehouse_cd="", location_cd="loc-quarantine", area_cd=None,
                                                 is_deleted=False, is_initialization_quarantined=True),
    }
    b_expected = {
        ("outbound", b_header): dict(outbound_no="W374-SAME-DOC ", creator="Tenant-B", warehouse_cd="W1",
                                     web_order_no=None, work_order_no="b-only-work", is_deleted=False),
        ("outbound_detail", b_line): dict(outbound_id=b_header, outbound_no="W374-SAME-DOC ", creator="Tenant-B",
                                          warehouse_cd=None, location_cd="LOC-1", is_deleted=False),
        ("order", b_order): dict(web_order_no="b-order", order_department="B-Dept", is_deleted=False),
        ("work_order", b_work): dict(work_order_no="B-ONLY-WORK ", web_order_no="B-ORDER ", is_deleted=False),
        ("location", b_location): dict(warehouse_cd="W1", location_cd="LOC-1", area_cd="Area-B",
                                       is_deleted=False, is_initialization_quarantined=False),
    }
    return a_expected, b_expected, (a_header, empty, deleted, a_location)


def own_rows(f, tenant, denied, allowed):
    return json.loads(f.sql(DATABASE, f"""
        SELECT coalesce(json_agg(o."Id" ORDER BY o."Id"),'[]'::json)
        FROM public."T_OutboundOrder" o
        WHERE o."TenantId"={q(tenant)} AND o."Id" IN ({q(denied)},{q(allowed)}) AND NOT o."IsDeleted"
          AND o."Creator"='mixed-actor' AND NOT EXISTS (SELECT FROM public."T_OutboundOrderDetail" d
              WHERE d."TenantId"=o."TenantId" AND d."OutboundNo"=o."OutboundNo"
                AND (d."Creator" IS NULL OR d."Creator"<>'mixed-actor'));
    """))


def assert_capture(bundle, tenant, expected):
    manifest = archive.verify_bundle(bundle, tenant)
    counts = {kind: sum(row[0] == kind for row in expected) for kind in visibility_v4.FACTS}
    require(manifest["format"] == visibility_v4.FORMAT and manifest["recoveryReady"] is False
            and manifest["rmaVisibility"]["counts"] == counts, "wrong visibility manifest")
    columns, records = csv_rows(bundle / visibility_v4.MEMBER)
    keys = [(row["kind"], row["id"]) for row in records]
    require(columns == list(visibility_v4.COLUMNS) and keys == sorted(expected)
            and all(row["tenant_id"] == tenant for row in records), "wrong visibility closure or native order")
    for row, key in zip(records, keys):
        require(json.loads(row["facts"]) == expected[key], "wrong native visibility facts for " + str(key))
    return manifest


def exercise(f):
    commission = seed(f)
    print("published DatabaseInit and native A/B commissioned", flush=True)
    target = archive.PhysicalTarget.from_mapping(commission["Target"])
    installation = commission["InstallationId"]
    a, b = (row["TenantId"] for row in commission["Tenants"])
    a_expected, b_expected, (denied, allowed, deleted, a_location) = fixture_facts(f, a, b)
    require(own_rows(f, a, denied, allowed) == [allowed], "deleted detail did not affect native self visibility")

    def after_snapshot(snapshot):
        holder = next(row for row in f.activity(DATABASE) if row["application_name"] == "cp6-rma-archive-holder")
        require(holder["state"] == "idle in transaction", "visibility snapshot holder is not live")
        pid = f.sql(DATABASE, f"""
            BEGIN;
            UPDATE public."T_OutboundOrderDetail" SET "Creator"='Mixed-Actor' WHERE "Id"={q(deleted)};
            UPDATE public."T_Location" SET "AreaCd"='AFTER-CUT' WHERE "Id"={q(a_location)};
            SELECT pg_backend_pid(); COMMIT;
        """, label="after-visibility-cut")
        f.save("visibility-snapshot.json", {"Snapshot": snapshot, "Holder": holder["pid"], "Writer": int(pid)})

    environment = {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}
    a_bundle = archive.export_bundle(f.args.pg_bin, target, OWNER, installation, a, f.root / "v4-cut",
                                     include_original_journal=True, include_rma_source=True, include_rma_visibility=True,
                                     environ=environment, on_snapshot=after_snapshot)
    a_manifest = assert_capture(a_bundle, a, a_expected)
    require(a_manifest["rmaSource"]["counts"]["details"] == 3, "v3 active detail cut changed in v4")
    require(own_rows(f, a, denied, allowed) == sorted((denied, allowed)) and f.sql(DATABASE, f"""
        SELECT count(*) FROM public."T_Location" WHERE "Id"={q(a_location)} AND "AreaCd"='AFTER-CUT';
    """) == "1", "post-snapshot visibility change did not commit")

    target_path = f.root / "target.json"
    f.save(target_path.name, target.mapping())
    command = ("export", "--pg-bin", f.args.pg_bin, "--target-file", target_path, "--username", OWNER,
               "--installation", installation, "--tenant", b, "--include-original-journal")
    visibility_flag = "--include-rma-visibility"
    rejected = archive_cli(f, archive, "v4-without-source", *command, visibility_flag,
                           "--output", f.root / "v4-no-source")
    require(rejected.returncode == 1 and b"rma-visibility-source-required" in rejected.stderr
            and not (f.root / "v4-no-source").exists(), "v4 accepted missing source opt-in")
    b_bundle = f.root / "v4-b" / "bundle"
    exported = archive_cli(f, archive, "v4-cli-export-b", *command, "--include-rma-source", visibility_flag,
                           "--output", b_bundle.parent)
    require(exported.returncode == 0, "normal v4 export CLI failed")
    b_manifest = assert_capture(b_bundle, b, b_expected)
    verified = archive_cli(f, archive, "v4-cli-verify-b", "verify", "--bundle", b_bundle, "--tenant", b)
    require(verified.returncode == 0 and json.loads(verified.stdout)["format"] == visibility_v4.FORMAT,
            "normal v4 verify CLI failed")

    legacy = f.root / "v3-b" / "bundle"
    exported = archive_cli(f, archive, "v3-cli-export-b", *command, "--include-rma-source", "--output", legacy.parent)
    require(exported.returncode == 0, "normal v3 export CLI failed")
    v3_manifest = archive.verify_bundle(legacy, b)
    require(v3_manifest["format"] == source_v3.FORMAT
            and b_manifest["members"].keys() == v3_manifest["members"].keys() | {visibility_v4.MEMBER}
            and all(b_manifest["members"][member] == v3_manifest["members"][member]
                    for member in ("outbounds.csv", archive.JOURNAL_MEMBER, *source_v3.MEMBERS)),
            "unchanged v3 data member bytes changed when enabling v4")

    _, a_journal = csv_rows(a_bundle / archive.JOURNAL_MEMBER)
    _, b_journal = csv_rows(b_bundle / archive.JOURNAL_MEMBER)
    require(len(a_journal) == len(b_journal) == 1 and a_journal[0]["tenant_id"] == a and b_journal[0]["tenant_id"] == b
            and a_journal[0]["operation_id"] == b_journal[0]["operation_id"]
            and a_journal[0]["object_ids"] == b_journal[0]["object_ids"]
            and a_journal[0]["result_bytes"] != b_journal[0]["result_bytes"], "original A/B same-key isolation changed")

    broken = f.root / "v4-tampered"
    shutil.copytree(a_bundle, broken)
    with (broken / visibility_v4.MEMBER).open("ab") as member:
        member.write(b"\n")
    rejected = archive_cli(f, archive, "v4-tampered", "verify", "--bundle", broken, "--tenant", a)
    require(rejected.returncode == 1 and b"bundle-digest-mismatch" in rejected.stderr, "changed visibility member accepted")
    for index, version in enumerate((archive.FORMAT, archive.FORMAT_WITH_JOURNAL, source_v3.FORMAT), 1):
        path = getattr(f.args, "v{}_bundle".format(index))
        old = json.loads((path / "manifest.json").read_text(encoding="utf-8"))
        checked = archive_cli(f, archive, "v{}-original-verify".format(index), "verify", "--bundle", path,
                              "--tenant", old["tenantId"])
        require(checked.returncode == 0 and json.loads(checked.stdout)["format"] == version, "prior archive rejected")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    paths = ("root", "pg-bin", "dotnet", "cli", "journal-cli", "webapi", "v1-bundle", "v2-bundle", "v3-bundle")
    for name in paths:
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in paths:
        name = name.replace("-", "_")
        setattr(args, name, getattr(args, name).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "rma-visibility-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    artifacts = {name: hashlib.sha256(getattr(args, name).read_bytes()).hexdigest()
                 for name in ("webapi", "cli", "journal_cli")}
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source, "Sha256": artifacts})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("real Core v4 visibility, same-snapshot A/B scope and v1/v2/v3 compatibility passed", flush=True)


if __name__ == "__main__":
    main()
