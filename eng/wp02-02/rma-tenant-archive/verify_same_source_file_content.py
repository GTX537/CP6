#!/usr/bin/env python3
"""Compare W372 originals and actual FileContent owner uploads in one private source clone."""

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import file_content_history as history

A = "565d8f3c-75a5-4e2a-bef1-e410c3fd4a10"
B = "b37b657c-cc72-427e-9a57-e4066fd3f726"
INSTALLATION = "2ad5d2ef-54c4-4abd-9bf9-5d2b0a1a2cc4"
OWNER = "w350_owner"
ROLES = ("w350_a", 'w350 Tenant B"ID')
TARGET = archive.PhysicalTarget("127.0.0.1", 55567, "w372_live", 16386, "7706117870886737900")
PIN = "3aa7b1c19332be13d0c0d5e04ba92f20717bca84d0abeca832e1317f93a885cc"
ENGINEERING_PRODUCT = "Main.Plm.EngineeringProduct.v1"
ENV = {key: value for key, value in os.environ.items() if not key.upper().startswith("PG")}
Q = archive.literal


def require(value, reason):
    if not value:
        raise RuntimeError(reason)


def schema(tenant):
    return "t_" + tenant.replace("-", "")


def psql(args, name, statement):
    options = ["-XqAtw", "-v", "ON_ERROR_STOP=1", *archive.connection_args(TARGET, OWNER)]
    archive.run_native(args.pg_bin, "psql", options, ENV, args.output, name, sql=statement)
    return (args.output / (name + ".stdout")).read_bytes()


def copy(args, name, select, kind="csv"):
    fmt = "FORMAT csv, HEADER true" if kind == "csv" else "FORMAT text"
    return psql(args, name, f"""
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SET LOCAL search_path=pg_catalog;
        {archive.JOURNAL_COPY_SETTINGS}
        COPY ({select}) TO STDOUT WITH ({fmt});
        COMMIT;
    """)


def guard(args):
    live = json.loads(psql(args, "guard", f"""
        SELECT json_build_object('target',json_build_object('Server',host(inet_server_addr()),
                'Port',inet_server_port(),'Database',d.datname,'DatabaseOid',d.oid::bigint,
                'SystemIdentifier',p.system_identifier::text),'owner',pg_get_userbyid(d.datdba),'user',current_user,
                'path',current_setting('data_directory'),'marker',coalesce(shobj_description(d.oid,'pg_database'),''),
                'installation',(SELECT request_json::jsonb FROM cp6_rma_core_commission_v1.installation WHERE singleton),
                'roles',(SELECT jsonb_agg(jsonb_build_array(oid::bigint,rolname,rolcanlogin,rolsuper,rolbypassrls) ORDER BY rolname)
                    FROM pg_roles WHERE rolname IN ({Q(ROLES[0])},{Q(ROLES[1])})),
                'bindings',(SELECT jsonb_object_agg(tenant_id::text,jsonb_build_array(role_oid::bigint,role_name))
                    FROM cp6_tenancy_v1.login_tenant WHERE tenant_id IN ({Q(A)}::uuid,{Q(B)}::uuid)))
        FROM pg_database d CROSS JOIN pg_control_system() p WHERE d.datname=current_database();
    """))
    require(live["target"] == TARGET.mapping() and live["owner"] == live["user"] == OWNER
            and Path(live["path"]).resolve(strict=True) == args.data.resolve(strict=True)
            and live["marker"] == "" and live["installation"]["InstallationId"] == INSTALLATION
            and live["installation"]["Target"] == TARGET.mapping()
            and {item["TenantId"]: item["Login"] for item in live["installation"]["Tenants"]} == dict(zip((A, B), ROLES))
            and {row[1] for row in live["roles"]} == set(ROLES)
            and live["bindings"] == {tenant: [next(row[0] for row in live["roles"] if row[1] == role), role]
                                     for tenant, role in zip((A, B), ROLES)}
            and all(row[2:] == [True, False, False] for row in live["roles"]),
            "Wrong exact physical source, installation or A/B ordinary roles.")
    return live


