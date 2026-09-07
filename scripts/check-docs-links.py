#!/usr/bin/env python3
"""Check local file targets in Markdown; no network or heading-anchor checks.

Usage: python scripts/check-docs-links.py docs/README.md docs/CONTRIBUTING.md
Accepts inline links/images, reference definitions and HTML href/src attributes.
Fenced and inline code examples are ignored. Paths are checked case-sensitively,
including on Windows, to catch links that would fail on Linux/GitHub.
"""

import argparse
import posixpath
import re
import subprocess
from pathlib import Path
from urllib.parse import unquote


INLINE = re.compile(r'''\]\(\s*(<[^>\n]+>|(?:\\.|[^\s()]|\([^()\n]*\))+)(?:\s+(?:"[^"\n]*"|'[^'\n]*'))?\s*\)''')
REFERENCE = re.compile(r'^ {0,3}\[[^\]\n]+\]:\s*(<[^>\n]+>|\S+)', re.M)
HTML = re.compile(r'\b(?:href|src)\s*=\s*["\']([^"\']+)["\']', re.I)


def prose(text):
    """Mask code without shifting line numbers used in diagnostics."""
    fence = None
    lines = []
    for line in text.splitlines(keepends=True):
        marker = re.match(r'^ {0,3}(`{3,}|~{3,})(.*)$', line.rstrip('\r\n'))
        if fence:
            if marker and marker[1][0] == fence[0] and len(marker[1]) >= len(fence) and not marker[2].strip():
                fence = None
            lines.append('\n')
        elif marker:
            fence = marker[1]
            lines.append('\n')
        else:
            lines.append(re.sub(r'(`+).*?\1', '', line))
    return ''.join(lines)


def links(text):
    text = prose(text)
    matches = sorted((m for pattern in (INLINE, REFERENCE, HTML)
                      for m in pattern.finditer(text)), key=lambda m: m.start())
    for match in matches:
        value = match[1].strip('<>')
        yield text.count('\n', 0, match.start()) + 1, value


def local_target(source, value):
    if not value or value.startswith(('#', '//')) or re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', value):
        return None
    path = unquote(re.split('[?#]', value, maxsplit=1)[0])
    if not path:
        return None
    path = re.sub(r'\\([() ])', r'\1', path)
    return posixpath.normpath(path.lstrip('/') if path.startswith('/')
                             else posixpath.join(posixpath.dirname(source), path))


def exists_exact(root, target):
    if target == '..' or target.startswith('../'):
        return False
    current = root
    for component in Path(target).parts:
        if not current.is_dir() or component not in {p.name for p in current.iterdir()}:
            return False
        current = current / component
    # A symlink must also stay inside the repository.
    try:
        current.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return current.exists()


def check(root, sources):
    checked = 0
    errors = []
    for source in sources:
        if not exists_exact(root, source) or not (root / source).is_file():
            errors.append('{}: missing Markdown input'.format(source))
            continue
        for line, value in links((root / source).read_text(encoding='utf-8-sig')):
            target = local_target(source, value)
            if target is None:
                continue
            checked += 1
            if not exists_exact(root, target):
                errors.append('{}:{}: {} -> {}'.format(source, line, value, target))
    return checked, errors


def doc_sources(root):
    paths = subprocess.check_output(
        ['git', '-c', 'core.quotepath=false', 'ls-files', '-z', '--cached',
         '--others', '--exclude-standard', '--', 'docs'], cwd=root).decode('utf-8').split('\0')
    return sorted({p for p in paths if p.endswith('.md') and not p.startswith('docs/file/')})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('files', nargs='*', help='Markdown paths relative to the repository root')
    parser.add_argument('--all-docs', action='store_true',
                        help='Check maintained docs Markdown, excluding docs/file raw assets')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    sources = [Path(p).as_posix() for p in args.files]
    if args.all_docs:
        sources = sorted(set(sources + doc_sources(root)))
    if not sources:
        parser.error('provide Markdown paths or --all-docs')
    checked, errors = check(root, sources)
    for error in errors:
        print(error)
    print('{} Markdown files, {} local links, {} errors (file targets only).'.format(
        len(sources), checked, len(errors)))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
