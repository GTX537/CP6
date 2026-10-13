"""Produce review material only. Adoption into s01 requires the recorded COORD window."""
import json
from pathlib import Path
from prepare_sources import ROOT, ADMISSION


def ptr(definition, prop=None):
    return "/$defs/" + definition + ("/properties/" + prop if prop else "")


def binding(table, column, source_scale):
    field = column[0].lower() + column[1:]
    profile = "Decimal18" if source_scale in (8, 18) else "Qty12"
    rule, kind, policy = "Any", "NATIVE_FIELD", None
    path = []
    if table == "SourcePortion":
        if column.startswith("Ratio"):
            field = "numerator" if column.endswith("Numerator") else "denominator"
            path = [ptr("SourcePortionFact", "conversion"), ptr("UomConversion", "ratio"), ptr("Rational", field)]
            profile, rule = "PositiveInteger", "Positive"
        else:
            field = "sourceQuantity" if column == "SourceQty" else "baseQuantity"
            path = [ptr("SourcePortionFact", field), ptr("Quantity", "value"), ptr("Qty12")]
            rule = "NonNegative"
    elif table == "SourcePortionBalance":
        path = [ptr(table, field), ptr("NonNegativeQty12")]
        profile = "NonNegativeQty12"
    elif table == "ValuationPoolHead":
        path = [ptr("MovingAveragePoolRevision", "afterQty" if column == "CurrentQty" else "afterUnroundedValue")]
        kind = "DERIVED_CURRENT_REVISION_PROJECTION"
    elif table == "ValueLayer":
        field = {"OriginalUnroundedValue": "unroundedOriginalValue"}.get(column, field)
        path = [ptr(table, field)]
        if column == "OriginalQty": path += [ptr("Quantity", "value")]; rule = "NonNegative"
        if column == "UnitValue": path += [ptr("Money", "amount")]; policy = "Money.scale: integer 0..8, independent of amount lexical scale"
    elif table == "ValueLayerBalance":
        path = [ptr("ValueLayer", "originalQty" if column == "OriginalQty" else "availableQty"), ptr("Quantity", "value")]
        kind, rule = "DERIVED_LAYER_BALANCE_DICTIONARY_FIELD", "NonNegative"
        if column not in ("OriginalQty", "AvailableQty"): path = [ptr("Qty12")]
    elif table == "MovingAveragePoolRevision":
        path = [ptr(table, field)]
        if column.endswith("Numerator"): profile = "MavgNumerator"
        if column.endswith("Denominator"): profile, rule = "PositiveInteger", "Positive"
    elif table == "ValueLine":
        kind = "DERIVED_VALUE_EFFECT_LINE_DICTIONARY_FIELD"
        if column in ("RoundedBeforeResidue", "ResidueApplied", "FinalAmount"):
            path = [ptr("LayerConsumption", field), ptr("Money", "amount")]
            policy = "Money.scale / RoundingPolicy.currencyScale: 0..8, independently preserved by Owner"
        else:
            field = {"Qty": "quantity"}.get(column, field)
            path = ["/$defs/ValueEffectResult/properties/lines/items/properties/" + field]
            if column == "Qty": path += [ptr("Quantity", "value")]
            if column == "UnitValue": path += [ptr("Money", "amount")]; policy = "Money.scale: 0..8, independent"
    elif table == "LayerConsumption":
        path = [ptr(table, field)]
        if column.endswith("Qty"): path += [ptr("Quantity", "value")]
        if column == "TakeQty": rule = "Positive"
        if source_scale == 8:
            path += [ptr("Money", "amount")]
            policy = "Money.scale: integer 0..8, independent of amount lexical scale"
    elif table == "RoundingTrace":
        path = [ptr(table, field), ptr("Decimal18")]
        policy = "RoundingTrace.scale: integer 0..8; calculationScale=18; no storage rounding"
    elif table == "ReconciliationLine":
        path = [ptr("ReconciliationRow", field), ptr("Quantity", "value") if column == "Quantity" else ptr("Money", "amount")]
        if column != "Quantity": policy = "Money.scale: integer 0..8, independent"
    elif table == "SourcePortionRange":
        path = [ptr("SourcePortionFact", "ranges"), ptr("Range", "from" if column == "FromQty" else "to")]
        profile = "NonNegativeQty12"
    elif table == "FxRateFact":
        path = [ptr("Decimal18")]
        kind, rule = "DICTIONARY_DECIMAL18_FIELD_NO_NATIVE_FXRATE_OBJECT", "Positive"
    elif table == "ProvisionalValueFact":
        path = [ptr("ProvisionalEnabled", "maxQuantity" if column == "MaxQty" else "maxAmount")]
        path += [ptr("Quantity", "value") if column == "MaxQty" else ptr("Money", "amount")]
        if column == "MaxAmount": policy = "Money.scale: integer 0..8, independent"
    else:
        raise ValueError(table)
    return profile, rule, kind, path, policy


