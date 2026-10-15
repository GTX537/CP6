"""Local prospective acceptance gate; independent trust is never read from a package.

See adoption-v1.2/OPERATOR.md for the external signed-registry protocol. No registry
or signing identity is provisioned by this module. Unconfigured operation denies.
"""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
from urllib.parse import quote

VERSION = 'CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0'
TARGETS = ('PLM-ITER-01', 'PLM-REL-01')
ISSUE_211_MANIFEST = '7eaa62f8e2904978f7440de5a15c8e0d9bc8328ffe12199dfee99ebe098ab839'
INHERITED_MATRIX_HASH = '0f6e160d49b9fb0305c94f9a3ccbfc71ebae312a26bd1613fd38ce4043b57063'
DELTA_HASH = '41d06ff2e0e3b934977f72c927a2e943a7c76f2202a5aad5acafbedddaa1fb5c'
TIMEOUT = 30


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def exact(left, right):
    """JSON structural equality with array order and scalar types preserved."""
    return json.dumps(left, sort_keys=True, ensure_ascii=False, allow_nan=False) == json.dumps(right, sort_keys=True, ensure_ascii=False, allow_nan=False)


def parse(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, 'DUPLICATE_JSON_KEY')
            result[key] = value
        return result
    return json.loads(raw.decode('utf-8-sig'), object_pairs_hook=pairs,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError('NONFINITE_JSON')))


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def safe_path(value):
    require(isinstance(value, str) and value and '\\' not in value and ':' not in value, 'PATH_INVALID')
    path = PurePosixPath(value)
    require(not path.is_absolute() and all(p not in {'.', '..', ''} for p in value.split('/')), 'PATH_ESCAPE')
    return value


def contained(root, value):
    path = (root / safe_path(value)).resolve()
    require(path.is_relative_to(root.resolve()), 'PATH_ESCAPE')
    return path


def run(args, *, cwd=None, data=None):
    proc = subprocess.run(args, cwd=cwd, input=data, capture_output=True, timeout=TIMEOUT)
    require(proc.returncode == 0, 'INDEPENDENT_COMMAND_FAILED: ' + args[0])
    return proc.stdout


def read_ref(root, ref, *, archives=()):
    require(isinstance(ref, dict), 'SOURCE_REF_INVALID')
    path = safe_path(ref['path'])
    require(re.fullmatch('[0-9a-f]{64}', ref['sha256']) is not None, 'SOURCE_HASH_INVALID')
    require(type(ref.get('bytes')) is int and ref['bytes'] >= 0, 'SOURCE_LENGTH_INVALID')
    representation = ref.get('representation')
    if representation == 'GIT_BLOB_BYTES':
        revision = ref.get('git_revision')
        require(isinstance(revision, str) and re.fullmatch('[0-9a-f]{40}', revision), 'SOURCE_REVISION_INVALID')
        raw = run(['git', '-c', 'core.longpaths=true', 'show', revision + ':' + path], cwd=root)
    else:
        require(representation == 'RAW_FILE_BYTES', 'SOURCE_REPRESENTATION_INVALID')
        raw = None
        for source_root in (root, *archives):
            source = contained(source_root, path)
            if source.is_file():
                candidate = source.read_bytes()
                if sha(candidate) == ref['sha256']:
                    raw = candidate
                    break
        require(raw is not None, 'SOURCE_BYTES_UNAVAILABLE_OR_CHANGED: ' + path)
    require(len(raw) == ref['bytes'] and sha(raw) == ref['sha256'], 'SOURCE_BYTES_CHANGED: ' + path)
    return raw


def unique(items, key):
    require(isinstance(items, list), 'ARRAY_REQUIRED')
    values = [item[key] for item in items]
    require(len(values) == len(set(values)), 'DUPLICATE_' + key.upper())
    return {item[key]: item for item in items}


