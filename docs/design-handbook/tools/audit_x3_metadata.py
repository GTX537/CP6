"""Check X3 selectors, exact reviewed text reuse and original ZIP identities.

Reads data only. Source scripts and business examples are never executed.
This is documentation evidence, not runtime acceptance.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import zipfile

from audit_x4_metadata import leaves


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--handbook', required=True, type=Path)
    h = parser.parse_args().handbook
    reading = json.loads((h / 'evidence/root-specialty-reading.json').read_text(encoding='utf-8'))['files']
    known = json.loads((h / 'indexes/materialized-content-paths.json').read_text(encoding='utf-8'))
    errors = []

    def check(ok, message):
        if not ok:
            errors.append(message)

    def get(prefix):
        return next(r for r in reading if r['sha256'].startswith(prefix))

    def body(r):
        return json.loads(Path(r['source_path']).read_text(encoding='utf-8-sig'))

    index = body(get('fab930'))
    composition = body(get('bccbdd'))
    acs = body(get('5c6f67'))
    tasks = body(get('7ce1a9'))
    members = body(get('7fd745'))
    out = dict(schema='CP6-X3-LOCAL-METADATA-RECONCILIATION-1',
               business_tests_executed=False, targets=[], metadata_differential=[],
               packages=[], member_dispositions=[], errors=errors, limitations=[],
               interpretation='Exact-text reuse supplements semantic reading of current bodies; ZIP hashes prove bytes only, not source-code review or business execution.')
    check(acs['Count'] == len(acs['Cases']) == 112 and acs['ExecutedCount'] == 0, 'AC totals')
    check(tasks['Count'] == len(tasks['Tasks']) == 31, 'task total')
    check(sum(i['SpecCount'] for i in composition['Targets']) == 27, 'SPEC total')
    check(sum(i['Normative']['Bytes'] for i in composition['Targets']) == 321194, 'six normative bytes')
    base_ua = get('3c014')
    base_current = get('6977ea')
    for ix in index['Targets']:
        target = ix['Target']
        ur = next(r for r in reading if r['sha256'] == ix['Acceptance']['Sha256'])
        cr = next(r for r in reading if r['targets'] == [target] and 'acceptance_entry_provenance_not_body' in r['roles'])
        ua, current = body(ur), body(cr)
        norm = ix['Normative']
        raw = Path(known[norm['Sha256']]).read_bytes()
        check(hashlib.sha256(raw).hexdigest() == norm['Sha256'] and len(raw) == norm['Bytes'], target + ' current body identity')
        check(current['Normative']['Sha256'] == ua['Normative']['Sha256'] == norm['Sha256'], target + ' selector')
        check(current['Acceptance']['Sha256'] == ur['sha256'], target + ' CURRENT UA selector')
        check(current['OriginalScope'] == ua['OriginalScope'], target + ' scope reuse equality')
        co = next(r for r in composition['Targets'] if r['Target'] == target)
        check(co['Normative'] == norm and co['SpecIds'] == current['OriginalSpecIds'], target + ' composition')
        lines = raw.decode('utf-8-sig').splitlines()
        cases = [r for r in acs['Cases'] if r['Target'] == target]
        dev = [r for r in tasks['Tasks'] if r['Target'] == target]
        for case in cases:
            check(lines[case['SourceLine'] - 1] == case['ExactMarkdownRow'], case['Id'] + ' exact row')
            fields = [x.strip() for x in case['ExactMarkdownRow'].strip('|').split('|')]
            check(fields[:4] == [case['Id'], case['SpecLabelAsWritten'], case['ScenarioAndAction'], case['ExpectedResult']], case['Id'] + ' parsed meaning')
            check(case['Status'] == 'NOT_RUN' and case['Executed'] is False, case['Id'] + ' execution status')
            check(case['SpecId'] is None if case['AppliesToAllOriginalSpecs'] else case['SpecId'] in co['SpecIds'], case['Id'] + ' SPEC ownership')
        for task in dev:
            check(task['ExactSourceText'] in lines[task['SourceLine'] - 1], task['TaskIdentityKey'] + ' exact task text')
            check(task['Status'] == 'NOT_RUN' and task['ImplementationAuthorized'] is False, task['TaskIdentityKey'] + ' status')
        check(len(cases) == ix['OriginalAcCount'] == current['OriginalAcCount'], target + ' AC count')
        check(len(dev) == ix['DevelopmentTaskCount'] == current['OriginalDevelopmentTaskCount'], target + ' task count')
        check([r['Id'] for r in cases] == current['OriginalAcIds'], target + ' AC IDs')
        check([r['OriginalId'] for r in dev] == current['OriginalTaskIds'], target + ' task IDs')
        out['targets'].append(dict(target=target, normative=norm, current_path=cr['source_path'], ua_path=ur['source_path'],
                                   ac_rows_exact=True, task_text_exact=True, ac_count=len(cases), task_count=len(dev)))
        for r, baseline in [(ur, base_ua), (cr, base_current)]:
            original, actual = dict(leaves(body(baseline))), dict(leaves(body(r)))
            out['metadata_differential'].append(dict(source_path=r['source_path'], sha256=r['sha256'], baseline_path=baseline['source_path'],
                shared_equal_leaf_count=sum(k in original and type(original[k]) is type(v) and original[k] == v for k, v in actual.items()),
                total_leaf_count=len(actual), changed_or_added={k: v for k, v in actual.items() if k not in original or type(original[k]) is not type(v) or original[k] != v},
                removed=sorted(original.keys() - actual.keys())))
    source_sha = next(s for s in known if s.startswith('b062866'))
    reuse = json.loads(Path(known[source_sha]).read_text(encoding='utf-8'))
    out['source_reuse'] = []
    for section in reuse['OriginalSections']:
        lines = Path(known[section['Normative']['Sha256']]).read_text(encoding='utf-8').splitlines()
        for key, value in section.items():
            if isinstance(value, dict) and 'ExactText' in value:
                exact = value['ExactText'] == '\n'.join(lines[value['StartLine'] - 1:value['EndLine']]) + '\n'
                check(exact, section['Target'] + '/' + key + ' source reuse text')
                out['source_reuse'].append(dict(target=section['Target'], role=key, lines=[value['StartLine'], value['EndLine']], exact=exact))
    for version in ['R2', 'R1']:
        package, listing = members[version + 'Package'], members[version + 'Members']
        path = Path(known[package['Sha256']]) if package['Sha256'] in known else None
        if path is not None:
            raw = path.read_bytes()
            check(len(raw) == package['Bytes'] and hashlib.sha256(raw).hexdigest() == package['Sha256'], version + ' ZIP identity')
        else:
            out['limitations'].append(dict(version=version, kind='original_zip_not_located', sha256=package['Sha256'],
                meaning='Original container bytes are not proven locally; exact declared members are independently checked. Do not reconstruct or claim the original ZIP.'))
        archive = zipfile.ZipFile(path) if path is not None else None
        try:
            if archive is not None:
                names = [i.filename for i in archive.infolist() if not i.is_dir()]
                check(set(names) == {r['Member'] for r in listing}, version + ' complete member set')
            for item in listing:
                raw = archive.read(item['Member']) if archive is not None else Path(known[item['Sha256']]).read_bytes()
                ok = len(raw) == item['Bytes'] and hashlib.sha256(raw).hexdigest() == item['Sha256']
                check(ok, version + '/' + item['Member'] + ' member hash')
                name = item['Member']
                if version == 'R1' or name.startswith('history/'):
                    role = 'historical_superseded_review_basis'
                elif name.startswith('documents/'):
                    role = 'current_body_semantically_read'
                elif name.startswith(('sources/code/', 'source-delta/')) or name.endswith(('.cs', '.vue', '.ts')):
                    role = 'fixed_historical_code_not_fresh_code_audit'
                else:
                    role = 'scope_review_source_provenance_attachment'
                out['member_dispositions'].append(dict(item, package_version=version, identity_verified=ok, disposition=role, local_path=known.get(item['Sha256'])))
        finally:
            if archive is not None:
                archive.close()
        out['packages'].append(dict(package, local_path=str(path) if path else None, version=version, member_count=len(listing),
                                   current_local_container_verified=path is not None, declared_member_bytes_checked=True))
    out['member_role_counts'] = dict(collections.Counter(r['disposition'] for r in out['member_dispositions']))
    out['validation_passed'] = not errors
    dest = h / 'evidence/x3-metadata-reconciliation.json'
    dest.write_text(json.dumps(out, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(output=str(dest), targets=len(out['targets']), members=len(out['member_dispositions']), errors=errors), ensure_ascii=False))
    raise SystemExit(bool(errors))


if __name__ == '__main__':
    main()
