"""Read-only, private candidate evaluator. No production adoption/completion API.

File integrity is not signer authenticity or remote ancestry. Actual mode always
rejects these unsupported trust checks; synthetic mode proves algorithm only.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath, PureWindowsPath
import re

TARGETS = frozenset({'PLM-ITER-01', 'PLM-REL-01'})
VERSION = 'CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0-proposed'


class Invalid(ValueError):
    pass


def strict_json(data):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise Invalid('JSON_DUPLICATE_KEY:' + key)
            result[key] = value
        return result

    def constant(value):
        raise Invalid('JSON_NONFINITE:' + value)

    return json.loads(data, object_pairs_hook=pairs, parse_constant=constant)


def read_ref(root, ref, *, as_json=False):
    """Read exactly the bytes named by a contained, non-symlink-escaping ref."""
    if not isinstance(ref, dict):
        raise Invalid('FILE_REFERENCE_REQUIRED')
    path = ref.get('path')
    if not isinstance(path, str) or not path or '\\' in path or ':' in path:
        raise Invalid('PATH_INVALID')
    if PureWindowsPath(path).is_absolute() or PurePosixPath(path).is_absolute() or '..' in PurePosixPath(path).parts:
        raise Invalid('PATH_ESCAPE')
    base = Path(root).resolve()
    resolved = (base/path).resolve(strict=True)
    if not resolved.is_relative_to(base) or not resolved.is_file():
        raise Invalid('PATH_ESCAPE')
    data = resolved.read_bytes()
    if not re.fullmatch('[0-9a-f]{64}', str(ref.get('sha256', ''))):
        raise Invalid('HASH_FORMAT')
    if hashlib.sha256(data).hexdigest() != ref['sha256'] or len(data) != ref.get('bytes'):
        raise Invalid('FILE_BYTES_MISMATCH')
    return strict_json(data) if as_json else data


def require(condition, reason):
    if not condition:
        raise Invalid(reason)


def exact_index(items, key='id'):
    require(isinstance(items, list), 'LIST_REQUIRED')
    result = {item[key]: item for item in items}
    require(len(result) == len(items), 'DUPLICATE_ID')
    return result


def evaluate_candidate(root, contract_ref, submission, *, mode='actual'):
    result = {'production_enabled': False, 'actual_adoption': False, 'actual_completion': False,
              'candidate_valid': False, 'mode': mode, 'errors': [],
              'authority_authenticity': 'UNVERIFIED', 'remote_ancestry': 'UNVERIFIED',
              'meaning': 'SYNTHETIC_ALGORITHM_ONLY' if mode == 'synthetic' else 'ACTUAL_USE_REJECTED'}
    if mode != 'synthetic':
        result['errors'] += ['AUTHORITY_AUTHENTICITY_UNVERIFIED', 'REMOTE_ANCESTRY_UNVERIFIED']
    try:
        require(mode in ('actual', 'synthetic'), 'MODE_INVALID')
        contract = read_ref(root, contract_ref, as_json=True)
        require(contract['production_enabled'] is False, 'PRODUCTION_ENABLE_FORBIDDEN')
        require(contract['version'] == VERSION == submission['basis_version'], 'BASIS_MISMATCH')
        target = submission['target']
        require(target in TARGETS, 'TARGET_NOT_SUPPORTED_OTHER_123_UNCHANGED')
        require(submission.get('synthetic') is (mode == 'synthetic'), 'SYNTHETIC_NOT_REAL')
        require(submission['historical_gaps'] == contract['historical_gaps'], 'HISTORY_CHANGED')
        for gap in contract['historical_gaps']:
            require(gap['legacy_case_ids'] is None and gap['missing_original_sha256'] is None
                    and gap['historical_inventory_state'] == 'NOT_INVENTORIED'
                    and gap['recovery_state'] == 'UNRECOVERABLE' and gap['equivalence'] == 'UNPROVABLE', 'HISTORY_CHANGED')
        require(sorted(g['declared_legacy_count'] for g in contract['historical_gaps']) == [30,48], 'HISTORY_COUNTS')
        sources = contract['sources']
        require(bool(sources), 'SOURCE_REQUIRED')
        for source in sources:
            read_ref(root, source)
        source_hashes = {s['sha256'] for s in sources}
        decisions = read_ref(root, contract['decision_package'], as_json=True)
        for flag in ('production_enabled', 'actual_adoption', 'actual_completion'):
            require(decisions[flag] is False, 'DECISION_PACKAGE_CANNOT_ENABLE_PRODUCTION')
        delta = read_ref(root, contract['basis_delta'], as_json=True)
        require(delta['additional_obligations'] == contract['additional_obligations']
                and delta['removed_obligations'] == [], 'EXACT_DELTA_REQUIRED')
        matrix = read_ref(root, contract['matrix'], as_json=True)
        expected = {}
        for row in matrix['rows'] + contract['additional_obligations']:
            if row['target'] == target:
                expected.setdefault(row['id'], set()).update(row['observations'])
                if row.get('source_assertion'):
                    expected[row['id']].add(row['source_assertion'])
        require(bool(expected), 'EMPTY_OBLIGATION_SET')
        provided = exact_index(submission['obligations'])
        require(set(expected) == set(provided), 'EXACT_OBLIGATION_SET_REQUIRED')
        authority = read_ref(root, submission['adoption'], as_json=True)
        require(authority['kind'] == ('SYNTHETIC_AUTHORITY' if mode == 'synthetic' else 'ADOPTION_AUTHORITY')
                and authority['synthetic'] is (mode == 'synthetic'), 'AUTHORITY_KIND_MISMATCH')
        require(authority['basis_version'] == VERSION and authority['target'] == target
                and set(authority['scope']) == set(expected) and bool(authority['actor']), 'AUTHORITY_SCOPE_MISMATCH')
        read_ref(root, authority['authority_source'])
        for oid, observations in expected.items():
            record = provided[oid]
            require(set(record['subassertions']) == observations and len(record['subassertions']) == len(observations), 'EXACT_SUBASSERTION_SET_REQUIRED')
            evidence = read_ref(root, record['evidence'], as_json=True)
            require(evidence['synthetic'] is (mode == 'synthetic')
                    and evidence['kind'] == ('SYNTHETIC_RUNTIME' if mode == 'synthetic' else 'RUNTIME')
                    and evidence['status'] == 'PASS', 'REAL_RUNTIME_EVIDENCE_REQUIRED')
            require(evidence['target'] == target and evidence['obligation'] == oid
                    and set(evidence['subassertions']) == observations
                    and len(evidence['subassertions']) == len(observations)
                    and evidence['source_sha256'] in source_hashes, 'EVIDENCE_BINDING_MISMATCH')
        graph = read_ref(root, contract['provider_graph'], as_json=True)
        graph_edges = exact_index(graph['edges'])
        # Graph owns applicability. The contract cannot privately delete a declared edge.
        expected_edges = {key: value for key, value in graph_edges.items()
                          if target in value.get('targets', [target])}
        require(set(contract['target_edges'][target]) == set(expected_edges), 'DECLARED_EDGE_SET_CHANGED')
        actual_edges = exact_index(submission['edges'])
        require(set(actual_edges) == set(expected_edges), 'DECLARED_PROVIDER_EDGE_MISSING')
        for eid, edge in expected_edges.items():
            supplied = actual_edges[eid]
            if supplied['state'] == 'EXCLUDED':
                authority = read_ref(root, supplied['authority'], as_json=True)
                require(authority['synthetic'] is True and authority['kind'] == 'SYNTHETIC_CONDITIONAL_AUTHORITY'
                        and authority['edge'] == eid and authority['target'] == target
                        and authority['condition'] == edge['condition'] and authority['basis_version'] == VERSION,
                        'EXCLUSION_SCOPE_MISMATCH')
                read_ref(root, authority['authority_source'])
                require(edge.get('exclusion_allowed') is True, 'DECLARED_TASK_DELIVERY_NOT_OPTIONAL')
                continue
            require(supplied['state'] == 'BOUND', 'PROVIDER_NOT_BOUND')
            receipt = read_ref(root, supplied['receipt'], as_json=True)
            require(receipt['synthetic'] is (mode == 'synthetic') and receipt['status'] == 'PASS' and receipt['edge'] == eid, 'RECEIPT_NOT_VALID')
            for key in ('provider_task', 'consumer_task', 'provider_repository', 'consumer_repository'):
                require(receipt[key] == edge[key], 'RECEIPT_EDGE_MISMATCH')
            selected = edge['contract']
            read_ref(root, selected['file'])
            require(receipt['contract_version'] == selected['version']
                    and receipt['contract_profile'] == selected['profile']
                    and receipt['contract_sha256'] == selected['file']['sha256'], 'EXACT_CONTRACT_PIN_REQUIRED')
            read_ref(root, receipt['package'])
            for role in ('provider', 'consumer'):
                evidence = read_ref(root, receipt[role+'_evidence'], as_json=True)
                require(evidence['synthetic'] is (mode == 'synthetic') and evidence['status'] == 'PASS'
                        and evidence['task'] == edge[role+'_task']
                        and evidence['repository'] == edge[role+'_repository']
                        and evidence['edge'] == eid
                        and evidence['contract_sha256'] == selected['file']['sha256'], 'PROVIDER_CONSUMER_EVIDENCE_MISMATCH')
            remote = read_ref(root, receipt['remote'], as_json=True)
            require(remote['synthetic'] is (mode == 'synthetic') and remote['repository'] == edge['provider_repository']
                    and re.fullmatch('[0-9a-f]{40}', remote['commit'])
                    and remote['package_sha256'] == receipt['package']['sha256'], 'REMOTE_PACKAGE_BINDING_MISMATCH')
            consumer_remote = read_ref(root, receipt['consumer_remote'], as_json=True)
            require(consumer_remote['synthetic'] is (mode == 'synthetic')
                    and consumer_remote['repository'] == edge['consumer_repository']
                    and re.fullmatch('[0-9a-f]{40}', consumer_remote['commit'])
                    and consumer_remote['package_sha256'] == receipt['package']['sha256'], 'CONSUMER_REMOTE_PACKAGE_BINDING_MISMATCH')
        result['candidate_valid'] = not result['errors']
    except (Invalid, OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
        result['errors'].append(str(exc))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--contract-ref', type=Path, required=True)
    parser.add_argument('--submission', type=Path, required=True)
    args = parser.parse_args()
    result = evaluate_candidate(args.root, strict_json(args.contract_ref.read_bytes()), strict_json(args.submission.read_bytes()))
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result['candidate_valid'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