def original_rma(args):
    original = archive.verify_bundle(args.sealed_rma, A)
    require(archive.file_digest(args.sealed_rma / "manifest.json")["sha256"] == PIN
            and original["format"] == archive.FORMAT_WITH_JOURNAL and original["source"] == TARGET.mapping()
            and original["installationId"] == INSTALLATION, "The W372 RMA v2 input is not the exact sealed original.")
    output = args.output / "sealed-rma-tenant-data.sql"
    archive.run_native(args.pg_bin, "pg_restore", ["--data-only", "--no-owner", "--no-acl", "--schema=" + schema(A),
        "--table=heads", "--table=versions", "--file=-", str(args.sealed_rma / "rma.pgcustom")],
        ENV, args.output, "sealed-rma-restore", output=output)
    lines = output.read_bytes().splitlines(keepends=True)
    copies = {}
    for i, line in enumerate(lines):
        if line.startswith(b"COPY "):
            relation = line.split(b" ", 2)[1].decode("ascii")
            prefix, name = relation.split(".", 1)
            require(prefix == schema(A) and name not in copies, "Unexpected native W372 COPY target.")
            end = next(j for j in range(i + 1, len(lines)) if lines[j].rstrip(b"\r\n") == b"\\.")
            copies[name] = b"".join(lines[i + 1:end])
    require(set(copies) == {"heads", "versions"}, "Unexpected W372 native RMA COPY sections.")
    return copies


def original_state(args):
    live = guard(args)
    for letter, tenant in zip(("A", "B"), (A, B)):
        ns = schema(tenant)
        copy(args, letter + "-heads", f"SELECT tenant_id,id,actor_id,outbound_id,version FROM {ns}.heads ORDER BY id", "text")
        copy(args, letter + "-versions", f"SELECT tenant_id,id,version,state,payload FROM {ns}.versions ORDER BY id,version", "text")
        copy(args, letter + "-migration", f"SELECT * FROM {ns}.{archive.HISTORY} ORDER BY tenant_id,lineage")
        copy(args, letter + "-outbounds", archive.outbounds_sql(tenant, ns))
        copy(args, letter + "-rma-originals", archive.original_journal_rows(tenant) + " ORDER BY operation_id")
    copy(args, "all-originals", f"SELECT {', '.join(archive.JOURNAL_COLUMNS)} FROM {archive.JOURNAL_TABLE}"
         " ORDER BY tenant_id,owner COLLATE \"C\",operation_id", "text")
    return live


def rows(path):
    result = {}
    for line in path.read_bytes().splitlines(keepends=True):
        key = tuple(part.decode("ascii") for part in line.split(b"\t", 3)[:3])
        require(len(key) == 3 and key not in result, "Unexpected native original key.")
        result[key] = line
    return result


def verify_baseline(args):
    expected = original_rma(args)
    original_state(args)
    for name, raw in expected.items():
        require((args.output / ("A-" + name + ".stdout")).read_bytes() == raw, f"Original W372 RMA {name} changed.")
    for captured, sealed in (("A-rma-originals", archive.JOURNAL_MEMBER), ("A-outbounds", "outbounds.csv")):
        require((args.output / (captured + ".stdout")).read_bytes() == (args.sealed_rma / sealed).read_bytes(),
                f"Original W372 RMA {sealed} changed.")
    for letter in ("A", "B"):
        require(len((args.output / (letter + "-heads.stdout")).read_bytes().splitlines()) == 1
                and len((args.output / (letter + "-versions.stdout")).read_bytes().splitlines()) == 2,
                f"W372 tenant {letter} needs one actual head and both versions.")
    print("W420 baseline matches exact sealed W372 A RMA v2 and captures current B and other originals.")


def invoke(args, label, command, expected=0):
    with (args.output / (label + ".stdout")).open("xb") as out, (args.output / (label + ".stderr")).open("xb") as err:
        process = subprocess.run([sys.executable, "-B", history.__file__, *map(str, command)], stdout=out, stderr=err,
                                 env=ENV, timeout=100, creationflags=archive.FLAGS)
    require(process.returncode == expected, f"{label} exit {process.returncode}, expected {expected}.")
    return (args.output / (label + ".stdout")).read_bytes()


