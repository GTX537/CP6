"""Read-only WP02-02 preparation identity checks; never a trust verifier.

Caller-supplied expectations require independent review. This tool cannot
authenticate that approval, retrieve a feed, verify signatures, or qualify Main.
CLI always exits 2 and reports BLOCKED, even when identity checks match.
"""
import argparse
import base64
import binascii
import hashlib
import io
import json
from pathlib import Path
import re
import stat
import sys
import xml.etree.ElementTree as ET
import zipfile

IDS = tuple('CP6.Platform.' + x for x in ('Abstractions', 'AspNetCore', 'Contracts',
            'Deployment', 'EntityFramework', 'Messaging', 'Release'))
RUNTIME_IDS = tuple(x for x in IDS if not x.endswith(('.Deployment', '.Release')))
MAIN_REPOSITORY = 'https://github.com/GTX537/CP6'
PLATFORM_REPOSITORY = 'https://github.com/GTX537/CP6.Platform'
CTX_ENTRY = 'contracts/release/capabilities/v1/capability-matrix.v1.schema.json'
CTX_SCHEMA_ID = 'https://schemas.cp6.dev/release/capabilities/v1/capability-matrix'
MAX_ARCHIVE = 128 * 1024 * 1024
MAX_ENTRY = 32 * 1024 * 1024
MAX_EXPANDED = 256 * 1024 * 1024
MAX_JSON = 4 * 1024 * 1024
MAX_ENTRIES = 4096
NUSPEC_NAMESPACES = (
    'http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd',
    'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd',
)


class Invalid(ValueError):
    pass


def require(condition, detail):
    if not condition:
        raise Invalid(detail)


def fields(value, expected, label):
    require(type(value) is dict and set(value) == set(expected.split()),
            label + ': missing/unknown fields or wrong object type')


def string(value, label):
    require(type(value) is str and bool(value) and value == value.strip()
            and len(value) <= 4096 and not any(ord(c) < 32 for c in value),
            label + ': expected nonempty bounded string')


def digest(value, length, label):
    require(type(value) is str and re.fullmatch('[0-9a-f]{' + str(length) + '}', value),
            label + ': expected lowercase hexadecimal digest')


def safe_entry(name):
    require(type(name) is str and bool(name) and len(name) <= 512
            and '\\' not in name and ':' not in name and '\x00' not in name
            and not name.startswith('/') and not any(ord(c) < 32 for c in name),
            'unsafe archive entry')
    parts = name.rstrip('/').split('/')
    require(all(p not in ('', '.', '..') and not p.endswith((' ', '.')) for p in parts),
            'unsafe archive entry segments')
    require(all(not re.fullmatch(r'(?i)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?', p)
                for p in parts), 'reserved archive entry')


def unique_strings(values, expected, label):
    require(type(values) is list and all(type(v) is str for v in values)
            and len(values) == len(expected) and set(values) == set(expected),
            label + ': exact unique set required')


def validate_manifest(manifest):
    fields(manifest, 'schemaVersion mode approval observation', 'manifest')
    require(type(manifest['schemaVersion']) is int and manifest['schemaVersion'] == 1,
            'unsupported schemaVersion')
    require(manifest['mode'] == 'PREPARATION_ONLY', 'only preparation mode supported')
    approval, observation = manifest['approval'], manifest['observation']
    fields(approval, 'reference mainRepository mainSourceSha platformRepository platformSourceSha '
           'packageVersion feedUrl compatibility requiredCapabilities runtimePackageIds', 'approval')
    for name in ('reference', 'packageVersion', 'feedUrl'):
        string(approval[name], name)
    require(approval['mainRepository'] == MAIN_REPOSITORY, 'unexpected Main repository')
    require(approval['platformRepository'] == PLATFORM_REPOSITORY, 'unexpected Platform repository')
    for name in ('mainSourceSha', 'platformSourceSha'):
        digest(approval[name], 40, name)
    require(re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?', approval['packageVersion']),
            'exact package version required')
    require(approval['compatibility'] in ('UNKNOWN', 'COMPATIBLE', 'INCOMPATIBLE'),
            'unknown compatibility token')
    unique_strings(approval['requiredCapabilities'], ['ctx-binding-v1'], 'requiredCapabilities')
    unique_strings(approval['runtimePackageIds'], RUNTIME_IDS, 'runtimePackageIds')
    fields(observation, 'feedUrl publication packages', 'observation')
    string(observation['feedUrl'], 'observed feedUrl')
    require(observation['publication'] in ('LOCAL_ONLY', 'UNVERIFIED'),
            'preparation cannot self-assert formal publication')
    packages = observation['packages']
    require(type(packages) is list and len(packages) == 7, 'exact seven producer packages required')
    for package in packages:
        fields(package, 'id path sha256 sha512Archive schemas', 'package')
        string(package['id'], 'package id')
        string(package['path'], 'package path')
        digest(package['sha256'], 64, 'package sha256')
        string(package['sha512Archive'], 'sha512Archive')
        try:
            decoded = base64.b64decode(package['sha512Archive'], validate=True)
        except (ValueError, binascii.Error) as ex:
            raise Invalid('invalid sha512Archive base64') from ex
        require(len(decoded) == 64 and base64.b64encode(decoded).decode() == package['sha512Archive'],
                'sha512Archive must be canonical base64 of 64 raw bytes')
        schemas = package['schemas']
        require(type(schemas) is list and len(schemas) <= MAX_ENTRIES, 'invalid schema list')
        names = []
        for schema in schemas:
            fields(schema, 'entry sha256', 'schema')
            safe_entry(schema['entry'])
            require(schema['entry'].endswith('.schema.json'), 'schema entry suffix required')
            digest(schema['sha256'], 64, 'schema sha256')
            names.append(schema['entry'].casefold())
        require(len(set(names)) == len(names), 'duplicate schema entry')
    unique_strings([p['id'] for p in packages], IDS, 'producer package IDs')
    paths = [p['path'].replace('\\', '/').casefold() for p in packages]
    require(len(set(paths)) == 7, 'duplicate archive path')