def load_basis(root, manifest_ref, archives=()):
    manifest = parse(read_ref(root, manifest_ref))
    require(manifest['schema'] == 'CP6-ACCEPTANCE-MANIFEST/1' and manifest['version'] == VERSION, 'MANIFEST_VERSION_INVALID')
    artifacts = unique(manifest['artifacts'], 'path')
    require(manifest['basis']['path'] in artifacts and exact(manifest['basis'], artifacts[manifest['basis']['path']]), 'BASIS_NOT_MANIFESTED')
    for artifact in artifacts.values():
        read_ref(root, artifact)
    basis = parse(read_ref(root, manifest['basis']))
    require(basis['schema'] == 'CP6-ACCEPTANCE-BASIS/1' and basis['version'] == VERSION and basis['identity'] == manifest['identity'], 'BASIS_IDENTITY_INVALID')
    require(exact(basis['targets'], list(TARGETS)), 'TARGET_SCOPE_INVALID')
    require(exact(basis['predecessors'], manifest['predecessors']), 'PREDECESSOR_IDENTITY_INVALID')
    require(any(r['sha256'] == ISSUE_211_MANIFEST for r in basis['predecessors']), 'ISSUE211_CORRECTED_BASELINE_REQUIRED')
    for ref in basis['predecessors']:
        read_ref(root, ref)
    require(basis['inherited_matrix']['sha256'] == INHERITED_MATRIX_HASH
            and basis['delta']['sha256'] == DELTA_HASH, 'INHERITED_CONTRACT_HASH_CHANGED')
    inherited = parse(read_ref(root, basis['inherited_matrix']))['rows']
    delta = parse(read_ref(root, basis['delta']))['additional_obligations']
    rows = basis['rows']
    unique(rows, 'id')
    require(len(inherited) == 577 and len(delta) == 2 and exact(rows, inherited + delta), 'OBLIGATION_EXACT_COVERAGE_INVALID')
    require([sum(r['target'] == t for r in rows) for t in TARGETS] == [401, 178], 'OBLIGATION_TARGET_COUNTS_INVALID')
    require(sum(isinstance(r.get('source_assertion'), dict) for r in rows) == 18, 'STRUCTURED_ASSERTION_COUNT_INVALID')
    sources = {}
    for ref in basis['source_documents']:
        key = (ref['path'], ref['sha256'])
        require(key not in sources, 'DUPLICATE_SOURCE')
        sources[key] = read_ref(root, ref, archives=archives)
    for row in rows:
        require(isinstance(row['source_refs'], list) and row['source_refs'], 'OBLIGATION_SOURCE_REQUIRED')
        for ref in row['source_refs']:
            safe_path(ref['path'])
            require((ref['path'], ref['sha256']) in sources, 'OBLIGATION_SOURCE_UNVERIFIED')
        owner = row.get('owner_authority')
        if owner:
            require((owner['source_path'], owner['source_sha256']) in sources, 'OWNER_SOURCE_UNVERIFIED')
    gaps = unique(basis['historical_gaps'], 'target')
    require(set(gaps) == set(TARGETS), 'LEGACY_GAPS_CHANGED')
    for target, count in zip(TARGETS, [30, 48]):
        g = gaps[target]
        require(type(g['declared_legacy_count']) is int and g['declared_legacy_count'] == count
                and g['legacy_case_ids'] is None and g['missing_original_sha256'] is None
                and g['historical_inventory_state'] == 'NOT_INVENTORIED'
                and g['recovery_state'] == 'UNRECOVERABLE' and g['equivalence'] == 'UNPROVABLE', 'LEGACY_GAPS_CHANGED')
    graph = parse(read_ref(root, basis['planning_graph']))
    unique(graph['edges'], 'id')
    return basis, graph


def timestamp(value):
    result = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    require(result.tzinfo is not None, 'TIMEZONE_REQUIRED')
    return result


def current_time(now=None):
    return timestamp(now) if now else dt.datetime.now(dt.timezone.utc)


def current_record(record, now):
    require(record.get('revoked') is False, 'AUTHORIZATION_REVOKED_OR_UNKNOWN')
    require(timestamp(record['effective_at']) <= now < timestamp(record['expires_at']), 'AUTHORIZATION_EXPIRED_OR_NOT_EFFECTIVE')


def current_registry(registry, now):
    issued, expires = timestamp(registry['issued_at']), timestamp(registry['expires_at'])
    require(issued <= now < expires and expires - issued <= dt.timedelta(hours=24), 'REGISTRY_STALE_OR_EXPIRED')


def validate_adoption(registry, manifest, basis, *, now=None):
    now = current_time(now)
    current_registry(registry, now)
    records = unique(registry['adoptions'], 'id')
    identities = unique(registry['identities'], 'identity')
    identity = identities[basis['identity']]
    require(identity['version'] == basis['version'] and identity['manifest_sha256'] == manifest['sha256'], 'APPEND_ONLY_IDENTITY_CHANGED')
    matching = [r for r in records.values() if r.get('identity') == basis['identity']]
    require(len(matching) == 1, 'ADOPTION_IDENTITY_MISSING_OR_DUPLICATED')
    record = matching[0]
    current_record(record, now)
    require(record['action'] == 'ADOPT_PROSPECTIVE_BASIS' and record['state'] == 'ADOPTED'
            and record['actual_adoption'] is True and record['version'] == basis['version']
            and record['manifest_sha256'] == manifest['sha256'] and exact(record['targets'], list(TARGETS)), 'ADOPTION_SCOPE_VERSION_HASH_INVALID')
    source = record['authorization_source']
    require(isinstance(source['uri'], str) and source['uri'].startswith('https://')
            and re.fullmatch('[0-9a-f]{64}', source['sha256']) and record['approved_by'], 'AUTHORIZATION_SOURCE_REQUIRED')
    return record


