"""Verify the named inquiry fixture metadata revision with no business execution."""

import argparse
from collections import Counter
from datetime import datetime
import json
from pathlib import Path
import re
import sys
from tempfile import NamedTemporaryFile

ADDENDUM = Path(__file__).resolve().parent
HANDBOOK = ADDENDUM.parents[2]
PROFILE_ID = "CRM-INQ-FIXTURE-INTEGRITY/20261112-R1"
sys.dont_write_bytecode = True
sys.path.insert(0, str(HANDBOOK / "tools"))
from validate_design_completion import apply_operations, pointer, sha


def require(condition, message):
    if not condition:
        raise ValueError(message)


def parse(raw):
    def unique_members(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, f"Duplicate JSON member {key}")
            result[key] = value
        return result

    def invalid_constant(token):
        raise ValueError(f"Invalid JSON number {token}")

    return json.loads(raw.decode("utf-8"), object_pairs_hook=unique_members, parse_constant=invalid_constant)


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def require_members(value, fields, where):
    require(isinstance(value, dict), f"{where} must be an object")
    for key, kind in fields.items():
        require(key in value and isinstance(value[key], kind), f"{where}.{key} has an invalid shape")


def keyed_rows(entries, where):
    result = {}
    for row in entries:
        require_members(row, {"table": str, "key": dict, "columns": dict, "rowIdentity": str}, where)
        key, columns = row["key"], row["columns"]
        require(row["table"] and key and all(name in columns and columns[name] == value for name, value in key.items()),
                f"{where} has a row key that disagrees with its columns")
        require(re.fullmatch(r"[0-9a-f]{64}", row["rowIdentity"]), f"{where} has an invalid row identity")
        identity = row["table"], canonical(key)
        require(identity not in result, f"{where} has a duplicate table and primary key")
        result[identity] = row
    return result


def write_delta(before, after):
    result = []
    for identity in sorted(before.keys() | after.keys()):
        old, new = before.get(identity), after.get(identity)
        if old == new:
            continue
        row = new if new is not None else old
        operation = "INSERT" if old is None else "DELETE" if new is None else "UPDATE"
        change = {"operation": operation, "table": row["table"], "key": row["key"]}
        if operation == "UPDATE":
            old_columns, new_columns = old["columns"], new["columns"]
            change["changedColumns"] = sorted(
                key for key in old_columns.keys() | new_columns.keys()
                if old_columns.get(key) != new_columns.get(key)
            )
        result.append(change)
    return result


