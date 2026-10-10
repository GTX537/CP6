"""Read-only Office text/cell view; retain formulas and coordinates, never execute them."""
from __future__ import annotations
import argparse
import hashlib
import json
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET
import openpyxl


def stringify(value):
    if value is None:
        return None
    if isinstance(value, (str, int, float, bool)):
        return value
    return str(value)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--indexes', required=True, type=Path)
    p.add_argument('--output', required=True, type=Path)
    a = p.parse_args()
    a.output.mkdir(parents=True, exist_ok=True)
    known = json.loads((a.indexes / 'materialized-content-paths.json').read_text(encoding='utf-8'))
    report = {'meaning': 'Structural extraction only, not semantic reading or visual verification. No formulas, macros or source scripts executed. Cached values are source-provided, not recalculated.', 'files': [], 'errors': []}
    for sha, name in known.items():
        path = Path(name)
        if path.suffix.lower() not in ('.xlsx', '.docx'):
            continue
        try:
            assert hashlib.sha256(path.read_bytes()).hexdigest() == sha
            with zipfile.ZipFile(path) as z:
                parts = z.namelist()
                media = [n for n in parts if '/media/' in n or 'drawing' in n or n.endswith('vbaProject.bin')]
                if path.suffix.lower() == '.xlsx':
                    formula = openpyxl.load_workbook(path, read_only=False, data_only=False, keep_links=False)
                    cached = openpyxl.load_workbook(path, read_only=False, data_only=True, keep_links=False)
                    sheets = []
                    for ws in formula:
                        cells = []
                        cw = cached[ws.title]
                        # Access populated cells only; avoid oversized stale worksheet dimensions.
                        for cell in sorted(ws._cells.values(), key=lambda c: (c.row, c.column)):
                            if cell.value is None and cell.comment is None:
                                continue
                            cells.append({'address': cell.coordinate, 'value': stringify(cell.value), 'type': cell.data_type, 'cached_value': stringify(cw[cell.coordinate].value) if cell.data_type == 'f' else None, 'number_format': cell.number_format, 'comment': cell.comment.text if cell.comment else None})
                        sheets.append({'name': ws.title, 'state': ws.sheet_state, 'merged_cells': [str(x) for x in ws.merged_cells.ranges], 'cells': cells})
                    formula.close(); cached.close()
                    extracted = {'sha256': sha, 'source_path': path.as_posix(), 'sheets': sheets, 'media_and_drawings_not_rendered': media}
                    lines = [f'# XLSX source: {path.as_posix()}', f'SHA-256: {sha}', '', 'Cells are source literals; formulas were not calculated.']
                    for sheet in sheets:
                        lines += ['', f'## Sheet {sheet["name"]} ({sheet["state"]})', 'Merged: ' + ', '.join(sheet['merged_cells'])]
                        for cell in sheet['cells']:
                            lines.append(cell['address'] + ': ' + str(cell['value']).replace('\n', ' ⏎ '))
                            if cell['type'] == 'f':
                                lines.append('  Source cached value: ' + str(cell['cached_value']))
                            if cell['comment']:
                                lines.append('  Comment: ' + cell['comment'].replace('\n', ' ⏎ '))
                    count = sum(len(s['cells']) for s in sheets)
                else:
                    ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
                    extracted_parts = []
                    for part in parts:
                        if part == 'word/document.xml' or part.startswith(('word/header', 'word/footer', 'word/footnotes', 'word/endnotes', 'word/comments')) and part.endswith('.xml'):
                            tree = ET.fromstring(z.read(part))
                            paragraphs = [''.join(t.text or '' for t in para.findall('.//w:t', ns)) for para in tree.findall('.//w:p', ns)]
                            extracted_parts.append({'part': part, 'paragraphs': paragraphs})
                    extracted = {'sha256': sha, 'source_path': path.as_posix(), 'parts': extracted_parts, 'media_and_drawings_not_rendered': media}
                    lines = [f'# DOCX source: {path.as_posix()}', f'SHA-256: {sha}', '', 'Paragraph order retained; table geometry and drawings require original visual inspection.']
                    for part in extracted_parts:
                        lines += ['', '## ' + part['part']]
                        lines.extend(f'P{i + 1}: {t}' for i, t in enumerate(part['paragraphs']))
                    count = sum(len(x['paragraphs']) for x in extracted_parts)
            dest = a.output / (sha + '.json')
            dest.write_text(json.dumps(extracted, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
            view = a.output / (sha + '.md')
            view.write_text('\n'.join(lines) + '\n', encoding='utf-8')
            report['files'].append({'sha256': sha, 'source_path': path.as_posix(), 'format': path.suffix, 'structured_path': dest.as_posix(), 'reading_path': view.as_posix(), 'text_units': count, 'media_or_drawings': len(media), 'status': 'structurally_extracted_not_semantically_read'})
        except Exception as e:
            report['errors'].append({'sha256': sha, 'source_path': path.as_posix(), 'error': repr(e)})
    (a.indexes / 'office-extraction.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'extracted': len(report['files']), 'errors': report['errors']}, ensure_ascii=False))


if __name__ == '__main__':
    main()