def json_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate JSON field: ' + key)
        result[key] = value
    return result


def read_json_bytes(data):
    require(len(data) <= MAX_JSON, 'JSON size limit exceeded')
    return json.loads(data.decode('utf-8-sig'), object_pairs_hook=json_pairs,
                      parse_constant=lambda value: require(False, 'nonfinite JSON value: ' + value))


def read_bounded(path, limit):
    with path.open('rb') as stream:
        data = stream.read(limit + 1)
    require(len(data) <= limit, 'file size limit exceeded')
    return data


def initial_report():
    return {'task': 'WP02-02', 'mode': 'PREPARATION_ONLY', 'admission': 'BLOCKED',
            'entryGate': 'NOT_CHECKED', 'identityChecks': 'NOT_RUN',
            'states': {k: ('BLOCKED' if k == 'trust' else 'NOT_RUN') for k in
                       ('source', 'shape', 'artifacts', 'trust', 'consumer', 'host', 'runtime', 'business')},
            'findings': [], 'packages': [],
            'limits': ['No signature/feed authentication or independent approval authentication.',
                       'Nuspec source metadata comparison is not clean source/reproducible build verification.',
                       'Schema shape is not DLL API, Main host, database, or business qualification.',
                       'Raw archive SHA-512 is not asserted equivalent to signed NuGet lock contentHash.']}


def finding(report, code, detail, category=None):
    report['findings'].append({'code': code, 'detail': detail, 'category': category})
    if category:
        report['states'][category] = 'REJECTED'


def check_nuspec(data, package_id, approval, report):
    require(len(data) <= MAX_JSON, 'nuspec size limit')
    text = data.decode('utf-8-sig')
    require(not re.search(r'<!\s*(DOCTYPE|ENTITY)', text, re.IGNORECASE), 'DTD/entities forbidden')
    root = ET.fromstring(text)
    namespaces = [ns for ns in NUSPEC_NAMESPACES if root.tag == '{' + ns + '}package']
    require(len(namespaces) == 1, 'unexpected nuspec namespace/root')
    namespace_prefix = '{' + namespaces[0] + '}'
    require(all(element.tag.startswith(namespace_prefix) for element in root.iter()),
            'mixed/unknown nuspec element namespace')
    tag = lambda name: namespace_prefix + name
    metadata = root.findall(tag('metadata'))
    require(len(metadata) == 1, 'exactly one metadata element required')
    metadata = metadata[0]
    def one(name):
        nodes = metadata.findall(tag(name))
        require(len(nodes) == 1, 'exactly one ' + name + ' required')
        return nodes[0]
    actual_id, version = one('id').text, one('version').text
    repository = one('repository')
    if actual_id != package_id:
        finding(report, 'PACKAGE_ID_MISMATCH', package_id, 'source')
    if version != approval['packageVersion']:
        finding(report, 'VERSION_MISMATCH', package_id, 'source')
    if repository.get('type') != 'git' or repository.get('url') != approval['platformRepository']:
        finding(report, 'REPOSITORY_MISMATCH', package_id, 'source')
    if repository.get('commit') != approval['platformSourceSha']:
        finding(report, 'SOURCE_MISMATCH', package_id, 'source')
    dependencies = []
    for group in metadata.iter(tag('group')):
        ids = [dep.get('id', '').casefold() for dep in group.findall(tag('dependency'))]
        require(len(ids) == len(set(ids)), 'duplicate dependency in target framework group')
    for dep in metadata.iter(tag('dependency')):
        dep_id, dep_version = dep.get('id'), dep.get('version')
        require(type(dep_id) is str and type(dep_version) is str, 'dependency ID/version required')
        dependencies.append({'id': dep_id, 'version': dep_version})
        if dep_id.casefold().startswith('cp6.platform.'):
            v = approval['packageVersion']
            if dep_id not in IDS or dep_version not in (v, '[' + v + ']', '[' + v + ', ' + v + ']', '[' + v + ',' + v + ']'):
                finding(report, 'DEPENDENCY_MISMATCH', package_id + ': ' + dep_id + ' ' + dep_version, 'source')
    return {'id': actual_id, 'version': version, 'sourceSha': repository.get('commit'),
            'repository': repository.get('url'), 'dependencies': dependencies}