def validate_step(step, case_id):
    require_members(step, {
        "caseId": str, "name": str, "method": str, "path": str, "request": dict, "actor": (dict, type(None)),
        "dbBefore": list, "dbBeforeDigest": str, "dbAfter": list, "dbAfterDigest": str, "writeSet": list,
        "expectedHttp": dict, "expectedAudit": list, "expectedOutbox": list, "expectedReplay": list,
        "uiExpectation": (dict, str), "transaction": dict, "derivedBy": str, "mutation": dict,
    }, case_id)
    require(step["caseId"] == case_id and step["name"] and step["derivedBy"], f"{case_id} has invalid step identity")
    require(step.get("executionStatus", "NOT_RUN") == "NOT_RUN", f"{case_id} asserts fixture execution")
    clocks = [step[field] for field in ("clock", "clockUtc") if field in step]
    require(len(clocks) == 1 and isinstance(clocks[0], str) and "T" in clocks[0] and clocks[0].endswith("Z"),
            f"{case_id} must supply one UTC clock")
    require(datetime.fromisoformat(clocks[0][:-1]).tzinfo is None, f"{case_id} clock is not UTC")
    require_members(step["mutation"], {"kind": str, "validatedBy": str}, f"{case_id}.mutation")

    request, response = step["request"], step["expectedHttp"]
    require_members(request, {"method": str, "path": str}, f"{case_id}.request")
    require_members(response, {"status": int, "body": dict}, f"{case_id}.expectedHttp")
    require(step["method"] and request["method"] == step["method"] and step["path"].startswith("/")
            and request["path"].split("?", 1)[0] == step["path"].split("?", 1)[0],
            f"{case_id} request method or route disagrees with the step")
    require(100 <= response["status"] <= 599, f"{case_id} has an invalid HTTP status")
    for name, value in (("request", request), ("expectedHttp", response)):
        if "headers" in value:
            headers = value["headers"]
            require(isinstance(headers, dict) and all(isinstance(header, str)
                    or isinstance(header, list) and all(isinstance(item, str) for item in header)
                    for header in headers.values()), f"{case_id}.{name}.headers has an invalid shape")
    if "bodyDigest" in response:
        require(response["bodyDigest"] == sha(canonical(response["body"])),
                f"{case_id}.expectedHttp.bodyDigest disagrees with the canonical response body")

    before = keyed_rows(step["dbBefore"], f"{case_id}.dbBefore")
    after = keyed_rows(step["dbAfter"], f"{case_id}.dbAfter")
    for phase in ("dbBefore", "dbAfter"):
        require(step[phase + "Digest"] == sha(canonical(step[phase])), f"{case_id}.{phase}Digest disagrees with its rows")
    require(step["writeSet"] == write_delta(before, after), f"{case_id}.writeSet disagrees with the row delta")
    transaction = step["transaction"]
    require_members(transaction, {"isolation": str, "writes": int}, f"{case_id}.transaction")
    writes = transaction["writes"]
    require(type(writes) is int and writes == len(step["writeSet"]),
            f"{case_id}.transaction.writes is {writes}, expected {len(step['writeSet'])}")
    for field, table in (("expectedAudit", "AuditFact"), ("expectedOutbox", "OutboxFact"), ("expectedReplay", "CommandReplay")):
        for row in step[field]:
            require(row in step["dbAfter"] and row["table"] == f"crm_inq.{table}",
                    f"{case_id}.{field} references a row absent from dbAfter")

    stages = transaction.get("executionStages", [])
    require(isinstance(stages, list), f"{case_id}.transaction.executionStages must be an array")
    for stage in stages:
        require_members(stage, {"statement": str, "dbAfter": list}, f"{case_id}.transaction.executionStages")
        keyed_rows(stage["dbAfter"], f"{case_id}.transaction.executionStages.dbAfter")
    return len(stages), int("bodyDigest" in response)


def validate_fixture(document, parent):
    require_members(document, {
        "schema": str, "identity": str, "families": list, "familyCount": int, "stepCount": int,
        "familyProvenance": dict, "semanticValidator": dict,
    }, "fixture")
    require(document["schema"] == parent["schema"] and document["identity"] == parent["revision"],
            "Fixture parent identity or schema differs")
    require(document.get("syntheticOnly") is True and document.get("executionStatus") == "NOT_RUN",
            "Fixture synthetic provenance or execution status differs")
    families = document["families"]
    family_ids = [f"FX{number:02d}" for number in range(1, 29)]
    require(len(families) == document["familyCount"] == len(family_ids)
            and set(document["familyProvenance"]) == set(family_ids), "Fixture family coverage differs")
    validators = document["semanticValidator"].get("presentValidators")
    require(isinstance(validators, list) and validators and all(isinstance(path, str) and path for path in validators),
            "Fixture validator provenance is missing")

    steps_checked = stages_checked = body_digests_checked = 0
    for family, family_id in zip(families, family_ids):
        require_members(family, {"familyId": str, "title": str, "steps": list}, family_id)
        require(family["familyId"] == family_id and family.get("syntheticOnly") is True
                and family.get("executionStatus") == "NOT_RUN", f"{family_id} has invalid family provenance")
        provenance = document["familyProvenance"][family_id]
        require_members(provenance, {"stepCount": int, "transition": str}, f"{family_id} provenance")
        require(family["steps"] and len(family["steps"]) == provenance["stepCount"] and provenance["transition"],
                f"{family_id} step coverage disagrees with its provenance")
        for index, step in enumerate(family["steps"], 1):
            stages, body_digests = validate_step(step, f"{family_id}-S{index:02d}")
            steps_checked += 1
            stages_checked += stages
            body_digests_checked += body_digests
    require(steps_checked == document["stepCount"] == 136, "Fixture step coverage differs")
    return {"families": len(families), "steps": steps_checked, "stage_snapshots": stages_checked,
            "response_body_digests": body_digests_checked}


