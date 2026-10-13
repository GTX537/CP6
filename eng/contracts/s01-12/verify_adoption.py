"""Reconcile immutable S01 execution evidence; never execute a product or database.

All source reads use explicit Git commits. --write-manifests writes only the three
task-owned derived JSON documents. The default verifies them against original
evidence, rather than trusting PASS strings in the derived documents.
"""
from __future__ import annotations

import argparse
import atexit
import collections
import copy
import datetime as dt
import hashlib
import json
from pathlib import Path
import platform
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

PINS = {
    "Main": "47c9b12c796620effe41c62d16992ccd629ee949",
    "CRM": "1e4eea61c31d7547c3b3495d392eefbed99b7339",
    "Platform": "9259a45d39bae7e98ca804a74778a588c1ad4619",
}
PACKAGE = "CP6-S01-CONTRACT-INPUTS/1.0.0"
PROFILE_HASH = "2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e"
VECTOR_HASH = "b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01"
EVIDENCE = "docs/development/evidence/"
OUT = EVIDENCE + "s01-adoption/"
CRM_EVIDENCE = "docs/evidence/s01-08/terminal-r1/"
S11 = EVIDENCE + "S01-11/implementation/"
S11_CS = S11 + "20261013T060354Z-dotnet-final-407086e6/"
S11_TS = S11 + "20261013T103957Z-ts-bug196-final-3c3e1efc/"
S11_DESIGN = S11 + "20261013T060706Z-design-f299e68e/"
SURFACES = ("Main:C#", "Main:TypeScript", "Main:PostgreSQL", "Main:bounded-scalar-schema",
            "CRM:C#", "CRM:TypeScript", "Platform:C#")
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


class EvidenceError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise EvidenceError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def decode(data):
    return json.loads(data.decode("utf-8-sig"))


def pointer(value, selector):
    require(selector == "" or selector.startswith("/"), "Invalid JSON pointer: " + selector)
    for part in selector.split("/")[1:]:
        part = part.replace("~1", "/").replace("~0", "~")
        value = value[int(part)] if isinstance(value, list) else value[part]
    return value


