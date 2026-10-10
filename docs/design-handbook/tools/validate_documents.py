"""Static documentation checks only; does not execute project or archive code."""
import argparse
import csv
import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import unquote


def links(text):
    # Fenced code may contain pseudo-links or instructions; it is never interpreted.
    in_fence, fence = False, ''
    for number, line in enumerate(text.splitlines(), 1):
        marker = re.match(r'^\s*(`{3,}|~{3,})', line)
        if marker:
            if not in_fence:
                in_fence, fence = True, marker[1][0]
            elif marker[1][0] == fence:
                in_fence = False
            continue
        if in_fence:
            continue
        for match in re.finditer(r'\]\(', line):
            i = match.end()
            if i < len(line) and line[i] == '<':
                end = line.find('>', i + 1)
                if end >= 0:
                    yield number, line[i + 1:end]
                continue
            depth, end = 1, i
            while end < len(line):
                if line[end] == '\\':
                    end += 2; continue
                if line[end] == '(':
                    depth += 1
                elif line[end] == ')':
                    depth -= 1
                    if depth == 0:
                        break
                end += 1
            if depth == 0:
                yield number, line[i:end]


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--handbook', required=True, type=Path)
    p.add_argument('--delivery', required=True, type=Path)
    a = p.parse_args()
    root = a.handbook.resolve()
    errors, warnings, json_count, csv_count, link_count = [], [], 0, 0, 0
    line_cache = {}
    for path in root.rglob('*'):
        if not path.is_file() or '__pycache__' in path.parts or path.name.endswith('.next.json'):
            continue
        try:
            if path.suffix == '.json':
                json.loads(path.read_text(encoding='utf-8-sig')); json_count += 1
            elif path.suffix == '.csv':
                with path.open(encoding='utf-8-sig', newline='') as f:
                    rows = csv.reader(f); header = next(rows)
                    for i, row in enumerate(rows, 2):
                        if len(row) != len(header):
                            errors.append({'file': str(path), 'line': i, 'error': 'CSV field count mismatch'})
                csv_count += 1
            elif path.suffix == '.md':
                text = path.read_text(encoding='utf-8-sig')
                for line, link in links(text):
                    if re.match(r'^(?:https?|mailto|app|codex):', link) or link.startswith('#'):
                        continue
                    link_count += 1
                    target = unquote(link).split('#', 1)[0]
                    match = re.search(r':(\d+)$', target)
                    target_line = int(match[1]) if match else None
                    if match:
                        target = target[:match.start()]
                    if target.startswith('/') and re.match(r'^/[A-Za-z]:/', target):
                        target = target[1:]
                    resolved = Path(target)
                    if not resolved.is_absolute():
                        resolved = path.parent / resolved
                    # Validate against the live authoring equivalent so a new valid doc
                    # does not fail merely because the in-progress local copy is older.
                    if resolved == a.delivery or a.delivery in resolved.parents:
                        resolved = root / resolved.relative_to(a.delivery)
                    if not resolved.exists():
                        errors.append({'file': path.relative_to(root).as_posix(), 'line': line, 'target': link, 'error': 'missing local link'})
                    elif target_line and resolved.is_file():
                        key = str(resolved)
                        if key not in line_cache:
                            try:
                                line_cache[key] = len(resolved.read_text(encoding='utf-8-sig').splitlines())
                            except UnicodeError:
                                line_cache[key] = None
                        if line_cache[key] is not None and not 1 <= target_line <= line_cache[key]:
                            errors.append({'file': path.relative_to(root).as_posix(), 'line': line, 'target': link, 'error': 'source line out of bounds', 'source_total_lines': line_cache[key]})
                    if 'CP6-worktrees' in target and '/docs/design-handbook' in target.replace('\\', '/'):
                        warnings.append({'file': path.relative_to(root).as_posix(), 'line': line, 'target': link, 'warning': 'internal handbook link depends on authoring worktree'})
        except Exception as exc:
            errors.append({'file': path.relative_to(root).as_posix(), 'error': repr(exc)})
    modules = list((root / 'modules').glob('*.md'))
    expected = {r['target_id'] for r in json.loads((root / 'indexes/required-inputs.json').read_text(encoding='utf-8'))['targets']}
    for module in modules:
        if module.stem not in expected:
            errors.append({'file': module.name, 'error': 'unexpected Target ID'})
        headings = re.findall(r'^##\s+', module.read_text(encoding='utf-8'), flags=re.M)
        if len(headings) < 12:
            warnings.append({'file': module.name, 'warning': 'fewer than 12 sections; author must confirm complete structure', 'count': len(headings)})
    report = {'as_of_utc': datetime.now(timezone.utc).isoformat(), 'scope': 'JSON/CSV parse, written module IDs/section presence, local Markdown file and line links. Link existence and heading count do not establish correct semantics or full source reading.', 'json_files_parsed': json_count, 'csv_files_parsed': csv_count, 'local_links_checked': link_count, 'written_modules': len(modules), 'expected_targets': len(expected), 'modules_not_yet_written': sorted(expected - {m.stem for m in modules}), 'errors': errors, 'warnings': warnings, 'business_tests_executed': False, 'semantic_completion_assessed_by_this_check': False}
    (root / 'evidence/document-validation.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('modules_not_yet_written', 'warnings', 'errors')} | {'error_count': len(errors), 'warning_count': len(warnings), 'first_errors': errors[:12]}, ensure_ascii=False))


if __name__ == '__main__':
    main()