def derive(raw, profile):
    require(sha(raw) == profile["parent_fixture"]["sha256"], "Parent fixture SHA differs")
    parent = parse(raw)
    operations = profile["operations"]
    paths = set()
    fields = Counter()
    for op in operations:
        path = op["path"]
        step_path, group, field = path.rsplit("/", 2)
        require(op["op"] == "replace_json" and path not in paths
                and re.fullmatch(r"/families/[0-9]+/steps/[0-9]+", step_path),
                "Each operation must replace one exact fixture step field")
        step = pointer(parent, step_path)
        require(step["caseId"] == op["case_id"], f"Operation step identity differs at {path}")
        if (group, field) == ("transaction", "writes"):
            expected = len(step["writeSet"])
        elif (group, field) == ("expectedHttp", "bodyDigest"):
            expected = sha(canonical(step["expectedHttp"]["body"]))
        else:
            raise ValueError(f"Operation exceeds fixture metadata scope at {path}")
        require(op["after"] == expected and op["before"] != expected, f"Operation is not the derived value at {path}")
        paths.add(path)
        fields[field] += 1
    require(fields == {"writes": 7, "bodyDigest": 7}, "The fourteen scoped metadata replacements are required")
    effective = apply_operations(raw, operations)
    inverse = [{**op, "before": op["after"], "after": op["before"]} for op in operations]
    require(apply_operations(effective, inverse) == raw, "Metadata replacements do not restore the exact parent bytes")
    return effective


def write_receipt(path, receipt):
    temporary = None
    try:
        with NamedTemporaryFile(mode="wb", dir=path.parent, prefix=".fixture-validation-", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write((json.dumps(receipt, indent=2) + "\n").encode("utf-8"))
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    inputs = parser.add_mutually_exclusive_group(required=True)
    inputs.add_argument("--parent", type=Path, help="Exact W82 fixture to derive in memory")
    inputs.add_argument("--fixture", type=Path, help="Effective fixture to validate without applying replacements")
    parser.add_argument("--report", type=Path, required=True, help="External receipt replaced by this validation attempt")
    parser.add_argument("--output", type=Path, help="Optional new external file for the derived fixture")
    args = parser.parse_args()
    input_path = (args.parent or args.fixture).resolve()
    report = args.report.resolve()
    output = args.output.resolve() if args.output else None
    outputs = [path for path in (report, output) if path is not None]
    if args.fixture and output or len(set(outputs)) != len(outputs) or any(
        path == input_path or path.is_relative_to(HANDBOOK.parents[1]) for path in outputs
    ):
        parser.error("Provide distinct external paths. Only derivation can create a new fixture.")

    scope = "STATIC_FIXTURE_METADATA_AND_SHAPE"
    if report.exists():
        try:
            previous = parse(report.read_bytes())
        except (OSError, ValueError):
            parser.error("The report path already contains a different artifact")
        if not isinstance(previous, dict) or previous.get("profile_id") != PROFILE_ID or previous.get("scope") != scope:
            parser.error("The report path already contains a different artifact")

    receipt = {"profile_id": PROFILE_ID, "scope": scope, "result": "NOT_RUN",
               "business_execution": "NOT_RUN", "crm_adoption": "NOT_RUN"}
    write_receipt(report, receipt)
    try:
        profile = parse((ADDENDUM / "profile.json").read_bytes())
        require(profile["profile_id"] == PROFILE_ID, "Fixture revision profile differs")
        raw = input_path.read_bytes()
        receipt["input_sha256"] = sha(raw)
        effective = derive(raw, profile) if args.parent else raw
        receipt["checks"] = validate_fixture(parse(effective), profile["parent_fixture"])
        require(sha(effective) == profile["effective_fixture"]["sha256"], "Effective fixture SHA differs")
        receipt["effective_sha256"] = sha(effective)
        if args.parent:
            receipt["metadata_replacements"] = len(profile["operations"])
            receipt["parent_bytes_restored_by_inverse"] = True
        if output is not None:
            with output.open("xb") as stream:
                stream.write(effective)
    except (ValueError, OSError, KeyError, IndexError, TypeError) as error:
        receipt["result"] = "FAIL"
        receipt["error"] = str(error)
    else:
        receipt["result"] = "PASS"
    write_receipt(report, receipt)
    print(json.dumps(receipt, ensure_ascii=False))
    return 0 if receipt["result"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
