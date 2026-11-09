#!/usr/bin/env python3
"""Verify the same-cut FileContent asset inventory on one real Core PostgreSQL."""

import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
from verify_identity_priority import DATABASE, cli, seed
from verify import BASE_ENV, FLAGS, OWNER, Fixture, q, require, token
from verify_commission import expected_histories

import archive
import file_content_assets as assets


EARLY_BYTES = b"\x00\xff\r\nW389 immutable original content A"
OTHER_BYTES = b"\x00\x81\r\nW389 immutable original content B"
INSPECTION = dict(status="CLEAN", provider="W389-fixture", engineVersion="1", signatureVersion="1",
                  evidenceDigest="a" * 64, checkedAt="2026-11-09T11:00:00Z", reason="fixture")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def register_parent(f, tenant, parent, actor):
    candidate = token()
    original = dict(tenantId=tenant, id=parent, version="1", etag=token(), creatorId=actor,
                    candidateId=candidate, code="W389-ASSET", draft={})
    body = json.dumps(original, separators=(",", ":")).encode("utf-8")
    f.sql(DATABASE, """
        BEGIN;
        SELECT cp6_authentication_v1.acquire();
        INSERT INTO public."Sys_Tenants"("Id","TenantCode","TenantName","Enable","TwoFactorMode","CreateDate")
          VALUES ({tenant},{code},'W389 Fixture',true,0,localtimestamp) ON CONFLICT ("Id") DO NOTHING;
        INSERT INTO cp6_engineering_revision_v1.heads(tenant_id,revision_id,code,creator_id,candidate_id,revision)
          VALUES ({tenant},{parent},'W389-ASSET',{actor},{candidate},1);
        INSERT INTO cp6_engineering_revision_v1.working_versions(tenant_id,revision_id,revision,body_bytes,body_digest,actor_id)
          VALUES ({tenant},{parent},1,decode({body},'hex'),{digest},{actor});
        COMMIT;
    """.format(tenant=q(tenant), code=q("w389-" + tenant[:8]), parent=q(parent), actor=q(actor), candidate=q(candidate),
               body=q(body.hex()), digest=q(digest(body))))
    latest = json.dumps({**original, "version": "2", "parentVersion": "1", "etag": token()},
                        separators=(",", ":")).encode("utf-8")
    f.sql(DATABASE, """
        BEGIN;
        UPDATE cp6_engineering_revision_v1.heads SET revision=2 WHERE tenant_id={tenant} AND revision_id={parent};
        INSERT INTO cp6_engineering_revision_v1.working_versions(tenant_id,revision_id,revision,body_bytes,body_digest,actor_id)
          VALUES ({tenant},{parent},2,decode({body},'hex'),{digest},{actor});
        COMMIT;
    """.format(tenant=q(tenant), parent=q(parent), actor=q(actor), body=q(latest.hex()), digest=q(digest(latest))))
    return digest(body)


def event(tenant, file_id, actor, revision, etag, action, held):
    value = dict(tenantId=tenant, id=file_id, version=str(revision), etag=etag, inspection=INSPECTION, held=held,
                 actorId=actor, recordedAt="2026-11-09T11:00:00Z", action=action, reason="W389 fixture")
    body = json.dumps(value, separators=(",", ":")).encode("utf-8")
    statement = """
        INSERT INTO cp6_file_content_v1.events(tenant_id,file_id,revision,body_bytes,body_digest,actor_id)
          VALUES ({tenant},{file_id},{revision},decode({body},'hex'),{digest},{actor});
    """.format(tenant=q(tenant), file_id=q(file_id), actor=q(actor), revision=revision, body=q(body.hex()), digest=q(digest(body)))
    return statement, digest(body)


def file_content(f, tenant, file_id, actor, parent, parent_digest, key, body):
    etag = token()
    state, state_digest = event(tenant, file_id, actor, 1, etag, "UPLOAD", False)
    f.sql(DATABASE, """
        BEGIN;
        INSERT INTO cp6_file_content_v1.contents(tenant_id,file_id,creator_id,parent_id,parent_version,parent_digest,
            file_name,digest,byte_length,media_type,store_path)
          VALUES ({tenant},{file_id},{actor},{parent},'1',{parent_digest},'W389-é.bin',{digest},{size},
                  'application/octet-stream',{key});
        INSERT INTO cp6_file_content_v1.heads(tenant_id,file_id,revision) VALUES ({tenant},{file_id},1);
    """.format(tenant=q(tenant), file_id=q(file_id), actor=q(actor), parent=q(parent), parent_digest=q(parent_digest),
               digest=q(digest(body)), size=len(body), key=q(key)) + state + "COMMIT;")
    return etag, state_digest


def store(root, key, body):
    file = root.joinpath(*key.split("/"))
    file.parent.mkdir(parents=True, exist_ok=True)
    file.write_bytes(body)
    return file


