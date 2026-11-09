#!/usr/bin/env python3
"""Verify the v6 historical RMA IAM cut on actual PostgreSQL Core."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import shutil
import sys

sys.dont_write_bytecode = True

import archive
import iam_v6
import identity_priority
import source_v3
import visibility_v4
from verify_identity_priority import checkpoint, outbox
from verify_source_archive import (A, BASE_ENV, DATABASE, OWNER, Fixture, archive_cli, csv_rows, expected_histories,
                                   q, require, schema, seed, token, write_original)
from verify_visibility_archive import order

PASSWORD_SENTINEL = "v6-private-password-fixture"
MFA_SENTINEL = "v6-private-mfa-fixture"
PRIMARY_ROLE = 72
SECONDARY_ROLE = 73


def insert(table, **fields):
    fields.setdefault("CreateDate", "2026-11-09")
    columns = ",".join(f'"{key}"' for key in fields)
    values = ",".join("NULL" if value is None else q(value) for value in fields.values())
    return f'INSERT INTO public."{table}" ({columns}) VALUES ({values});\n'


def user(tenant, actor, name, department, locked=None):
    return insert("Sys_Users", Id=actor, TenantId=tenant, UserName=name, Password=PASSWORD_SENTINEL, RoleId=PRIMARY_ROLE,
                  Enable=True, DeptId=department, FailedLoginCount=0, MustChangePassword=False, TwoFactorEnabled=False,
                  TwoFactorSecret=MFA_SENTINEL, AuthenticationEpoch=token(), AllowPasswordFallback=False,
                  IsPlatformAdmin=False, LockedUntil=locked)


def seed_iam(f, tenant, marker, menu):
    actor, outbound = f.sql(DATABASE, f'SELECT actor_id,outbound_id FROM {schema(tenant)}.heads;').split("|")
    root, department, field, binding, warehouse, action, menu_action, scope = (token() for _ in range(8))
    name = "RMA-Actor-" + marker + "  "
    expiry = "2027-03-01 05:06:07.123456" if marker == "A" else None
    locked = None if marker == "A" else "2026-11-09 09:10:11.123456"
    order_no, dept_code = "ORD-IAM-" + marker, "DePt-IAM "
    root_path = f"/{root}/"
    dept_path = root_path + department + "/"
    custom = f" ({department.upper()}) , raw-invalid  "
    sql = (
        insert("Sys_Tenants", Id=tenant, TenantCode=tenant.replace("-", ""), TenantName="Fixture " + marker,
               Enable=True, ExpireDate=expiry, TwoFactorMode=0)
        + insert("Sys_Dept", Id=root, TenantId=tenant, DeptCode="ROOT-" + marker, DeptName="Root " + marker,
                 Path=root_path, Enable=True, Sort=0)
        + insert("Sys_Dept", Id=department, TenantId=tenant, ParentId=root, DeptCode="CHILD-" + marker, DeptName="Child " + marker,
                 Path=dept_path, Enable=marker == "A", Sort=0)
        + insert("Sys_Roles", TenantId=tenant, RoleId=PRIMARY_ROLE, RoleName="Primary " + marker, Enable=True, OrderNo=0)
        + user(tenant, actor, name, department, locked)
        + insert("Sys_MenuAction", Id=menu_action, TenantId=tenant, MenuId=menu, ActionCode="query", ActionName="Query", Sort=0)
        + insert("Sys_RoleAction", Id=action, TenantId=tenant, RoleId=PRIMARY_ROLE, MenuId=menu, ActionCode="query")
        + insert("Sys_RoleDataScope", Id=scope, TenantId=tenant, RoleId=PRIMARY_ROLE, ResourceKey="wms-rma",
                 ScopeType=4, CustomDeptIds=custom)
        + insert("Sys_RoleFieldPerm", Id=field, TenantId=tenant, RoleId=PRIMARY_ROLE, ResourceKey="wms-rma",
                 FieldName="ReturnReason", Access=1 if marker == "A" else 2)
        + insert("T_WmsRoleScope", Id=warehouse, TenantId=tenant, RoleId=PRIMARY_ROLE, WarehouseCd="w1  ",
                 AreaCd="" if marker == "A" else "AREA-B")
        + order(tenant, token(), order_no, dept_code)
        + outbox(tenant, token(), ("priority-" + marker).encode("utf-8"), None)
        + checkpoint(tenant)
    )
    f.sql(DATABASE, "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + sql + f"""
        INSERT INTO cp6_order_owner_v1.department_mappings(id,tenant_id,code,department_id,enabled)
        VALUES ({q(binding)},{q(tenant)},'dept-iam',{q(department)},true);
        UPDATE public."T_OutboundOrder" SET "WebOrderNo"={q(order_no.lower())},"Creator"={q(name)} WHERE "Id"={q(outbound)};
        UPDATE public."T_OutboundOrderDetail" SET "Creator"={q(name.lower().rstrip())} WHERE "TenantId"={q(tenant)};
        COMMIT;
    """, label="iam-source-" + marker)
    return dict(actor=actor, name=name, expiry=expiry, locked=locked, department=department, root=root,
                dept_path=dept_path, custom=custom, field=field, binding=binding, warehouse=warehouse,
                action=action, menu_action=menu_action, scope=scope, menu=str(menu))


def seed_a_edges(f, tenant, actor):
    missing, unrelated, additional, unknown_field, wildcard = (token() for _ in range(5))
    sql = (
        insert("Sys_Roles", TenantId=tenant, RoleId=SECONDARY_ROLE, RoleName="Disabled", Enable=False, OrderNo=0)
        + insert("Sys_Roles", TenantId=tenant, RoleId=99, RoleName="Unrelated", Enable=True, OrderNo=0)
        + insert("Sys_UserRole", Id=additional, TenantId=tenant, UserId=actor, RoleId=SECONDARY_ROLE)
        + insert("Sys_RoleFieldPerm", Id=unknown_field, TenantId=tenant, RoleId=SECONDARY_ROLE, ResourceKey="WmS-RmA ",
                 FieldName="UnknownOriginal  ", Access=3)
        + insert("Sys_RoleFieldPerm", Id=token(), TenantId=tenant, RoleId=PRIMARY_ROLE, ResourceKey="mes-work-order",
                 FieldName="Excluded", Access=3)
        + insert("T_WmsRoleScope", Id=token(), TenantId=tenant, RoleId=PRIMARY_ROLE, WarehouseCd="W9", AreaCd="*")
        + insert("T_WmsRoleScope", Id=wildcard, TenantId=tenant, RoleId=SECONDARY_ROLE, WarehouseCd="*", AreaCd="ANY-A")
    )
    f.sql(DATABASE, "BEGIN; SELECT cp6_authentication_v1.acquire();\n" + sql + f"""
        INSERT INTO cp6_main_owner_v1.operations(tenant_id,owner,operation_id,actor_id,contract,state,
            input_bytes,input_sha256,result_bytes,object_ids,audit_json)
        VALUES ({q(tenant)},'Main.Wms.StockMovement',{q(token())},{q(unrelated)},'fixture-other-owner/1','CLOSED',
            NULL,NULL,convert_to('{{}}','UTF8'),ARRAY[]::uuid[],'{{}}'::jsonb);
        COMMIT;
    """, label="iam-a-edges")
    operation = token()
    result = json.dumps({"OperationId": operation, "State": "CLOSED"}).encode("utf-8")
    f.sql(DATABASE, write_original(tenant, operation, missing, None, result, []), role=A)
    return dict(missing=missing, unrelated=unrelated, additional=additional, unknown_field=unknown_field, wildcard=wildcard)


def revisions(f, tenant, actor):
    return json.loads(f.sql(DATABASE, f"""
        SELECT json_build_object('tenant',t."AuthenticationPolicyRevision",'expiry',t."ExpireDate",
          'revisions',(SELECT json_object_agg(object_key,revision) FROM cp6_authentication_v1.identity_admission_revisions
             WHERE tenant_id=t."Id" AND object_key IN ('user:'||{q(actor)},'role:72','tree','order-department-mapping')))
        FROM public."Sys_Tenants" t WHERE t."Id"={q(tenant)};
    """))


def assert_capture(bundle, tenant, fixture, before, a_edges=None):
    manifest = archive.verify_bundle(bundle, tenant)
    require(manifest["format"] == iam_v6.FORMAT and manifest["recoveryReady"] is False, "wrong historical IAM format")
    member = bundle / iam_v6.MEMBER
    columns, rows = csv_rows(member)
    keys = [(row["kind"], row["id"]) for row in rows]
    require(columns == list(iam_v6.COLUMNS) and keys == sorted(set(keys))
            and all(row["tenant_id"] == tenant for row in rows), "historical IAM rows crossed scope or order")
    facts = {key: json.loads(row["facts"]) for key, row in zip(keys, rows)}
    require(all(set(row) == set(iam_v6.FACTS[kind][1]) for (kind, _), row in facts.items())
            and all(value.encode("utf-8") not in member.read_bytes() for value in (PASSWORD_SENTINEL, MFA_SENTINEL)),
            "unexpected IAM fact or synthetic secret escaped")
    counts = {kind: sum(key[0] == kind for key in keys) for kind in iam_v6.FACTS}
    expected_counts = dict.fromkeys(iam_v6.FACTS, 1)
    expected_counts.update(department=2, additional_role=0)
    if a_edges:
        expected_counts.update(participant=2, role=2, additional_role=1, role_field=2, warehouse_scope=2)
    require(counts == expected_counts == manifest["rmaIamReferences"]["counts"], "wrong historical IAM reference closure")
    actor, rev = fixture["actor"], before["revisions"]
    require(facts["tenant", tenant]["authentication_policy_revision"] == before["tenant"]
            and facts["tenant", tenant]["expire_date"] == before["expiry"]
            and facts["tenant", tenant]["tree_revision"] == rev["tree"]
            and facts["tenant", tenant]["order_department_mapping_revision"] == rev["order-department-mapping"]
            and facts["tenant", tenant]["enabled"] is True, "wrong native tenant authority facts")
    require(facts["participant", actor] == dict(referenced_head=True, referenced_journal=True, user_present=True,
                user_name=fixture["name"], enabled=True,
                locked_until=fixture["locked"].replace(" ", "T") if fixture["locked"] else None,
                department_id=fixture["department"], primary_role_id=PRIMARY_ROLE, revision=rev["user:" + actor])
            and facts["role", str(PRIMARY_ROLE)] == dict(role_present=True, enabled=True, revision=rev["role:72"]),
            "wrong native participant or original role revision")
    require(facts["menu_reference", fixture["menu"]] == {"menu_key": "wms-rma"}
            and facts["menu_action", fixture["menu_action"]] == dict(menu_id=int(fixture["menu"]), action_code="query")
            and facts["role_action", fixture["action"]] == dict(role_id=PRIMARY_ROLE, menu_id=int(fixture["menu"]), action_code="query")
            and facts["role_scope", fixture["scope"]]["custom_department_ids"] == fixture["custom"]
            and facts["role_field", fixture["field"]]["access"] == (1 if a_edges else 2), "wrong raw IAM permission references")
    require(facts["warehouse_scope", fixture["warehouse"]] == dict(role_id=PRIMARY_ROLE, warehouse_cd="w1  ",
                area_cd="" if a_edges else "AREA-B")
            and facts["order_department_mapping", fixture["binding"]]["department_id"] == fixture["department"]
            and facts["department", fixture["department"]] == dict(parent_id=fixture["root"], path=fixture["dept_path"],
                                                                   enabled=bool(a_edges)), "wrong warehouse or department reference")
    if a_edges:
        missing = facts["participant", a_edges["missing"]]
        require(not missing["referenced_head"] and missing["referenced_journal"] and not missing["user_present"]
                and missing["user_name"] is None and ("participant", a_edges["unrelated"]) not in facts
                and facts["additional_role", a_edges["additional"]] == dict(user_id=actor, role_id=SECONDARY_ROLE)
                and facts["role", str(SECONDARY_ROLE)]["enabled"] is False
                and facts["role_field", a_edges["unknown_field"]]["field_name"] == "UnknownOriginal  "
                and facts["warehouse_scope", a_edges["wildcard"]]["warehouse_cd"] == "*", "wrong original actor or role edges")
    return manifest


def exercise(f):
    commission = seed(f)
    print("published DatabaseInit and native A/B commissioned", flush=True)
    a, b = (row["TenantId"] for row in commission["Tenants"])
    target = archive.PhysicalTarget.from_mapping(commission["Target"])
    menu = f.sql(DATABASE, "SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-rma';")
    require(menu.isdecimal(), "published W351 native menu unavailable")
    a_facts, b_facts = seed_iam(f, a, "A", menu), seed_iam(f, b, "B", menu)
    a_edges = seed_a_edges(f, a, a_facts["actor"])
    a_before, b_before = revisions(f, a, a_facts["actor"]), revisions(f, b, b_facts["actor"])

    def after_snapshot(snapshot):
        holder = next(row for row in f.activity(DATABASE) if row["application_name"] == "cp6-rma-archive-holder")
        require(holder["state"] == "idle in transaction", "IAM archive snapshot holder not live")
        writer = f.sql(DATABASE, f"""
            BEGIN;
            UPDATE public."Sys_RoleFieldPerm" SET "Access"=3 WHERE "Id"={q(a_facts['field'])};
            UPDATE public."Sys_Tenants" SET "ExpireDate"='2028-04-02 12:00:00' WHERE "Id"={q(a)};
            SELECT pg_backend_pid(); COMMIT;
        """, label="iam-after-cut")
        f.save("iam-snapshot.json", {"Snapshot": snapshot, "Holder": holder["pid"], "Writer": int(writer)})

    installation = commission["InstallationId"]
    a_bundle = archive.export_bundle(f.args.pg_bin, target, OWNER, installation, a, f.root / "v6-cut",
                                     include_original_journal=True, include_rma_source=True, include_rma_visibility=True,
                                     include_identity_priority=True, include_rma_iam_references=True,
                                     environ={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, on_snapshot=after_snapshot)
    assert_capture(a_bundle, a, a_facts, a_before, a_edges)
    a_after = revisions(f, a, a_facts["actor"])
    require(a_after["tenant"] != a_before["tenant"] and a_after["expiry"] != a_before["expiry"]
            and a_after["revisions"]["role:72"] != a_before["revisions"]["role:72"]
            and f.sql(DATABASE, f'SELECT "Access" FROM public."Sys_RoleFieldPerm" WHERE "Id"={q(a_facts["field"])};') == "3"
            and revisions(f, b, b_facts["actor"]) == b_before, "native A revisions did not change independently of B")

    target_path = f.root / "target.json"
    f.save(target_path.name, target.mapping())
    base = ("export", "--pg-bin", f.args.pg_bin, "--target-file", target_path, "--username", OWNER,
            "--installation", installation, "--tenant", b, "--include-original-journal", "--include-rma-source",
            "--include-rma-visibility")
    iam_flag = "--include-rma-iam-references"
    failure = archive_cli(f, archive, "v6-missing-priority", *base, iam_flag, "--output", f.root / "missing-priority")
    require(failure.returncode == 1 and b"rma-iam-reference-identity-priority-required" in failure.stderr,
            "v6 accepted missing v5 opt-in")
    b_bundle = f.root / "v6-b" / "bundle"
    v5 = f.root / "v5-b" / "bundle"
    base += ("--include-identity-priority",)
    require(archive_cli(f, archive, "v6-cli-b", *base, iam_flag, "--output", b_bundle.parent).returncode == 0,
            "normal v6 export CLI failed")
    b_manifest = assert_capture(b_bundle, b, b_facts, b_before)
    require(archive_cli(f, archive, "v6-cli-verify", "verify", "--bundle", b_bundle, "--tenant", b).returncode == 0,
            "normal v6 verify CLI failed")
    require(archive_cli(f, archive, "v5-cli-b", *base, "--output", v5.parent).returncode == 0, "normal v5 CLI failed")
    previous = archive.verify_bundle(v5, b)
    require(b_manifest["members"].keys() == previous["members"].keys() | {iam_v6.MEMBER}
            and all(b_manifest["members"][name] == data for name, data in previous["members"].items() if name.endswith(".csv")),
            "prior v5 member bytes changed when enabling v6")
    broken = f.root / "v6-missing-iam"
    shutil.copytree(a_bundle, broken)
    (broken / iam_v6.MEMBER).unlink()
    rejected = archive_cli(f, archive, "v6-missing-iam", "verify", "--bundle", broken, "--tenant", a)
    require(rejected.returncode == 1 and b"bundle-member-mismatch" in rejected.stderr, "v6 accepted a missing IAM member")
    versions = (archive.FORMAT, archive.FORMAT_WITH_JOURNAL, source_v3.FORMAT, visibility_v4.FORMAT, identity_priority.FORMAT)
    for index, expected in enumerate(versions, 1):
        old = getattr(f.args, f"v{index}_bundle")
        tenant = json.loads((old / "manifest.json").read_text(encoding="utf-8"))["tenantId"]
        verified = archive_cli(f, archive, f"v{index}-original-verify", "verify", "--bundle", old, "--tenant", tenant)
        require(verified.returncode == 0 and json.loads(verified.stdout)["format"] == expected, "prior bundle rejected")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    paths = ("root", "pg-bin", "dotnet", "cli", "journal-cli", "webapi", *(f"v{i}-bundle" for i in range(1, 6)))
    for name in paths:
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    for name in paths:
        key = name.replace("-", "_")
        setattr(args, key, getattr(args, key).resolve(strict=True))
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "rma-iam-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    artifacts = {name: hashlib.sha256(getattr(args, name).read_bytes()).hexdigest() for name in ("webapi", "cli", "journal_cli")}
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source, "Sha256": artifacts})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("real Core v6 historical IAM cut, A/B and v1-v5 compatibility passed", flush=True)


if __name__ == "__main__":
    main()
