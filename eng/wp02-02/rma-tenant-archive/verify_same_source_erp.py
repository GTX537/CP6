#!/usr/bin/env python3
"""Exercise the sealed C03 driver and a normal v8 cut on the exact W420 source."""
import argparse
import csv
import io
import json
from pathlib import Path
import re
import sys
from types import SimpleNamespace
from uuid import uuid4

sys.dont_write_bytecode = True
import archive
import file_content_history as history
import verify_erp_owner_cut as c03
import verify_same_source_file_content as original

TENANTS = (original.A, original.B)
Q = archive.literal
ASSETS = history.assets
V8 = c03.V10[:-2]
PROOF_PIN = "d7559baf72de403bdef9afa03a8e42de4eb1f6859746b9798da442b619df02cf"


def protect(args, label, sealed):
    scope = args.output / label
    archive.private_directory(scope)
    native = SimpleNamespace(pg_bin=args.pg_bin, data=args.data, output=scope)
    original.original_state(native)
    expected = ["guard", "all-originals"] + [
        f"{side}-{kind}" for side in ("A", "B")
        for kind in ("heads", "versions", "migration", "outbounds", "rma-originals")]
    for name in expected:
        file = name + ".stdout"
        c03.require((scope / file).read_bytes() == (args.file_proof / file).read_bytes(),
                    name + " differs from the W420 original")
    for side, tenant in zip(("A", "B"), TENANTS):
        bundle = args.file_proof / (side + "-history/bundle")
        pin = sealed["FileContentBundles"][side]["Pin"]
        manifest = history.verify_bundle(bundle, archive.guid(tenant), pin, args.asset_root)
        c03.require(manifest["source"] == original.TARGET.mapping(), "Wrong FileContent source")
        for key, rows in history.rows_sql(tenant).items():
            name = history.filename(key)
            member = history.MEMBERS[key]
            actual = original.copy(native, side + "-" + key, rows + " ORDER BY " + ", ".join(member.key))
            c03.require(manifest["members"][name]["rows"] == 1 and actual == (bundle / name).read_bytes(),
                        f"Original {side} {name} changed")
    return original.psql(native, "business-partners", f"""
        SELECT json_build_array(count(*),encode(sha256(convert_to(
            coalesce(jsonb_agg(to_jsonb(r) ORDER BY to_jsonb(r)),'[]'::jsonb)::text,'UTF8')),'hex'))
        FROM public."T_WebBusinessPartner" r WHERE "TenantId" IN ({Q(TENANTS[0])}::uuid,{Q(TENANTS[1])}::uuid);
    """)


def ordinary(fixture):
    evidence = {}
    for side, tenant, other, role in zip(("A", "B"), TENANTS, reversed(TENANTS), original.ROLES):
        quoted_role = '"' + role.replace('"', '""') + '"'
        local, foreign = map(original.schema, (tenant, other))
        evidence[side] = value = json.loads(original.psql(fixture.args, side + "-ordinary-rma", f"""
            BEGIN READ ONLY;
            SET LOCAL SESSION AUTHORIZATION {quoted_role};
            SET LOCAL row_security=on;
            SELECT json_build_object('role',current_user,'login',session_user,'tenant',cp6_tenancy_v1.current_tenant_id(),
              'heads',(SELECT count(*) FROM {local}.heads),
              'versions',(SELECT count(*) FROM {local}.versions),
              'ownOriginals',(SELECT count(*) FROM {archive.JOURNAL_TABLE} WHERE owner={Q(archive.JOURNAL_OWNER)}
                AND tenant_id={Q(tenant)}::uuid),
              'foreignOriginals',(SELECT count(*) FROM {archive.JOURNAL_TABLE} WHERE owner={Q(archive.JOURNAL_OWNER)}
                AND tenant_id={Q(other)}::uuid),
              'foreignHeads',has_table_privilege(current_user,
                (SELECT c.oid FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                 WHERE n.nspname={Q(foreign)} AND c.relname='heads'),'SELECT'));
            COMMIT;
        """))
        c03.require(value == dict(role=role, login=role, tenant=tenant, heads=1, versions=2,
                                  ownOriginals=2, foreignOriginals=0, foreignHeads=False),
                    f"Original {side} effective ordinary access changed")
    return evidence


def same_snapshot(log, manifest):
    snapshot = manifest["snapshot"]["snapshotId"]
    expected = {"pg-dump", "source-headers-copy", "source-details-copy", "source-lineage-copy",
                "journal-copy", "visibility-copy", "iam-references-copy", "file-content-assets-copy"}
    expected.update(Path(name).stem + "-copy" for name in (*archive.identity_priority.MEMBERS, *c03.erp.MEMBERS))
    reads = re.findall(r"cp6-rma-archive-(\S+) xid=\d+ .*statement: SET TRANSACTION SNAPSHOT '([^']+)'",
                       log.read_text(encoding="utf-8", errors="replace"))
    c03.require(len(reads) == len(expected) and dict(reads) == dict.fromkeys(expected, snapshot),
                "Native RMA/IAM/FileContent/ERP importers did not share the exact holder")
    return sorted(expected)


