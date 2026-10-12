"""Verify WP05-01 proposed supplement integrity only; execute no source or business code.

Sources resolve through the byte-pinned 1.0.0 sources.json registry:
GIT_BLOB_BYTES uses its exact git_revision via git cat-file, never the worktree.
RAW_FILE_BYTES uses --archive-root. Locators are sheet+cells, line_start/line_end
+excerpt, or json_pointer+expected_value. Only --out is written; inputs cannot
be used as the output path. This check does not establish semantic completeness,
formal adoption, owner implementation, runtime acceptance or task delivery.
"""
import argparse
import collections
import datetime
import hashlib
import io
import json
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

BASE_MANIFEST_SHA256 = '979cb7f4fc779f6200e16b2919a89afc244681d122683036378e068931fcd3ee'
BASE_VERSION = 'CP6-WP05-01-ACCEPTANCE-BASIS/1.0.0-proposed'
VERSION = 'CP6-WP05-01-ACCEPTANCE-BASIS/1.1.0-proposed'
SCHEMA = 'CP6-WP05-01-ACCEPTANCE-BASIS-SUPPLEMENT/1'
BUNDLE = pathlib.Path('docs/development/task-deliveries/WP05-01')
BASE_FILES = {'README.md', 'sources.json', 'cases.json', 'normative-coverage.json', 'coverage.md', 'adoption.json'}
COUNTS = {'recovered': 221, 'r1': 8, 'detail': 10, 'body_locations': 295}
ROW_IDS = {f'WP05-01-REVIEW/{group}-{n:02}' for group, count in [('WB', 18), ('CP22', 15), ('R1', 10)] for n in range(1, count + 1)}


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('duplicate JSON key: ' + key)
        result[key] = value
    return result


def parse(raw):
    def invalid_number(value):
        raise ValueError('non-JSON numeric constant: ' + value)
    return json.loads(raw.decode('utf-8-sig'), object_pairs_hook=unique_object, parse_constant=invalid_number)


def canonical(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False)


def json_pointer(document, pointer):
    if not isinstance(pointer, str) or (pointer and not pointer.startswith('/')):
        raise ValueError('invalid JSON pointer')
    if pointer == '':
        return document
    value = document
    for raw in pointer.split('/')[1:]:
        if re.search(r'~(?![01])', raw):
            raise ValueError('invalid JSON pointer escape')
        token = raw.replace('~1', '/').replace('~0', '~')
        if isinstance(value, list):
            if not re.fullmatch(r'0|[1-9][0-9]*', token):
                raise ValueError('invalid JSON array index')
            value = value[int(token)]
        elif isinstance(value, dict):
            value = value[token]
        else:
            raise ValueError('pointer traverses a scalar')
    return value


def relative_path(value):
    if not isinstance(value, str) or not value or '\\' in value or ':' in value:
        raise ValueError('source path must be repository-relative POSIX text')
    path = pathlib.PurePosixPath(value)
    if path.is_absolute() or any(part in ('', '.', '..') for part in value.split('/')):
        raise ValueError('unsafe source path')
    return pathlib.Path(*path.parts)