def outside_git(path):
    require(path.is_absolute(), 'TRUST_PATH_MUST_BE_ABSOLUTE')
    resolved = path.resolve()
    require(not any((parent / '.git').exists() for parent in (resolved.parent, *resolved.parents)), 'TRUST_POLICY_OR_KEYS_INSIDE_WORKTREE')
    return resolved


class TrustedRegistryVerifier:
    """Read-only GitHub + OpenSSH adapter, anchored exclusively by operator policy."""
    def __init__(self, policy):
        self.policy = policy
        self.registries = {}
        self.remote_results = set()

    @classmethod
    def from_environment(cls, root):
        value = os.environ.get('CP6_ACCEPTANCE_TRUST_POLICY')
        require(value, 'INDEPENDENT_VERIFIER_UNCONFIGURED')
        policy_path = outside_git(Path(value))
        require(not policy_path.is_relative_to(root.resolve()), 'TRUST_POLICY_INSIDE_PACKAGE')
        policy = parse(policy_path.read_bytes())
        require(policy['schema'] == 'CP6-ACCEPTANCE-TRUST/1', 'TRUST_POLICY_SCHEMA_INVALID')
        for role in ('authority', 'execution'):
            item = policy[role]
            require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', item['repository']), 'TRUST_REPOSITORY_INVALID')
            safe_path(item['path'])
            require(isinstance(item['principal'], str) and item['principal'].strip(), 'TRUST_PRINCIPAL_REQUIRED')
            item['allowed_signers'] = str(outside_git(Path(item['allowed_signers'])))
        require(isinstance(policy['repositories'], dict) and policy['repositories'], 'PRODUCT_REPOSITORIES_REQUIRED')
        for repo in policy['repositories'].values():
            require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repo), 'PRODUCT_REPOSITORY_INVALID')
        return cls(policy)

    def api(self, endpoint, *, raw=False):
        args = ['gh', 'api', '--hostname', 'github.com', endpoint]
        if raw:
            args += ['-H', 'Accept: application/vnd.github.raw+json']
        response = run(args)
        require(len(response) <= 32 * 1024 * 1024, 'REGISTRY_RESPONSE_TOO_LARGE')
        return response if raw else parse(response)

    def registry(self, role):
        if role not in self.registries:
            cfg = self.policy[role]
            repo = cfg['repository']
            head = self.api(f'repos/{repo}/commits/main')['sha']
            require(re.fullmatch('[0-9a-f]{40}', head), 'REGISTRY_MAIN_UNVERIFIED')
            endpoint = f'repos/{repo}/contents/{quote(cfg["path"], safe="/")}'
            raw = self.api(endpoint + '?ref=' + head, raw=True)
            signature = self.api(endpoint + '.sig?ref=' + head, raw=True)
            with tempfile.TemporaryDirectory(prefix='cp6-registry-verify-') as tmp:
                sig = Path(tmp) / 'registry.sig'
                sig.write_bytes(signature)
                run(['ssh-keygen', '-Y', 'verify', '-f', cfg['allowed_signers'], '-I', cfg['principal'],
                     '-n', 'cp6-acceptance-' + role + '-v1', '-s', str(sig)], data=raw)
            registry = parse(raw)
            require(registry['schema'] == 'CP6-ACCEPTANCE-' + role.upper() + '-REGISTRY/1', 'REGISTRY_ROLE_INVALID')
            current_registry(registry, current_time())
            self.registries[role] = registry
        # Recheck time on every use, even inside one invocation.
        current_registry(self.registries[role], current_time())
        return self.registries[role]

    def adoption(self, manifest, basis):
        record = validate_adoption(self.registry('authority'), manifest, basis)
        delivery = record['delivery']
        require(delivery['repository'] == 'Main' and delivery['path'] == manifest['path'], 'BASIS_DELIVERY_SCOPE_INVALID')
        self.remote_main(delivery['repository'], delivery['commit'])
        repository = self.policy['repositories'][delivery['repository']]
        raw = self.api(f'repos/{repository}/contents/{quote(safe_path(delivery["path"]), safe="/")}?ref={delivery["commit"]}', raw=True)
        require(sha(raw) == manifest['sha256'] and len(raw) == manifest['bytes'], 'REMOTE_BASIS_BYTES_MISMATCH')
        identity_source = record['identity_source']
        repository = self.policy['authority']['repository']
        self._main_ancestor(repository, identity_source['commit'])
        raw = self.api(f'repos/{repository}/contents/{quote(safe_path(identity_source["path"]), safe="/")}?ref={identity_source["commit"]}', raw=True)
        require(sha(raw) == identity_source['sha256'], 'IMMUTABLE_IDENTITY_BYTES_MISMATCH')
        require(exact(parse(raw), {'identity': basis['identity'], 'version': basis['version'],
                                 'manifest_sha256': manifest['sha256'], 'targets': list(TARGETS)}), 'IMMUTABLE_IDENTITY_BINDING_CHANGED')

    def scenario(self, scenario_id, manifest, target):
        scenarios = unique(self.registry('authority')['scenarios'], 'id')
        record = scenarios[scenario_id]
        current_record(record, current_time())
        require(record['state'] == 'APPROVED' and record['manifest_sha256'] == manifest['sha256']
                and record['basis_version'] == VERSION and record['target'] == target, 'SCENARIO_AUTHORIZATION_INVALID')
        return record

    def execution_origin(self, report_ref, report):
        registry = self.registry('execution')
        records = unique(registry['executions'], 'id')
        matches = [r for r in records.values() if r.get('report_sha256') == report_ref['sha256']]
        require(len(matches) == 1, 'EXECUTION_ORIGIN_UNVERIFIED')
        attestation = matches[0]
        current_record(attestation, current_time())
        require(attestation['state'] == 'VERIFIED_BUSINESS_RUNTIME'
                and attestation['report_bytes'] == report_ref['bytes']
                and exact(attestation['verified_raw_logs'], report['logs'])
                and exact(attestation['origin'], {key: report[key] for key in ('run_id', 'command', 'environment', 'observed_at', 'repository', 'source_sha', 'owner', 'consumer', 'kind', 'state', 'synthetic')}), 'EXECUTION_ORIGIN_MISMATCH')

    def remote_main(self, repository, commit):
        require(repository in self.policy['repositories'] and re.fullmatch('[0-9a-f]{40}', commit), 'REMOTE_REPOSITORY_OR_COMMIT_UNKNOWN')
        key = (repository, commit)
        if key in self.remote_results:
            return
        self._main_ancestor(self.policy['repositories'][repository], commit)
        self.remote_results.add(key)

    def _main_ancestor(self, repo, commit):
        require(isinstance(commit, str) and re.fullmatch('[0-9a-f]{40}', commit), 'REMOTE_COMMIT_INVALID')
        head = self.api(f'repos/{repo}/commits/main')['sha']
        require(re.fullmatch('[0-9a-f]{40}', head), 'REMOTE_MAIN_INVALID')
        comparison = self.api(f'repos/{repo}/compare/{commit}...{head}')
        require(comparison['status'] in {'ahead', 'identical'} and comparison['merge_base_commit']['sha'] == commit
                and comparison['base_commit']['sha'] == commit, 'REMOTE_ANCESTRY_UNVERIFIED')


