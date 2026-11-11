#!/usr/bin/env python3
"""Verify the original v11 RMA Core rows in a closed candidate."""

import json
import os
from pathlib import Path
import subprocess
import sys
from types import SimpleNamespace
from unittest.mock import patch

import archive
import candidate_core as candidate
import stage
import verify_core_candidate_import as core
from verify_stage import B, BASE_ENV, OWNER, require
from verify import schema


def command(args):
    argv = [sys.executable, "-B", str(Path(candidate.__file__)), args.action]
    for field in ("pg_bin", "target_file", "commission_receipt", "output", "username", "tenant",
                  "candidate_id", "manifest_sha256"):
        argv.extend(("--" + field.replace("_", "-"), str(getattr(args, field))))
    if args.action.startswith("import"):
        argv.extend(("--bundle", str(args.bundle), "--file-content-root", str(args.file_content_root)))
        if args.include_file_content_history:
            argv.append("--include-file-content-history")
    return argv


def invoke(f, args, label, *, action=None, rejection=None, **changes):
    args = SimpleNamespace(**{**vars(args), "action": action or args.action, "output": f.root / label, **changes})
    result = subprocess.run(command(args), cwd=f.root, env={**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]},
                            capture_output=True, creationflags=archive.FLAGS, timeout=120)
    f.save(label + ".stdout", result.stdout)
    f.save(label + ".stderr", result.stderr)
    f.save(label + ".exit.json", {"Exit": result.returncode})
    if rejection:
        require(result.returncode == 1 and not result.stdout and rejection.encode() in result.stderr,
                label + " wrong rejection")
    else:
        require(result.returncode == 0 and not result.stderr, label + " failed; see native evidence")
    return result.stdout


def late_failure(f, args):
    output = f.root / "import-late"
    args = SimpleNamespace(**{**vars(args), "output": output})
    run_driver = stage.run_driver

    def inject(pg_bin, target, username, folder, operation):
        with (folder / "driver.sql").open("a", encoding="utf-8") as stream:
            stream.write("\nSELECT 1 / 0;\n")
        return run_driver(pg_bin, target, username, folder, operation)

    with patch.dict(os.environ, {**BASE_ENV, "PGPASSWORD": f.passwords[OWNER]}, clear=True), \
            patch.object(stage, "run_driver", side_effect=inject):
        try:
            candidate.execute(args)
        except stage.StageError as error:
            require(str(error) == "rma-stage-native-failed (see native logs)", "unexpected late failure")
        else:
            raise RuntimeError("late test unexpectedly returned a receipt")
    require(b"22012" in (output / "stage.stderr").read_bytes()
            and b'"IMPORTED"' in (output / "stage.stdout").read_bytes(), "did not fail after the actual receipt")


def verify_core(f, args, tenant_b, old_bundle, old_pin, old_root):
    a = args.tenant
    manifest = json.loads((args.bundle / "manifest.json").read_bytes())
    before = core.evidence_state(f, a, tenant_b, True)
    require(before["aCounts"] == [0, 0] and before["admission"] == 0 and not before["receiptSchema"]
            and all(count == 0 for count in before["stockCounts"].values()), "closed candidate must start empty")
    invoke(f, args, "missing-opt-in", include_file_content_history=False, rejection="rma-stage-bundle-member-rejected")
    invoke(f, args, "v10-with-v11-option", bundle=old_bundle, manifest_sha256=old_pin, file_content_root=old_root,
           rejection="rma-stage-bundle-member-rejected")
    require(not any((f.root / label / "driver.sql").exists() for label in ("missing-opt-in", "v10-with-v11-option")),
            "wrong archive reached PostgreSQL")
    late_failure(f, args)
    require(core.evidence_state(f, a, tenant_b, True) == before, "Core rollback changed candidate data or authority")
    invoke(f, args, "recover-after-core-rollback", action="recover", rejection="rma-stage-candidate-receipt-not-found")
    original = invoke(f, args, "core-v11")
    require(original == invoke(f, args, "core-v11-recover", action="recover") == invoke(f, args, "core-v11-repeat"),
            "the v11 Core receipt changed")
    receipt = json.loads(original)
    counts = {name: manifest["counts"][name] for name in candidate.COPY_COLUMNS}
    require(receipt["format"] == archive.FORMAT_WITH_FILE_CONTENT_HISTORY and receipt["counts"] == counts
            and receipt["sourceMigration"] == manifest["migration"] and receipt["recoveryReady"] is False,
            "Core receipt lost original format, lineage or counts")
    copy = (f.root / "core-v11/tenant-rows.sql").read_bytes().splitlines()
    starts = [index for index, row in enumerate(copy) if row.startswith(b"COPY ")]
    require(len(starts) == len(candidate.COPY_COLUMNS), "native COPY count differs")
    for (table, columns), start in zip(candidate.COPY_COLUMNS.items(), starts):
        order = "id" if table == "heads" else "id,version"
        label = "actual-" + table
        f.sql(core.DB, f"COPY (SELECT {columns} FROM {schema(a)}.{table} ORDER BY {order}) TO STDOUT;", label=label)
        expected = copy[start + 1:copy.index(b"\\.", start + 1)]
        require((f.root / (label + ".stdout")).read_bytes().splitlines() == expected, "original Core COPY differs")
    require(core.evidence_state(f, a, tenant_b, True) ==
            {**before, "aCounts": [counts["heads"], counts["versions"]], "receiptSchema": True},
            "Core import changed B, other Owners or native gates")
    f.sql(core.DB, f"SELECT id FROM {schema(a)}.heads;", role=B, error="42501", label="B-no-A-Core")
    invoke(f, args, "journal-existing-format-gate", action="import-journal", include_file_content_history=False,
           rejection="rma-stage-bundle-member-rejected")
    require(not (f.root / "journal-existing-format-gate/driver.sql").exists(), "v11 journal reached PostgreSQL")
    return original
