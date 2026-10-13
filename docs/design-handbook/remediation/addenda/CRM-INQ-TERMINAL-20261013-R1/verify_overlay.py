"""Read-only, standard-library verifier and deterministic effective-schema generator.

Default/--check prints static evidence JSON. --emit-effective writes schema bytes
to stdout only. The caller owns any output redirection; source files are never written.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys


PROFILE = "CRM-INQ-TERMINAL-PROJECTION/20261013-R1"
PREFIX = "/components/schemas/PrivacyExportView/allOf/3/then"
PATHS = [PREFIX + "/properties/expiresAtUtc", PREFIX + "/x-semantic"]
NATIVE_SHA = "740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349"
NEW_SEMANTIC = (
    "requestedAtUtc <= completedAtUtc; public expiresAtUtc, artifactId and downloadProof "
    "are null; retain the historical expiry clock in the internal original record only, "
    "never as current download authorization"
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key: " + key)
        result[key] = value
    return result


def parse(data):
    return json.loads(data, object_pairs_hook=no_duplicates)


def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def pointer(document, path):
    current = document
    require(path.startswith("/"), "Absolute JSON pointer required")
    for token in path[1:].split("/"):
        token = token.replace("~1", "/").replace("~0", "~")
        current = current[int(token)] if isinstance(current, list) else current[token]
    return current


def references(value):
    if isinstance(value, dict):
        for key, child in value.items():
            if key == "$ref":
                yield child
            yield from references(child)
    elif isinstance(value, list):
        for child in value:
            yield from references(child)


def apply(base_bytes, patch):
    require(sha(base_bytes) == NATIVE_SHA, "Native source hash mismatch")
    require(patch["profile_id"] == PROFILE, "Unknown overlay profile")
    require(patch["expected_operation_count"] == 2, "Expected exactly two operations")
    operations = patch["operations"]
    require([op["path"] for op in operations] == PATHS, "Operation paths/order drift")
    require([op["value"] for op in operations] == [{"type": "null"}, NEW_SEMANTIC],
            "Replacement values drift")
    original = parse(base_bytes)
    locator = patch["base_native_schema_locator"]
    require(locator == {
        "source_id": "composed-20", "variant": "composed",
        "openapi": original["openapi"], "title": original["info"]["title"],
        "version": original["info"]["version"], "schema_root": "/components/schemas",
        "schema_sha256": NATIVE_SHA,
    }, "Native source locator mismatch")
    effective = copy.deepcopy(original)
    for op in operations:
        require(op["op"] == "replace" and op["expected_application_count"] == 1,
                "Each exact replacement must apply once")
        require(pointer(effective, op["path"]) == op["expected_old"],
                "Old value mismatch: " + op["path"])
        parent, key = op["path"].rsplit("/", 1)
        pointer(effective, parent)[key] = copy.deepcopy(op["value"])
    restored = copy.deepcopy(effective)
    for op in operations:
        parent, key = op["path"].rsplit("/", 1)
        pointer(restored, parent)[key] = copy.deepcopy(op["expected_old"])
    require(restored == original, "Unregistered semantic mutation")
    require(len(original["components"]["schemas"]) == len(effective["components"]["schemas"]) == 38,
            "Definition count drift")
    before_refs, after_refs = list(references(original)), list(references(effective))
    require(before_refs == after_refs and len(after_refs) == 257, "Reference set/count drift")
    require(after_refs.count("#/components/schemas/PrivacyExportView") == 7,
            "PrivacyExportView reference count drift")
    for ref in after_refs:
        require(ref.startswith("#/"), "Unexpected external reference")
        pointer(effective, ref[1:])
    view = effective["components"]["schemas"]["PrivacyExportView"]
    for index in [0, 1, 2]:
        require(view["allOf"][index] == original["components"]["schemas"]["PrivacyExportView"]["allOf"][index],
                "Status 20/30/90 branch changed")
    for index in [2, 3]:
        properties = view["allOf"][index]["then"]["properties"]
        for field in ["expiresAtUtc", "artifactId", "downloadProof"]:
            require(properties[field] == {"type": "null"}, "Terminal public field must be null")
            require(field in view["required"], "Terminal field must remain required")
    require(view["additionalProperties"] is False, "Closed payload changed")
    result = encode(effective)
    require(sha(result) == patch["effective_schema_sha256"], "Effective Schema hash mismatch")
    return result


def verify_identity(base_bytes, patch_bytes, profile):
    patch = parse(patch_bytes)
    require(profile["profile_id"] == PROFILE, "Unknown effective profile")
    require(profile["base_native_schema_locator"] == patch["base_native_schema_locator"],
            "Composite base identity mismatch")
    require(profile["patch"] == {"path": "overlay.json", "sha256": sha(patch_bytes),
                                   "hash_basis": "RAW_FILE_BYTES"}, "Composite patch identity mismatch")
    result = apply(base_bytes, patch)
    require(profile["effective_schema"] == {
        "generated_name": "crm-inq.terminal.openapi.json", "sha256": sha(result),
        "hash_basis": "GENERATED_UTF8_BYTES"}, "Composite effective identity mismatch")
    require(profile["payload_version_field"] is None, "Do not add a payload version field")
    return result


def rejected(action):
    try:
        action()
    except (ValueError, KeyError, IndexError, TypeError):
        return True
    return False


def mutation_checks(base_bytes, patch_bytes, profile):
    outcomes = {}
    outcomes["changed_base_bytes"] = rejected(lambda: verify_identity(base_bytes + b" ", patch_bytes, profile))
    outcomes["changed_patch_bytes"] = rejected(lambda: verify_identity(base_bytes, patch_bytes + b" ", profile))
    for name in ["unknown_profile", "wrong_native_identity", "wrong_patch_hash", "wrong_effective_hash"]:
        changed = copy.deepcopy(profile)
        if name == "unknown_profile":
            changed["profile_id"] += "-unknown"
        elif name == "wrong_native_identity":
            changed["base_native_schema_locator"]["version"] = "unknown"
        elif name == "wrong_patch_hash":
            changed["patch"]["sha256"] = "0" * 64
        else:
            changed["effective_schema"]["sha256"] = "0" * 64
        outcomes[name] = rejected(lambda: verify_identity(base_bytes, patch_bytes, changed))
    patch = parse(patch_bytes)
    changed = copy.deepcopy(patch)
    changed["operations"][0]["expected_old"] = {"type": "null"}
    outcomes["wrong_old_value"] = rejected(lambda: apply(base_bytes, changed))
    changed = copy.deepcopy(patch)
    changed["operations"].append(copy.deepcopy(changed["operations"][0]))
    outcomes["extra_operation"] = rejected(lambda: apply(base_bytes, changed))
    require(all(outcomes.values()), "A negative identity/bounds check was accepted")
    return outcomes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-schema", type=Path, required=True)
    parser.add_argument("--authority-root", type=Path, required=True,
                        help="Main checkout with the pinned authority commit available locally")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true")
    mode.add_argument("--emit-effective", action="store_true")
    args = parser.parse_args()
    directory = Path(__file__).resolve().parent
    patch_bytes = (directory / "overlay.json").read_bytes()
    profile_bytes = (directory / "profile.json").read_bytes()
    base_bytes = args.base_schema.read_bytes()
    patch, profile = parse(patch_bytes), parse(profile_bytes)
    effective = verify_identity(base_bytes, patch_bytes, profile)
    authority_results = []
    for authority in patch["source_authority"]:
        raw = subprocess.check_output([
            "git", "-c", "core.longpaths=true", "show",
            authority["commit"] + ":" + authority["path"]], cwd=args.authority_root)
        require(sha(raw) == authority["sha256"], "Authority hash mismatch")
        lines = raw.decode("utf-8").splitlines()
        for line in authority["lines"]:
            require(lines[line["number"] - 1] == line["text"], "Authority line mismatch")
        authority_results.append({"path": authority["path"], "sha256": sha(raw), "lines_verified": True})
    mutations = mutation_checks(base_bytes, patch_bytes, profile)
    require(args.base_schema.read_bytes() == base_bytes, "Base source changed during verification")
    if args.emit_effective:
        sys.stdout.buffer.write(effective)
        return
    report = {
        "result": "PASS", "scope": "STATIC_OVERLAY_IDENTITY_AND_SCHEMA_INVARIANTS_ONLY",
        "profile_id": PROFILE, "base_schema_sha256": sha(base_bytes),
        "patch_sha256": sha(patch_bytes), "profile_manifest_sha256": sha(profile_bytes),
        "verifier_sha256": sha(Path(__file__).read_bytes()), "effective_schema_sha256": sha(effective),
        "effective_schema_bytes": len(effective), "applications": 2, "definitions": 38,
        "references": 257, "resolved_references": 257, "privacy_export_view_references": 7,
        "unregistered_changes": 0, "status_20_30_90_unchanged": True,
        "authority": authority_results, "negative_checks": mutations,
        "base_source_unchanged": True, "schema_written": False,
        "consumer_adoption": "NOT_RUN", "business_validation": "NOT_RUN",
    }
    sys.stdout.buffer.write(encode(report))


if __name__ == "__main__":
    main()
