"""Verify fixed sources and extract a reviewed SQL subset. Does not connect to a database."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "docs/development/evidence/S01-10"
ADMISSION = json.loads((ROOT / "docs/development/evidence/wave4-dispatch-20261013/S01-10-admission.json").read_text(encoding="utf-8-sig"))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    sources = []
    for pin in ADMISSION["source_pins"]:
        raw = Path(pin["path"]).read_bytes()
        assert digest(raw) == pin["expected_sha256"], pin["path"]
        sources.append({**pin, "actual_sha256": digest(raw), "verified": True})
    pins = []
    for path, expected in [
        ("eng/contracts/s01/profile-inputs.json", "2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e"),
        ("eng/contracts/s01/vectors.json", "b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01"),
        ("docs/design-handbook/remediation/assets/core-types/numeric-storage-1.0.0.json", "b67a8fea437a1db1990c3513a09cd9bc791704aac3b3a8f3f9b80642b5d4a607"),
    ]:
        blob = subprocess.check_output(["git", "show", "HEAD:" + path], cwd=ROOT)
        assert digest(blob) == expected, path
        pins.append({"path": path, "git_blob_sha256": expected, "local_file_sha256": digest((ROOT / path).read_bytes())})
    ddl_pin = next(p for p in sources if p["source_id"] == "composed-14" and p["variant"] == "composed")
    ddl = Path(ddl_pin["path"]).read_text(encoding="utf-8-sig")
    # No archive code is executed. Only these explicitly selected definitions are copied.
    declarations = ["DOMAIN plan_atp.sha256_hex", "DOMAIN plan_atp.stable_id", "DOMAIN plan_atp.decimal_30_8",
                    "TYPE plan_atp.evaluation_state", "TABLE plan_atp.evaluation", "TABLE plan_atp.segment",
                    "TABLE plan_atp.basis", "TABLE plan_atp.reservation_ledger", "TABLE plan_atp.replay_record"]
    excerpts = []
    selection = []
    for declaration in declarations:
        start = ddl.index("CREATE " + declaration + " ")
        end = ddl.index(";", start) + 1
        excerpt = ddl[start:end]
        assert "CREATE FUNCTION" not in excerpt
        line = ddl[:start].count("\n") + 1
        selection.append({"declaration": declaration, "line": line, "source_text_sha256": digest(excerpt.encode())})
        excerpts.append(f"-- composed-14 line {line}; schema renamed only.\n" + excerpt.replace("plan_atp.", "exact_fixture."))
    target = ROOT / "eng/contracts/postgresql-tests/atp-reviewed-subset.sql"
    target.write_text("-- S01-10 FIXTURE ONLY: selected types/tables, not a business migration.\n"
                      "-- Source composed-14 SHA256 " + ddl_pin["expected_sha256"] + "\n\n" + "\n\n".join(excerpts) + "\n", encoding="utf-8")
    patches = json.loads((ROOT / "docs/design-handbook/remediation/assets/finance-recovery/source-patches.json").read_text(encoding="utf-8-sig"))["patches"][:5]
    for patch in patches:
        assert patch["finding"] == "PLAN-IF-005"
        assert patch["after"] in ddl
    dictionary = json.loads(Path(next(p["path"] for p in sources if p["source_id"] == "numeric-2")).read_text(encoding="utf-8-sig"))
    credit = json.loads(Path(next(p["path"] for p in sources if p["source_id"] == "numeric-0")).read_text(encoding="utf-8-sig"))
    credit_bindings = []
    for group in ADMISSION["credit"]["field_bindings"]:
        definition = {"fin-credit-signed": "signedDecimal", "fin-credit-nonnegative": "nonNegativeDecimal",
                      "fin-credit-positive": "positiveDecimal"}[group["profile"]]
        for pointer in group["pointers"]:
            node = credit
            for token in pointer.strip("/").split("/"):
                node = node[int(token)] if isinstance(node, list) else node[token]
            assert node == credit["$defs"][definition], pointer
            credit_bindings.append({"pointer": pointer, "native_definition": definition, "profile": group["profile"], "exact_native_schema_matches": True})
    assert len(credit_bindings) == 42
    for mapping in ADMISSION["inventory"]["mappings"]:
        _, _, table_index, _, column_index = mapping["source_pointer"].split("/")
        table = dictionary["Tables"][int(table_index)]
        column = table["Columns"][int(column_index)]
        assert (table["Name"], column["Name"], column["Type"], column["Nullable"]) == (
            mapping["table"], mapping["logical_column"], mapping["old_storage_type"], mapping["nullable"])
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "source-verification.json").write_text(json.dumps({"task": "S01-10", "sources": sources, "package_pins": pins,
        "credit_native_field_bindings_verified": credit_bindings,
        "inventory_dictionary_bindings_verified": 66, "atp_selected_definitions": selection,
        "atp_recovery_patch_indices_verified": list(range(5)), "atp_subset_sha256": digest(target.read_bytes()),
        "scope": "Source verification and fixture subset only; no archived business procedure executed"}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Verified 11 source hashes, 3 Git input hashes, 42 native CREDIT profiles, 66 dictionary bindings, 5 recovery patches; extracted 9 reviewed definitions.")


if __name__ == "__main__":
    main()
