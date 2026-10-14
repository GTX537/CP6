"""Pure preparation proposal model, NOT the roadmap completion gate.

This compares record shapes, not signatures, file contents, trusted time, runtime
truth or remote ancestry. Callers may supply entirely invented records. Therefore
even a positive can NEVER authorize adoption/completion. No I/O, database or writes.
"""
import re

WHITELIST = frozenset({"PLM-ITER-01", "PLM-REL-01"})


def nonempty(value):
    return isinstance(value, str) and bool(value.strip())


def digest(value, length):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{%d}" % length, value) is not None


def compare(candidate, contract):
    """Malformed documents are rejections too; this still validates no truth."""
    try:
        if not isinstance(candidate, dict) or not isinstance(contract, dict):
            raise ValueError("object required")
        if not isinstance(candidate.get("target"), str):
            raise ValueError("target string required")
        adoption = candidate.get("adoption")
        if isinstance(adoption, dict) and (not isinstance(adoption.get("targets"), list) or any(not isinstance(t, str) for t in adoption["targets"])):
            raise ValueError("adoption targets must be a list of strings")
        for key in ("obligations", "r1", "tasks", "repositories", "decisions"):
            rows = candidate.get(key, [])
            if not isinstance(rows, list) or any(not isinstance(r, dict) or not isinstance(r.get("id"), str) for r in rows):
                raise ValueError("evidence IDs must be strings in row arrays")
        return _compare(candidate, contract)
    except (KeyError, TypeError, ValueError, AttributeError):
        return {"scope": "PREPARATION", "proposal_conditions_satisfied": False,
                "actual_completion": False, "actual_adoption": False,
                "reasons": ["INPUT_SHAPE_INVALID"], "limitation": "STRUCTURE_ONLY; no authenticity or runtime verification."}


def _compare(candidate, contract):
    reasons = []
    target = candidate.get("target")
    if target not in WHITELIST or target not in contract["targets"]:
        reasons.append("TARGET_NOT_WHITELISTED")
    if candidate.get("version") != contract["version"] or candidate.get("pins") != contract["pins"]:
        reasons.append("PIN_MISMATCH")
    adoption = candidate.get("adoption")
    if not isinstance(adoption, dict) or adoption.get("status") != "SIGNED" or any(not nonempty(adoption.get(k)) for k in ("actor", "time", "authority", "effective_at", "decision_text")):
        reasons.append("ADOPTION_REQUIRED")
    if isinstance(adoption, dict):
        if adoption.get("version") != contract["version"] or adoption.get("pins") != contract["pins"] or target not in adoption.get("targets", []) or not set(adoption.get("targets", [])) <= WHITELIST:
            reasons.append("ADOPTION_BINDING_MISMATCH")
        if "以当前可知义务建立新标准，无法证明旧30/48逐项等价" not in (adoption.get("decision_text") or ""):
            reasons.append("EXPLICIT_NEW_STANDARD_ACKNOWLEDGEMENT_REQUIRED")
    if candidate.get("historical_gaps") != contract["historical_gaps"]:
        reasons.append("HISTORICAL_GAP_CHANGED")

    def exact_rows(name, expected, reason):
        rows = candidate.get(name, [])
        if not isinstance(rows, list) or any(not isinstance(r, dict) for r in rows):
            reasons.append(reason)
            return []
        ids = [r.get("id") for r in rows]
        if len(ids) != len(expected) or set(ids) != set(expected):
            reasons.append(reason)
        return rows

    def runtime_ok(row, level):
        return (row.get("target") == target and row.get("level") == level and row.get("state") == "PASS"
                and row.get("synthetic") is False and digest(row.get("source_sha"), 40)
                and digest(row.get("evidence_sha256"), 64)
                and all(nonempty(row.get(k)) for k in ("owner", "run_id", "environment", "observed_at", "command", "inputs", "observed_result", "persistent_before_after", "authorization_context", "path")))

    if target in WHITELIST and target in contract["targets"]:
        expected = contract["targets"][target]
        if expected.get("cross_repository_scope_state") != "RESOLVED_BY_EXACT_SOURCE":
            reasons.append("CROSS_REPOSITORY_SCOPE_UNRESOLVED")
        for key, prefix, level in (("obligations", "OBLIGATION", "BUSINESS_RUNTIME"), ("r1", "R1", "BUSINESS_RUNTIME"), ("tasks", "TASK", "TASK_DELIVERY")):
            rows = exact_rows(key, expected[key], prefix + "_SET_MISMATCH")
            if any(not runtime_ok(row, level) for row in rows):
                reasons.append(prefix + "_RUNTIME_EVIDENCE_REQUIRED")
        repositories = exact_rows("repositories", expected["repositories"], "REPOSITORY_SET_MISMATCH")
        if any(not (digest(row.get("remote_main_sha"), 40) and digest(row.get("task_sha"), 40) and row.get("contains_task") is True and nonempty(row.get("package_contract_version")) and nonempty(row.get("adoption_evidence"))) for row in repositories):
            reasons.append("CROSS_REPOSITORY_DELIVERY_REQUIRED")
        decisions = exact_rows("decisions", expected["decisions"], "DECISION_SET_MISMATCH")
        if any(row.get("status") != "RESOLVED_BY_EXACT_SUCCESSOR" or not nonempty(row.get("authority")) or not digest(row.get("successor_sha256"), 64) for row in decisions):
            reasons.append("EXACT_SUCCESSOR_DECISION_REQUIRED")
    return {"scope": "PREPARATION", "proposal_conditions_satisfied": not reasons,
            "actual_completion": False, "actual_adoption": False, "reasons": reasons,
            "limitation": "STRUCTURE_ONLY: authenticity, signatures, semantic sufficiency, actual runtime and remote-main containment are NOT verified. Existing manager/ledger gates unchanged."}
