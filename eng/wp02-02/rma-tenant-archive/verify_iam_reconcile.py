#!/usr/bin/env python3
"""Verify current RMA IAM reconciliation against the retained W387 native source."""

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
import archive
import iam_v6
import reconcile_iam
import visibility_v4


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def read_report(path):
    with path.open(encoding="utf-8") as stream:
        header, *rows, summary = (json.loads(line) for line in stream)
    return header, {(row["kind"], row["id"]): row for row in rows}, summary


def native_graph(pg_bin, target, username, evidence, label, tenant):
    rows = iam_v6.rows_sql(tenant, archive.original_journal_rows(tenant), visibility_v4.rows_sql(tenant))
    output = evidence / (label + ".csv")
    archive.run_native(pg_bin, "psql", ["-XqAtw", "-v", "ON_ERROR_STOP=1", *archive.connection_args(target, username)],
                       os.environ, evidence, label, output=output, sql=f"""
        BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
        SET LOCAL search_path=pg_catalog;
        SET LOCAL row_security=off;
        COPY ({rows} ORDER BY {iam_v6.ORDER}) TO STDOUT WITH (FORMAT csv, HEADER true);
        COMMIT;
    """)
    return archive.file_digest(output)


def exercise(args):
    archive.private_directory(args.evidence)
    target = archive.PhysicalTarget.from_mapping(json.loads(args.target_file.read_text(encoding="utf-8-sig")))
    bundles = {"a": (args.a_bundle, args.a_manifest_sha256), "b": (args.b_bundle, args.b_manifest_sha256)}
    tenants = {label: archive.guid(json.loads((bundle / "manifest.json").read_bytes())["tenantId"])
               for label, (bundle, _) in bundles.items()}
    before = {label: native_graph(args.pg_bin, target, args.username, args.evidence, label + "-before", tenant)
              for label, tenant in tenants.items()}

    def cli(label, tenant, bundle, pin, target_file=args.target_file, expected=0, error=None):
        output = args.evidence / label
        result = subprocess.run([sys.executable, "-B", reconcile_iam.__file__, "--pg-bin", str(args.pg_bin), "--bundle", str(bundle),
                                 "--manifest-sha256", pin, "--target-file", str(target_file), "--output", str(output),
                                 "--username", args.username, "--tenant", str(tenant)], env=os.environ, capture_output=True,
                                timeout=45, creationflags=archive.FLAGS)
        (args.evidence / (label + ".stdout")).write_bytes(result.stdout)
        (args.evidence / (label + ".stderr")).write_bytes(result.stderr)
        require(result.returncode == expected, label + " returned unexpected exit")
        if error:
            require(not result.stdout and error.encode() in result.stderr and not (output / reconcile_iam.REPORT).exists(),
                    label + " did not fail closed")
            return None
        require(not result.stderr, label + " did not return a clean report path")
        return Path(json.loads(result.stdout)["report"])

    paths = {label: cli(label, tenants[label], bundle, pin) for label, (bundle, pin) in bundles.items()}
    a_header, a, a_summary = read_report(paths["a"])
    b_header, b, b_summary = read_report(paths["b"])
    require(a_header["tenantId"] == str(tenants["a"]) and b_header["tenantId"] == str(tenants["b"])
            and a_header["original"]["manifestSha256"] == args.a_manifest_sha256
            and a_header["current"]["readOnly"] is True and a_header["current"]["isolation"] == "repeatable read"
            and a_summary["recoveryReady"] is False and b_summary["recoveryReady"] is False,
            "the reports are missing exact source and isolation evidence")
    def return_reason(rows):
        return next(row for (kind, _), row in rows.items()
                    if kind == "role_field" and row["originalFacts"]["field_name"] == "ReturnReason")
    a_field, b_field = return_reason(a), return_reason(b)
    require(a_field["decision"] == "CHANGED" and a_field["originalFacts"]["access"] == 1
            and a_field["currentFacts"]["access"] == 3 and b_field["decision"] == "UNCHANGED"
            and b_field["currentFacts"]["access"] == 2, "current field revision or the other tenant's role control was lost")
    a_role, b_role = a[("role", "72")], b[("role", "72")]
    require(a_role["originalFacts"]["revision"] != a_role["currentFacts"]["revision"]
            and b_role["originalFacts"] == b_role["currentFacts"]
            and a_role["currentFacts"]["revision"] != b_role["currentFacts"]["revision"], "same numeric role crossed tenant")
    a_tenant, b_tenant = (rows[("tenant", str(tenants[label]))] for label, rows in (("a", a), ("b", b)))
    require(a_tenant["originalFacts"]["expire_date"] != a_tenant["currentFacts"]["expire_date"]
            and a_tenant["originalFacts"]["authentication_policy_revision"] != a_tenant["currentFacts"]["authentication_policy_revision"]
            and b_tenant["originalFacts"] == b_tenant["currentFacts"], "current tenant policy facts were replaced")
    missing = next(row for (kind, _), row in a.items()
                   if kind == "participant" and not row["originalFacts"]["user_present"])
    require(missing["decision"] == "UNCHANGED" and missing["currentFacts"] == missing["originalFacts"]
            and missing["currentFacts"]["referenced_journal"] is True and missing["currentFacts"]["user_name"] is None,
            "an absent historical actor was turned into a current identity")
    require(str(tenants["b"]).encode() not in paths["a"].read_bytes() and b"RMA-Actor-B" not in paths["a"].read_bytes(),
            "the report exposed the other tenant's identity facts")

    # Exercise both streaming one-sided outcomes using unchanged native facts from this same fixture.
    old_key = (missing["kind"], missing["id"])
    live_key = (a_field["kind"], a_field["id"])
    unknown, current_only = list(reconcile_iam.merge(iter([(old_key, missing["originalFacts"])]),
                                                     iter([(live_key, a_field["currentFacts"])])))
    require(unknown["decision"] == "UNKNOWN" and unknown["currentFacts"] is None
            and current_only["decision"] == "CURRENT_ONLY" and current_only["currentFacts"] == a_field["currentFacts"],
            "one-sided graph facts were interpreted as grants or lost")

    cli("wrong-pin", tenants["a"], args.a_bundle, "0" * 64, expected=1, error="manifest-digest-mismatch")
    wrong = {**target.mapping(), "DatabaseOid": target.database_oid + 1}
    wrong_target = args.evidence / "wrong-target.json"
    wrong_target.write_text(json.dumps(wrong), encoding="utf-8")
    cli("wrong-target", tenants["a"], args.a_bundle, args.a_manifest_sha256, wrong_target,
        expected=1, error="source-target-mismatch")
    wrong_bundle = args.evidence / "wrong-physical-bundle"
    shutil.copytree(args.a_bundle, wrong_bundle)
    manifest_path = wrong_bundle / "manifest.json"
    manifest = json.loads(manifest_path.read_bytes())
    manifest["source"] = wrong
    manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
    cli("wrong-physical", tenants["a"], wrong_bundle, archive.file_digest(manifest_path)["sha256"], wrong_target,
        expected=1, error="target-identity-rejected")

    after = {label: native_graph(args.pg_bin, target, args.username, args.evidence, label + "-after", tenant)
             for label, tenant in tenants.items()}
    require(before == after, "actual A or B native IAM graph changed during read-only reconciliation")
    (args.evidence / "evidence-summary.json").write_text(json.dumps({"NativeBefore": before, "NativeAfter": after,
        "CurrentReports": {label: archive.file_digest(path) for label, path in paths.items()}}, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("pg-bin", "target-file", "a-bundle", "b-bundle", "evidence"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("username", "a-manifest-sha256", "b-manifest-sha256"):
        parser.add_argument("--" + name, required=True)
    exercise(parser.parse_args())
    print("native current IAM revisions, absent original participant and isolated A/B graphs passed", flush=True)


if __name__ == "__main__":
    main()