def check_ctx(schema):
    require(type(schema) is dict and schema.get('$id') == CTX_SCHEMA_ID, 'ctx schema ID mismatch')
    alternatives = schema['properties']['capabilities']['items']['oneOf']
    require(type(alternatives) is list, 'capability alternatives required')
    ids = [a.get('properties', {}).get('id', {}).get('const') for a in alternatives]
    require(all(type(i) is str and i in ('S01-09', 'ctx-binding-v1') for i in ids)
            and len(ids) == len(set(ids)), 'unknown/duplicate capability declaration')
    ctx = [a for a in alternatives if a.get('properties', {}).get('id', {}).get('const') == 'ctx-binding-v1']
    require(len(ctx) == 1, 'exact ctx-binding-v1 clause required')
    clause = ctx[0]
    require(clause.get('type') == 'object' and clause.get('additionalProperties') is False,
            'closed ctx capability object required')
    unique_strings(clause.get('required'), ['id', 'version', 'profiles', 'scope'], 'ctx required fields')
    properties = clause['properties']
    fields(properties, 'id version profiles scope', 'ctx properties')
    require(properties == {'id': {'const': 'ctx-binding-v1'}, 'version': {'const': '1.0.0'},
                           'profiles': {'const': ['ctx-v1']},
                           'scope': {'const': ['Background', 'Http', 'Message']}},
            'ctx profile/version/scope mismatch')


def inspect_package(package, approval, base, report):
    package_id = package['id']
    path = Path(package['path'])
    if not path.is_absolute():
        path = base / path
    data = read_bounded(path, MAX_ARCHIVE)
    sha256 = hashlib.sha256(data).hexdigest()
    sha512 = base64.b64encode(hashlib.sha512(data).digest()).decode()
    observation = {'id': package_id, 'sha256': sha256, 'sha512Archive': sha512, 'schemas': []}
    report['packages'].append(observation)
    if sha256 != package['sha256'] or sha512 != package['sha512Archive']:
        finding(report, 'HASH_MISMATCH', package_id, 'artifacts')
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        infos = archive.infolist()
        require(0 < len(infos) <= MAX_ENTRIES, 'archive entry count limit')
        require(sum(i.file_size for i in infos) <= MAX_EXPANDED, 'archive expanded size limit')
        names = set()
        for info in infos:
            safe_entry(info.filename)
            key = info.filename.rstrip('/').casefold()
            require(key not in names, 'duplicate/case-colliding archive entry')
            names.add(key)
            require(info.file_size <= MAX_ENTRY and info.file_size <= max(1024 * 1024, info.compress_size * 200),
                    'archive member size/compression ratio limit')
            require(not info.flag_bits & 1 and info.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED),
                    'encrypted/unsupported compression')
            mode = (info.external_attr >> 16) & 0xFFFF
            require(stat.S_IFMT(mode) in (0, stat.S_IFREG, stat.S_IFDIR), 'special archive entry forbidden')
        nuspecs = [i.filename for i in infos if i.filename.casefold().endswith('.nuspec')]
        require(nuspecs == [package_id + '.nuspec'], 'exact package root nuspec required')
        try:
            observation['nuspec'] = check_nuspec(archive.read(nuspecs[0]), package_id, approval, report)
        except (Invalid, ET.ParseError, UnicodeError) as ex:
            finding(report, 'NUSPEC_INVALID', package_id + ': ' + str(ex), 'source')
        actual_schemas = {i.filename for i in infos if i.filename.casefold().endswith('.schema.json')}
        pins = {s['entry']: s['sha256'] for s in package['schemas']}
        if actual_schemas != set(pins):
            finding(report, 'SCHEMA_SET_MISMATCH', package_id, 'shape')
        parsed = {}
        for name in sorted(actual_schemas):
            schema_bytes = archive.read(name)
            actual_hash = hashlib.sha256(schema_bytes).hexdigest()
            observation['schemas'].append({'entry': name, 'sha256': actual_hash})
            if pins.get(name) != actual_hash:
                finding(report, 'SCHEMA_HASH_MISMATCH', package_id + ': ' + name, 'shape')
            try:
                parsed[name] = read_json_bytes(schema_bytes)
                require(type(parsed[name]) is dict, 'schema must be JSON object')
            except (Invalid, ValueError, UnicodeError, RecursionError) as ex:
                finding(report, 'SCHEMA_INVALID', package_id + ': ' + name + ': ' + str(ex), 'shape')
        if package_id == 'CP6.Platform.Release':
            if CTX_ENTRY not in actual_schemas:
                finding(report, 'MISSING_CTX_SCHEMA', 'Package cannot supply ctx-binding-v1 capability schema.', 'shape')
            elif CTX_ENTRY in parsed:
                try:
                    check_ctx(parsed[CTX_ENTRY])
                except (Invalid, KeyError, TypeError, AttributeError) as ex:
                    finding(report, 'CTX_SCHEMA_SHAPE_MISMATCH', str(ex), 'shape')