def execution(root, reference, verifier):
    report = parse(read_ref(root, reference))
    require(report['kind'] == 'BUSINESS_RUNTIME' and report['state'] == 'PASS' and report['synthetic'] is False, 'REAL_RUNTIME_REQUIRED')
    for key in ('run_id', 'command', 'environment', 'observed_at', 'repository', 'source_sha', 'owner', 'consumer'):
        require(isinstance(report[key], str) and report[key].strip(), 'EXECUTION_' + key.upper() + '_REQUIRED')
    require(timestamp(report['observed_at']) <= current_time(), 'EXECUTION_TIME_IN_FUTURE')
    require(isinstance(report['logs'], list) and report['logs'], 'RAW_LOGS_REQUIRED')
    unique(report['logs'], 'path')
    for log in report['logs']:
        require(read_ref(root, log), 'EMPTY_RAW_LOG')
    verifier.execution_origin(reference, report)
    verifier.remote_main(report['repository'], report['source_sha'])
    return report


def runtime_valid(root, ref, manifest, basis, graph, target, verifier):
    report = parse(read_ref(root, ref))
    require(report['target'] == target and report['basis_version'] == basis['version']
            and report['manifest_sha256'] == manifest['sha256'], 'RUNTIME_BASIS_BINDING_INVALID')
    expected = [row for row in basis['rows'] if row['target'] == target]
    require(exact(report['obligations'], expected), 'RUNTIME_OBLIGATION_COVERAGE_INVALID')
    planning = {e['id']: e for e in graph['edges'] if target in e['targets']}
    scenarios = unique(report['scenarios'], 'id')
    require(scenarios, 'APPROVED_SCENARIOS_REQUIRED')
    covered = set()
    for scenario_id, scenario in scenarios.items():
        approved = verifier.scenario(scenario_id, manifest, target)
        require(approved['target'] == target and approved['obligation_ids'] and len(set(approved['obligation_ids'])) == len(approved['obligation_ids']), 'SCENARIO_OBLIGATIONS_INVALID')
        require(set(approved['obligation_ids']) <= {r['id'] for r in expected}, 'SCENARIO_FOREIGN_OBLIGATION')
        covered.update(approved['obligation_ids'])
        configuration = parse(read_ref(root, approved['configuration']))
        require(configuration.get('state') == 'CONFIGURED', 'ACTUAL_CONFIGURATION_REQUIRED')
        def bound_execution(reference):
            proof = execution(root, reference, verifier)
            require(proof['basis_version'] == basis['version'] and proof['manifest_sha256'] == manifest['sha256']
                    and proof['target'] == target and proof['scenario_id'] == scenario_id
                    and exact(proof['obligation_ids'], approved['obligation_ids'])
                    and exact(proof['contract_bindings'], approved['edges'])
                    and exact(proof['configuration'], approved['configuration']), 'EXECUTION_SCENARIO_BINDING_INVALID')
            return proof

        runs = [bound_execution(ref) for ref in scenario['executions']]
        require(runs and set(approved['required_evidence_types']) == {r['kind'] for r in runs}, 'SCENARIO_EVIDENCE_TYPES_MISSING')
        selected = unique(approved['edges'], 'id')
        observed = unique(scenario['edges'], 'id')
        require(set(selected) == set(observed), 'SCENARIO_EDGE_COVERAGE_INVALID')
        for edge_id, choice in selected.items():
            require(edge_id in planning and choice['reason'], 'SCENARIO_EDGE_UNDECLARED')
            edge = observed[edge_id]
            contract = choice['contract']
            require(contract['profile'] and contract['version'] and re.fullmatch('[0-9a-f]{64}', contract['sha256'])
                    and exact(contract, edge['contract']), 'EXACT_PROFILE_VERSION_HASH_REQUIRED')
            require(contract['artifact']['sha256'] == contract['sha256'], 'PACKAGE_HASH_BINDING_INVALID')
            read_ref(root, contract['artifact'])
            for role in ('provider', 'consumer'):
                proof = bound_execution(edge[role])
                require(proof['repository'] == planning[edge_id][role + '_repository'], 'EDGE_REPOSITORY_MISMATCH')
    require(covered == {r['id'] for r in expected}, 'SCENARIO_FULL_OBLIGATION_COVERAGE_REQUIRED')
    return True