def verify_file(args, letter, original):
    tenant, upload = original["Tenant"], original["Upload"]
    parent = upload["Parent"]
    options = ["--tenant", tenant, "--file-content-root", args.asset_root]
    exported = json.loads(invoke(args, letter + "-history-export", ["export", *options, "--pg-bin", args.pg_bin,
        "--target-file", args.target_file, "--username", OWNER, "--output", args.output / (letter + "-history")]))
    bundle, pin = Path(exported["bundle"]), exported["manifestSha256"]
    command = ["verify", "--bundle", bundle, "--manifest-sha256", pin]
    verified = json.loads(invoke(args, letter + "-history-verify", [*command, *options]))
    require(verified["manifestSha256"] == pin, "FileContent CLI did not confirm the exact exported pin.")
    if letter == "A":
        invoke(args, "A-history-as-B", [*command, "--tenant", B, "--file-content-root", args.asset_root], expected=1)
        require(b"file-content-history-manifest-rejected" in (args.output / "A-history-as-B.stderr").read_bytes(),
                "Another tenant's exact bundle was not rejected at the tenant boundary.")
    manifest = json.loads((bundle / "manifest.json").read_bytes())
    require(manifest["source"] == TARGET.mapping() and manifest["recoveryReady"] is False,
            "Exported FileContent bundle physical identity differs.")
    file_id, operation = Q(archive.guid(upload["FileId"])), Q(archive.guid(upload["OperationId"]))
    exact = f"tenant_id={Q(tenant)}::uuid"
    predicates = {
        "contents": exact + f" AND file_id={file_id}::uuid",
        "heads": exact + f" AND file_id={file_id}::uuid",
        "events": exact + f" AND file_id={file_id}::uuid",
        "operations": exact + f" AND owner={Q(history.assets.OWNER)} AND operation_id={operation}::uuid",
        "parent-versions": exact + f" AND revision_id={Q(archive.guid(parent['Id']))}::uuid"
        + f" AND revision={int(parent['Version'])} AND body_digest={Q(parent['Digest'])}",
    }
    for key, member in history.MEMBERS.items():
        raw = copy(args, letter + "-native-" + key,
                   f"SELECT {', '.join(member.columns)} FROM {member.relation} WHERE {predicates[key]}"
                   " ORDER BY " + ", ".join(member.key))
        name = history.filename(key)
        require(manifest["members"][name]["rows"] == 1 and raw == (bundle / name).read_bytes(),
                f"Independent native {letter} {key} did not match the pinned member.")
    return {"Bundle": str(bundle), "Pin": pin, "Snapshot": manifest["snapshot"], "Members": manifest["members"]}


def verify_after(args):
    actual = json.loads(args.owner_result.read_bytes())
    require(actual["Native"]["Source"] == TARGET.mapping() and actual["Native"]["Installation"] == INSTALLATION
            and actual["A"]["Tenant"] == A and actual["B"]["Tenant"] == B and actual["B"]["ForeignRejected"]
            and actual["A"]["Upload"]["OperationId"] == actual["B"]["Upload"]["OperationId"]
            and actual["A"]["Upload"]["FileId"] == actual["B"]["Upload"]["FileId"]
            and actual["A"]["Upload"]["Digest"] != actual["B"]["Upload"]["Digest"], "Real A/B owner result differs.")
    live = original_state(args)
    require(live == json.loads((args.before / "guard.stdout").read_bytes()), "RMA physical/control role bindings changed.")
    for label in ("heads", "versions", "migration", "outbounds", "rma-originals"):
        for letter in ("A", "B"):
            name = f"{letter}-{label}.stdout"
            require((args.output / name).read_bytes() == (args.before / name).read_bytes(), f"Original {name} changed.")
    previous, current = (rows(directory / "all-originals.stdout") for directory in (args.before, args.output))
    added = {(item["Tenant"], owner, item[field]["OperationId"]) for item in (actual["A"], actual["B"])
             for owner, field in ((ENGINEERING_PRODUCT, "Product"), (history.assets.PARENT_OWNER, "Revision"),
                                  (history.assets.OWNER, "Receipt"))}
    require(set(current) - set(previous) == added and all(current.get(key) == raw for key, raw in previous.items()),
            "The original shared journal changed beyond the six exact new Owner commands.")
    snapshots = {letter: verify_file(args, letter, actual[letter]) for letter in ("A", "B")}
    summary = {"Source": TARGET.mapping(), "InstallationId": INSTALLATION, "OriginalRmaV2Pin": PIN,
               "UnchangedJournalRows": len(previous), "AddedJournalRows": len(added),
               "SourceOwners": actual, "FileContentBundles": snapshots}
    with (args.output / "result.json").open("x", encoding="utf-8") as out:
        json.dump(summary, out, indent=2, ensure_ascii=False)
        out.write("\n")
    print("W420 PASS: real same-source A/B uploads, five raw members each, exact pre-existing RMA and shared originals.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    before, after = actions.add_parser("before"), actions.add_parser("after")
    for action in (before, after):
        for option in ("pg-bin", "data", "output"):
            action.add_argument("--" + option, type=Path, required=True)
    before.add_argument("--sealed-rma", type=Path, required=True)
    for option in ("before", "asset-root", "owner-result", "target-file"):
        after.add_argument("--" + option, type=Path, required=True)
    args = parser.parse_args()
    archive.private_directory(args.output)
    (verify_baseline if args.action == "before" else verify_after)(args)


if __name__ == "__main__":
    main()