def evaluate(manifest, base):
    report = initial_report()
    try:
        validate_manifest(manifest)
    except (Invalid, TypeError, ValueError, RecursionError) as ex:
        finding(report, 'INVALID_INPUT', str(ex))
        return report
    approval, observation = manifest['approval'], manifest['observation']
    report['approvalDeclaration'] = approval
    report['publicationObservation'] = observation['publication']
    report['compatibilityDeclaration'] = approval['compatibility']
    report['producerPackageIds'] = list(IDS)
    report['runtimePackageIds'] = list(RUNTIME_IDS)
    report['states'].update(source='NUSPEC_METADATA_MATCHED', shape='SCHEMA_BYTES_AND_CTX_DECLARATION_MATCHED',
                            artifacts='ARCHIVE_HASHES_MATCHED')
    if approval['feedUrl'] != observation['feedUrl']:
        finding(report, 'FEED_MISMATCH', 'Declared observed feed differs from independent expected feed.', 'artifacts')
    for package in observation['packages']:
        try:
            inspect_package(package, approval, Path(base), report)
        except (Invalid, OSError, ValueError, zipfile.BadZipFile, RuntimeError, EOFError,
                NotImplementedError, RecursionError) as ex:
            finding(report, 'ARCHIVE_INVALID', package['id'] + ': ' + str(ex), 'artifacts')
            report['states']['source'] = 'REJECTED'
            report['states']['shape'] = 'REJECTED'
    report['identityChecks'] = ('REJECTED' if any(report['states'][s] == 'REJECTED'
                                for s in ('source', 'shape', 'artifacts')) else 'MATCHED')
    if approval['compatibility'] != 'COMPATIBLE':
        finding(report, 'COMPATIBILITY_NOT_QUALIFIED', approval['compatibility'])
    if observation['publication'] == 'LOCAL_ONLY' or '-wp02.' in approval['packageVersion']:
        finding(report, 'LOCAL_CANDIDATE_NOT_FORMAL', 'Local candidate is not formal signed feed admission.')
    finding(report, 'FORMAL_TRUST_NOT_RUN', 'Signer, RFC3161 timestamp, current revocation and immutable feed readback not verified.')
    finding(report, 'PREPARATION_ONLY', 'No approval/adoption/host/runtime/business success path exists in this tool.')
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        # Exclusive creation; no overwrite, delete, extraction, parent creation or subprocess.
        args.output.mkdir(exist_ok=False)
    except OSError as ex:
        print('Output must be a new task-owned directory with an existing parent: ' + str(ex), file=sys.stderr)
        return 2
    report = initial_report()
    input_hash = None
    try:
        data = read_bounded(args.input, MAX_JSON)
        input_hash = hashlib.sha256(data).hexdigest()
        report = evaluate(read_json_bytes(data), args.input.resolve().parent)
    except (Invalid, OSError, ValueError, UnicodeError, RecursionError) as ex:
        finding(report, 'INVALID_INPUT', str(ex))
    report['execution'] = {'python': sys.version, 'inputSha256': input_hash,
                           'checkerSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                           'exitCode': 2}
    with (args.output / 'report.json').open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(report, stream, indent=2, ensure_ascii=True)
        stream.write('\n')
    print('WP02-02 BLOCKED / PREPARATION_ONLY; report: ' + str(args.output / 'report.json'))
    return 2


if __name__ == '__main__':
    sys.exit(main())