def main():
    schema_pin = next(p for p in ADMISSION["source_pins"] if p["source_id"] == "numeric-1")
    schema = json.loads(Path(schema_pin["path"]).read_text(encoding="utf-8-sig"))
    fields = []
    for original in ADMISSION["inventory"]["mappings"]:
        old_max = original["source_scale_range"][1]
        profile, rule, kind, paths, policy = binding(original["table"], original["logical_column"], old_max)
        for path in paths:
            item = schema
            for token in path.strip("/").split("/"): item = item[token]
        fields.append({"table": original["table"], "logical_column": original["logical_column"],
            "source_pointer": original["source_pointer"], "source_id": "numeric-2", "old_storage_type": original["old_storage_type"],
            "source_scale_range": original["source_scale_range"], "nullable": original["nullable"],
            "digits_column": original["digits_column"], "digits_type": "text", "scale_column": original["scale_column"],
            "scale_type": "smallint", "wire_column": original["logical_column"] + "OriginalWire", "wire_type": "text",
            "profile": profile, "numeric_rule": rule, "binding_kind": kind, "native_source_id": "numeric-1", "native_pointers": paths,
            "storage_scale_range": [0, 18 if old_max == 8 else old_max],
            "numeric_scale": "Normalized exact coefficient scale; zero=(0,0); fractional trailing zeros removed only from value pair",
            "original_wire": "Unchanged decoded field string, independent from normalized pair; whole receipt UTF8 stored separately as bytea when required",
            "policy_scale": policy,
            "lossless_basis": "Decimal18 allows 18 fractional digits; old storage scale8 is not a wire restriction; no quantization" if old_max == 8 else
                "Native lexical limit and numeric storage range agree; normalization is reversible using independently preserved originalWire",
            "coupling": "All three null iff logical field null; otherwise all non-null and raw wire reparses to exactly this canonical pair"})
    assert len(fields) == 66 and sum(x["nullable"] for x in fields) == 15
    result = {"mapping_version": "CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0", "adoption": "PROPOSED_PENDING_COORD",
        "database": "PostgreSQL", "source_revision": "CP6-EXACT-NUMERIC-STORAGE/1",
        "source_sha256": "b67a8fea437a1db1990c3513a09cd9bc791704aac3b3a8f3f9b80642b5d4a607",
        "package_version": "CP6-S01-CONTRACT-INPUTS/1.0.0",
        "native_inventory_schema_sha256": schema_pin["expected_sha256"],
        "credit": ADMISSION["credit"], "inventory_mappings": fields,
        "atp": ADMISSION["atp"], "assigned_vectors": ADMISSION["mapped_vectors"],
        "arithmetic": "Application BigInteger exact arithmetic; no numeric CAST/SUM of unbounded inventory coefficients",
        "owner_relations": "Dictionary multi-field arithmetic, full native object validation, Owner identity, receipt bytes, tenant/authorization and migrations remain Owner adoption obligations; this fixture enforces scalar sign/null rules and explicit MAVG/ATP state relations",
        "scale_change": {"count": 20, "from_max": 8, "to_max": 18, "approval": "PENDING", "question_commit": "cc6dde813dfa841f9302c9cc5b9b9a25ded25f68"}}
    target = ROOT / "docs/development/evidence/S01-10/postgresql-mappings.proposed.json"
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Draft 66-field mapping written; all native pointers resolve; 20 widenings remain pending COORD.")


if __name__ == "__main__":
    main()
