# Private WP01-01 candidate tools

Issue #211 corrects six original ITER identity mappings. Its current focused validation is:

```text
<python> -B -X utf8 -m unittest discover -s eng/wp01-01/decision-closure/tests -p test_original_identity_mapping.py -v
<python> -B -X utf8 docs/development/evidence/WP01-01/decision-closure/coord-corrections/issue-211/verify-correction.py --root . --archive-root <registered archive workspace>
```

The original 54 gate tests and roadmap check retain their exact unchanged input/log identities and are reused, not rerun. The broad build/finalization entry points below reproduce the original wave7 workflow; do not run them merely to refresh correction metadata. Issue #211 invokes only `build_candidate.mapping()` after checking the original source pins, and records that bounded regeneration. Historical validation, review and manifest snapshots are preserved under `coord-corrections/issue-211/superseded/`.

Use an explicitly selected Python 3.13 interpreter with `-B -X utf8`; no dependencies are installed. Run from repository root:

```text
<python> -B -X utf8 -m unittest discover -s eng/wp01-01/decision-closure/tests -v
<python> -B -X utf8 eng/wp01-01/decision-closure/validate_candidate.py --platform-repo <Platform checkout> --crm-repo <CRM checkout> --archive-root <registered archive workspace>
```

`build_candidate.py --platform-repo ... --crm-repo ... --archive-root ...` reproduces the design JSON from fixed source revisions and explicit repository locations. `mapping_constraints.py` supplies concrete table columns and constraints. It writes only this task's evidence directory and local `.gitattributes`. `current-source-audit.json` is maintained independently and is never regenerated. It creates no signature, actor, authority, adoption or business PASS. The archive location is a read-only source input; it is never copied or modified by these tools.

`gate_candidate.py` is a read-only evaluator; its CLI always rejects actual use because authority authenticity and remote ancestry are unsupported. Tests create explicitly synthetic files in temporary directories and assert the evaluator never changes any file. They validate the algorithm, not production acceptance. The gate has no dependency from production tooling.

`validate_candidate.py` performs static/source/hash/reference/delta checks and finalizes private validation/manifest metadata. The recorded read-only `manage_roadmap.py` result is reused on subsequent private verification when its protected inputs are unchanged. It never uses `--write` and never runs a product build or connects to a DB. Tests are run only when candidate code changes; old wave6 tests are reused at their original input hashes.