def execute(args, invoke=c03.child):
    sealed_file = args.file_proof / "result.json"
    c03.require(archive.file_digest(sealed_file)["sha256"] == PROOF_PIN, "The exact W420 result is required")
    sealed = json.loads(sealed_file.read_bytes())
    args.tenant_a, args.tenant_b = TENANTS
    args.username = original.OWNER
    archive.private_directory(args.output)
    f = c03.Fixture(args, original.TARGET, args.output)
    partners = protect(args, "protected-before", sealed)
    roles = ordinary(f)
    for side, tenant in zip(("A", "B"), TENANTS):
        c03.native_rows(f.snapshot_erp("before-" + side, tenant), expect_empty=True)
    request, account = str(uuid4()), str(uuid4())
    input_file = args.output / "driver-input.json"
    c03.save(input_file, dict(DataDirectory=args.data.as_posix(), Database=f.target.database, Port=f.target.port,
        DatabaseOid=f.target.database_oid, SystemIdentifier=f.target.system_identifier, Username=args.username,
        BinaryRoot=args.sealed_root.resolve(strict=True).as_posix(), Installation=original.INSTALLATION,
        TenantA=TENANTS[0], TenantB=TENANTS[1], RequestId=request, AccountId=account,
        ModulePins=json.loads(args.module_pins.read_bytes())))
    managed = json.loads(invoke(args.output, "erp-owner", [args.dotnet, "exec", "--runtimeconfig",
        args.runtime_config, "--depsfile", args.sealed_root / "CP6.Tests.deps.json", args.driver, input_file], f.env))
    c03.require((managed["RequestId"], managed["AccountId"], managed["RequestVersion"], managed["AggregateVersion"])
                == (request, account, 3, 7), "The exact sealed C03 request and versions are required")
    samples = {side: f.snapshot_erp("native-" + side, tenant) for side, tenant in zip(("A", "B"), TENANTS)}
    facts = {side: c03.check_erp(c03.native_rows(samples[side]), tenant, managed[side], request, account)
             for side, tenant in zip(("A", "B"), TENANTS)}
    c03.require(all(facts["A"][key] != facts["B"][key] for key in ("command", "result")),
                "The tenant-local originals collided globally")
    target_file = args.output / "target.json"
    c03.save(target_file, f.target.mapping())
    cli = [sys.executable, "-B", c03.HERE / "archive.py"]
    bundle = args.output / "v8-cut/bundle"
    options = ["--tenant", TENANTS[0], "--file-content-root", args.asset_root]
    c03.child(args.output, "archive-export", [*cli, "export", "--pg-bin", args.pg_bin, "--target-file", target_file,
        "--username", args.username, "--installation", original.INSTALLATION, "--output", bundle.parent,
        *options, *V8], f.env, timeout=90)
    c03.child(args.output, "archive-verify", [*cli, "verify", "--bundle", bundle, *options], f.env)
    manifest = json.loads((bundle / "manifest.json").read_bytes())
    c03.require(manifest["format"] == c03.erp.FORMAT and manifest["source"] == f.target.mapping()
                and manifest["installationId"] == original.INSTALLATION and manifest["tenantId"] == TENANTS[0]
                and manifest["fileContentAssets"]["count"] == 1 and manifest["recoveryReady"] is False,
                "The normal v8 nonempty asset cut differs")
    for table in c03.erp.TABLES:
        c03.require((bundle / table.member).read_bytes() == samples["A"][table.name],
                    table.name + " differs from independent native A COPY")
    file_projection = original.copy(args, "native-A-asset", ASSETS.rows_sql(TENANTS[0]))
    c03.require((bundle / ASSETS.MEMBER).read_bytes() == file_projection, "A asset differs from independent native COPY")
    assets = list(csv.DictReader(io.StringIO(file_projection.decode("utf-8"), newline="")))
    upload = sealed["SourceOwners"]["A"]["Upload"]
    c03.require(len(assets) == 1 and assets[0]["file_id"] == upload["FileId"]
                and assets[0]["digest"] == upload["Digest"] and assets[0]["parent_digest"] == upload["Parent"]["Digest"],
                "The v8 asset no longer identifies W420's exact file and parent")
    c03.require((bundle / archive.JOURNAL_MEMBER).read_bytes() == (args.file_proof / "A-rma-originals.stdout").read_bytes(),
                "The v8 RMA journal differs from the original W372/W420 bytes")
    imports = same_snapshot(args.postgres_log, manifest)
    c03.require(samples["B"] == f.snapshot_erp("after-cut-B", TENANTS[1])
                and partners == protect(args, "protected-after", sealed), "B or protected Owner facts changed")
    result = dict(source=f.target.mapping(), installation=original.INSTALLATION, tenants=TENANTS, request=request,
                  account=account, duplicateSha256=managed["DuplicateSnapshotSha256"], backend=managed["Backend"],
                  native=facts, roles=roles, bundle=str(bundle), pin=archive.file_digest(bundle / "manifest.json"),
                  snapshot=manifest["snapshot"], imports=imports, asset=assets[0], recoveryReady=False)
    c03.save(args.output / "actual.json", result)
    print("W423 PASS: original A/B C03 and duplicate semantics; actual v8 same-holder FileContent reference and ERP.")
    return result


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    for field in ("pg-bin", "data", "file-proof", "asset-root", "postgres-log", "output", "dotnet", "driver",
                  "sealed-root", "runtime-config", "module-pins"):
        result.add_argument("--" + field, type=Path, required=True)
    return result


if __name__ == "__main__":
    execute(parser().parse_args())
