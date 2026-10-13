"""Read-only S01-03 admission receipt. Does not run archived code or mutate inputs."""
import hashlib
import json
from pathlib import Path
import subprocess
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[4]
OUT = Path(__file__).with_name("entry-check.json")


def git(*args):
    return subprocess.check_output(["git", "-c", "core.longpaths=true", *args], cwd=ROOT)


def pointer(value, path):
    for part in path.lstrip("/").split("/"):
        part = part.replace("~1", "/").replace("~0", "~")
        value = value[int(part)] if isinstance(value, list) else value[part]
    return value


dispatch = json.loads((ROOT / "docs/development/dispatch/20261012-wave3/dispatch.json").read_bytes())
task = next(x for x in dispatch["tasks"] if x["id"] == "S01-03")
head = git("rev-parse", "HEAD").decode().strip()
branch = git("branch", "--show-current").decode().strip()
assert branch == task["branch"]
assert git("rev-parse", "origin/main").decode().strip() == head
pins = []
for pin in dispatch["input_pins"]:
    raw = git("show", f"{head}:{pin['path']}")
    digest = hashlib.sha256(raw).hexdigest()
    assert digest == pin["sha256"], pin["path"]
    assert raw == (ROOT / pin["path"]).read_bytes(), pin["path"]
    pins.append({**pin, "actual_sha256": digest, "worktree_bytes_match": True})
manifest = json.loads((ROOT / "eng/contracts/s01/profile-inputs.json").read_bytes())
vectors = json.loads((ROOT / "eng/contracts/s01/vectors.json").read_bytes())
assert manifest["package_version"] == vectors["package_version"] == dispatch["input_package_version"]
cases = [c for c in vectors["cases"] if c["profile_id"] in task["profiles"]]
assert sorted(c["id"] for c in cases) == sorted(task["case_ids"])
assert len(cases) == len(set(c["id"] for c in cases)) == 25
assert all(c["operation"] == "lexical" for c in cases)
storage_path = "docs/design-handbook/remediation/assets/core-types/numeric-storage-1.0.0.json"
storage = json.loads(git("show", f"{head}:{storage_path}"))
native_source = storage["source_integrity"][0]
native_bytes = Path(native_source["path"]).read_bytes()
assert hashlib.sha256(native_bytes).hexdigest() == native_source["sha256"]
native = json.loads(native_bytes)
profiles = []
for selected in task["selected_profiles"]:
    profile = next(p for p in manifest["profiles"] if p["id"] == selected["id"])
    assert profile == selected
    identity = selected["schema_or_type_identity"]
    assert native["$id"] == identity["schema_id"]
    definition = pointer(native, identity["json_pointer"])
    assert definition == identity["definition"]
    for binding in selected["native_field_bindings"]:
        field = pointer(native, binding)
        assert field == definition or field.get("$ref") == "#" + identity["json_pointer"], binding
    profiles.append({"id": selected["id"], "schema_id": native["$id"],
                     "definition_pointer": identity["json_pointer"],
                     "field_bindings": selected["native_field_bindings"],
                     "fixed_case_ids": [c["id"] for c in cases if c["profile_id"] == selected["id"]]})
roadmap = json.loads((ROOT / "docs/development/roadmap.json").read_bytes())


def find_task(value, task_id):
    if isinstance(value, dict):
        if value.get("id") == task_id:
            return value
        values = value.values()
    elif isinstance(value, list):
        values = value
    else:
        return None
    return next((found for item in values if (found := find_task(item, task_id))), None)


assigned = find_task(roadmap, "S01-03")
predecessor = find_task(roadmap, "S01-02")
assert assigned["entry_gate"] == "CONFIRMED"
assert assigned["assignment"]["branch"] == branch
assert assigned["assignment"]["allowed_write_paths"] == task["write_paths"]
assert predecessor["state"] == "DONE"
shared_paths = ["CP6.Entity/CP6.Entity.csproj", "CP6.Entity/packages.lock.json", "Directory.Build.props",
                "eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj",
                "eng/contracts/exact-values-tests/StrictJsonTests.cs",
                "CP6.Entity/Contracts/Wire/StrictJson.cs", "CP6.Entity/Contracts/Wire/NullOnly.cs",
                "CP6.Entity/Contracts/Wire/JsonSemanticEquality.cs",
                "CP6.Core/Services/Fin/CreditControlService.cs", "CP6.Core/Services/Fin/ICreditControlService.cs"]
shared_pins = [{"path": path, "sha256_git_blob": hashlib.sha256(git("show", f"{head}:{path}")).hexdigest()}
               for path in shared_paths]
receipt = {"task": "S01-03", "checked_at_utc": datetime.now(timezone.utc).isoformat(),
           "head": head, "branch": branch, "main_and_origin_main": head,
           "coordination_sha": "ad1260fd0de832817d52522c6111df6bacd788b3",
           "created_baseline": task["baseline"], "only_coordination_documents_changed_since_creation":
           git("diff", "--name-only", task["baseline"], head).decode().splitlines(),
           "entry_gate": assigned["entry_gate"], "predecessor_state": predecessor["state"],
           "write_paths": task["write_paths"], "input_package_version": manifest["package_version"],
           "input_pins": pins, "native_source": native_source, "profiles": profiles,
           "shared_read_only_pins": shared_pins, "result": "PASS",
           "limits": "Read-only source/assignment admission, no business test, DB, Actions or deployment."}
OUT.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"result": "PASS", "head": head, "fixed_cases": len(cases),
                  "native_bindings": [len(p["field_bindings"]) for p in profiles], "receipt": str(OUT)}, ensure_ascii=False))
