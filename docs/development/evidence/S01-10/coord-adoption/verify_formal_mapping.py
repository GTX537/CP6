"""Read-only adoption check. Does not connect to or initialize any database."""
from pathlib import Path
import datetime, hashlib, json, re

ROOT = Path(__file__).resolve().parents[5]
HERE = Path(__file__).resolve().parent
def load(path): return json.loads(path.read_text(encoding="utf-8"))
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

formal_path = ROOT / "eng/contracts/s01/postgresql-mappings.json"
proposal_path = HERE.parent / "postgresql-mappings.proposed.json"
review = load(HERE / "scale-adoption-review.json")
proposal, formal = load(proposal_path), load(formal_path)
assert sha(proposal_path) == review["proposal_sha256"]
assert formal["mapping_version"] == proposal["mapping_version"] == "CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0"
assert formal["adoption"] == "ADOPTED_BY_COORD"
assert formal["scale_change"]["approval"] == "APPROVED_STORAGE_CAPACITY_ONLY"
decision = formal["coordinator_decision"]
assert decision["id"] == "CP6-S01-10-SCALE-20261013-R1"
assert decision["candidate_sha"] == review["candidate_sha"]
assert decision["proposal_sha256"] == sha(proposal_path)
comparison = json.loads(json.dumps(formal))
comparison.pop("coordinator_decision")
comparison["adoption"] = proposal["adoption"]
comparison["scale_change"]["approval"] = proposal["scale_change"]["approval"]
assert comparison == proposal, "Only adoption metadata may differ from the tested proposal"
semantics = {key: formal[key] for key in review["semantic_keys"]}
canonical = json.dumps(semantics, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
semantic_hash = hashlib.sha256(canonical).hexdigest()
rows = formal["inventory_mappings"]
assert len(rows) == 66 and len({row["table"] for row in rows}) == 13
expanded = {row["table"] + "." + row["logical_column"] for row in rows if row["source_scale_range"] != row["storage_scale_range"]}
assert expanded == {row["path"] for row in review["rows"]} and len(expanded) == 20
for row in rows:
    if row["table"] + "." + row["logical_column"] in expanded:
        assert row["source_scale_range"] == [0, 8] and row["storage_scale_range"] == [0, 18]
assert sum(row["nullable"] for row in rows) == 15
adapter = (ROOT / "CP6.Persistence.PostgreSql/Contracts/ExactValueStorage.cs").read_text(encoding="utf-8")
assert re.search(r'MappingVersion\s*=\s*"CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0"', adapter)
test_input = (ROOT / "eng/contracts/postgresql-tests/TestInputs.cs").read_text(encoding="utf-8")
assert '? "eng/contracts/s01/postgresql-mappings.json"' in test_input
assert len(formal["assigned_vectors"]) == 56
result = dict(result="PASS", observed_at=datetime.datetime.now(datetime.timezone.utc).isoformat(),
    formal_sha256=sha(formal_path), proposal_sha256=sha(proposal_path),
    formal_mapping_version=formal["mapping_version"], formal_adoption=formal["adoption"],
    comparison="Only adoption, scale_change.approval and coordinator_decision differ; every tested semantic value unchanged",
    canonical_semantic_sha256=semantic_hash, canonical_serialization="json.dumps ensure_ascii=False sort_keys=True separators=(',',':')",
    inventory_rows=66, inventory_tables=13, nullable=15, expanded_fields=sorted(expanded), assigned_vectors=56,
    mapping_path_selection="Formal file selected by existing TestInputs.MappingPath; adapter version matches",
    validation_reuse="Original PG210 and overlapping57, lifecycle1 against proposal; NOT newly executed at formal identity",
    database_operations=0, production_owner_acceptance="NOT_RUN")
(HERE / "verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False))