def evaluate(root, selection, *, verifier=None, archives=None):
    """Return independent predicates; never mutate ledger or infer business PASS.

    `verifier` is a trusted Python caller injection point for isolated tests.
    The CLI never obtains a verifier, key, registry URL or trust flag from JSON.
    """
    result = {'basis_adoption_valid': False, 'inventory_coverage_valid': False,
              'target_runtime_valid': {t: False for t in TARGETS}, 'errors': []}
    if not selection:
        result['errors'].append('NO_BASIS_SELECTED')
        return result
    try:
        if archives is None:
            archives = tuple(Path(p).resolve() for p in os.environ.get('CP6_ACCEPTANCE_ARCHIVE_ROOTS', '').split(os.pathsep) if p)
        basis, graph = load_basis(root, selection['manifest'], archives)
        result['inventory_coverage_valid'] = True
        if verifier is None:
            verifier = TrustedRegistryVerifier.from_environment(root)
        verifier.adoption(selection['manifest'], basis)
        result['basis_adoption_valid'] = True
        runtime = selection.get('runtime', {})
        require(set(runtime) <= set(TARGETS), 'RUNTIME_TARGET_NOT_WHITELISTED')
        for target in TARGETS:
            if target not in runtime:
                result['errors'].append(target + ': RUNTIME_NOT_SUBMITTED')
                continue
            try:
                result['target_runtime_valid'][target] = runtime_valid(root, runtime[target], selection['manifest'], basis, graph, target, verifier)
            except (ValueError, KeyError, TypeError, OSError, StopIteration, subprocess.SubprocessError) as exc:
                result['errors'].append(target + ': ' + str(exc))
    except (ValueError, KeyError, TypeError, OSError, StopIteration, subprocess.SubprocessError) as exc:
        result['errors'].append(str(exc))
    return result