def workbook_cells(raw):
    ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}

    def string_value(node):
        if node is None:
            return ''
        # Only displayed text and rich-text runs, not phonetic annotation text.
        return ''.join((part.text or '') for part in node.findall('s:t', ns) + node.findall('s:r/s:t', ns))

    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        strings = [string_value(node) for node in ET.fromstring(archive.read('xl/sharedStrings.xml')).findall('s:si', ns)] if 'xl/sharedStrings.xml' in archive.namelist() else []
        rels = {node.attrib['Id']: node.attrib['Target'] for node in ET.fromstring(archive.read('xl/_rels/workbook.xml.rels'))}
        result = {}
        for sheet in ET.fromstring(archive.read('xl/workbook.xml')).findall('s:sheets/s:sheet', ns):
            target = rels[sheet.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id']]
            target = target.lstrip('/') if target.startswith('/') else 'xl/' + target
            values = {}
            for cell in ET.fromstring(archive.read(target)).findall('.//s:c', ns):
                kind = cell.attrib.get('t')
                cached = cell.find('s:v', ns)
                if kind == 'inlineStr':
                    value = string_value(cell.find('s:is', ns))
                elif kind == 's' and cached is not None:
                    value = strings[int(cached.text)]
                elif cell.find('s:f', ns) is not None:
                    # Do not evaluate a formula or confuse its cache with source text.
                    value = '=' + (cell.find('s:f', ns).text or '')
                else:
                    value = cached.text if cached is not None else ''
                values[cell.attrib['r']] = value
            result[sheet.attrib['name']] = values
        return result


def verify(args):
    errors, inputs = [], []
    protected = {args.supplement.resolve(), pathlib.Path(__file__).resolve()}
    root, archive_root = args.root.resolve(), args.archive_root.resolve()
    checks = 0

    def check(condition, label):
        nonlocal checks
        checks += 1
        if not condition:
            errors.append(label)

    def read_file(path, role):
        resolved = path.resolve()
        protected.add(resolved)
        raw = path.read_bytes()
        inputs.append({'role': role, 'path': str(resolved), 'representation': 'RAW_FILE_BYTES', 'sha256': sha(raw), 'bytes': len(raw)})
        return raw

    # Pre-protect all base artifacts even when parsing one of them fails.
    protected.update((root / BUNDLE / name).resolve() for name in BASE_FILES | {'basis-manifest.json'})
    checked_refs = 0
    try:
        data = parse(read_file(args.supplement, 'SUPPLEMENT'))
        manifest_raw = read_file(root / BUNDLE / 'basis-manifest.json', 'BASE_MANIFEST')
        check(sha(manifest_raw) == BASE_MANIFEST_SHA256, 'base manifest byte hash changed')
        manifest = parse(manifest_raw)
        check(manifest['version'] == BASE_VERSION, 'base version changed')
        artifacts = manifest['artifacts']
        check(len(artifacts) == 6 and {item['path'] for item in artifacts} == BASE_FILES, 'base manifest must contain the exact six normative artifacts')
        old = {}
        for item in artifacts:
            name = item['path']
            if name not in BASE_FILES:
                continue
            raw = read_file(root / BUNDLE / name, 'BASE_NORMATIVE_ARTIFACT')
            check(sha(raw) == item['sha256'], 'base artifact byte hash changed: ' + name)
            if name.endswith('.json'):
                old[name] = parse(raw)
        registry_list = old['sources.json']['sources']
        registry = {source['path']: source for source in registry_list}
        check(len(registry) == len(registry_list), 'duplicate source registry path')
        for source in registry_list:
            path = relative_path(source['path'])
            protected.update({(root / path).resolve(), (archive_root / path).resolve()})
        check(data['schema'] == SCHEMA and data['version'] == VERSION, 'supplement schema/version mismatch')
        check(data['base_version'] == BASE_VERSION, 'supplement base version mismatch')
        check(data['base_manifest_sha256'] == BASE_MANIFEST_SHA256, 'supplement base_manifest_sha256 mismatch')
        check(data.get('preserved_proposed_manifest_sha256') == BASE_MANIFEST_SHA256, 'preserved manifest pin mismatch')
        check(data['status'] == 'PROPOSED' and data['runtime_state'] == 'NOT_RUN', 'supplement must remain PROPOSED/NOT_RUN')
        check('decision_record' in data and data['decision_record'] is None, 'no formal adoption decision permitted')
        check(not data.get('approval_evidence') and data.get('approved_by') is None and data.get('effective_at') is None and not data.get('execution_evidence'), 'no approval/runtime evidence permitted')
        check(data['preserve_existing_counts'] == COUNTS, 'declared predecessor counts changed')
        cases = old['cases.json']['cases']
        body = old['normative-coverage.json']['obligations']
        counts = collections.Counter(case['kind'] for case in cases)
        check(counts == {'RECOVERED_SCENARIO': 221, 'R1_REQUIRED_SCENARIO': 8, 'R1_DETAIL_VECTOR': 10} and len(body) == 295, 'actual predecessor case/body counts changed')
        check(all(row['runtime_state'] == 'NOT_RUN' and not row['execution_evidence'] for row in cases + body), 'predecessor runtime result changed')
        known = {row['id'] for row in cases + body}
        check(len(known) == len(cases) + len(body), 'predecessor IDs must be unique')
        adoption = old['adoption.json']
        check(adoption['status'] == 'PROPOSED' and adoption['decision_record'] is None and not adoption['approval_evidence'] and adoption['gate_implementation'] == 'NOT_CHANGED', 'predecessor adoption/gate boundary changed')
        gaps = adoption['legacy_gaps']
        check([(gap['target'], gap['declared_legacy_count']) for gap in gaps] == [('PLM-ITER-01', 30), ('PLM-REL-01', 48)], 'legacy30/48 gap identities changed')
        check(all(gap['legacy_case_ids'] is None and gap['missing_original_sha256'] is None and gap['historical_inventory_state'] == 'NOT_INVENTORIED' and gap['recovery_state'] == 'UNRECOVERABLE' and gap['equivalence'] == 'UNPROVABLE' for gap in gaps), 'legacy gaps were incorrectly promoted or populated')
        rows = data['rows']
        check(len(rows) == 43 and {row['id'] for row in rows} == ROW_IDS, 'exact 43 supplement IDs required')
        check(data['summary'] == dict(collections.Counter(row['revision_action'] for row in rows)), 'supplement action summary differs from actual rows')
        source_cache, workbook_cache = {}, {}

        def source_bytes(ref, label):
            path = ref['path']
            relative_path(path)
            if path not in registry:
                raise ValueError('source not registered in immutable predecessor: ' + path)
            registered = registry[path]
            representation = registered['representation']
            check(ref['sha256'] == registered['sha256'], label + ': source pin differs from registered source')
            if 'representation' in ref:
                check(ref['representation'] == representation, label + ': representation mismatch')
            if representation == 'GIT_BLOB_BYTES':
                revision = registered['git_revision']
                if not re.fullmatch(r'[0-9a-f]{40}', revision or ''):
                    raise ValueError('source requires a full pinned Git commit')
                check(ref.get('git_revision') == revision, label + ': Git revision must match registered fixed blob')
            elif representation == 'RAW_FILE_BYTES':
                check(ref.get('git_revision') is None, label + ': archive source cannot claim a Git revision')
            else:
                raise ValueError('unsupported registered source representation')
            if path not in source_cache:
                if representation == 'GIT_BLOB_BYTES':
                    proc = subprocess.run(['git', '--no-optional-locks', 'cat-file', 'blob', revision + ':' + path], cwd=root, capture_output=True, check=False)
                    if proc.returncode:
                        raise ValueError('cannot read exact Git blob: ' + proc.stderr.decode('utf-8', errors='replace').strip())
                    raw = proc.stdout
                    inputs.append({'role': 'PINNED_SOURCE', 'path': path, 'representation': representation, 'git_revision': revision, 'git_object': revision + ':' + path, 'sha256': sha(raw), 'bytes': len(raw)})
                else:
                    source_path = (archive_root / relative_path(path)).resolve()
                    if not source_path.is_relative_to(archive_root):
                        raise ValueError('archive source resolves outside archive root')
                    raw = read_file(source_path, 'PINNED_SOURCE')
                check(sha(raw) == registered['sha256'], 'actual source byte hash mismatch: ' + path)
                source_cache[path] = raw
            return source_cache[path]

        for row in rows:
            label = row['id']
            check(row['target'] in {'PLM-ITER-01', 'PLM-REL-01'}, label + ': invalid target')
            check(row['runtime_state'] == 'NOT_RUN' and row['approval_state'] == 'PROPOSED', label + ': must remain PROPOSED/NOT_RUN')
            check(row.get('decision_record') is None and not row.get('approval_evidence') and not row.get('execution_evidence') and row.get('approved_by') is None and row.get('effective_at') is None, label + ': no approval/runtime evidence permitted')
            check(set(row['prior_basis_ids']) <= known, label + ': unknown predecessor ID')
            check(bool(row['scenario'] and row['required_observation'] and row['owner_scope']), label + ': observation and responsibility required')
            check(bool(row['source_refs']), label + ': source references required')
            for ref in row['source_refs']:
                checked_refs += 1
                try:
                    raw = source_bytes(ref, label)
                    modes = ['sheet' in ref or 'cells' in ref, any(key in ref for key in ('line_start', 'line_end', 'excerpt')), 'json_pointer' in ref or 'expected_value' in ref]
                    if sum(modes) != 1:
                        raise ValueError('exactly one complete locator type required')
                    if modes[0]:
                        if not ref['path'].endswith('.xlsx') or not isinstance(ref['cells'], dict) or not ref['cells']:
                            raise ValueError('xlsx sheet/cells locator required')
                        if ref['path'] not in workbook_cache:
                            workbook_cache[ref['path']] = workbook_cells(raw)
                        book = workbook_cache[ref['path']]
                        check(len(book) == data['sheet_count'] == 37, label + ': workbook sheet count changed')
                        cells = book[ref['sheet']]
                        for cell, expected in ref['cells'].items():
                            check(cell in cells and type(expected) is str and cells[cell] == expected, label + ': exact workbook cell mismatch: ' + ref['sheet'] + '!' + cell)
                    elif modes[1]:
                        start, end = ref['line_start'], ref['line_end']
                        lines = raw.decode('utf-8-sig').splitlines()
                        if type(start) is not int or type(end) is not int or not 1 <= start <= end <= len(lines):
                            raise ValueError('invalid 1-based inclusive line range')
                        check(ref['excerpt'] == '\n'.join(lines[start - 1:end]), label + ': exact line excerpt mismatch')
                    else:
                        actual = json_pointer(parse(raw), ref['json_pointer'])
                        check(canonical(actual) == canonical(ref['expected_value']), label + ': exact JSON pointer value mismatch')
                except (KeyError, IndexError, TypeError, ValueError, OSError, zipfile.BadZipFile, ET.ParseError) as exc:
                    errors.append(label + ': source locator error: ' + str(exc))
    except (KeyError, IndexError, TypeError, ValueError, OSError) as exc:
        errors.append('input/structure error: ' + str(exc))
    output_safe = args.out.resolve() not in protected
    check(output_safe, 'output path overlaps a read-only input; report not written')
    report = {
        'scope': 'WP05_SUPPLEMENT_STATIC_INTEGRITY_ONLY',
        'result': 'FAIL' if errors else 'PASS',
        'checked_at_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'checker_sha256': sha(pathlib.Path(__file__).read_bytes()),
        'version': VERSION,
        'base_manifest_sha256': BASE_MANIFEST_SHA256,
        'input_hashes': inputs,
        'checks': checks,
        'source_refs_checked': checked_refs,
        'errors': errors,
        'runtime_result': 'NOT_RUN',
        'adoption': 'PROPOSED',
        'limitations': ['No source program, Office formula, business service, database or GitHub Actions executed.', 'Only source integrity, locator equality and stated boundaries checked; not semantic completeness or formal approval.', 'No WP01-01 completion gate implementation, task delivery or remote-main inclusion certified.'],
    }
    if output_safe:
        try:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_bytes((json.dumps(report, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
        except OSError as exc:
            report['errors'].append('report write failed: ' + str(exc))
            report['result'] = 'FAIL'
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report['result'] == 'PASS' else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', required=True, type=pathlib.Path, help='Repository/worktree containing the unchanged 1.0.0 bundle and pinned Git blobs')
    parser.add_argument('--archive-root', required=True, type=pathlib.Path, help='Read-only archive root containing the registered docs/ paths')
    parser.add_argument('--supplement', required=True, type=pathlib.Path, help='1.1.0-proposed supplement JSON')
    parser.add_argument('--out', required=True, type=pathlib.Path, help='Only writable output: static verification JSON report')
    return verify(parser.parse_args())


if __name__ == '__main__':
    sys.exit(main())