class Store:
    def __init__(self, repos):
        self.repos = repos
        self.cache = {}
        self.references = {}
        self.source_checks = []
        self.git_cache = {}
        self.batch = {}
        atexit.register(self.close)

    def close(self):
        for proc in self.batch.values():
            if proc.poll() is None:
                proc.stdin.close()
                proc.wait(timeout=10)
            proc.stdout.close()

    def git(self, repo, *args, check=True):
        key = (repo, args)
        if key not in self.git_cache:
            command = ["git", "-c", "core.longpaths=true", "-C", str(self.repos[repo])]
            if args[0] == "show" and len(args) == 2:
                # Thousands of overlapping recorded inputs are read through one
                # read-only Git batch process per repository, not one process/file.
                if repo not in self.batch:
                    self.batch[repo] = subprocess.Popen(command + ["cat-file", "--batch"],
                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
                proc = self.batch[repo]
                require("\n" not in args[1], "Invalid Git object expression")
                proc.stdin.write(args[1].encode("utf-8") + b"\n")
                proc.stdin.flush()
                header = proc.stdout.readline().rstrip(b"\n")
                if header.endswith(b" missing"):
                    result = subprocess.CompletedProcess(command + list(args), 1, b"", header)
                else:
                    fields = header.split()
                    require(len(fields) == 3 and fields[1] == b"blob", "Unexpected Git batch response")
                    data = proc.stdout.read(int(fields[2]))
                    require(proc.stdout.read(1) == b"\n", "Invalid Git batch framing")
                    result = subprocess.CompletedProcess(command + list(args), 0, data, b"")
            else:
                result = subprocess.run(command + list(args), capture_output=True, check=False)
            self.git_cache[key] = result
        result = self.git_cache[key]
        if check and result.returncode:
            raise subprocess.CalledProcessError(result.returncode, result.args, result.stdout, result.stderr)
        return result

    def read(self, repo, path, commit=None):
        commit = commit or PINS[repo]
        key = (repo, commit, path)
        if key not in self.cache:
            result = self.git(repo, "show", commit + ":" + path, check=False)
            require(result.returncode == 0, f"Missing Git evidence: {repo}@{commit}:{path}")
            self.cache[key] = result.stdout
        return self.cache[key]

    def ref(self, repo, path, selector=None, expected=None, commit=None):
        commit = commit or PINS[repo]
        data = self.read(repo, path, commit)
        digest = sha(data)
        require(expected is None or digest == expected, f"Evidence hash mismatch: {repo}:{path}")
        key = repo + ":" + path if commit == PINS[repo] else repo + "@" + commit + ":" + path
        self.references[key] = {"repository": repo, "commit": commit, "path": path,
                                "sha256_git_blob_bytes": digest, "bytes": len(data)}
        result = {"ref": key}
        if selector is not None:
            pointer(decode(data), selector)
            result["json_pointer"] = selector
        return result

    def doc(self, repo, path):
        self.ref(repo, path)
        return decode(self.read(repo, path))

    def source_check(self, repo, record_path, selector, entries, candidate):
        """Compare recorded raw identity with pinned current bytes and delivery bytes.

        CRLF/BOM variants are reported explicitly, never called raw-byte equality.
        Build outputs absent from Git remain original artifact metadata only.
        """
        rows = []
        if isinstance(entries, dict):
            entries = [{"path": p, "sha256": h} for p, h in entries.items()]
        for item in entries:
            path = item["path"].replace("\\", "/")
            recorded = next((item[k] for k in ("sha256_executed_file_bytes", "sha256_file_bytes", "sha256", "sha256_candidate_git_blob") if isinstance(item.get(k), str)), None)
            if not isinstance(recorded, str):
                continue
            row = {"path": path, "recorded_sha256": recorded,
                   "recorded_identity_basis": item.get("execution_hash_basis", "ORIGINAL_RECORDED_FILE_BYTES")}
            if repo == "Main" and path == "NuGet.Config":
                path = "NuGet.config"
                row["tracked_path"] = path
                row["path_mapping"] = "Explicit original Windows case-insensitive NuGet.Config to tracked NuGet.config; hash still independently checked"
            result = self.git(repo, "show", PINS[repo] + ":" + path, check=False)
            if result.returncode:
                row.update(status="ORIGINAL_LOCAL_ARTIFACT_ONLY", current_sha256=None,
                           reason="Not tracked at the pinned main; original recorded hash is retained, not rebuilt.")
            else:
                data = result.stdout
                row["current_sha256"] = sha(data)
                row["current_ref"] = self.ref(repo, path)
                old = self.git(repo, "show", candidate + ":" + path, check=False)
                row["delivery_sha256"] = sha(old.stdout) if old.returncode == 0 else None
                row["delivery_equals_current"] = old.returncode == 0 and old.stdout == data
                if sha(data) == recorded:
                    row["status"] = "EXACT_ORIGINAL_GIT_PIN" if item.get("sha256_executed_file_bytes", "present") is None else "EXACT_RECORDED_BYTES"
                elif sha(data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")) == recorded:
                    row["status"] = "GIT_LF_TO_RECORDED_CRLF"
                elif sha(b"\xef\xbb\xbf" + data) == recorded:
                    row["status"] = "RECORDED_UTF8_BOM"
                else:
                    row["status"] = "CHANGED_SINCE_EXECUTION"
                    row["reuse_resolution"] = self.resolve_change(record_path, repo, path, recorded, data)
            rows.append(row)
        return {"record": self.ref(repo, record_path, selector), "files": rows,
                "counts": dict(collections.Counter(r["status"] for r in rows))}

    def resolve_change(self, record_path, repo, path, recorded, data):
        if path in ("cp6.web/src/contracts/erp-credit/creditCodec.ts", "cp6.web/src/contracts/erp-credit/creditCodec.test.ts"):
            require(record_path in (S11_CS + "metadata.json", S11_DESIGN + "metadata.json"), "Unexpected changed S11 execution input")
            return {"status": "CAPTURED_BUT_NOT_EXECUTED_BY_THIS_COMMAND",
                    "reason": "Original C# command compiles the net8 test/Entity projects; original design command reads Python/JSON design fixtures. Neither executes these TypeScript files. Final TS428 separately binds and executes both corrected files.",
                    "command_scope": self.ref("Main", record_path, "/command"),
                    "dependency_scope": self.ref("Main", "eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj" if record_path.startswith(S11_CS) else "eng/contracts/s01-11/verify_design.py"),
                    "superseding_run": "S11-TS"}
        if path == "eng/contracts/s01-11/.gitattributes":
            return {"status": "CHECKOUT_POLICY_CHANGE_WITH_INDIVIDUAL_ASSET_HASH_CHECKS",
                    "reason": "Git checkout metadata is not executed by C#/design. Each recorded native schema/fixture/source file is independently compared above; no source byte identity is inferred from this attributes file.",
                    "basis": self.ref("Main", EVIDENCE + "S01-11/coord-fix-196/validation.json", "/reused")}
        if path.startswith("eng/contracts/postgresql-tests/"):
            final_path = EVIDENCE + "S01-10/runs/pin-reconnect-regression/run.json"
            final = self.doc("Main", final_path)
            if path.endswith(("TestInputs.cs", "ExactValueStorageTests.Boundaries.cs")):
                final_hash = final["source_sha256"][path]
                require(final_hash in (sha(data), sha(data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n"))), "Final PG regression source is no longer current")
                detail = "Added fixed-package hash/profile guards; final57 re-executes all56 frozen vectors with these guards." if path.endswith("TestInputs.cs") else "Added three ATP128-character reconnect lines; final57 executes the final reconnect method plus all56 frozen vectors."
                return {"status": "SUPERSEDED_BY_RECORDED_TARGETED_REGRESSION", "reason": detail,
                        "final_source_identity": self.ref("Main", final_path, "/source_sha256"),
                        "superseding_run": "S10-PG-overlap", "matrix_uses": "Final57 original results for all56 fixed IDs"}
            if path.endswith("prepare_sources.py"):
                old_commit = "b622a9664c0eff4f0d296770d8cd45f91667a5f6"
                old = self.read("Main", path, old_commit)
                require(sha(old) == recorded, "Original PG source-audit script identity mismatch")
                diff = self.git("Main", "diff", old_commit, PINS["Main"], "--", path).stdout
                require(b"credit_native_field_bindings_verified" in diff and b"len(credit_bindings) == 42" in diff, "Unexpected source-audit change")
                return {"status": "NON_RUNTIME_SOURCE_AUDIT_ADDITION", "reason": "Post-run change adds read-only42-field native CREDIT verification and its report output; dotnet tests do not execute this Python source-audit script. Runtime adapter, fixture SQL and dependencies are individually bound.",
                        "original_script": self.ref("Main", path, commit=old_commit),
                        "current_script": self.ref("Main", path), "diff_sha256": sha(diff),
                        "review_basis": self.ref("Main", EVIDENCE + "wave4-integration-20261013/S01-10/original-validation.json", "/bindings/reuse_basis")}
        raise EvidenceError("Unresolved execution-to-current input change: " + repo + ":" + path)


def read_results(store, repo, path, kind):
    data = store.read(repo, path)
    ref = store.ref(repo, path)
    rows = []
    if kind == "trx":
        root = ET.fromstring(data)
        for i, node in enumerate(root.findall(".//t:UnitTestResult", NS)):
            rows.append({"name": node.attrib["testName"], "outcome": node.attrib["outcome"],
                         "locator": {**ref, "test_id": node.attrib["testId"], "result_index": i}})
        counter = root.find(".//t:ResultSummary/t:Counters", NS)
        counts = {"discovered": int(counter.attrib["total"]), "executed": int(counter.attrib["executed"]),
                  "passed": int(counter.attrib["passed"]), "failed": int(counter.attrib["failed"]),
                  "skipped": int(counter.attrib.get("notExecuted", "0"))}
        require(len(rows) == counts["executed"], "TRX result/count mismatch: " + path)
    elif kind == "vitest":
        doc = decode(data)
        for i, suite in enumerate(doc["testResults"]):
            for j, item in enumerate(suite["assertionResults"]):
                rows.append({"name": item["fullName"], "outcome": item["status"],
                             "locator": {**ref, "json_pointer": f"/testResults/{i}/assertionResults/{j}"}})
        counts = {"discovered": doc["numTotalTests"], "executed": doc["numPassedTests"] + doc["numFailedTests"],
                  "passed": doc["numPassedTests"], "failed": doc["numFailedTests"], "skipped": doc["numPendingTests"]}
        require(len(rows) == counts["discovered"], "Vitest result/count mismatch: " + path)
    elif kind == "tap":
        text = data.decode("utf-8-sig").replace("\r", "")
        for match in re.finditer(r"^(ok|not ok) (\d+) - (.+)$", text, re.M):
            rows.append({"name": match[3], "outcome": "passed" if match[1] == "ok" else "failed",
                         "locator": {**ref, "tap_number": int(match[2])}})
        counts = {"discovered": len(rows), "executed": len(rows),
                  "passed": sum(r["outcome"] == "passed" for r in rows),
                  "failed": sum(r["outcome"] != "passed" for r in rows), "skipped": 0}
        require(not re.search(r"# (?:SKIP|TODO)\b", text), "Unexpected TAP skip: " + path)
        for label, key in (("tests", "discovered"), ("pass", "passed"), ("fail", "failed"), ("skipped", "skipped")):
            footer = re.search(r"^# " + label + r" (\d+)\s*$", text, re.M)
            require(footer is not None and int(footer[1]) == counts[key], "TAP footer/result mismatch: " + label)
    elif kind == "verbose-vitest":
        text = data.decode("utf-8-sig")
        for i, line in enumerate(text.splitlines()):
            if "✓" in line and " > " in line:
                rows.append({"name": line.strip(), "outcome": "passed", "locator": {**ref, "line": i + 1}})
        match = re.search(r"Tests\s+(\d+) passed\s+\((\d+)\)", text)
        require(match is not None and match[1] == match[2], "Missing successful verbose summary: " + path)
        counts = {"discovered": int(match[2]), "executed": int(match[2]), "passed": int(match[1]), "failed": 0, "skipped": 0}
        require(len(rows) == counts["executed"], "Verbose result/count mismatch: " + path)
    else:
        raise EvidenceError("Unknown result format " + kind)
    require(counts["passed"] + counts["failed"] + counts["skipped"] == counts["discovered"], "Inconsistent test counters")
    require(sum(r["outcome"].lower() == "passed" for r in rows) == counts["passed"], "Reported passed count differs from actual result rows: " + path)
    require(sum(r["outcome"].lower() == "failed" for r in rows) == counts["failed"], "Reported failed count differs from actual result rows: " + path)
    return ref, rows, counts


# Final executions only. Historical/red evidence is separately referenced below.
RUNS = [
    ("S02-CS", "S01-02", "Main", "Main:C#", EVIDENCE + "S01-02/implementation/green.trx", "trx", 77),
    ("S03-CS", "S01-03", "Main", "Main:C#", EVIDENCE + "S01-03/implementation/green.trx", "trx", 205),
    ("S04-CS", "S01-04", "Main", "Main:C#", EVIDENCE + "S01-04/green/inventory-green.trx", "trx", 132),
    ("S06-CS", "S01-06", "Main", "Main:C#", EVIDENCE + "S01-06/green-csharp.trx", "trx", 60),
    ("S06-TS", "S01-06", "Main", "Main:TypeScript", EVIDENCE + "S01-06/green-ts.json", "vitest", 81),
    ("S07-CS", "S01-07", "Main", "Main:C#", EVIDENCE + "S01-07/csharp/quality-contracts-format-final.trx", "trx", 657),
    ("S07-TS", "S01-07", "Main", "Main:TypeScript", EVIDENCE + "S01-07/ts/08-date-regression-1.log", "verbose-vitest", 130),
    ("S08-CS", "S01-08", "CRM", "CRM:C#", CRM_EVIDENCE + "csharp-final.trx", "trx", 186),
    ("S08-TS", "S01-08", "CRM", "CRM:TypeScript", CRM_EVIDENCE + "web-final-142.txt", "tap", 142),
    ("S09-CS", "S01-09", "Platform", "Platform:C#", EVIDENCE + "S01-09/test-results/task-pass.trx", "trx", 110),
    ("S10-PG", "S01-10", "Main", "Main:PostgreSQL", EVIDENCE + "S01-10/runs/full-storage/s01-10-postgresql.trx", "trx", 210),
    ("S10-PG-overlap", "S01-10", "Main", "Main:PostgreSQL", EVIDENCE + "S01-10/runs/pin-reconnect-regression/s01-10-postgresql.trx", "trx", 57),
    ("S10-PG-lifecycle", "S01-10", "Main", "Main:PostgreSQL", EVIDENCE + "S01-10/runs/lifecycle-regression/s01-10-postgresql.trx", "trx", 1),
    ("S11-CS", "S01-11", "Main", "Main:C#", S11_CS + "results.trx", "trx", 417),
    ("S11-TS", "S01-11", "Main", "Main:TypeScript", S11_TS + "vitest.json", "vitest", 428),
]


def observation(row, run, scope):
    return {"status": "PASS_REUSED", "run": run, "observation_kind": "RECORDED_TEST_ASSERTION",
            "scope": scope, "actual_scalar": None,
            "actual_scalar_reason": "Original result records assertion outcome, not a captured scalar; expected is not observed output.",
            "actual_test": {"name_prefix": row["name"][:220], "full_name_sha256": sha(row["name"].encode("utf-8")),
                            "outcome": row["outcome"], "locator": row["locator"]}}


def match_vector(rows, ident, suffix=None):
    pattern = re.compile(r"(?<![A-Z0-9-])" + re.escape(ident) + r"(?![A-Z0-9-])")
    matched = [r for r in rows if pattern.search(r["name"]) and (suffix is None or suffix in r["name"])]
    require(len(matched) == 1, f"Expected one original result for {ident}/{suffix}, found {len(matched)}")
    require(matched[0]["outcome"].lower() == "passed", "Original assertion failed: " + ident)
    return matched[0]


def bindings(store, dependencies):
    result = {}
    for dep in dependencies:
        task, repo = dep["task"], dep["repository"]
        record = dep["existing_evidence"]["path"]
        doc = store.doc(repo, record)
        groups = []
        if task == "S01-02":
            path = EVIDENCE + "S01-02/implementation/green-metadata.json"
            groups.append((path, "/sources", store.doc(repo, path)["sources"]))
        elif task in ("S01-03", "S01-04", "S01-06"):
            groups.append((record, "/source_and_input_hashes", doc["source_and_input_hashes"]))
        elif task == "S01-05":
            for name in ("input_and_config_pins", "tested_source_files"):
                groups.append((record, "/" + name, doc[name]))
        elif task == "S01-07":
            path = EVIDENCE + "S01-07/csharp/verification.json"
            cs = store.doc(repo, path)
            for name in ("codeHashes", "dependencyHashes"):
                groups.append((path, "/" + name, cs[name]))
            path = EVIDENCE + "S01-07/ts/08-date-regression-validation.json"
            groups.append((path, "/inputs_sha256", store.doc(repo, path)["inputs_sha256"]))
        elif task == "S01-08":
            groups.append((record, "/inputs", doc["inputs"]))
        elif task == "S01-09":
            groups.append((record, "/input_files", doc["input_files"]))
            # The original report binds working sources to this real source commit.
            source_commit = doc["source_commit"]
            names = store.git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", source_commit).stdout.decode().splitlines()
            source_rows = [{"path": p, "sha256": sha(store.read(repo, p, source_commit))}
                           for p in names if p.startswith(("src/", "tests/")) and not p.endswith(".trx")]
            result[task + "-source-commit"] = {"original_binding": store.ref(repo, record, "/source_binding"),
                "source_commit": source_commit, "current_bytes": store.source_check(repo, record, "", source_rows, source_commit)}
        elif task == "S01-10":
            for run in ("full-storage", "pin-reconnect-regression", "lifecycle-regression"):
                path = EVIDENCE + f"S01-10/runs/{run}/run.json"
                groups.append((path, "/source_sha256", store.doc(repo, path)["source_sha256"]))
        elif task == "S01-11":
            for path in (S11_CS + "metadata.json", S11_TS + "metadata.json", S11_DESIGN + "metadata.json"):
                original = store.doc(repo, path)
                # Metadata captures a broad working tree. Reuse concerns the command's actual inputs,
                # and every captured entry is retained by reference for audit.
                groups.append((path, "/inputs_before", original["inputs_before"]))
        result[task] = [store.source_check(repo, p, ptr, entries, dep["candidate_sha"]) for p, ptr, entries in groups]
    return result


def formal_pg(store):
    path = "eng/contracts/s01/postgresql-mappings.json"
    formal = store.doc("Main", path)
    store.ref("Main", path, expected="96ef3c9f22919e790f750773c2a4bdc675b7df5f145f7b9bfffeda6d19c1ec16")
    proposed_path = EVIDENCE + "S01-10/postgresql-mappings.proposed.json"
    proposal = store.doc("Main", proposed_path)
    review_path = EVIDENCE + "S01-10/coord-adoption/scale-adoption-review.json"
    review = store.doc("Main", review_path)
    comparison = copy.deepcopy(formal)
    comparison.pop("coordinator_decision")
    comparison["adoption"] = proposal["adoption"]
    comparison["scale_change"]["approval"] = proposal["scale_change"]["approval"]
    require(comparison == proposal, "Formal mapping changed executed proposal semantics")
    digest = sha(canonical({k: formal[k] for k in review["semantic_keys"]}))
    require(digest == "7bb24e1e36610996a46d13cace0a58e8032a3eb369e4dda235af5ec3c58e8752", "PG semantic hash mismatch")
    rows = formal["inventory_mappings"]
    expanded = [r["table"] + "." + r["logical_column"] for r in rows if r["source_scale_range"] != r["storage_scale_range"]]
    require(len(rows) == 66 and len(expanded) == 20 and len(formal["assigned_vectors"]) == 56, "PG mapping scope mismatch")
    require(formal["adoption"] == "ADOPTED_BY_COORD", "Formal PG adoption missing")
    for row in rows:
        if row["table"] + "." + row["logical_column"] in expanded:
            require(row["source_scale_range"] == [0, 8] and row["storage_scale_range"] == [0, 18], "Wrong expanded scale")
    return {"formal": store.ref("Main", path), "proposal": store.ref("Main", proposed_path),
            "mapping_version": formal["mapping_version"], "adoption": formal["adoption"],
            "semantic_sha256": digest, "semantic_keys": review["semantic_keys"],
            "expanded_fields": sorted(expanded), "unchanged_inventory_fields": 46,
            "expanded_binding_basis": "17 native amount bindings + 3 explicitly derived dictionary amounts",
            "wire_limits": "Money.scale and trace.scale remain 0..8; calculationScale remains 18",
            "execution_identity": "Original PG18.6/Npgsql8.0.8 PROPOSED execution; formal metadata only adoption reuses its unchanged semantics",
            "run_count_note": "210 final + overlapping 57 reconnect regression + 1 lifecycle; never 267 independent tests",
            "formal_identity_new_execution": "NOT_RUN", "production_owner_acceptance": "NOT_RUN"}


def crm_revision(store):
    base = "contracts/s01/revisions/CRM-INQ-TERMINAL-20261013-R1/"
    profile = store.doc("CRM", base + "profile.json")
    require(profile["profile_id"] == "CRM-INQ-TERMINAL-PROJECTION/20261013-R1", "Wrong CRM profile")
    require(profile["base_native_schema_locator"]["schema_sha256"] == "740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349", "Wrong CRM base")
    patch = store.ref("CRM", base + "overlay.json", expected="bce20c5a3bc93826993be6c2ba7c9a440fffd467f746245e5665707d8eedf549")
    effective = store.ref("CRM", base + "crm-inq.terminal.openapi.json", expected="fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d")
    return {"profile": store.ref("CRM", base + "profile.json"), "profile_id": profile["profile_id"],
            "base_locator": profile["base_native_schema_locator"], "patch": patch,
            "effective_schema": profile["effective_schema"], "effective_schema_bytes": effective, "original_adoption_state": profile["adoption_state"],
            "delivery_evidence": store.ref("CRM", CRM_EVIDENCE + "executed-source.json"),
            "boundary": "90/99 public expiry/artifact/proof required-null; 99 requested<=completed; 30 requested<=completed<expires. No public terminal download authorization.",
            "platform_scope": "Native locator only; no effective terminal profile decode claim",
            "fixed_vectors_scope": "INQ-VERSION four original native locator assertions; effective revision checks are separate"}


def make_vectors(store, frozen, profiles, results):
    matrix = []
    ts05_path = EVIDENCE + "S01-05/verification.json"
    ts05 = store.doc("Main", ts05_path)
    dispatch_path = "docs/development/dispatch/20261012-wave2/dispatch.json"
    dispatch = next(t for t in store.doc("Main", dispatch_path)["tasks"] if t["id"] == "S01-05")
    source_path = "cp6.web/src/contracts/exact/profiles.test.ts"
    source = store.read("Main", source_path).decode("utf-8-sig")
    for text in ("it.each(shared)", "expect(shared).toHaveLength(34)", "assignment.case_ids", "expect(pair).toEqual(item.expected.pair)", "expect(value.toWire()).toBe(item.input)"):
        require(text in source, "S05 suite completeness proof changed: " + text)
    require(not re.search(r"\b(?:it|describe)\.(?:skip|only)\b", source), "S05 skipped/focused tests")
    log_path = EVIDENCE + "S01-05/green.log"
    require(re.search(r"Tests\s+202 passed\s+\(202\)", store.read("Main", log_path).decode()), "S05 suite summary missing")
    assigned = [c for c in frozen["cases"] if c["profile_id"] in dispatch["profiles"]]
    require(len(assigned) == 34 and {c["id"] for c in assigned} == set(dispatch["case_ids"]), "S05 assigned set mismatch")
    require({c["case_id"] for c in ts05["shared_vector_results"]} == set(dispatch["case_ids"]), "S05 summary IDs differ")
    for i, case in enumerate(frozen["cases"]):
        row = copy.deepcopy(case)
        row["original_vector"] = store.ref("Main", "eng/contracts/s01/vectors.json", f"/cases/{i}")
        row["consumers"] = {s: {"status": "NOT_APPLICABLE", "reason": "No implementation of this profile on this surface in S01-02..11."} for s in SURFACES}
        ident, profile = case["id"], case["profile_id"]
        targets = []
        if profile.startswith("fin-credit-"):
            targets = [("Main:C#", "S03-CS"), ("Main:PostgreSQL", "S10-PG-overlap")]
        elif profile.startswith("fin-inv-"):
            targets = [("Main:C#", "S04-CS"), ("Main:PostgreSQL", "S10-PG-overlap")]
        elif profile == "atp-stableid-r1":
            targets = [("Main:C#", "S06-CS"), ("Main:TypeScript", "S06-TS"), ("Main:PostgreSQL", "S10-PG-overlap"), ("Main:bounded-scalar-schema", "S06-TS")]
        elif profile in ("strict-json", "null-only", "json-semantic-equality"):
            targets = [("Main:C#", "S02-CS"), ("CRM:C#", "S08-CS"), ("CRM:TypeScript", "S08-TS"), ("Platform:C#", "S09-CS")]
        elif profile.startswith("ncr-"):
            targets = [("Main:C#", "S07-CS")]
        elif profile == "ipqc-r4":
            targets = [("Main:TypeScript", "S07-TS")]
        elif profile == "crm-inq-r3-v15":
            targets = [("CRM:C#", "S08-CS"), ("CRM:TypeScript", "S08-TS"), ("Platform:C#", "S09-CS")]
        elif profile == "crm-lead-cp20":
            targets = [("CRM:C#", "S08-CS"), ("CRM:TypeScript", "S08-TS")]
        elif profile.startswith("erp-credit-"):
            targets = [("Main:C#", "S11-CS"), ("Main:TypeScript", "S11-TS"), ("Platform:C#", "S09-CS")]
        scope = case["expected"].get("assertion_scope", case["verification_level"])
        if profile == "crm-inq-r3-v15":
            scope = "NATIVE_LOCATOR_ONLY"
        if ident.startswith("BYTES-"):
            scope = "ORIGINAL_BYTES_RETENTION_ONLY; NativeSchemaValidated=false; full PostInput RV invalid"
        for surface, run in targets:
            suffix = None
            if run == "S06-TS":
                suffix = "TypeScript lexical" if surface == "Main:TypeScript" else "Schema"
            actual = match_vector(results[run], ident, suffix)
            own_scope = "NATIVE_ROUTING_OR_RETENTION_ONLY" if surface == "Platform:C#" and profile.startswith("erp-") else scope
            row["consumers"][surface] = observation(actual, run, own_scope)
        if profile.startswith(("fin-credit-", "fin-inv-")):
            index, summary = next((j, r) for j, r in enumerate(ts05["shared_vector_results"]) if r["case_id"] == ident)
            require(summary["profile_id"] == profile and summary["operation"] == case["operation"] and summary["expected_accepted"] == case["expected"]["accepted"] and summary["result"] == "PASS", "S05 per-ID summary mismatch")
            row["consumers"]["Main:TypeScript"] = {"status": "PASS_REUSED", "run": "S05-TS", "scope": scope,
                "observation_kind": "SUITE_BACKED_ASSERTION", "actual_scalar": None,
                "actual_scalar_reason": "No scalar captured; summary is not original per-test reporter output.",
                "actual_test": {"declared_template": "S01-05 fixed shared input package > $id / $profile_id",
                                "outcome": "PASS_IN_ORIGINAL_ID_SUMMARY", "locator": store.ref("Main", ts05_path, f"/shared_vector_results/{index}")},
                "suite_proof": [store.ref("Main", log_path), store.ref("Main", source_path), store.ref("Main", dispatch_path)],
                "evidence_granularity_gap": "Original log has only 202/202 summary. All 34 IDs execute through it.each(shared), with exact assigned-set assertion; individual reporter records were not captured."}
        comparable = profile.startswith(("fin-credit-", "fin-inv-")) or profile == "atp-stableid-r1"
        row["comparison"] = {"three_surface_assertions_comparable": comparable,
            "scope": "SAME_FROZEN_LEXICAL_ASSERTIONS" if comparable else "PROFILE_SPECIFIC_ASYMMETRIC_ASSERTIONS",
            "scalar_equality_observed": False, "whole_behavior_equivalent": False,
            "basis": "Same original ID/profile/operation/input/expected; original passing assertions only. No fabricated scalar output or universal 122-way equivalence."}
        matrix.append(row)
    require(len(matrix) == 122 and sum(r["comparison"]["three_surface_assertions_comparable"] for r in matrix) == 56, "Wrong matrix scope")
    return {"schema": "CP6-S01-ADOPTION-VECTORS/1", "package_version": PACKAGE,
            "original_execution_flag": frozen["execution"], "new_product_test_execution": False,
            "cases": matrix, "counts": {"vectors": 122, "profiles": 16, "three_surface_lexical": 56,
                "consumer_states": dict(collections.Counter(o["status"] for r in matrix for o in r["consumers"].values()))}}


def supplementary(results, run, patterns, limit=16):
    matches = [r for r in results[run] if any(re.search(p, r["name"], re.I) for p in patterns)]
    require(matches, f"No original supplementary result: {run}/{patterns}")
    require(all(r["outcome"].lower() == "passed" for r in matches), "Failed supplementary result")
    return [observation(r, run, "SELECTED_CONTRACT_SUBASSERTION") for r in matches[:limit]]


def ncr_field_assertions(store, rows):
    """The five metadata fields are distinct from the frozen const-null fields."""
    test_source_path = "eng/contracts/exact-values-tests/QualityContractTests.cs"
    metadata_path = "CP6.Entity/Contracts/Quality/NcrFieldMetadata.g.cs"
    inventory_path = "eng/contracts/s01-07/csharp/native-field-inventory.json"
    native_path = "eng/contracts/s01-07/inputs/native/design/NCR-R6-WIRE.json"
    native = store.doc("Main", native_path)
    inventory = store.doc("Main", inventory_path)
    source_lines = store.read("Main", test_source_path).decode("utf-8-sig").splitlines()
    def exact_test(test_id, method):
        name = "CP6.ExactValues.Tests.QualityContractTests." + method
        matches = [r for r in rows if r["locator"].get("test_id") == test_id and r["name"] == name]
        require(len(matches) == 1 and matches[0]["outcome"] == "Passed", "Missing exact S07 field assertion: " + method)
        line = source_lines.index("    public void " + method + "()") + 1
        end = next(i + 1 for i in range(line, len(source_lines)) if source_lines[i] == "    }")
        result = observation(matches[0], "S07-CS", "EXACT_NCR_FIELD_SUBASSERTION")
        result["test_source"] = {**store.ref("Main", test_source_path), "line_start": line, "line_end": end}
        return result
    metadata = exact_test("9769af35-af53-08f7-9c17-c76f48c68c70", "Five_field_metadata_rows_retain_exact_required_nullable_and_editable_flags")
    serialized_null = exact_test("04dbbf18-9182-683a-8813-d9a7c858892a", "Required_nullable_values_are_not_omitted_by_relaxed_caller_options")
    declarations = re.findall(r'new\("([^"]+)", (true|false), (true|false), (true|false)\)',
                              store.read("Main", metadata_path).decode("utf-8-sig"))
    require(len(declarations) == 5, "Expected exactly five NCR field metadata declarations")
    fields = []
    for schema_pointer, required, nullable, editable in declarations:
        name = schema_pointer.rsplit("/", 1)[1]
        flags = {"required": required == "true", "nullable": nullable == "true", "editable": editable == "true"}
        require(flags == {"required": True, "nullable": name != "priorHeadRef", "editable": False}, "Unexpected NCR field metadata flags")
        native_pointer = schema_pointer.removeprefix("#")
        shape = pointer(native, native_pointer)
        parent = pointer(native, native_pointer.rsplit("/properties/", 1)[0])
        require(name in parent["required"], "NCR required flag differs from original native object")
        source_pointer = "design/NCR-R6-WIRE.json" + schema_pointer
        matched = [(i, j, f) for i, d in enumerate(inventory["declarations"]) for j, f in enumerate(d["fields"])
                   if f["sourcePointer"] == source_pointer]
        require(len(matched) == 1, "Ambiguous native NCR field inventory pointer")
        i, j, original = matched[0]
        require(original["required"] == flags["required"] and original["nullable"] == flags["nullable"], "Native NCR field flags differ")
        if name in ("sourceKind", "keyword", "cursor"):
            dynamic = {"coverage": "PARTIAL", "assertion": "Explicit null decodes and remains present when caller requests WhenWritingNull omission",
                       "evidence": serialized_null,
                       "remaining": "This exact test does not enumerate all legal non-null values, missing-member rejection or all runtime shape transitions."}
        else:
            dynamic = {"coverage": "NOT_COVERED", "reason": "The selected five-field test checks metadata only; neither selected result exercises this field's dynamic shape, non-null value domain or missing/null rejection."}
        fields.append({"field": name, "assertion": "Original required/nullable/editable metadata for " + name,
                       "coverage": "PARTIAL", "original_schema": store.ref("Main", native_path, native_pointer),
                       "original_shape": shape, "original_field_inventory": store.ref("Main", inventory_path, f"/declarations/{i}/fields/{j}"),
                       "metadata_assertion": {"coverage": "FULL", "expected_flags": flags,
                                              "metadata_source": store.ref("Main", metadata_path), "evidence": metadata,
                                              "scope": "Exact five-row metadata flags only; Editable=false does not establish runtime write authorization."},
                       "dynamic_shape": dynamic})
    require({f["field"] for f in fields} == {"priorHeadRef", "sourceKind", "keyword", "cursor", "nextCursor"}, "Wrong NCR field set")
    return fields, [metadata, serialized_null]


def revisions(store, profiles, matrix, results):
    catalog_path = "docs/design-handbook/remediation/08-acceptance-catalog.json"
    catalog = store.doc("Main", catalog_path)
    all_refs = {r["case_id"]: r for p in profiles["profiles"] for r in p["revision_acceptance_refs"]}
    cases = []
    for ident in sorted(all_refs):
        ref = all_refs[ident]
        original = pointer(catalog, ref["json_pointer"])
        require(original["id"] == ident and original["result"] == "NOT_RUN", "Revision catalog identity changed")
        applicable_profiles = [p["id"] for p in profiles["profiles"] if any(r["case_id"] == ident for r in p["revision_acceptance_refs"])]
        positive = ident.endswith("-POS")
        negative = ident.endswith("-NEG")
        fixed = [c for c in matrix["cases"] if c["profile_id"] in applicable_profiles and
                 (not (positive or negative) or c["expected"].get("accepted", c["expected"].get("equal", True)) == positive)]
        # Do not count unrelated generic JSON cases as evidence for native field or revision semantics.
        if "NCR-TYPE" in ident:
            fixed = [c for c in fixed if c["id"].startswith(("NULL-", "NCR-NULL-"))]
        if "NCR-FIELD" in ident:
            fixed = [c for c in fixed if c["id"].startswith("NCR-NULL-")]
        clauses = []
        supplement = []
        def clause(text, coverage, reason):
            clauses.append({"assertion": text, "coverage": coverage, "reason": reason})
        if "OPS-IF-002" in ident:
            clause("38 digit / 30 integer + 8 fraction exact wire and coefficient-scale roundtrip" if positive else
                   "39 digit, exponent and negative zero rejection without decimal(38,8) intermediary", "FULL",
                   "Selected immutable lexical inputs have C#/TS/PG passing assertions; scalar values were not separately captured.")
        elif "OPS-IF-030" in ident:
            clause("90 integer digits, 18/12 fractional places and original -0.000 preservation" if positive else
                   "Fractional overflow rejected without silent rounding", "FULL", "Covered by the selected original inventory vector assertions.")
            if negative:
                clause("Every production CAST/SUM path and zero-denominator business input", "NOT_COVERED",
                       "This join establishes lexical/isolated exact-storage vectors, not all financial writers or aggregations.")
        elif "PLAN-IF-004" in ident:
            clause("17/128 character inputs retained; full end-of-string lexical rules" if positive else
                   "Empty/whitespace/Chinese/129 characters and LF/CRLF suffix rejected", "FULL",
                   "Original 22-case C#/TS, bounded scalar-schema assertions and isolated PG storage checks are joined; no full ATP engine claim.")
            clause("Historical profiles remain unchanged in existing business records", "NOT_COVERED",
                   "No production migration or historical-record recovery was executed.")
        elif "NCR-TYPE" in ident:
            clause("Required null-only fields accept explicit null and retain it" if positive else
                   "Null-only fields reject missing, number, object and string-null inputs", "FULL",
                   "Generic null-only vectors and native NCR field subassertions have separate recorded C# results; no universal language equivalence.")
        elif "NCR-FIELD" in ident:
            if positive:
                fixed = []
                applicable_profiles = ["ncr-main-r6"]
                fields, supplement = ncr_field_assertions(store, results["S07-CS"])
                clauses.extend(fields)
            else:
                fixed = []
                clause("Field metadata grants no write permission and preserves business Source scope", "NOT_COVERED",
                       "A declaration/field codec cannot prove runtime authorization or Source qualification; future QA Owner integration is required.")
        elif "OPS-IF-015" in ident:
            clause("QapEvidence/QapUom original member type bindings" if positive else
                   "ExecEvidence/Uom same-name substitutions rejected", "FULL",
                   "Six original TYPE_BINDING_ONLY assertions execute in TypeScript; no C#/PG applicability is fabricated.")
            clause("Complete original evidence object and Owner qualification", "PARTIAL",
                   "Selected complete native fixtures and typecheck exist, while the full business object sequence remains unexecuted.")
        elif "COM-LEAD" in ident:
            clause("ownerId null, unassigned and native UUID forms retained" if positive else
                   "Invalid owner string and missing required ownerId rejected", "FULL",
                   "Five original field projections are present in corrected CRM C# and Web runs; backend lead service integration is separate.")
        elif "COM-INQ" in ident:
            if positive:
                clause("Exact source selection and semantic object-key equality", "PARTIAL",
                       "Four fixed INQ cases identify the native locator; effective terminal profile is separately pinned, and EQUAL vectors cover JSON semantics.")
                clause("Status90/99 required-null public fields and status99/status30 clock rules", "FULL",
                       "Corrected terminal-R1 run covers local C# decoder and TS view-state behavior; no HTTP delivery acceptance is inferred.")
                supplement = supplementary(results, "S08-TS", [r"terminal", r"clock", r"99", r"90", r"expiry", r"expired"], 16)
                clause("All55 reference closure and HTTP201/422 at201/4001 character boundaries", "NOT_COVERED",
                       "Fixed native locator/equality evidence is insufficient for this complete HTTP scenario; no new HTTP execution occurred.")
            else:
                clause("Duplicate JSON keys rejected and local decoder validates selected closed members", "PARTIAL",
                       "Frozen strict JSON vectors and corrected decoder tests execute; fixed vectors do not enumerate every extension-field rule.")
                clause("Expired proof/wrongETag cannot download or write through real HTTP/Owner", "NOT_COVERED",
                       "Local codec and download-state tests do not establish authorization, persistence, or real HTTP negative paths.")
        elif "AC-W5-COM-06-R" in ident:
            clause("Wrong registry/schema identity and APPLIED-without-ref/REJECTED-with-ref rejected", "FULL",
                   "Native routing vectors plus explicit current/frozen outcome tests cover these codec assertions.")
            supplement = supplementary(results, "S11-TS", [r"outcome-APPLIED-False", r"outcome-REJECTED-True", r"unproven-completed", r"exact routes, schema pins"], 10)
            clause("NO_CHANGE/COMPLETED transport alone never createsFOUND or releases occupancy", "PARTIAL",
                   "Unproven contract projections remain fenced; actual Owner/Store effects and recovery were not executed.")
        elif "AC-W5-COM-07-R" in ident:
            clause("Money amount alias and extra Money members rejected", "FULL",
                   "Explicit original current/frozen Money negative tests are located separately from routing vectors.")
            supplement = supplementary(results, "S11-TS", [r"current-money-alias$", r"current-money-extra$", r"frozen-money-alias$", r"frozen-money-extra$"], 4)
            clause("Old digest/length/handle/receipt combinations rejected in selected design chain", "PARTIAL",
                   "Original design5 audit has343 projections/60 tamper rejections; this is selected fixture evidence, not all native C06 roots or runtime Store proof.")
            clause("Real issuer/Store receipts required before business effects", "NOT_COVERED",
                   "No real issuer/Store is wired; UNPROVEN_FENCED remains the adoption state.")
        elif "AC-W5-COM-08-R" in ident:
            clause("Duplicate keys rejected and C01/C02 replay retains exact original body bytes", "PARTIAL",
                   "Byte retention and replay tests exist; BYTES vectors have invalid native RV and NativeSchemaValidated=false.")
            supplement = supplementary(results, "S11-TS", [r"C01 replays", r"C02 replays"], 2)
            clause("Every multi-root generator input and repeat business write rejected", "NOT_COVERED",
                   "Full generator/Owner write sequence is outside the bounded codec; no native fullC06 decode or repeat-write acceptance.")
        else:
            raise EvidenceError("Unmapped revision " + ident)
        clause(original["expected_writes"], "NOT_COVERED", "Original business write-set acceptance has not run; library and isolated fixture PASS cannot discharge it.")
        cases.append({"revision_case_id": ident, "original_ac_id": original["original_acceptance_id"],
                      "original_ac": store.ref("Main", catalog_path, ref["json_pointer"], ref["sha256"]),
                      "original_expected": original["expected"], "verification_level": original["verification_level"],
                      "whole_case_result": "NOT_RUN", "coverage": "PARTIAL" if fixed or supplement else "NOT_COVERED",
                      "applicable_profiles": applicable_profiles, "fixed_vector_ids": [c["id"] for c in fixed],
                      "fixed_vector_evidence": "vectors.json/cases: each ID retains original input, expectation and per-surface result locator",
                      "subassertions": clauses, "supplementary_evidence": supplement})
    require(len(cases) == 19, "Expected 19 unique revision cases")
    return {"schema": "CP6-S01-REVISION-COVERAGE/1", "coverage_semantics": "FULL applies only to explicitly bounded contract subassertion; never promotes REQUIRED_RUNTIME to PASS.",
            "full_runtime_catalog": {"cases": 247, "result": "NOT_RUN"}, "cases": cases,
            "counts": dict(collections.Counter(c["coverage"] for c in cases))}


def original_records(store, task, repo, main_record):
    """Use pointers instead of copying log packages or historical local paths."""
    specs = {
        "S01-02": [(main_record, "/runtime"), (main_record, "/sdk"), (EVIDENCE + "S01-02/implementation/green-metadata.json", "")],
        "S01-03": [(main_record, k) for k in ("/environment", "/command", "/original_artifact_hashes", "/original_red_history", "/source_identity_basis")],
        "S01-04": [(main_record, k) for k in ("/environment", "/command", "/original_artifact_hashes", "/original_red_history", "/source_identity_basis")],
        "S01-05": [(main_record, k) for k in ("/environment", "/runs", "/logs", "/case_result_basis")],
        "S01-06": [(main_record, k) for k in ("/environment", "/command", "/original_artifact_hashes", "/original_red_history", "/source_identity_basis")],
        "S01-07": [(EVIDENCE + "S01-07/csharp/verification.json", ""), (EVIDENCE + "S01-07/ts/08-date-regression-validation.json", ""), (main_record, "/binding/artifact_scope")],
        "S01-08": [(main_record, k) for k in ("/environment", "/identity_basis", "/artifacts", "/outputs", "/results", "/source_delivery")],
        "S01-09": [(main_record, k) for k in ("/environment", "/executed_command", "/source_binding", "/build_artifacts", "/test_runs")],
        "S01-10": [(main_record, "/env"), (main_record, "/bindings/reuse_basis")] + [(EVIDENCE + f"S01-10/runs/{r}/run.json", "") for r in ("full-storage", "pin-reconnect-regression", "lifecycle-regression")],
        "S01-11": [(main_record, ""), (S11_CS + "metadata.json", ""), (S11_TS + "metadata.json", ""), (S11_DESIGN + "metadata.json", ""), (EVIDENCE + "S01-11/validation.json", "/history")],
    }
    return [store.ref(repo, p, selector) for p, selector in specs[task]]


def execution_fields(store, name, task, repo, evidence_path, kind, main_record):
    """Name the exact observed fields; do not infer absent exit/time/artifact data."""
    fields = {}
    def field(label, path, ptr):
        fields[label] = store.ref(repo, path, ptr)
    if task == "S01-02":
        meta = EVIDENCE + "S01-02/implementation/green-metadata.json"
        for key in ("command", "started_utc", "finished_utc", "exit_code", "artifacts", "base_head", "source_state"):
            field(key, meta, "/" + key)
        field("runtime", main_record, "/runtime")
        field("sdk", main_record, "/sdk")
    elif task in ("S01-03", "S01-04", "S01-06"):
        for key in ("command", "environment", "original_started_at", "original_artifact_hashes", "source_identity_basis", "execution_base_head"):
            if key in store.doc(repo, main_record):
                field(key, main_record, "/" + key)
        field("result_and_exit", main_record, "/original_result")
        field("observed_at_with_basis", main_record, "/observed_at")
    elif task == "S01-05":
        field("command_and_exit", main_record, "/runs/2")
        field("environment", main_record, "/environment")
        field("captured_at_not_test_start", main_record, "/recorded_utc")
        fields["test_times"] = {"status": "PARTIALLY_RECORDED", "reason": "GREEN log has Start at clock/duration; machine report lacks explicit test UTC start/end."}
        fields["artifacts"] = {"status": "NO_ARCHIVED_COMPILED_JS_IDENTITY", "reason": "Vitest executes recorded TS sources; final GREEN log is retained."}
    elif task == "S01-07":
        if name.endswith("CS"):
            meta = EVIDENCE + "S01-07/csharp/verification.json"
            for key in ("command", "times", "runtime", "sdk", "os", "artifacts"):
                field(key, meta, "/" + key)
            fields["exit_code"] = {"status": "NOT_RECORDED_AS_SEPARATE_MACHINE_FIELD", "basis": "Original TRX counters/results are independently checked"}
            fields["original_dll_sha256"] = {"value": None, "status": "ORIGINAL_DLL_SHA256_MISSING"}
        else:
            meta = EVIDENCE + "S01-07/ts/08-date-regression-validation.json"
            for key in ("commands", "started_at", "finished_at", "node", "npm", "execution_head"):
                field(key, meta, "/" + key)
            fields["artifacts"] = {"status": "SOURCE_AND_LOG_IDENTITY; NO_ARCHIVED_JS_BINARY"}
    elif task == "S01-08":
        for key in ("environment", "captured_at_utc", "executed_head", "identity_basis", "artifacts", "results", "outputs"):
            field(key, main_record, "/" + key)
        fields["command_record"] = {"output": store.ref(repo, evidence_path), "delivery_commands": store.ref(repo, CRM_EVIDENCE + "README.md"),
                                     "status": "ORIGINAL_OUTPUT_AND_DELIVERY_COMMANDS; no separate command array in executed-source.json"}
        fields["exit_observation"] = store.ref(repo, main_record, "/exit_observation")
    elif task == "S01-09":
        for key in ("executed_command", "environment", "source_commit", "execution_head", "source_binding", "build_artifacts", "test_runs"):
            field(key, main_record, "/" + key)
        fields["exit_code"] = {"status": "NO_SEPARATE_FINAL_EXIT_FIELD", "basis": "Original task-pass TRX outcome and counters retained"}
    elif task == "S01-10":
        meta = str(Path(evidence_path).with_name("run.json")).replace("\\", "/")
        for key in ("command", "started_utc", "finished_utc", "exit_code", "source_head", "source_identity", "artifact_sha256"):
            field(key, meta, "/" + key)
        field("environment", main_record, "/env")
    elif task == "S01-11":
        meta = (S11_CS if name.endswith("CS") else S11_TS) + "metadata.json"
        for key in ("command", "started_utc", "ended_utc", "exit_code", "environment", "source_baseline", "execution_identity", "evidence_files"):
            field(key, meta, "/" + key)
        if name.endswith("CS"):
            field("compiled_artifacts", meta, "/compiled_artifacts_after")
        else:
            fields["compiled_artifact_scope"] = "TS run uses bound TS source/Vitest transform; captured C# DLLs in this broad snapshot are not TS output artifacts."
    if kind == "trx":
        root = ET.fromstring(store.read(repo, evidence_path))
        times = root.find("t:Times", NS)
        fields["original_trx_times"] = {"values": times.attrib, "source": store.ref(repo, evidence_path), "xml_selector": "TestRun/Times"}
    return fields


def build(store):
    admission = store.doc("Main", EVIDENCE + "wave5-dispatch-20261013/S01-12-admission.json")
    profiles = store.doc("Main", "eng/contracts/s01/profile-inputs.json")
    frozen = store.doc("Main", "eng/contracts/s01/vectors.json")
    store.ref("Main", "eng/contracts/s01/profile-inputs.json", expected=PROFILE_HASH)
    store.ref("Main", "eng/contracts/s01/vectors.json", expected=VECTOR_HASH)
    require(profiles["package_version"] == frozen["package_version"] == PACKAGE, "Wrong frozen package")
    require(len(profiles["profiles"]) == 16 and len(profiles["sources"]) == 33 and len(frozen["cases"]) == 122, "Wrong frozen input counts")
    require(frozen["execution"] == "NOT_RUN", "Original input execution flag was changed")
    require(len({c["id"] for c in frozen["cases"]}) == 122, "Duplicate frozen vector ID")
    # Resolve every package-local source selector, patch, acceptance and field reference.
    def pins(value):
        if isinstance(value, dict):
            if "path" in value and "sha256" in value and value["path"].startswith(("docs/", "eng/")):
                store.ref("Main", value["path"], value.get("json_pointer"), value["sha256"])
            for child in value.values():
                pins(child)
        elif isinstance(value, list):
            for child in value:
                pins(child)
    pins(profiles)
    pins(frozen)
    source_joins = []
    for source in profiles["sources"]:
        locator = source["selector"]
        selected = pointer(store.doc("Main", locator["path"]), locator["json_pointer"])
        if source["id"].startswith("composed-"):
            pairs = [("original_sha256", "source_sha256"), ("composed_sha256", "derived_sha256"),
                     ("composed_bytes", "derived_bytes"), ("patch_operation_count", "operations")]
            for left, right in pairs:
                require(source[left] == selected[right], f"Source selector mismatch: {source['id']}/{left}")
        else:
            # The remaining six selectors have native integrity/evidence or patch
            # operation layouts; each original hash must occur in its selected record.
            require(source["original_sha256"] in json.dumps(selected, ensure_ascii=False),
                    "Native source selector hash mismatch: " + source["id"])
        source_joins.append({"source_id": source["id"], "selector": store.ref("Main", locator["path"], locator["json_pointer"]),
                             "result": "PINNED_METADATA_JOIN", "original_archive_reextracted": False})
    runs, results = [], {}
    dep_by_task = {d["task"]: d for d in admission["dependency_deliveries"]}
    for name, task, repo, surface, path, kind, expected_count in RUNS:
        ref, rows, counts = read_results(store, repo, path, kind)
        require(counts["passed"] == expected_count and counts["failed"] == counts["skipped"] == 0, "Final run counters differ: " + name)
        results[name] = rows
        runs.append({"id": name, "task": task, "repository": repo, "surface": surface,
                     "evidence": ref, "format": kind, "counts": counts, "execution": "REUSED_ORIGINAL",
                     "original_execution_identity": execution_fields(store, name, task, repo, path, kind, dep_by_task[task]["existing_evidence"]["path"]),
                     "scope": "LOCAL_CONTRACT_ONLY" if name != "S10-PG" else "ISOLATED_POSTGRESQL_FIXTURE_ONLY"})
    runs.append({"id": "S05-TS", "task": "S01-05", "repository": "Main", "surface": "Main:TypeScript",
                 "evidence": store.ref("Main", EVIDENCE + "S01-05/green.log"), "format": "suite-summary",
                 "counts": {"discovered": 202, "executed": 202, "passed": 202, "failed": 0, "skipped": 0},
                 "original_execution_identity": execution_fields(store, "S05-TS", "S01-05", "Main", EVIDENCE + "S01-05/green.log", "suite-summary", dep_by_task["S01-05"]["existing_evidence"]["path"]),
                 "execution": "REUSED_ORIGINAL", "individual_reporter_output": "NOT_CAPTURED"})
    deps = []
    for dep in admission["dependency_deliveries"]:
        task, repo = dep["task"], dep["repository"]
        require(store.git(repo, "merge-base", "--is-ancestor", dep["candidate_sha"], PINS[repo], check=False).returncode == 0,
                "Delivered candidate is not contained in pinned main: " + task)
        record = dep["existing_evidence"]
        receipt = dep["receipt"]
        deps.append({"task": task, "repository": repo, "candidate_sha": dep["candidate_sha"],
            "pinned_main": PINS[repo], "candidate_contained": True, "historical_remote_main": dep["historical_remote_main"],
            "pr_url": dep["pr_url"], "result_scope": dep["existing_result_to_reuse_only"],
            "delivery_receipt": store.ref(receipt["repository"], receipt["path"], expected=receipt["sha256_git_blob_bytes"]),
            "original_evidence": store.ref(repo, record["path"], expected=record["sha256_git_blob_bytes"]),
            "execution_identity_records": original_records(store, task, repo, record["path"]),
            "original_dll_sha256": None if task == "S01-07" else "SEE_ORIGINAL_ARTIFACT_RECORD",
            "original_dll_status": "ORIGINAL_DLL_SHA256_MISSING" if task == "S01-07" else "RECORDED_PER_EXECUTION; NOT_REBUILT",
            "new_execution": False, "real_owner_adoption": "NOT_RUN"})
    source_binding = bindings(store, admission["dependency_deliveries"])
    matrix = make_vectors(store, frozen, profiles, results)
    revision = revisions(store, profiles, matrix, results)
    require({c["revision_case_id"] for c in revision["cases"]} == set(admission["revision_case_ids_to_map"]), "Revision set mismatch")
    bug_tests = [r for r in results["S11-TS"] if r["name"].startswith("Buffer ownership regression #196")]
    require(len(bug_tests) == 12, "Missing final Buffer ownership regressions")
    design = store.doc("Main", S11_DESIGN + "design.json")
    require(design["tests"] == design["passed"] == 5 and design["failed"] == design["skipped"] == 0, "Design audit counts changed")
    crm = crm_revision(store)
    pg = formal_pg(store)
    typecheck_path = S11 + "20261013T103957Z-typecheck-edf96295/metadata.json"
    require(store.doc("Main", typecheck_path)["exit_code"] == 0, "Final S11 typecheck failed")
    crm_results = store.doc("CRM", CRM_EVIDENCE + "executed-source.json")["results"]
    require(crm_results["typecheck_exit"] == 0 and crm_results["importer_integrity"]["passed"] == 6, "Corrected CRM auxiliary checks failed")
    # Profile identities and 33 source/derived hashes remain independently authored
    # input; no implementation result is used as the expected side of a comparison.
    manifest = {"schema": "CP6-S01-ADOPTION-MANIFEST/1", "task": "S01-12",
        "scope": "EVIDENCE_RECONCILIATION_ONLY; no new behavior, product execution or business acceptance",
        "repository_pins": PINS, "package": {"version": PACKAGE, "profiles": 16, "vectors": 122, "sources": 33,
            "profile_input": store.ref("Main", "eng/contracts/s01/profile-inputs.json"),
            "vectors": store.ref("Main", "eng/contracts/s01/vectors.json"), "original_execution": "NOT_RUN"},
        "profiles": [{"id": p["id"], "owner": p["owner"], "selected_sources": p["selected_sources"],
                      "schema_or_type_identity": p["schema_or_type_identity"], "target_consumers": p["target_consumers"],
                      "scope": p["scope"], "input": store.ref("Main", "eng/contracts/s01/profile-inputs.json", f"/profiles/{i}")}
                     for i, p in enumerate(profiles["profiles"])],
        "sources": copy.deepcopy(profiles["sources"]), "source_selector_joins": source_joins,
        "source_read_scope": "Package source/derived hashes, pinned selectors and patch bytes are checked; historical external archive paths are not runtime configuration and are not re-extracted by this comparator.",
        "deliveries": deps, "runs": runs, "recorded_input_to_current_bytes": source_binding,
        "current_bytes_scope": "Compare each recorded source/input/config to pinned main; original raw identity, Git representation and delivery containment are distinct. Captured unrelated files and later shared compilation additions do not imply a combined current-main run.",
        "formal_postgresql": pg, "crm_terminal_revision": crm,
        "additional_original_checks": {"S11_final_typecheck": store.ref("Main", typecheck_path),
            "CRM_typecheck_and_importer6": store.ref("CRM", CRM_EVIDENCE + "executed-source.json", "/results"),
            "CRM_web_split": {"task_contracts": 82, "existing_web": 60, "frozen_vectors": 30, "ui_cases": 49},
            "other_typechecks": [store.ref("Main", EVIDENCE + "S01-05/verification.json", "/runs/3"),
                store.ref("Main", EVIDENCE + "wave3-integration-20261012/S01-06/original-validation.json", "/original_result/type_check"),
                store.ref("Main", EVIDENCE + "S01-07/ts/08-date-regression-validation.json", "/commands/2")]},
        "s11_final": {"typescript_final": "S11-TS", "typescript_historical": 416,
            "buffer_regressions": [observation(r, "S11-TS", "BUFFER_OWNERSHIP") for r in bug_tests],
            "csharp_reused": "S11-CS", "design_reused": store.ref("Main", S11_DESIGN + "design.json"),
            "design_counts": design,
            "unsupported_roots": ["EC_C06_200_StoredTerminal", "EC_C06_202_StoredAdmission", "WholeC06PublicRequest"],
            "native_closure": "Three schema files; F09 absent. No generic C06 root exposure.",
            "bytes_scope": "Retention only; NativeSchemaValidated=false; fixed full PostInput RV is invalid",
            "capacity_risk": "O(n^2) range overlap; maximum-scale acceptance required before real consumer wiring"},
        "unchanged_acceptance": {"business": "0/125", "runtime": "247 NOT_RUN", "static": "41 REFERENCE_ONLY",
            "inventory": "123/125; ITER/REL missing", "WP05": "PROPOSED", "package_publication": "NOT_RUN",
            "all_real_owners_http_writers_pages_storage": "NOT_RUN"},
        "new_operations": {"database": 0, "product_tests": 0, "actions": 0, "network_writes": 0},
        "references": dict(sorted(store.references.items()))}
    return {"manifest.json": manifest, "vectors.json": matrix, "revision-coverage.json": revision}


def verify_documents(actual, expected):
    """Equality to fresh immutable-source derivation also rejects inflated claims."""
    for name in ("manifest.json", "vectors.json", "revision-coverage.json"):
        require(name in actual, "Missing adoption document: " + name)
        require(actual[name] == expected[name], "Derived document differs from independently read source evidence: " + name)


def validate_consumer_document(store, path):
    require(path.exists(), "Mandatory consumers.json is missing")
    doc = decode(path.read_bytes())
    require(doc.get("task") == "S01-12" and doc.get("pinned_mains") == PINS, "Consumer task/pins mismatch")
    pins = doc.get("source_pins", [])
    require(pins and doc.get("current_consumers") and doc.get("contract_capabilities") and doc.get("roadmap_task_refs"), "Empty mandatory consumer inventory")
    pin_ids = {p["id"] for p in pins}
    require(len(pin_ids) == len(pins), "Duplicate consumer source pin")
    for item in pins:
        data = store.read(item["repository"], item["path"], item["commit"])
        expected = item.get("sha256", item.get("sha256_git_blob_bytes"))
        require(expected and sha(data) == expected, "Consumer source hash mismatch: " + item["path"])
        blob = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        require(blob == item["git_blob"] and len(data) == item["bytes"], "Consumer Git blob/length mismatch")
    roadmap = store.doc("Main", "docs/development/roadmap.json")
    tasks = {t["id"]: t for t in roadmap["implementation_tasks"]}
    task_ids = {t["id"] for t in doc["roadmap_task_refs"]}
    require(len(task_ids) == len(doc["roadmap_task_refs"]), "Duplicate consumer task reference")
    for snapshot in doc["roadmap_task_refs"]:
        require(snapshot["id"] in tasks, "Unknown roadmap task reference: " + snapshot["id"])
        actual = tasks[snapshot["id"]]
        require(all(actual.get(k) == v for k, v in snapshot.items()), "Roadmap snapshot differs from pinned original: " + snapshot["id"])
        plan = store.read("Main", snapshot["plan_path"]).decode("utf-8-sig")
        require('<a id="' + snapshot["card_anchor"] + '"></a>' in plan, "Missing task card anchor: " + snapshot["id"])
    consumers = {c["id"] for c in doc["current_consumers"]}
    capabilities = {c["id"] for c in doc["contract_capabilities"]}
    def check_refs(value):
        if isinstance(value, dict):
            for key, child in value.items():
                allowed = {"source_refs": pin_ids, "responsibility_task_ids": task_ids,
                           "consumer_ids": consumers, "available_capability_ids": capabilities}.get(key)
                if allowed is not None:
                    require(isinstance(child, list) and all(i in allowed for i in child), "Dangling consumer " + key)
                check_refs(child)
        elif isinstance(value, list):
            for item in value:
                check_refs(item)
    check_refs(doc)
    handoffs = doc.get("handoffs", [])
    require(len(handoffs) == 6 and {h["slice"] for h in handoffs} == {"S02", "S03", "S05", "S07", "S08", "S09"}, "Six consumer handoff slices required")
    require(all(h.get("responsibility_task_ids") and h.get("consumer_ids") for h in handoffs), "Handoff lacks actionable task/consumer references")
    return {"status": "PASS", "source_pins": len(pins), "task_snapshots": len(task_ids), "handoff_slices": 6,
            "sha256_file_bytes": sha(path.read_bytes()),
            "scope": "Fixed source hashes, reference closure, exact pinned roadmap snapshots and task anchors; owner narrative reviewed separately"}


def report(store, docs, args, status="PASS", error=None):
    refs = {}
    for repo in PINS:
        result = store.git(repo, "rev-parse", "origin/main", check=False)
        observed = result.stdout.decode().strip() if result.returncode == 0 else None
        contains = observed is not None and store.git(repo, "merge-base", "--is-ancestor", PINS[repo], observed, check=False).returncode == 0
        refs[repo] = {"pinned_main": PINS[repo], "observed_local_origin_main": observed,
                      "contains_pin": contains, "network_fetch": False}
    files = {}
    for name in docs:
        path = args.main_repo / OUT / name
        if path.exists():
            files[name] = sha(path.read_bytes())
    return {"schema": "CP6-S01-12-VERIFICATION/1", "observed_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "result": status, "error": error,
            "execution": "READ_ONLY_ORIGINAL_EVIDENCE_RECONCILIATION; not a product test execution",
            "environment": {"python": sys.version, "platform": platform.platform(), "git": store.git("Main", "--version").stdout.decode().strip()},
            "comparator_sha256": sha(Path(__file__).read_bytes()), "input_files_sha256": files,
            "git_references": refs, "reference_files_checked": len(store.references),
            "derived_documents_checked": len(docs),
            "vectors": docs.get("vectors.json", {}).get("counts"),
            "revision_cases": len(docs.get("revision-coverage.json", {}).get("cases", [])),
            "product_tests_executed": 0, "database_operations": 0, "actions_started": 0,
            "new_original_scalar_values": 0, "combined_current_main_product_run": "NOT_RUN",
            "failures_preserved": "Original failed runs are referenced; this result never rewrites their historical outcomes"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--main-repo", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--crm-repo", type=Path, required=True)
    parser.add_argument("--platform-repo", type=Path, required=True)
    parser.add_argument("--write-manifests", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    store = Store({"Main": args.main_repo, "CRM": args.crm_repo, "Platform": args.platform_repo})
    docs = {}
    try:
        docs = build(store)
        if args.write_manifests:
            directory = args.main_repo / OUT
            directory.mkdir(parents=True, exist_ok=True)
            for name, value in docs.items():
                (directory / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        actual = {name: decode((args.main_repo / OUT / name).read_bytes()) for name in docs}
        verify_documents(actual, docs)
        result = report(store, docs, args)
        result["consumer_inventory"] = validate_consumer_document(store, args.main_repo / OUT / "consumers.json")
    except (EvidenceError, KeyError, ValueError, OSError, subprocess.SubprocessError) as exc:
        result = report(store, docs, args, "FAIL", str(exc))
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: result[k] for k in ("result", "error", "reference_files_checked", "vectors", "revision_cases")}, ensure_ascii=False))
    return 0 if result["result"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
