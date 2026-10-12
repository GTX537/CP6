"""Reproduce S01 expected vectors from repository inputs; never run archived code.

--check compares the checked-in artifact and resolves every source reference.
--verify-native-fields additionally reads hash-pinned schema archives as data.
This checks packaging only. No consumer, codec, database or Owner is executed.
Added inputs are contract unit fixtures, not captured business requests/results.
"""

import argparse
import hashlib
import json
import subprocess
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
OUTPUT = ROOT / "eng/contracts/s01/vectors.json"
CORE = "docs/design-handbook/remediation/assets/core-types/"
NUMERIC = CORE + "numeric-validation.json"
ATP = CORE + "atp-stableid-vectors.json"
CHECKS = CORE + "DesignTypesCheck.cs"
PLAN = "docs/superpowers/plans/2026-10-11-s01-task-breakdown.md"
PATCHES = "docs/design-handbook/remediation/assets/commercial-fixtures/bounded-patches.json"
CORE_PATCHES = CORE + "bounded-patches.json"
INDEX = "docs/development/s01-input-index.json"
MANIFEST = "eng/contracts/s01/profile-inputs.json"
VERSION = "CP6-S01-CONTRACT-INPUTS/1.0.0"


def read_json(path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def git_bytes(*args):
    return subprocess.run(
        ["git", "-c", "core.longpaths=true", *args], cwd=ROOT,
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    ).stdout


def source_pin(path):
    if path == MANIFEST:
        return {"path": path, "sha256": hashlib.sha256((ROOT / path).read_bytes()).hexdigest(),
                "hash_basis": "file_bytes"}
    blob = git_bytes("rev-parse", f"HEAD:{path}").decode("ascii").strip()
    worktree_blob = git_bytes("hash-object", f"--path={path}", path).decode("ascii").strip()
    if worktree_blob != blob:
        raise ValueError(f"Vector source differs from committed Git blob: {path}")
    return {"path": path, "sha256": hashlib.sha256(git_bytes("cat-file", "blob", blob)).hexdigest(),
            "git_blob": blob, "hash_basis": "git_blob_bytes"}


def pointer(path, value):
    return {"path": path, "json_pointer": value}


def line_ref(path, text):
    lines = (ROOT / path).read_text(encoding="utf-8-sig").splitlines()
    matches = [i + 1 for i, line in enumerate(lines) if text in line]
    if len(matches) != 1:
        raise ValueError(f"Expected one source line for {path}: {text!r}")
    return {"path": path, "line": matches[0]}


PROFILES = {
    "signedDecimal": "fin-credit-signed",
    "nonNegativeDecimal": "fin-credit-nonnegative",
    "positiveDecimal": "fin-credit-positive",
    "Decimal18": "fin-inv-decimal18",
    "Qty12": "fin-inv-qty12",
    "ATP.StableId": "atp-stableid-r1",
}
ATP_FIELD_POINTERS = [
    "/$defs/Quantity/properties/uom", "/$defs/DemandRef/properties/uom",
    "/$defs/ConversionRule/properties/fromUom", "/$defs/ConversionRule/properties/toUom",
    "/$defs/AtpPolicySet/properties/resultUom",
]


def lexical_fields(manifest):
    profiles = {profile["id"]: profile for profile in manifest["profiles"]}
    fields = {}
    for wire_type, profile_id in PROFILES.items():
        profile = profiles[profile_id]
        identity = profile["schema_or_type_identity"]
        pointers = ATP_FIELD_POINTERS if wire_type == "ATP.StableId" else profile["native_field_bindings"]
        if not pointers:
            raise ValueError(f"Native field bindings are empty: {profile_id}")
        fields[wire_type] = [identity["schema_id"] + "#" + ptr for ptr in pointers]
    return fields


def resolve_pointer(node, value):
    if value and not value.startswith("/"):
        raise ValueError(f"Invalid JSON pointer: {value}")
    for token in value.split("/")[1:]:
        key = token.replace("~1", "/").replace("~0", "~")
        node = node[int(key)] if isinstance(node, list) else node[key]
    return node


def verify_native_fields(package):
    """Optional source audit; reads archives, never executes their programs."""
    manifest = read_json(MANIFEST)
    profiles = {profile["id"]: profile for profile in manifest["profiles"]}
    sources = {source["id"]: source for source in manifest["sources"]}
    schemas = {}
    checked_fields = set()
    for case in package["cases"]:
        if case["operation"] != "lexical":
            continue
        identity = profiles[case["profile_id"]]["schema_or_type_identity"]
        source = sources[identity["source_id"]]
        variant = identity["variant"]
        cache_key = (source["id"], variant)
        if cache_key not in schemas:
            selector = source["selector"]
            locator = resolve_pointer(read_json(selector["path"]), selector["json_pointer"])
            source_path = locator["local_derived_path"] if variant == "composed" else locator.get("source_path", locator.get("path"))
            raw = Path(source_path).read_bytes()
            expected_hash = source["composed_sha256"] if variant == "composed" else source["original_sha256"]
            if hashlib.sha256(raw).hexdigest() != expected_hash:
                raise ValueError(f"Native schema archive hash differs: {source['id']}")
            schemas[cache_key] = json.loads(raw.decode("utf-8-sig"))
        schema = schemas[cache_key]
        if schema["$id"] != identity["schema_id"]:
            raise ValueError(f"Native schema identity differs: {source['id']}")
        scalar = resolve_pointer(schema, identity["json_pointer"])
        if scalar != identity["definition"]:
            raise ValueError(f"Native scalar definition differs: {case['profile_id']}")
        for field in case["applicable_fields"]:
            schema_id, field_pointer = field.split("#", 1)
            if schema_id != schema["$id"]:
                raise ValueError(f"Lexical field has incorrect schema identity: {field}")
            binding = resolve_pointer(schema, field_pointer)
            if "$ref" in binding:
                if binding["$ref"] != "#" + identity["json_pointer"]:
                    raise ValueError(f"Lexical field selects another scalar profile: {field}")
            elif any(binding.get(key) != scalar.get(key) for key in ("type", "pattern")):
                raise ValueError(f"Lexical field has another inline scalar definition: {field}")
            checked_fields.add(field)
    return {"scope": "static source hash/pointer/profile audit only", "schema_files": len(schemas),
            "distinct_field_pointers": len(checked_fields)}


def build():
    cases = []
    fields = lexical_fields(read_json(MANIFEST))

    def add(id_, profile, operation, fields, ref, input_, expected, **extra):
        cases.append({
            "id": id_, "profile_id": profile, "operation": operation,
            "applicable_fields": fields,
            "verification_level": extra.pop("verification_level", "CONTRACT_UNIT"),
            "source_case_id": None, "source_ref": ref,
            "input": input_, "expected": expected, **extra,
        })

    originals = read_json(NUMERIC)["cases"]
    if len(originals) != 23:
        raise ValueError("The pinned numeric input must have exactly 23 cases")
    for i, case in enumerate(originals):
        wire_type = case["wire_type"]
        expected = {key: case[key] for key in ("accepted", "pair") if key in case}
        add(f"NUM-{i + 1:03}", PROFILES[wire_type], "lexical", fields[wire_type],
            pointer(NUMERIC, f"/cases/{i}"), case["input"], expected, wire_type=wire_type)

    # These expectations come from the accepted field profiles and task plan,
    # not from a consumer implementation being exercised by this generator.
    credit_ref = line_ref(PLAN, "测试 38 个 9")
    credit = [
        ("nonNegativeDecimal", "0", True, "0", 0),
        ("nonNegativeDecimal", "1", True, "1", 0),
        ("positiveDecimal", "0.00000001", True, "1", 8),
        ("positiveDecimal", "-1", False, None, None),
        ("signedDecimal", "01", False, None, None),
        ("signedDecimal", "9" * 39, False, None, None),
        ("nonNegativeDecimal", "9" * 38, True, "9" * 38, 0),
        ("positiveDecimal", "9" * 30 + ".12345678", True, "9" * 30 + "12345678", 8),
        ("nonNegativeDecimal", "-0", False, None, None),
        ("positiveDecimal", "1.0", False, None, None),
        ("nonNegativeDecimal", "1e3", False, None, None),
    ]
    for i, (wire_type, value, accepted, coefficient, scale) in enumerate(credit):
        add(f"CREDIT-{i + 1:03}", PROFILES[wire_type], "lexical", fields[wire_type],
            credit_ref, value, {"accepted": accepted, "pair":
                {"unscaledValue": coefficient, "scale": scale} if accepted else None},
            wire_type=wire_type)

    # The plan explicitly includes both sides of the JavaScript exact-integer boundary.
    for i, value in enumerate(("9007199254740991", "9007199254740992", "9007199254740993")):
        add(f"CREDIT-JS-{i + 1:03}", "fin-credit-signed", "lexical", fields["signedDecimal"],
            line_ref(PLAN, "覆盖 JS 安全整数边界两侧"), value,
            {"accepted": True, "pair": {"unscaledValue": value, "scale": 0}}, wire_type="signedDecimal")

    inv = [
        ("Qty12", "1.1111111111111", False, None),
        ("Qty12", "1.111111111111", True,
            {"unscaledDigits": "1111111111111", "scale": 12, "originalWire": "1.111111111111"}),
        ("Decimal18", "1.111111111111111111", True,
            {"unscaledDigits": "1111111111111111111", "scale": 18, "originalWire": "1.111111111111111111"}),
        ("Decimal18", "-0.000", True,
            {"unscaledDigits": "0", "scale": 0, "originalWire": "-0.000"}),
    ]
    for i, (wire_type, value, accepted, pair) in enumerate(inv):
        add(f"INV-{i + 1:03}", PROFILES[wire_type], "lexical", fields[wire_type],
            line_ref(PLAN, "按 DR-OPS-IF-030"), value,
            {"accepted": accepted, "pair": pair}, wire_type=wire_type)

    for i, case in enumerate(read_json(ATP)["cases"]):
        add(f"ATP-R1-{i + 1:03}", "atp-stableid-r1", "lexical", fields["ATP.StableId"],
            pointer(ATP, f"/cases/{i}"), case["value"], {"accepted": case["valid"]},
            wire_type="ATP.StableId")
    for i, value in enumerate((".EA", "_EA", ":EA", "/EA", "-EA")):
        add(f"ATP-LEADING-{i + 1:03}", "atp-stableid-r1", "lexical", fields["ATP.StableId"],
            line_ref(PLAN, "覆盖 16/17/128 字符接受"), value, {"accepted": False}, wire_type="ATP.StableId")

    strict_cases = [
        ('{"a":1,"b":{"a":2}}', True),
        ('{"a":1,"a":2}', False),
        ('{"outer":{"a":1,"a":2}}', False),
        ('{"items":[{"a":1,"a":2}]}', False),
        ('{"a":1,"\\u0061":2}', False),
        ('{"outer":{"a":1,"\\u0061":2}}', False),
        ('{"a":1,"A":2}', True),
    ]
    for i, (raw_json, accepted) in enumerate(strict_cases):
        add(f"JSON-{i + 1:03}", "strict-json", "strict_json_parse", ["$ (every nested object)"],
            line_ref(PLAN, "重复键在任意嵌套层"), raw_json, {"accepted": accepted})

    null_cases = [('null', True), ('0', False), ('1', False), ('false', False), ('"null"', False), ('{}', False)]
    add("NULL-001", "null-only", "required_null_property", ["NullCarrier.fixedNull"],
        line_ref(CHECKS, "foreach(var text"), {"raw_json": "{}", "required_property": "fixedNull"},
        {"accepted": False})
    for i, (token, accepted) in enumerate(null_cases):
        add(f"NULL-{i + 2:03}", "null-only", "required_null_property", ["NullCarrier.fixedNull"],
            line_ref(CHECKS, '"null-only round trip"') if accepted else line_ref(CHECKS, "foreach(var text"),
            {"raw_json": '{"fixedNull":' + token + '}', "required_property": "fixedNull"}, {"accepted": accepted})

    # Project individual required fields only. These small objects intentionally
    # do not purport to satisfy the other properties of a native business type.
    ncr_fields = [
        ("ncr-main-r6", "originalQuery", 2, 0, [
            f"QaNcrMainR6Declarations.Externalf70281e4_C09FailedProblem_Variant{i}_Payload.originalQuery"
            for i in range(3)]),
        ("ncr-main-r6", "guardRef", 2, 1,
            ["QaNcrMainR6Declarations.External6d9d3eee__types_IpqcFinalGuardResult_Variant1_Payload.guardRef"]),
        ("ncr-cp4", "guardRef", 3, 0, ["SourceQA.IpqcFinalGuardResult_Case2.guardRef"]),
        ("ncr-cp4", "code", 3, 1, ["SourceQA.ObservationInput_Case1.code"]),
        ("ncr-cp4", "value", 3, 2, ["SourceQA.ObservationInput_Case2.value"]),
    ]
    ncr_number = 0
    for profile, field, patch_index, operation_index, fields in ncr_fields:
        for raw, accepted in ((json.dumps({field: None}), True), ("{}", False),
                              (json.dumps({field: 0}), False), (json.dumps({field: {}}), False)):
            ncr_number += 1
            add(f"NCR-NULL-{ncr_number:03}", profile, "required_null_field_projection", fields,
                pointer(CORE_PATCHES, f"/patches/{patch_index}/operations/{operation_index}"),
                {"raw_json": raw, "required_property": field},
                {"accepted": accepted, "assertion_scope": "FIELD_SUBASSERTION_ONLY"})

    lead_cases = [
        ('{"ownerId":null}', True),
        ('{"ownerId":"unassigned"}', True),
        ('{"ownerId":"00000000-0000-4000-8000-000000000001"}', True),
        ('{"ownerId":"arbitrary-owner"}', False),
        ('{}', False),
    ]
    for i, (raw, accepted) in enumerate(lead_cases):
        add(f"LEAD-OWNER-{i + 1:03}", "crm-lead-cp20", "required_owner_id_field_projection",
            ["CP6.CRM.CP21.Contracts.LeadQueryFilters.ownerId",
             "CP20-qualification-h02.schema.json#/$defs/LeadQueryFilters/properties/ownerId"],
            pointer("docs/design-handbook/remediation/00-disposition-register.json", "/issues/25/evidence/1"),
            {"raw_json": raw, "required_property": "ownerId"},
            {"accepted": accepted, "assertion_scope": "FIELD_SUBASSERTION_ONLY"})

    for i, (field, supplied_type, accepted, op_index) in enumerate([
        ("R4FrozenPayload.registrationRef", "QapEvidence", True, 0),
        ("R4FrozenPayload.registrationRef", "ExecEvidence", False, 0),
        ("R4QapFullCountPlanContent.characteristics[].measurementUnitRef", "QapUom", True, 1),
        ("R4QapFullCountPlanContent.characteristics[].measurementUnitRef", "Uom", False, 1),
        ("R4QapFullCountPlanContent.characteristics[].rule.unitRef", "QapUom", True, 1),
        ("R4QapFullCountPlanContent.characteristics[].rule.unitRef", "Uom", False, 1),
    ]):
        add(f"IPQC-TYPE-{i + 1:03}", "ipqc-r4", "native_type_binding", [field],
            pointer(CORE_PATCHES, f"/patches/6/operations/{op_index}"),
            {"supplied_type": supplied_type},
            {"accepted": accepted, "assertion_scope": "TYPE_BINDING_ONLY"},
            verification_level="CONTRACT_ROUTING")

    profiles = read_json(MANIFEST)["profiles"]
    inq_index = next(i for i, profile in enumerate(profiles) if profile["id"] == "crm-inq-r3-v15")
    inq_identity = profiles[inq_index]["schema_or_type_identity"]
    inq_sha = read_json(INDEX)["sources"][20]["derived_sha256"]
    inq_route = {"openapi": inq_identity["openapi"], "title": inq_identity["info"]["title"],
                 "version": inq_identity["info"]["version"], "schema_root": inq_identity["schema_root"],
                 "schema_sha256": inq_sha}
    for i, (changes, accepted) in enumerate([
        ({}, True), ({"version": "unknown-version"}, False),
        ({"schema_root": "/$defs"}, False), ({"schema_sha256": "0" * 64}, False),
    ]):
        add(f"INQ-VERSION-{i + 1:03}", "crm-inq-r3-v15", "native_schema_identity",
            ["crm-inq.openapi.json:info.version", "crm-inq.openapi.json#/components/schemas"],
            pointer(MANIFEST, f"/profiles/{inq_index}/schema_or_type_identity"),
            {**inq_route, **changes}, {"accepted": accepted}, verification_level="CONTRACT_ROUTING")

    equality = [
        ('{"a":1,"b":[true,null]}', '{"b":[true,null],"a":1.0}', True, "property order and exact numeric equality"),
        ('[1,2]', '[2,1]', False, "array order"),
        ('"1"', '1', False, "JSON type distinction"),
        ('-0.00', '0e1000', True, "zero variants"),
        ('12345678901234567890123456789012345678', '123456789012345678901234567890123456780e-1', True, "38 digit equality without double"),
        ('1e1000', '10e999', True, "large exponent no floating point overflow"),
        ('{"a:b":"x,y"}', '{"a":"b:x,y"}', False, "escaped property boundary"),
    ]
    for i, (left, right, equal, source) in enumerate(equality):
        add(f"EQUAL-{i + 1:03}", "json-semantic-equality", "json_semantic_equality",
            ["JSON Schema const/enum/uniqueItems operands"], line_ref(CHECKS, source),
            {"left_raw_json": left, "right_raw_json": right}, {"equal": equal})

    patch = read_json(PATCHES)["patches"][13]
    old_hash = patch["operations"][0]["before"]
    new_hash = patch["operations"][0]["after"]
    old_namespace = patch["operations"][1]["before"].removeprefix("namespace ").removesuffix(";")
    new_namespace = patch["operations"][1]["after"].removeprefix("namespace ").removesuffix(";")
    registry = "ERP-CREDIT-01_TYPE-REGISTRY.json"
    routes = [
        ("erp-credit-current-v1", new_namespace, registry, new_hash, True),
        ("erp-credit-current-v1", new_namespace, registry, old_hash, False),
        ("erp-credit-current-v1", old_namespace, registry, new_hash, False),
        ("erp-credit-current-v1", new_namespace, registry, "0" * 64, False),
        ("erp-credit-current-v1", new_namespace, "ERP-CREDIT-01_TYPE-REGISTRY.unknown.json", new_hash, False),
        ("erp-credit-frozen-r8", old_namespace, registry, old_hash, True),
        ("erp-credit-frozen-r8", old_namespace, registry, new_hash, False),
        ("erp-credit-frozen-r8", new_namespace, registry, old_hash, False),
    ]
    for i, (profile, namespace, file, sha256, accepted) in enumerate(routes):
        add(f"VERSION-{i + 1:03}", profile, "native_registry_identity", [registry, "codec namespace"],
            pointer(PATCHES, "/patches/13"),
            {"codec_namespace": namespace, "schema_file": file, "schema_sha256": sha256},
            {"accepted": accepted}, verification_level="CONTRACT_ROUTING")

    # Native PostInput field names come from the archived codec located by the
    # index. These constructed bytes test only retention; no command validity,
    # authorisation, Owner identity or business effect is asserted.
    original = '{ "commandId": "00000000-0000-4000-8000-000000000001", "expectedRowVersion": "1", "reason": "fixture", "planRevision": "1" }\r\n'
    reformatted = '{"planRevision":"1","reason":"fixture","expectedRowVersion":"1","commandId":"00000000-0000-4000-8000-000000000001"}'
    for i, replay in enumerate((original, reformatted)):
        add(f"BYTES-{i + 1:03}", "erp-credit-frozen-r8", "original_utf8_retention",
            ["ERP-CREDIT-01_FIN-AR-R2-EXACT.schema.json#/$defs/PostInput (bytes only)"],
            pointer(INDEX, "/sources/11"),
            {"native_schema_file": "ERP-CREDIT-01_FIN-AR-R2-EXACT.schema.json",
             "native_schema_root": "PostInput", "fixture_origin": "CONSTRUCTED_CONTRACT_UNIT_INPUT",
             "original_utf8": original, "replay_utf8": replay},
            {"bytes_equal": i == 0, "digest_equal": i == 0,
             "original_sha256": hashlib.sha256(original.encode("utf-8")).hexdigest(),
             "replay_sha256": hashlib.sha256(replay.encode("utf-8")).hexdigest()},
            verification_level="CONTRACT_ROUTING")

    source_pins = [
        source_pin(path)
        for path in sorted({case["source_ref"]["path"] for case in cases})
    ]
    return {"package_version": VERSION, "role": "EXPECTED_VECTORS_NOT_EXECUTION_RESULTS",
            "execution": "NOT_RUN", "source_pins": source_pins, "cases": cases}


def verify_structure(package):
    cases = package["cases"]
    expected_fields = lexical_fields(read_json(MANIFEST))
    if len({case["id"] for case in cases}) != len(cases):
        raise ValueError("Duplicate vector ID")
    source_paths = {case["source_ref"]["path"] for case in cases}
    pins = package["source_pins"]
    if len(pins) != len(source_paths) or {pin["path"] for pin in pins} != source_paths:
        raise ValueError("Every vector source must have exactly one pin")
    for pin in pins:
        if source_pin(pin["path"]) != pin:
            raise ValueError(f"Vector source pin differs: {pin['path']}")
    for case in cases:
        ref = case["source_ref"]
        source = ROOT / ref["path"]
        if not source.is_file() or Path(ref["path"]).is_absolute():
            raise ValueError(f"Source must be a repository-relative file: {ref}")
        if "json_pointer" in ref:
            resolve_pointer(read_json(ref["path"]), ref["json_pointer"])
        elif not 1 <= ref["line"] <= len(source.read_text(encoding="utf-8-sig").splitlines()):
            raise ValueError(f"Source line out of range: {ref}")
        if case["verification_level"] not in ("CONTRACT_UNIT", "CONTRACT_ROUTING"):
            raise ValueError("Unexpected verification level")
        if "result" in case or "result" in case["expected"]:
            raise ValueError("Historical result must not enter expected vectors")
        if case["operation"] == "lexical":
            if case["profile_id"] != PROFILES[case["wire_type"]]:
                raise ValueError(f"Lexical profile differs from wire type: {case['id']}")
            if case["applicable_fields"] != expected_fields[case["wire_type"]]:
                raise ValueError(f"Lexical fields differ from native bindings: {case['id']}")
    for source, case in zip(read_json(NUMERIC)["cases"], cases[:23], strict=True):
        if any(source[key] != case[key] for key in ("input", "wire_type")):
            raise ValueError("Original numeric input changed")
        expected = {key: source[key] for key in ("accepted", "pair") if key in source}
        if case["expected"] != expected:
            raise ValueError("Original numeric expectation changed")
    manifest_profiles = {profile["id"] for profile in read_json(MANIFEST)["profiles"]}
    if {case["profile_id"] for case in cases} != manifest_profiles:
        raise ValueError("Every manifest profile must have scoped expected vectors")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--verify-native-fields", action="store_true",
                        help="Audit lexical field pointers against hash-verified local schema archives")
    args = parser.parse_args()
    package = build()
    verify_structure(package)
    native_audit = verify_native_fields(package) if args.verify_native_fields else "NOT_REQUESTED"
    content = (json.dumps(package, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    if args.write:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_bytes(content)
    elif OUTPUT.read_bytes() != content:
        raise SystemExit("vectors.json differs from reproducible expected artifact")
    print(json.dumps({"scope": "static vector packaging; no consumer execution",
                      "vector_count": len(package["cases"]),
                      "source_pin_count": len(package["source_pins"]),
                      "native_field_audit": native_audit,
                      "profiles": dict(sorted(Counter(c["profile_id"] for c in package["cases"]).items())),
                      "source_refs_resolve": True, "original_numeric_cases_preserved": 23,
                      "execution": "NOT_RUN", "sha256": hashlib.sha256(content).hexdigest()}))


if __name__ == "__main__":
    main()