def read_member(bundle):
    with (bundle / assets.MEMBER).open(encoding="utf-8", newline="") as stream:
        return list(csv.DictReader(stream))


def exercise(f):
    commission = seed(f)
    a, b = (row["TenantId"] for row in commission["Tenants"])
    installation = commission["InstallationId"]
    target = archive.PhysicalTarget.from_mapping(commission["Target"])
    f.save("archive-target.json", target.mapping())
    file_id, parent, actor, leaf = (token() for _ in range(4))
    a_parent = register_parent(f, a, parent, actor)
    b_parent = register_parent(f, b, parent, actor)
    a_key, b_key = ("native-content-v1/{}/{}".format(tenant.replace("-", ""), leaf.replace("-", "")) for tenant in (a, b))
    root = f.root / "private-local-store"
    archive.private_directory(root)
    flags = ("--include-original-journal", "--include-rma-source", "--include-rma-visibility",
             "--include-identity-priority", "--include-rma-iam-references")

    def export_cli(label, expected=0, error=None, *, tenant=a, local_root=root, with_assets=True):
        output = f.root / label
        arguments = (*flags, "--file-content-root", local_root) if with_assets else flags
        result = cli(f, archive, label, OWNER, "export", "--pg-bin", f.args.pg_bin, "--target-file", f.root / "archive-target.json",
                     "--output", output, "--username", OWNER, "--installation", installation, "--tenant", tenant, *arguments)
        bundle = output / "bundle"
        require(result.returncode == expected, label + " returned unexpected exit")
        if error:
            require(error.encode() in result.stderr and not (bundle / "manifest.json").exists(), label + " did not fail closed")
        else:
            require(not result.stderr and (bundle / "manifest.json").is_file(), label + " did not publish a manifest")
        return bundle

    def verify_cli(label, bundle, *args, expected=0, error=None, tenant=a):
        result = cli(f, archive, label, None, "verify", "--bundle", bundle, "--tenant", tenant, *args)
        require(result.returncode == expected and (error.encode() in result.stderr if error else not result.stderr),
                label + " returned unexpected result")
        return json.loads(result.stdout) if expected == 0 else None

    empty = export_cli("v7-empty")
    empty_manifest = json.loads((empty / "manifest.json").read_bytes())
    require(read_member(empty) == [] and empty_manifest["fileContentAssets"] == assets.manifest_section(0),
            "the selected empty FileContent slice was omitted")
    a_state, a_state_digest = file_content(f, a, file_id, actor, parent, a_parent, a_key, EARLY_BYTES)
    file_content(f, b, file_id, actor, parent, b_parent, b_key, OTHER_BYTES)
    original = store(root, a_key, EARLY_BYTES)
    orphan_key = "native-content-v1/{}/{}".format(a.replace("-", ""), token().replace("-", ""))
    store(root, orphan_key, b"unreferenced immutable staging")
    # A must not require B's physical bytes, even with the same native FileId and storage leaf.
    require(not root.joinpath(*b_key.split("/")).exists(), "B physical file must be absent during A's export")

    def after_cut(snapshot):
        next_etag = token()
        state, latest_digest = event(a, file_id, actor, 2, next_etag, "HOLD", True)
        with f.psql(DATABASE, app="W389-file-head-after-cut") as writer:
            statement = """
                BEGIN;
                UPDATE cp6_file_content_v1.heads SET revision=2 WHERE tenant_id={} AND file_id={};
            """.format(q(a), q(file_id)) + state + "COMMIT; SELECT pg_backend_pid();"
            out, err = f.collect(writer, statement.encode("utf-8"))
            require(writer.returncode == 0 and not err, "A current file head did not advance after the cut")
            f.save("after-cut.json", {"SnapshotId": snapshot, "WriterBackendPid": int(out), "StateEtag": next_etag,
                                      "StateDigest": latest_digest})
        new_b = token()
        b_new_key = "native-content-v1/{}/{}".format(b.replace("-", ""), token().replace("-", ""))
        file_content(f, b, new_b, actor, parent, b_parent, b_new_key, OTHER_BYTES)
        store(root, b_new_key, OTHER_BYTES)

    cut = archive.export_bundle(f.args.pg_bin, target, OWNER, installation, a, f.root / "cut",
                                include_original_journal=True, include_rma_source=True, include_rma_visibility=True,
                                include_identity_priority=True, include_rma_iam_references=True, file_content_root=root,
                                environ={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, on_snapshot=after_cut)
    row = dict(tenant_id=a, file_id=file_id, parent_id=parent, parent_version="1", parent_digest=a_parent,
               file_name="W389-é.bin", digest=digest(EARLY_BYTES), byte_length=str(len(EARLY_BYTES)),
               media_type="application/octet-stream", store_path=a_key, state_version="1", state_etag=a_state, state_digest=a_state_digest)
    require(read_member(cut) == [row], "the file/parent/reference bytes were not bound to the original cut")
    manifest = json.loads((cut / "manifest.json").read_bytes())
    require(manifest["format"] == assets.FORMAT and manifest["recoveryReady"] is False
            and manifest["fileContentAssets"] == assets.manifest_section(1), "v7 manifest metadata changed")
    raw_manifest = (cut / "manifest.json").read_bytes()
    require(a_key.encode() not in raw_manifest and str(root).encode() not in raw_manifest
            and b_key.encode() not in (cut / assets.MEMBER).read_bytes() and original.read_bytes() == EARLY_BYTES,
            "private root, other tenant, or external bytes escaped their original boundary")

    normal = export_cli("v7-current")
    advanced = json.loads((f.root / "after-cut.json").read_bytes())
    require(read_member(normal) == [{**row, "state_version": "2", "state_etag": advanced["StateEtag"],
                                     "state_digest": advanced["StateDigest"]}], "normal v7 did not see current head and original bytes")
    verified = verify_cli("v7-verify", normal, "--file-content-root", root)
    require(verified["format"] == assets.FORMAT, "normal verification did not validate v7")
    verify_cli("v7-missing-root", normal, expected=1, error="file-content-root-required")
    prior = export_cli("v6-current", with_assets=False)
    require(verify_cli("v6-verify", prior)["format"] == "cp6-rma-source-archive-v6"
            and not (prior / assets.MEMBER).exists(), "the old opt-in format changed")

    original.unlink()
    export_cli("missing-asset", 1, "file-content-asset-not-found")
    original.write_bytes(b"x")
    export_cli("wrong-length", 1, "file-content-asset-length-mismatch")
    original.write_bytes(b"x" * len(EARLY_BYTES))
    export_cli("wrong-asset", 1, "file-content-asset-digest-mismatch")
    verify_cli("v7-verify-wrong-asset", normal, "--file-content-root", root,
               expected=1, error="file-content-asset-digest-mismatch")
    original.write_bytes(EARLY_BYTES)

    alias_root = f.root / "alias-local-store"
    (alias_root / "native-content-v1").mkdir(parents=True)
    alias = alias_root / "native-content-v1" / a.replace("-", "")
    if archive.WINDOWS:
        created = subprocess.run(["cmd", "/d", "/c", "mklink", "/J", str(alias), str(original.parent)],
                                 capture_output=True, creationflags=FLAGS, timeout=10)
        require(created.returncode == 0, "real junction fixture could not be created")
    else:
        alias.symlink_to(original.parent, target_is_directory=True)
    try:
        export_cli("linked-asset", 1, "file-content-asset-path-rejected", local_root=alias_root)
    finally:
        if archive.WINDOWS:
            alias.rmdir()
        else:
            alias.unlink()

    store(root, b_key, OTHER_BYTES)
    other_bundle = export_cli("v7-b", tenant=b)
    other_rows = read_member(other_bundle)
    require(len(other_rows) == 2 and {r["tenant_id"] for r in other_rows} == {b}
            and all(r["digest"] == digest(OTHER_BYTES) for r in other_rows), "B did not retain its isolated originals")

    file_content(f, b, token(), actor, parent, b_parent, "../" + a_key, OTHER_BYTES)
    export_cli("bad-store-key", 1, "file-content-asset-key-rejected", tenant=b)
    file_content(f, a, token(), actor, parent, "0" * 64, orphan_key, EARLY_BYTES)
    export_cli("bad-parent-reference", 1, "file-content-reference-count-mismatch")
    f.save("evidence-summary.json", {"PinnedManifest": archive.file_digest(cut / "manifest.json"),
                                     "PinnedMember": archive.file_digest(cut / assets.MEMBER),
                                     "CurrentMember": archive.file_digest(normal / assets.MEMBER)})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("root", "pg-bin", "dotnet", "cli", "journal-cli", "webapi"):
        parser.add_argument("--" + name, type=lambda path: Path(path).resolve(strict=True), required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--webapi-source", required=True)
    args = parser.parse_args()
    args.expected = expected_histories(args.webapi_source)
    fixture = Fixture(args, "file-content-assets-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + token()[:8])
    fixture.save("inputs.json", {"DatabaseInitSource": args.webapi_source, "Sha256": {
        name: digest(getattr(args, name).read_bytes()) for name in ("webapi", "cli", "journal_cli")}})
    print("evidence:", fixture.root, flush=True)
    with fixture.running():
        exercise(fixture)
    print("real FileContent asset cut, normal CLI, exact bytes and traversal rejection passed", flush=True)


if __name__ == "__main__":
    main()
